using ERental.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERental.Tests;

/// <summary>
/// 会话校验的 5 秒短缓存必须被写操作主动失效：
/// 否则「换设备登录」后旧 token 还能在缓存窗口内继续访问，单点登录就形同虚设。
/// </summary>
public class SessionRegistryCacheTests
{
    [MySqlFact]
    public async Task WritesInvalidateCachedSessionState()
    {
        using var testDb = TestDatabase.Create();
        var registry = new SessionRegistry(
            testDb.Db,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SessionRegistry>.Instance);

        var first = await registry.IssueAsync("customer", 7);
        Assert.True((await registry.CheckAsync("customer", 7, first)).Current);
        //第二次走缓存，仍应返回同一结论
        Assert.True((await registry.CheckAsync("customer", 7, first)).Current);

        //新登录顶掉旧会话：Issue 必须主动失效缓存，旧 sid 立即判为失效
        var second = await registry.IssueAsync("customer", 7);
        Assert.False((await registry.CheckAsync("customer", 7, first)).Current);
        Assert.True((await registry.CheckAsync("customer", 7, second)).Current);

        //强制下线：写入撤销原因并失效缓存
        await registry.RevokeAllAsync("customer", 7, "账号被停用");
        var (online, reason) = await registry.CheckAsync("customer", 7, second);
        Assert.False(online);
        Assert.Equal("账号被停用", reason);

        //主动登出只清掉当前会话，且不能被旧缓存重新读出来
        var third = await registry.IssueAsync("customer", 7);
        await registry.RevokeAsync("customer", 7, third);
        Assert.False((await registry.CheckAsync("customer", 7, third)).Current);
    }
}
