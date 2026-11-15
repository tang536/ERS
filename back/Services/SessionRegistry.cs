using System.Text.Json;
using ERental.Data;
using ERental.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERental.Services;

///<summary>在线会话登记表</summary>
public sealed class SessionRegistry(AppDbContext db, IMemoryCache cache, ILogger<SessionRegistry> logger)
{
    ///<summary>会话键前缀</summary>
    public const string Prefix = "sess.";

    ///<summary>
    /// 会话校验的本地短缓存（秒）。单点校验是每个已认证请求的必经读路径，
    /// 不加缓存等于每个请求多打一次 system_config。取 5 秒：多实例部署时
    /// 「另一台实例强制下线」最多延迟这么久生效，既挡住高频查询又不会让用户等太久。
    /// </summary>
    private const int CacheSeconds = 5;

    private readonly AppDbContext _db = db;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<SessionRegistry> _logger = logger;

    ///<summary>登记表里的一行：当前会话标识 + 绑定的续期令牌 jti（旧数据没有该字段）</summary>
    private sealed record Payload(string Token, string? Revoked = null, string? RefreshJti = null);

    ///<summary>账号唯一键：员工与顾客的 ID 各自独立计数，必须带类型前缀区分</summary>
    private static string KeyOf(string userType, int userId) => $"{Prefix}{userType}.{userId}";

    ///<summary>缓存键与登记表键分开命名，避免有人把两者混为一谈</summary>
    private static string CacheKeyOf(string key) => "sess-cache:" + key;

    ///<summary>签发会话；<paramref name="refreshJti"/> 绑定本次登录签发的续期令牌</summary>
    public async Task<string> IssueAsync(string userType, int userId, string? refreshJti = null)
    {
        var token = Guid.NewGuid().ToString("N");
        await SaveAsync(KeyOf(userType, userId), new Payload(token, null, refreshJti));
        return token;
    }

    ///<summary>一次取出「是否当前会话+撤销原因」，供中间件与续期校验使用</summary>
    public async Task<(bool Current, string? RevokedReason)> CheckAsync(string userType, int userId, string? sessionToken)
    {
        var entry = await LoadAsync(KeyOf(userType, userId));
        if (entry is null) return (false, null);
        var current = !string.IsNullOrEmpty(sessionToken) && string.Equals(entry.Token, sessionToken, StringComparison.Ordinal);
        return (current, entry.Revoked);
    }

    ///<summary>
    /// 续期令牌校验：除了会话 sid，还必须匹配登记表里的刷新令牌 jti。
    /// 续期是低频操作，这里强制读库（绕过 5 秒缓存），确保「已被换掉的旧令牌」立刻失效。
    /// </summary>
    public async Task<(bool Current, string? RevokedReason)> CheckRefreshAsync(string userType, int userId, string? sessionToken, string? refreshJti)
    {
        var entry = await LoadAsync(KeyOf(userType, userId), useCache: false);
        if (entry is null) return (false, null);
        var current = !string.IsNullOrEmpty(sessionToken)
            && string.Equals(entry.Token, sessionToken, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(refreshJti)
            && string.Equals(entry.RefreshJti, refreshJti, StringComparison.Ordinal);
        return (current, entry.Revoked);
    }

    ///<summary>
    /// 刷新令牌轮换：把登记表里的 jti 从旧值换成新值，旧令牌立即作废。
    /// 更新条件带「旧值未变」，并发重放时只有一个请求能成功（另一个拿到 0 行更新）。
    /// </summary>
    public async Task RotateRefreshAsync(string userType, int userId, string sessionToken, string oldJti, string newJti)
    {
        var key = KeyOf(userType, userId);
        var raw = await DbConcurrency.RetryTransientAsync(
            async () => await _db.SystemConfigs.AsNoTracking().Where(c => c.ConfigKey == key).Select(c => c.ConfigValue).FirstOrDefaultAsync(),
            _logger, "刷新令牌轮换");

        var entry = Deserialize(raw);
        if (entry is null
            || !string.Equals(entry.Token, sessionToken, StringComparison.Ordinal)
            || !string.Equals(entry.RefreshJti, oldJti, StringComparison.Ordinal))
        {
            throw new BusinessException("登录状态已失效，请重新登录");
        }

        var next = Serialize(entry with { RefreshJti = newJti });
        var affected = await _db.SystemConfigs
            .Where(c => c.ConfigKey == key && c.ConfigValue == raw)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfigValue, next).SetProperty(c => c.UpdateTime, AppClock.Now));
        _cache.Remove(CacheKeyOf(key));

        //0 行 = 另一并发请求已先一步轮换（重放），本次必须失败
        if (affected != 1) throw new BusinessException("登录状态已失效，请重新登录");
    }

    ///<summary>退出登录：只撤销当前这一个会话</summary>
    public async Task RevokeAsync(string userType, int userId, string? sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken)) return;
        var key = KeyOf(userType, userId);
        var entry = await LoadAsync(key);
        if (entry is null) return;
        if (!string.Equals(entry.Token, sessionToken, StringComparison.Ordinal)) return;
        //主动登出不做墓碑
        await _db.SystemConfigs.Where(c => c.ConfigKey == key && c.ConfigValue == Serialize(entry)).ExecuteDeleteAsync();
        _cache.Remove(CacheKeyOf(key));
    }

    ///<summary>强制该账号全部会话下线，并留下原因供中间件提示</summary>
    public async Task RevokeAllAsync(string userType, int userId, string reason) => await SaveAsync(KeyOf(userType, userId), new Payload(string.Empty, reason));

    //── 存取 ──

    private async Task<Payload?> LoadAsync(string key, bool useCache = true)
    {
        //只缓存「确实存在的会话」：不缓存 null，避免刚在另一实例登录后被本地空缓存挡住
        var cacheKey = CacheKeyOf(key);
        if (useCache && _cache.TryGetValue(cacheKey, out Payload? cached)) return cached;

        //单点登录校验是每个已认证请求都会走的读路径，且位于授权中间件里、没有任何try/catch 兜底
        var value = await DbConcurrency.RetryTransientAsync(
            async () => await _db.SystemConfigs.AsNoTracking().Where(c => c.ConfigKey == key).Select(c => c.ConfigValue).FirstOrDefaultAsync(),
            _logger, "会话校验");

        var payload = Deserialize(value);
        if (payload is not null) _cache.Set(cacheKey, payload, TimeSpan.FromSeconds(CacheSeconds));
        return payload;
    }

    ///<summary>写入（存在则改、不存在则插）</summary>
    private async Task SaveAsync(string key, Payload payload)
    {
        var json = Serialize(payload);
        var row = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (row is null)
        {
            _db.SystemConfigs.Add(SystemConfig.Create(key, json, "在线会话（系统维护，请勿手工修改）"));
            try
            {
                await _db.SaveChangesAsync();
                _cache.Remove(CacheKeyOf(key));
                return;
            }
            catch (DbUpdateException)
            {
                //并发下已被另一实例插入：脱离跟踪，改走更新分支
                _db.Entry(_db.SystemConfigs.Local.First(c => c.ConfigKey == key)).State = EntityState.Detached;
            }
        }

        await _db.SystemConfigs.Where(c => c.ConfigKey == key).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfigValue, json).SetProperty(c => c.UpdateTime, AppClock.Now));
        _cache.Remove(CacheKeyOf(key));
    }

    private static string Serialize(Payload p) => JsonSerializer.Serialize(p);

    private static Payload? Deserialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<Payload>(raw); }
        catch (JsonException) { return null; }   //被手工改坏的键按"无会话"处理，登录一次即自愈
    }
}
