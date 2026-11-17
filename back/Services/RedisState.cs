using StackExchange.Redis;

namespace ERental.Services;

/// <summary>
/// Redis 分布式状态的最小封装。
/// 设计原则：
///   ① Redis 是**可选增强**，不是启动硬依赖——没配连接串、连接失败或运行中掉线时，
///      所有方法都返回 null/false，调用方自动回落到数据库或内存实现；
///   ② 只暴露字符串读写、计数和短期锁这类真正跨实例的能力，不把 Redis 概念泄漏到业务层；
///   ③ 所有键统一加 <c>erental:</c> 前缀，避免与同一台 Redis 上的其他应用撞键。
/// 当前用途：会话校验的跨实例短缓存（见 <see cref="SessionRegistry"/>）。
/// </summary>
public sealed class RedisState : IDisposable
{
    private const string KeyPrefix = "erental:";
    private readonly ConnectionMultiplexer? _connection;
    private readonly ILogger<RedisState> _logger;

    public RedisState(IConfiguration configuration, ILogger<RedisState> logger)
    {
        _logger = logger;

        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration["Redis:Connection"];

        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            // 启动阶段 Redis 不在线不应拖垮整个门店系统：连接失败后由调用方回落。
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 2000;
            options.AsyncTimeout = 2000;
            _connection = ConnectionMultiplexer.Connect(options);
            _logger.LogInformation("Redis 已接入：{Endpoint}", _connection.GetEndPoints().FirstOrDefault());
        }
        catch (Exception ex)
        {
            // 这里只记异常类型与消息，不打印连接串（可能含密码）。
            _logger.LogWarning(ex, "Redis 连接失败，分布式状态将回落到数据库/进程内实现（{ExType}）", ex.GetType().Name);
        }
    }

    /// <summary>当前是否真的可用：连接对象存在且至少一个端点在线。</summary>
    public bool Enabled => _connection is { IsConnected: true };

    /// <summary>读取字符串；Redis 不可用或键不存在时返回 null。</summary>
    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_connection is not { IsConnected: true }) return null;

        try
        {
            var db = _connection.GetDatabase();
            var value = await db.StringGetAsync(Prefixed(key));
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis 读取失败（{Key}），回落（{ExType}）", key, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>写入字符串并设置 TTL；Redis 不可用时静默跳过。</summary>
    public async Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_connection is not { IsConnected: true }) return;

        try
        {
            var db = _connection.GetDatabase();
            await db.StringSetAsync(Prefixed(key), value, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis 写入失败（{Key}），忽略（{ExType}）", key, ex.GetType().Name);
        }
    }

    /// <summary>删除键；Redis 不可用时静默跳过。</summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_connection is not { IsConnected: true }) return;

        try
        {
            await _connection.GetDatabase().KeyDeleteAsync(Prefixed(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis 删除失败（{Key}），忽略（{ExType}）", key, ex.GetType().Name);
        }
    }

    /// <summary>
    /// 固定窗口计数：用于跨实例的登录失败/接口频率统计。
    /// 第一次 INCR 后设置 TTL；Redis 不可用时返回 null，让调用方走进程内兜底。
    /// </summary>
    public async Task<long?> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_connection is not { IsConnected: true }) return null;

        try
        {
            var db = _connection.GetDatabase();
            var prefixed = Prefixed(key);
            var count = await db.StringIncrementAsync(prefixed);
            if (count == 1) await db.KeyExpireAsync(prefixed, window);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis 计数失败（{Key}），回落（{ExType}）", key, ex.GetType().Name);
            return null;
        }
    }

    private static string Prefixed(string key) => key.StartsWith(KeyPrefix, StringComparison.Ordinal) ? key : KeyPrefix + key;

    public void Dispose() => _connection?.Dispose();
}
