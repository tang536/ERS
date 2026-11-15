using ERental.Data;
using ERental.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERental.Tests;

/// <summary>
/// 支付回调去重从「进程内字典」改成数据库唯一键：
/// 窗口内重复报文只放行一次，窗口过后同一报文可以重新登记（入账侧幂等）。
/// </summary>
public class PayNotifyDeduplicatorTests
{
    private static PayNotifyDeduplicator Create(ERental.Data.AppDbContext db)
        => new(db, NullLogger<PayNotifyDeduplicator>.Instance);

    [MySqlFact]
    public async Task FirstRegisterWinsAndDuplicateIsRejected()
    {
        using var testDb = TestDatabase.Create();
        DbInitializer.EnsureSchemaUpgrades(testDb.Db);
        var dedup = Create(testDb.Db);

        const string key = "ABCDEF0123456789";
        Assert.True(await dedup.TryRegisterAsync("alipay", "ORD-1", key));
        Assert.False(await dedup.TryRegisterAsync("alipay", "ORD-1", key));

        //不同报文指纹互不影响
        Assert.True(await dedup.TryRegisterAsync("alipay", "ORD-2", "ABCDEF012345678A"));
    }

    [MySqlFact]
    public async Task ExpiredEntryCanBeRegisteredAgain()
    {
        using var testDb = TestDatabase.Create();
        DbInitializer.EnsureSchemaUpgrades(testDb.Db);
        var db = testDb.Db;
        var dedup = Create(db);

        const string key = "0123456789ABCDEF";
        Assert.True(await dedup.TryRegisterAsync("wechat", null, key));

        //把窗口拨到过去，模拟网关 5 分钟后的重推
        db.Database.ExecuteSqlRaw(
            "UPDATE `pay_notify_log` SET `expires_at` = {0} WHERE `replay_key` = {1}",
            AppClock.Now.AddMinutes(-1), key);

        Assert.True(await dedup.TryRegisterAsync("wechat", null, key));
        //刷新后又落回「窗口内」，再次投递必须拒绝
        Assert.False(await dedup.TryRegisterAsync("wechat", null, key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12.3.4")]
    public void AmountRejectsMalformedValues(string text)
    {
        Assert.False(PayNotifyDeduplicator.TryParseAmount(text, out _));
    }

    [Fact]
    public void AmountParsesInvariantCulture()
    {
        Assert.True(PayNotifyDeduplicator.TryParseAmount("1234.50", out var value));
        Assert.Equal(1234.50m, value);
    }
}
