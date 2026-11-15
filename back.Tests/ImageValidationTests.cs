using ERental.Services;

namespace ERental.Tests;

/// <summary>
/// 图片上传内容校验：只认扩展名不够，必须再用文件头确认「真的是图片」且与扩展名一致。
/// 这里用固定文件头覆盖各格式的接受/拒绝分支，避免把脚本改名为 .jpg 落进 wwwroot。
/// </summary>
public class ImageValidationTests
{
    private static readonly string[] Allowed = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    [Fact]
    public void AcceptsKnownFormatsWhenExtensionMatches()
    {
        Assert.Equal(".png", ImageStorage.ValidateImage("a.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Allowed));
        Assert.Equal(".jpeg", ImageStorage.ValidateImage("a.jpeg", [0xFF, 0xD8, 0xFF, 0xE0], Allowed));
        Assert.Equal(".gif", ImageStorage.ValidateImage("a.gif", "GIF89a"u8, Allowed));
        Assert.Equal(".webp", ImageStorage.ValidateImage("a.webp", "RIFF\0\0\0\0WEBP"u8, Allowed));
    }

    [Fact]
    public void RejectsExtensionOutsideWhitelist()
    {
        var ex = Assert.Throws<BusinessException>(() =>
            ImageStorage.ValidateImage("shell.exe", [0xFF, 0xD8, 0xFF], Allowed));
        Assert.Contains("仅支持", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownContent()
    {
        var ex = Assert.Throws<BusinessException>(() =>
            ImageStorage.ValidateImage("a.png", "<script>"u8, Allowed));
        Assert.Contains("不是可识别的图片", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsContentThatDoesNotMatchExtension()
    {
        //PNG 字节套 .jpg 后缀：浏览器会按声明的图片类型解析失败，更危险的是掩盖真实内容
        var ex = Assert.Throws<BusinessException>(() =>
            ImageStorage.ValidateImage("a.jpg", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Allowed));
        Assert.Contains("与扩展名不一致", ex.Message, StringComparison.Ordinal);
    }
}
