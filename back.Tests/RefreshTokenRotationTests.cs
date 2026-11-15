using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERental.Tests;

/// <summary>
/// 续期令牌必须一次性使用：换新后旧令牌立即作废，重放旧令牌要 401。
/// 这是防「续期令牌被窃取后长期复用」的关键一环。
/// </summary>
public class RefreshTokenRotationTests
{
    private static AuthService CreateAuth(ERental.Data.AppDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = new string('k', 48),
                ["Jwt:Issuer"] = "ERentalTests",
                ["Jwt:Audience"] = "ERentalTests",
            })
            .Build();

        return new AuthService(
            db, config,
            new SessionRegistry(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<SessionRegistry>.Instance),
            new LoginIpGuard(),
            new HttpContextAccessor());
    }

    [MySqlFact]
    public async Task RefreshRotatesTokenAndRejectsReplay()
    {
        using var testDb = TestDatabase.Create();
        var db = testDb.Db;

        const string account = "cust_refresh";
        const string password = "Correct#123";
        db.Customers.Add(Customer.Register(account, null, BCrypt.Net.BCrypt.HashPassword(password)));
        await db.SaveChangesAsync();

        var auth = CreateAuth(db);
        var login = await auth.CustomerLogin(new CustomerLoginDto { Account = account, Password = password }, "203.0.113.20");
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));

        //第一次续期：拿到新的续期令牌
        var rotated = await auth.RefreshAsync(login.RefreshToken);
        Assert.False(string.IsNullOrEmpty(rotated.RefreshToken));
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);

        //旧令牌重放：必须拒绝
        await Assert.ThrowsAsync<BusinessException>(() => auth.RefreshAsync(login.RefreshToken));

        //新令牌仍然可用（连续续期）
        var again = await auth.RefreshAsync(rotated.RefreshToken);
        Assert.False(string.IsNullOrEmpty(again.Token));
    }
}
