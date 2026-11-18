using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ERental.Services;

/// <summary>
/// 登录滑动拼图验证。
///
/// 服务端现场画一张带缺口的背景图和一块同形状的拼图块，前端只拿得到 PNG 与坐标，
/// 正确答案（缺口横向位置）留在服务端缓存里。用户拖动滑块把拼图对进缺口后，
/// 调 verify 接口校验偏移；通过后签发一张 5 分钟、一次性、绑定 IP 的登录票据，
/// 登录接口只认这张票据——不能拿“验证通过”的布尔值直接登录。
///
/// 与字符验证码相比，拼图验证的答案不是可读文本，脚本必须先拿到图再做缺口识别；
/// 加上随机底色、噪声、干扰曲线与每次不同的拼图形状，抬高批量破解成本。
/// </summary>
public sealed class SliderCaptchaService(IMemoryCache cache)
{
    /// <summary>拼图挑战有效期：2 分钟，够用户看清并拖动。</summary>
    public const int ExpiresInSeconds = 120;

    /// <summary>票据有效期：验证通过后 5 分钟内必须完成登录。</summary>
    public const int TicketExpiresInSeconds = 300;

    public const int Width = 340;
    public const int Height = 190;
    public const int PieceSize = 48;

    /// <summary>拼图凸起半径；图片四周留出同等内边距，避免凸起被裁掉。</summary>
    private const int Bump = 9;

    /// <summary>拼图块画布尺寸（含凸起外扩）。</summary>
    public const int PieceCanvas = PieceSize + (Bump * 2);

    /// <summary>允许的横向误差（像素）。太严会让人手抖永远对不上，太松则失去校验意义。</summary>
    private const int Tolerance = 7;

    private const string ChallengePrefix = "auth:slider:";
    private const string TicketPrefix = "auth:slider-ticket:";

    /// <summary>生成一张新挑战，返回背景图、拼图块与前端定位所需尺寸。</summary>
    public SliderChallenge Create(string? clientIp)
    {
        var id = Guid.NewGuid().ToString("N");
        // 缺口位置随机，但要给右侧凸起和外边距留空间
        var targetX = RandomNumberGenerator.GetInt32(Bump + 16, Width - PieceCanvas - 12);
        var targetY = RandomNumberGenerator.GetInt32(12, Height - PieceCanvas - 12);

        using var background = RenderBackground();
        using var piece = RenderPiece(background, targetX, targetY);
        using var holed = RenderHole(background, targetX + Bump, targetY + Bump);

        cache.Set(ChallengePrefix + id, new Entry(targetX, NormalizeIp(clientIp)), TimeSpan.FromSeconds(ExpiresInSeconds));

        return new SliderChallenge(
            id,
            ToDataUrl(holed),
            ToDataUrl(piece),
            Width,
            Height,
            PieceCanvas,
            targetY,
            Width - PieceCanvas,
            ExpiresInSeconds);
    }

    /// <summary>
    /// 校验滑块偏移。无论对错挑战都只给一次机会；通过后返回一次性登录票据。
    /// 返回 null 表示校验失败（调用方统一提示“验证未通过，请重试”）。
    /// </summary>
    public string? Verify(string? id, int offset, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var key = ChallengePrefix + id.Trim();
        Entry? entry;
        try
        {
            entry = cache.Get<Entry>(key);
        }
        catch (Exception)
        {
            // 缓存故障时按失败处理，绝不因为“验证服务出错”而放行
            return null;
        }
        cache.Remove(key);
        if (entry is null) return null;
        if (!string.Equals(entry.Ip, NormalizeIp(clientIp), StringComparison.Ordinal)) return null;
        if (Math.Abs(offset - entry.TargetX) > Tolerance) return null;

        var ticket = Guid.NewGuid().ToString("N");
        cache.Set(TicketPrefix + ticket, new TicketEntry(entry.Ip), TimeSpan.FromSeconds(TicketExpiresInSeconds));
        return ticket;
    }

    /// <summary>登录时消费票据：一次性、绑定 IP，消费后立即删除。</summary>
    public bool ConsumeTicket(string? ticket, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return false;
        var key = TicketPrefix + ticket.Trim();
        TicketEntry? entry;
        try
        {
            entry = cache.Get<TicketEntry>(key);
        }
        catch (Exception)
        {
            return false;
        }
        cache.Remove(key);
        return entry is not null
            && string.Equals(entry.Ip, NormalizeIp(clientIp), StringComparison.Ordinal);
    }

    /// <summary>仅供本机开发/端到端测试读取缺口位置；生产环境控制器不会暴露该路由。</summary>
    public int? Peek(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return cache.Get<Entry>(ChallengePrefix + id.Trim())?.TargetX;
    }

    private static string NormalizeIp(string? ip) => string.IsNullOrWhiteSpace(ip) ? "unknown" : ip.Trim();

    private static string ToDataUrl(SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"data:image/png;base64,{Convert.ToBase64String(data.ToArray())}";
    }

    /// <summary>画一张足够“花”的底图：渐变、色块、曲线、点阵与暗角，避免纯色让缺口过于显眼。</summary>
    private static SKImage RenderBackground()
    {
        var rnd = new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
        var palette = PickPalette(rnd);
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(Width, Height),
                [palette.Base, palette.Mid, palette.Light],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Width, Height, paint);
        }

        // 大色块：低透明度圆与圆角矩形，打乱边缘
        for (var i = 0; i < 7; i++)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = palette.Accent.WithAlpha((byte)rnd.Next(28, 76))
            };
            var r = rnd.Next(24, 80);
            canvas.DrawCircle(rnd.Next(0, Width), rnd.Next(0, Height), r, paint);
        }

        // 弧形/斜向干扰线
        using (var paint = new SKPaint { IsAntialias = true })
        {
            for (var i = 0; i < 6; i++)
            {
                paint.Color = (i % 2 == 0 ? SKColors.White : palette.Accent).WithAlpha((byte)rnd.Next(40, 105));
                paint.StrokeWidth = rnd.Next(1, 5);
                using var path = new SKPath();
                path.MoveTo(rnd.Next(-20, 40), rnd.Next(0, Height));
                path.CubicTo(
                    rnd.Next(60, 130), rnd.Next(-30, Height + 30),
                    rnd.Next(180, 270), rnd.Next(-30, Height + 30),
                    Width + 20, rnd.Next(0, Height));
                canvas.DrawPath(path, paint);
            }
        }

        // 细点阵
        using (var paint = new SKPaint { IsAntialias = true })
        {
            for (var i = 0; i < 90; i++)
            {
                paint.Color = (i % 3 == 0 ? SKColors.White : palette.Light).WithAlpha((byte)rnd.Next(25, 95));
                canvas.DrawCircle(rnd.Next(0, Width), rnd.Next(0, Height), rnd.Next(1, 3), paint);
            }
        }

        // 暗角：让四周压暗，缺口边缘不会因为高对比一眼被识别
        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(Width * .5f, Height * .45f),
                Width * .72f,
                [SKColors.Transparent, new SKColor(0, 0, 0, 58)],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Width, Height, paint);
        }

        return surface.Snapshot();
    }

    /// <summary>从底图裁出拼图块：先按缺口坐标平移整张底图，再用拼图路径裁剪。</summary>
    private static SKImage RenderPiece(SKImage background, int targetX, int targetY)
    {
        using var surface = SKSurface.Create(new SKImageInfo(PieceCanvas, PieceCanvas, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var path = BuildJigsawPath(Bump, Bump, PieceSize, Bump);
        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        canvas.DrawImage(background, -targetX, -targetY);
        canvas.Restore();

        // 拼图块描边与内阴影：让它在深色/浅色底图上都能看清
        using (var shadow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5,
            Color = new SKColor(0, 0, 0, 70)
        })
            canvas.DrawPath(path, shadow);

        using (var border = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f,
            Color = new SKColor(255, 255, 255, 190)
        })
            canvas.DrawPath(path, border);

        return surface.Snapshot();
    }

    /// <summary>在底图上画出同形状的暗色缺口与白色描边。</summary>
    private static SKImage RenderHole(SKImage background, int holeX, int holeY)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.DrawImage(background, 0, 0);

        using var path = BuildJigsawPath(holeX, holeY, PieceSize, Bump);

        using (var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 0, 0, 92)
        })
            canvas.DrawPath(path, fill);

        using (var border = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            Color = new SKColor(255, 255, 255, 125)
        })
            canvas.DrawPath(path, border);

        return surface.Snapshot();
    }

    /// <summary>
    /// 拼图路径：正方形四边各带一个凸起，凸起方向交替向外/向内。
    /// 坐标以拼图块可视矩形的左上角为原点；凸起会超出 0..size，因此调用方必须留 padding。
    /// </summary>
    private static SKPath BuildJigsawPath(float x, float y, float size, float bump)
    {
        var path = new SKPath();
        path.MoveTo(x, y);
        // 上边：中间向上凸
        path.LineTo(x + (size * .34f), y);
        path.CubicTo(
            x + (size * .34f), y - bump,
            x + (size * .66f), y - bump,
            x + (size * .66f), y);
        path.LineTo(x + size, y);
        // 右边：中间向右凸
        path.LineTo(x + size, y + (size * .34f));
        path.CubicTo(
            x + size + bump, y + (size * .34f),
            x + size + bump, y + (size * .66f),
            x + size, y + (size * .66f));
        path.LineTo(x + size, y + size);
        // 下边：中间向下凸
        path.LineTo(x + (size * .66f), y + size);
        path.CubicTo(
            x + (size * .66f), y + size + bump,
            x + (size * .34f), y + size + bump,
            x + (size * .34f), y + size);
        path.LineTo(x, y + size);
        // 左边：中间向左凸
        path.LineTo(x, y + (size * .66f));
        path.CubicTo(
            x - bump, y + (size * .66f),
            x - bump, y + (size * .34f),
            x, y + (size * .34f));
        path.LineTo(x, y);
        path.Close();
        return path;
    }

    private static (SKColor Base, SKColor Mid, SKColor Light, SKColor Accent) PickPalette(Random rnd)
    {
        // 六套低饱和但彼此有对比的配色，避免每次都长得一样
        var palettes = new[]
        {
            (new SKColor(0x16, 0x2A, 0x4A), new SKColor(0x1E, 0x5C, 0x8A), new SKColor(0x5A, 0xC8, 0xD8), new SKColor(0xF2, 0xB0, 0x5E)),
            (new SKColor(0x3B, 0x1D, 0x2E), new SKColor(0x7A, 0x2E, 0x4C), new SKColor(0xD9, 0x6C, 0x8F), new SKColor(0xF3, 0xC9, 0x7A)),
            (new SKColor(0x13, 0x2E, 0x28), new SKColor(0x2C, 0x6B, 0x4F), new SKColor(0x8B, 0xC9, 0x7A), new SKColor(0xF0, 0xD3, 0x7C)),
            (new SKColor(0x2B, 0x21, 0x4A), new SKColor(0x52, 0x3D, 0x8C), new SKColor(0x9B, 0x7D, 0xD8), new SKColor(0xF0, 0x9F, 0xB8)),
            (new SKColor(0x2D, 0x24, 0x1B), new SKColor(0x6E, 0x4E, 0x2D), new SKColor(0xC9, 0x9A, 0x5E), new SKColor(0xE8, 0x7A, 0x5C)),
            (new SKColor(0x17, 0x2B, 0x33), new SKColor(0x2F, 0x5D, 0x6B), new SKColor(0x77, 0xB8, 0xB0), new SKColor(0xF2, 0x8C, 0x6B)),
        };
        return palettes[rnd.Next(0, palettes.Length)];
    }

    private sealed record Entry(int TargetX, string Ip);
    private sealed record TicketEntry(string Ip);
}

/// <summary>滑动挑战：背景图/拼图块均为 data URL，尺寸用于前端按比例定位。</summary>
public sealed record SliderChallenge(
    string Id,
    string BackgroundDataUrl,
    string PieceDataUrl,
    int Width,
    int Height,
    int PieceCanvas,
    int PieceY,
    int MaxOffset,
    int ExpiresInSeconds);
