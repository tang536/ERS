using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERental.Tests;

/// <summary>
/// 顾客登录也要有暴力破解防护：达到阈值后按「账号 + IP」封锁，
/// 且不能因为一个账号试错把整个共用出口 IP 的顾客都挡在门外。
/// </summary>
public class CustomerLoginGuardTests
{
    [Fact]
    public void GuardLocksOnTheConfiguredThreshold()
    {
        var guard = new LoginIpGuard();
        for (var i = 1; i <= 4; i++)
            Assert.False(guard.RegisterFailure("k", 5, 10, out _), $"第 {i} 次不应触发封锁");
        Assert.True(guard.RegisterFailure("k", 5, 10, out _), "第 5 次应触发封锁");
    }

    [MySqlFact]
    public async Task LocksAccountAndIpAfterRepeatedFailures()
    {
        using var testDb = TestDatabase.Create();
        var db = testDb.Db;

        const string account = "cust_guard";
        const string password = "Correct#123";
        db.Customers.Add(Customer.Register(account, null, BCrypt.Net.BCrypt.HashPassword(password)));
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = new string('k', 48),
                ["Jwt:Issuer"] = "ERentalTests",
                ["Jwt:Audience"] = "ERentalTests",
            })
            .Build();
        var auth = new AuthService(
            db, config,
            new SessionRegistry(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<SessionRegistry>.Instance),
            new LoginIpGuard(),
            new HttpContextAccessor());

        //默认阈值 5 次：前 4 次只提示剩余次数，第 5 次进入封锁
        string last = "";
        for (var i = 0; i < 5; i++)
        {
            var ex = await Assert.ThrowsAsync<BusinessException>(() =>
                auth.CustomerLogin(new CustomerLoginDto { Account = account, Password = "wrong" }, "203.0.113.9"));
            last = ex.Message;
        }
        Assert.Contains("封锁", last, StringComparison.Ordinal);

        //同一账号 + 同一 IP：即使密码正确也不再放行（入口封锁提示「登录失败次数过多」）
        var blocked = await Assert.ThrowsAsync<BusinessException>(() =>
            auth.CustomerLogin(new CustomerLoginDto { Account = account, Password = password }, "203.0.113.9"));
        Assert.Contains("登录失败次数过多", blocked.Message, StringComparison.Ordinal);

        //换一个 IP 不受影响：避免校园/门店共用出口被一个账号拖死
        var ok = await auth.CustomerLogin(
            new CustomerLoginDto { Account = account, Password = password }, "203.0.113.10");
        Assert.Equal("customer", ok.UserType);
    }
}
