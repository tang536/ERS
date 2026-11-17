using System.Text.Json;
using ERental.Data;
using ERental.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERental.Services;

///<summary>在线会话登记表（独立 session_state 表，不再占用 system_config）</summary>
public sealed class SessionRegistry(AppDbContext db, IMemoryCache cache, RedisState redis, ILogger<SessionRegistry> logger)
{
    ///<summary>会话键前缀</summary>
    public const string Prefix = "sess.";

    ///<summary>
    /// 会话校验的本地短缓存（秒）。单点校验是每个已认证请求的必经读路径，
    /// 不加缓存等于每个请求多打一次 session_state。取 5 秒：多实例部署时
    /// 「另一台实例强制下线」最多延迟这么久生效，既挡住高频查询又不会让用户等太久。
    /// </summary>
    private const int CacheSeconds = 5;

    private readonly AppDbContext _db = db;
    private readonly IMemoryCache _cache = cache;
    private readonly RedisState _redis = redis;
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
        var affected = await _db.Sessions
            .Where(s => s.SessionKey == key
                && s.SessionToken == sessionToken
                && s.RefreshJti == oldJti)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RefreshJti, newJti)
                .SetProperty(x => x.UpdateTime, AppClock.Now));

        if (affected != 1) throw new BusinessException("登录状态已失效，请重新登录");

        _cache.Remove(CacheKeyOf(key));
        await _redis.RemoveAsync(CacheKeyOf(key));
    }

    ///<summary>退出登录：只撤销当前这一个会话</summary>
    public async Task RevokeAsync(string userType, int userId, string? sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken)) return;
        var key = KeyOf(userType, userId);
        var affected = await _db.Sessions
            .Where(s => s.SessionKey == key && s.SessionToken == sessionToken)
            .ExecuteDeleteAsync();

        if (affected == 0) return;
        _cache.Remove(CacheKeyOf(key));
        await _redis.RemoveAsync(CacheKeyOf(key));
    }

    ///<summary>强制该账号全部会话下线，并留下原因供中间件提示</summary>
    public async Task RevokeAllAsync(string userType, int userId, string reason) =>
        await SaveAsync(KeyOf(userType, userId), new Payload(string.Empty, reason));

    //── 存取 ──

    private async Task<Payload?> LoadAsync(string key, bool useCache = true)
    {
        //只缓存「确实存在的会话」：不缓存 null，避免刚在另一实例登录后被本地空缓存挡住
        var cacheKey = CacheKeyOf(key);
        if (useCache && _cache.TryGetValue(cacheKey, out Payload? cached)) return cached;

        // 跨实例短缓存：一台实例写入后，其他实例可在 TTL 内直接命中，避免每个已认证请求都查库。
        // 续期校验必须读库（useCache=false），因此这里只在普通会话校验时启用。
        if (useCache)
        {
            var redisValue = await _redis.GetStringAsync(cacheKey);
            var redisPayload = Deserialize(redisValue);
            if (redisPayload is not null)
            {
                _cache.Set(cacheKey, redisPayload, TimeSpan.FromSeconds(CacheSeconds));
                return redisPayload;
            }
        }

        var payload = await DbConcurrency.RetryTransientAsync(
            async () => await _db.Sessions.AsNoTracking()
                .Where(s => s.SessionKey == key)
                .Select(s => new Payload(s.SessionToken ?? string.Empty, s.RevokedReason, s.RefreshJti))
                .FirstOrDefaultAsync(),
            _logger, "会话校验");

        if (payload is not null)
        {
            _cache.Set(cacheKey, payload, TimeSpan.FromSeconds(CacheSeconds));
            await _redis.SetStringAsync(cacheKey, Serialize(payload), TimeSpan.FromSeconds(CacheSeconds));
        }
        return payload;
    }

    ///<summary>写入（存在则改、不存在则插）</summary>
    private async Task SaveAsync(string key, Payload payload)
    {
        var row = await _db.Sessions.FirstOrDefaultAsync(s => s.SessionKey == key);
        if (row is null)
        {
            row = SessionState.Create(key, payload.Token, payload.RefreshJti, payload.Revoked);
            _db.Sessions.Add(row);
            try
            {
                await _db.SaveChangesAsync();
                await InvalidateAsync(key);
                return;
            }
            catch (DbUpdateException)
            {
                //并发下已被另一实例插入：脱离跟踪，改走条件更新分支
                _db.Entry(row).State = EntityState.Detached;
            }
        }

        await _db.Sessions
            .Where(s => s.SessionKey == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SessionToken, payload.Token)
                .SetProperty(x => x.RefreshJti, payload.RefreshJti)
                .SetProperty(x => x.RevokedReason, payload.Revoked)
                .SetProperty(x => x.UpdateTime, AppClock.Now));
        await InvalidateAsync(key);
    }

    private async Task InvalidateAsync(string key)
    {
        _cache.Remove(CacheKeyOf(key));
        await _redis.RemoveAsync(CacheKeyOf(key));
    }

    private static string Serialize(Payload p) => JsonSerializer.Serialize(p);

    private static Payload? Deserialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<Payload>(raw); }
        catch (JsonException) { return null; }   //被手工改坏的键按"无会话"处理，登录一次即自愈
    }
}
