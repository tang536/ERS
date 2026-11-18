using ERental.Data;

namespace ERental.Services;

///<summary>图片落盘的统一入口（维保现场照片、车辆图片共用一套规则）</summary>
public class ImageStorage(IWebHostEnvironment env)
{
    private readonly IWebHostEnvironment _env = env;

    private static readonly string[] AllowedImageExt = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    /// <summary>扩展名 → 内容格式的归一映射：.jpeg 与 .jpg 是同一格式</summary>
    private static readonly Dictionary<string, string> ExtAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ".jpg",
        [".jpeg"] = ".jpg",
        [".png"] = ".png",
        [".webp"] = ".webp",
        [".gif"] = ".gif"
    };

    private const long MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>PNG 文件头：避免在比较里写集合表达式导致类型推断成 int</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>读取文件头并判断真实图片格式</summary>
    private static string? DetectImageFormat(ReadOnlySpan<byte> header)
    {
        //JPEG：FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";
        //PNG：89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 && header[..8].SequenceEqual(PngSignature)) return ".png";
        //GIF：GIF87a / GIF89a
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return ".gif";
        //WebP：RIFF....WEBP
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    /// <summary>
    /// 校验上传文件确实是图片，并确认内容与扩展名一致。
    /// 只信任扩展名会让「把任意脚本改名为 .jpg」直接落进 wwwroot；这里读文件头做一次内容判定，
    /// 两类结果都拒绝：不是已知图片格式、或内容格式与声明的扩展名不一致。
    /// </summary>
    public static async Task<string> ValidateImageAsync(IFormFile file, IReadOnlyCollection<string> allowedExt, CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return ValidateImage(file.FileName, header.AsSpan(0, read), allowedExt);
    }

    /// <summary>纯内容校验：单独抽出来便于用固定文件头做单元测试</summary>
    public static string ValidateImage(string fileName, ReadOnlySpan<byte> header, IReadOnlyCollection<string> allowedExt)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!allowedExt.Contains(ext)) throw new BusinessException("仅支持 jpg / jpeg / png / webp / gif 格式的图片");

        var actual = DetectImageFormat(header)
            ?? throw new BusinessException("文件内容不是可识别的图片，请重新选择原始图片");

        if (!string.Equals(ExtAlias.GetValueOrDefault(ext), actual, StringComparison.Ordinal))
            throw new BusinessException("图片内容与扩展名不一致，请改用原始图片文件");

        return ext;
    }

    ///<summary>上传根目录（…/wwwroot/uploads）</summary>
    private string RootUploadsPath
    {
        get
        {
            //未启用静态文件中间件时 WebRootPath 可能为空串
            var root = _env.WebRootPath;
            if (root.Length == 0) root = Path.Combine(_env.ContentRootPath, "wwwroot");
            return Path.Combine(root, "uploads");
        }
    }

    ///<summary>保存图片并返回可直接引用的相对路径</summary>
    public async Task<string> SaveAsync(IFormFile? file, string subDir) => await SaveAsync(file, subDir, null, MaxImageBytes);

    ///<summary>判断 URL 是否属于指定上传目录，防止把任意外链写进数据库</summary>
    public static bool IsOwnedUrl(string? url, string subDir)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var prefix = $"/uploads/{subDir.Trim().Trim('/')}/";
        return url.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    ///<summary>
    /// 保存图片并返回可引用的相对路径。
    ///
    /// 命名规则从「时间戳+随机」升级为「前缀_时间戳_随机」，且按**年月分目录**：
    /// 验车、维保这类证据图会越攒越多（一天几十张），全平铺在一个目录里，
    /// 备份、迁移、清理都要 `ls` 上万行；按月分桶后，找「上个月的还车照」是点目录而不是翻列表。
    /// 前缀由调用方给（例如 Handover_197_还车），文件名自带业务线索，出事时不用反查数据库。
    ///</summary>
    public async Task<string> SaveAsync(IFormFile? file, string subDir, string? namePrefix, long maxBytes = MaxImageBytes)
    {
        if (file == null || file.Length == 0) throw new BusinessException("请选择要上传的图片");
        if (file.Length > maxBytes)
        {
            var limitMb = Math.Max(1, maxBytes / (1024 * 1024));
            throw new BusinessException($"图片不能超过 {limitMb}MB，请压缩后再上传");
        }

        //扩展名白名单 + 文件头魔数双重校验：只认扩展名的话，把脚本改名为 .jpg 也能落到 wwwroot 下
        var ext = await ValidateImageAsync(file, AllowedImageExt);

        //按年月分桶：老文件留在原目录照样能访问，新文件进新目录
        var bucket = AppClock.Now.ToString("yyyy-MM");
        var rel = $"{subDir}/{bucket}";
        var dir = Path.Combine(RootUploadsPath, subDir, bucket);
        Directory.CreateDirectory(dir);

        var prefix = SanitizePrefix(namePrefix);
        var name = $"{prefix}{AppClock.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}{ext}";
        await using (var fs = new FileStream(Path.Combine(dir, name), FileMode.Create))
        {
            await file.CopyToAsync(fs);
        }
        return $"/uploads/{rel}/{name}";
    }

    ///<summary>前缀只允许字母/数字/汉字/下划线/连字符，避免文件名里出现路径分隔符或空格</summary>
    private static string SanitizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
        var sb = new System.Text.StringBuilder(prefix.Length + 1);
        foreach (var ch in prefix.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-') sb.Append(ch);
        }
        return sb.Length == 0 ? string.Empty : sb.ToString() + "_";
    }

    ///<summary>保存内存中的图片字节流（AI 生图成品/合同签名下载后走这里，与上传同一套校验）</summary>
    public async Task<string> SaveStreamAsync(Stream content, string subDir, string ext)
    {
        if (!AllowedImageExt.Contains(ext.ToLowerInvariant())) throw new BusinessException("仅支持 jpg / jpeg / png / webp / gif 格式的图片");

        // SaveAsync 走 IFormFile 有文件大小与魔数校验；这里是内存流，必须补齐同一套规则，
        // 否则合同签名或 AI 返回内容可以被伪造成任意字节落进 uploads。
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered);
        if (buffered.Length == 0) throw new BusinessException("图片内容为空，请重新生成");
        if (buffered.Length > MaxImageBytes) throw new BusinessException("图片不能超过 5MB，请压缩后再上传");

        buffered.Seek(0, SeekOrigin.Begin);
        var header = new byte[12];
        var read = await buffered.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, CancellationToken.None);
        var actual = DetectImageFormat(header.AsSpan(0, read))
            ?? throw new BusinessException("文件内容不是可识别的图片，请重新生成");

        var normalizedExt = ext.ToLowerInvariant();
        if (!string.Equals(ExtAlias.GetValueOrDefault(normalizedExt), actual, StringComparison.Ordinal))
            throw new BusinessException("图片内容与扩展名不一致，请重新生成");

        var dir = Path.Combine(RootUploadsPath, subDir, AppClock.Now.ToString("yyyy-MM"));
        Directory.CreateDirectory(dir);

        var name = $"{AppClock.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}{normalizedExt}";
        var path = Path.Combine(dir, name);
        await using (var fs = new FileStream(path, FileMode.Create))
        {
            buffered.Seek(0, SeekOrigin.Begin);
            await buffered.CopyToAsync(fs);
        }
        return $"/uploads/{subDir}/{AppClock.Now:yyyy-MM}/{name}";
    }

    ///<summary>删除一张已上传的图片（按 /uploads/ 开头的相对路径）</summary>
    public void Delete(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return;
        var rel = url["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (rel.Length == 0) return;
        var root = Path.GetFullPath(RootUploadsPath);
        // 必须以「uploads + 目录分隔符」为前缀，否则 /uploads/../uploads-evil/x.jpg 会绕过
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        //防路径穿越：最终路径必须在 uploads 根目录内
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(full))
        {
            try { File.Delete(full); } catch {/* 占用中或权限不足时忽略，下次清理再删 */}
        }
    }
}
