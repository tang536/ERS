using System.Text.Json;
using ERental.Data;
using ERental.Services;

namespace ERental.Middleware;

/// <summary>
/// 认证与支付状态查询的跨实例固定窗口限流。
///
/// ASP.NET Core 内置 RateLimiter 是进程内计数器；多实例部署时，同一个 IP 可以在每台
/// 实例上各试 10 次。这里在使用内置限流器之前先做一次 Redis 计数：Redis 可用时由它
/// 提供全局上限，Redis 不可用时计数返回 null，继续走内置的本地策略兜底。
/// 只对最容易被打的两类路径计数，避免给每个业务请求增加一次 Redis 往返。
/// </summary>
public sealed class RedisRateLimitMiddleware(
    RequestDelegate next,
    RedisState redis,
    ILogger<RedisRateLimitMiddleware> logger)
{
    private const int AuthLimit = 10;
    private const int PayStatusLimit = 30;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(65);

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = ResolvePolicy(context.Request.Path);
        if (policy is null)
        {
            await next(context);
            return;
        }

        var ip = ClientIp.ResolveOrUnknown(context);
        var now = AppClock.Now;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Local);
        var key = $"rate-limit:{policy.Name}:{minute:yyyyMMddHHmm}:{ip}";

        var count = await redis.IncrementAsync(key, Window, context.RequestAborted);
        if (count is long current && current > policy.Limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = StatusCodes.Status429TooManyRequests,
                message = "操作过于频繁，请稍后再试",
                success = false
            }), context.RequestAborted);
            return;
        }

        if (count is null)
            logger.LogDebug("Redis 限流不可用，本次由内置本地限流策略兜底：{Policy}", policy.Name);

        await next(context);
    }

    private static RatePolicy? ResolvePolicy(PathString path)
    {
        if (path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
            return new RatePolicy("auth", AuthLimit);

        // 匿名支付状态查询：/api/payment/{orderNo}/status
        if (path.StartsWithSegments("/api/payment", StringComparison.OrdinalIgnoreCase)
            && path.Value?.EndsWith("/status", StringComparison.OrdinalIgnoreCase) == true)
            return new RatePolicy("pay-status", PayStatusLimit);

        return null;
    }

    private sealed record RatePolicy(string Name, int Limit);
}
