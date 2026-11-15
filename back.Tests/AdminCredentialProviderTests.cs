using ERental.Services;
using Microsoft.Extensions.Configuration;

namespace ERental.Tests;

/// <summary>
/// 管理员初始口令：仓库里的 123456 不能再自动生效。
/// 配置了强口令就用配置；配置的是弱口令就当作没配，宁可用随机口令并落盘。
/// </summary>
public class AdminCredentialProviderTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] items)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(items.Select(i => new KeyValuePair<string, string?>(i.Key, i.Value)))
            .Build();

    [Fact]
    public void StrongConfiguredPasswordIsUsed()
    {
        var credential = AdminCredentialProvider.Resolve(
            Config(("Admin:InitialPassword", "Str0ng-Pass!")), env: null);

        Assert.Equal("Str0ng-Pass!", credential.Password);
        Assert.Null(credential.GeneratedFilePath);
    }

    [Fact]
    public void WeakConfiguredPasswordFallsBackToRandomAndWritesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"erental-admin-{Guid.NewGuid():N}.txt");
        try
        {
            var credential = AdminCredentialProvider.Resolve(
                Config(("Admin:InitialPassword", "123456"), ("Admin:InitialPasswordFile", path)), env: null);

            Assert.NotEqual("123456", credential.Password);
            Assert.True(credential.Password.Length >= 12);
            Assert.True(AdminCredentialProvider.IsStrong(credential.Password));
            Assert.Equal(path, credential.GeneratedFilePath);
            Assert.Equal(credential.Password, File.ReadAllText(path).Trim());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PasswordFileIsReusedWhenPresent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"erental-admin-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "From-File-2026");
            var credential = AdminCredentialProvider.Resolve(
                Config(("Admin:InitialPasswordFile", path)), env: null);

            Assert.Equal("From-File-2026", credential.Password);
            Assert.Null(credential.GeneratedFilePath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacyDefaultHashIsDetected()
    {
        Assert.True(AdminCredentialProvider.IsLegacyDefaultHash(BCrypt.Net.BCrypt.HashPassword("123456")));
        Assert.False(AdminCredentialProvider.IsLegacyDefaultHash(BCrypt.Net.BCrypt.HashPassword("Str0ng-Pass!")));
        Assert.False(AdminCredentialProvider.IsLegacyDefaultHash(""));
    }
}
