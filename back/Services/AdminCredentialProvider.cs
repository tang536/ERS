using System.Security.Cryptography;
using System.Text;

namespace ERental.Services;

/// <summary>
/// 首次初始化管理员账号时的口令来源。
///
/// 出厂账号 <c>trq / 123456</c> 是公开写在 README 与演示脚本里的口令，
/// 任何还留着它的部署都等于把管理员入口敞开。这里的取值顺序是：
///   ① 配置项 <c>Admin:InitialPassword</c>（生产用环境变量 <c>Admin__InitialPassword</c> 注入）；
///   ② 口令文件 <c>Admin:InitialPasswordFile</c>（默认 <c>App_Data/admin-initial-password.txt</c>）；
///   ③ 都没有 → 随机生成一份，写入上述文件并在控制台提示路径。
/// 命中「弱口令名单」的配置值会被当成没配：不从公开默认值继续跑。
/// </summary>
internal static class AdminCredentialProvider
{
    /// <summary>出厂唯一的员工账号（与 DbInitializer 种子一致）</summary>
    public const string Account = "trq";

    /// <summary>仓库历史/演示脚本里出现过的弱口令，命中即视为「没配」</summary>
    private static readonly HashSet<string> WeakPasswords = new(StringComparer.Ordinal)
    {
        "123456", "12345678", "admin", "password", "admin123", "erental"
    };

    /// <summary>初始口令的最短长度：与用户自助改密的复杂度下限保持一致</summary>
    private const int MinLength = 8;

    /// <summary>随机口令去掉易混字符（0/O、1/l/I），方便管理员照着敲</summary>
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>口令来源</summary>
    public sealed record Credential(string Password, string Source, string? GeneratedFilePath);

    /// <summary>解析初始口令；<paramref name="config"/> 为空时按无配置处理</summary>
    public static Credential Resolve(IConfiguration? config, IHostEnvironment? env)
    {
        var configured = (config?["Admin:InitialPassword"] ?? "").Trim();
        if (IsStrong(configured)) return new Credential(configured, "配置项 Admin:InitialPassword", null);

        var path = (config?["Admin:InitialPasswordFile"] ?? "").Trim();
        if (path.Length == 0 && env is not null)
            path = Path.Combine(env.ContentRootPath, "App_Data", "admin-initial-password.txt");
        if (path.Length > 0)
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path))
            {
                var fromFile = File.ReadAllText(path).Trim();
                if (IsStrong(fromFile)) return new Credential(fromFile, $"口令文件 {path}", null);
            }
        }

        //随机生成：12 位字母数字混合，至少含 2 个字母 + 2 个数字
        var password = string.Concat(
            RandomNumberGenerator.GetString(Letters, 2),
            RandomNumberGenerator.GetString(Digits, 2),
            RandomNumberGenerator.GetString(Letters + Digits, 8));

        //落盘一份：只靠控制台输出，滚动日志一冲就找不回来了
        if (path.Length == 0) return new Credential(password, "系统随机生成（未启用文件，口令仅在控制台输出）", null);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, password + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new Credential(password, $"系统随机生成（已写入 {path}）", path);
    }

    /// <summary>这个哈希是不是仍在使用公开的出厂口令 123456（用于对存量部署告警）</summary>
    public static bool IsLegacyDefaultHash(string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) return false;
        try { return BCrypt.Net.BCrypt.Verify("123456", passwordHash); }
        catch { return false; }
    }

    /// <summary>配置值是否达到可用的强度</summary>
    public static bool IsStrong(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Trim().Length >= MinLength
           && !WeakPasswords.Contains(value.Trim());
}
