using ERental.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERental.Tests;

/// <summary>
/// 备份密钥与 JWT 密钥解耦：Jwt:Key 变了，历史备份也必须还能解开。
/// </summary>
public class BackupKeyTests
{
    private static BackupService Create(IConfiguration cfg)
        => new(db: null!, cfg, env: null!, NullLogger<BackupService>.Instance);

    private static IConfiguration Config(params (string Key, string? Value)[] items)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(items.Select(i => new KeyValuePair<string, string?>(i.Key, i.Value)))
            .Build();

    [Fact]
    public void ParseKeyRejectsWrongLengthAndNonBase64()
    {
        Assert.Throws<InvalidOperationException>(() => BackupService.ParseKey(Convert.ToBase64String(new byte[16]), "Backup:Key"));
        Assert.Throws<InvalidOperationException>(() => BackupService.ParseKey("not-base64!!", "Backup:Key"));
    }

    [Fact]
    public void ConfiguredKeyWins()
    {
        var expected = new byte[32];
        Random.Shared.NextBytes(expected);

        var svc = Create(Config(("Backup:Key", Convert.ToBase64String(expected))));

        Assert.Equal(expected, svc.ResolveKey());
    }

    [Fact]
    public void MissingKeyFileGeneratesKeyIndependentOfJwtAndReusesIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"erental-backup-key-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "backup.key");
        try
        {
            //刻意不提供 Jwt:Key：旧实现会因此拒绝启动备份
            var cfg = Config(("Backup:KeyFile", path));
            var svc = Create(cfg);

            var first = svc.ResolveKey();
            Assert.Equal(32, first.Length);
            Assert.True(File.Exists(path));
            Assert.Equal(first, BackupService.ParseKey(File.ReadAllText(path), path));

            //再次读取必须稳定，不能每次生成新密钥（否则旧备份全废）
            Assert.Equal(first, svc.ResolveKey());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
