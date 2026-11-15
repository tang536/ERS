using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ERental.Data;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace ERental.Services;

/// <summary>
/// 支付通知入账前的公共防护：严格金额解析 + **数据库支撑**的重放去重。
///
/// 旧实现用进程内的 ConcurrentDictionary，只对单实例有效；多实例部署时同一份回调
/// 打到不同实例会各放行一次，去重形同虚设，进程重启也会丢历史。这里改成
/// <c>pay_notify_log.replay_key</c> 唯一键：原子插入，全实例共享同一份判定。
/// 只保留 5 分钟窗口（与旧行为一致）：窗口过后网关重推同一报文仍可再处理，
/// 真正的幂等由入账侧的订单/支付状态判断兜底。
/// 记录本身保留 7 天：过期只是"可以重新登记"，行还留着供 /health 统计回调量与事后排障。
/// </summary>
public sealed class PayNotifyDeduplicator(AppDbContext db, ILogger<PayNotifyDeduplicator> logger)
{
    /// <summary>去重窗口：与旧实现保持一致</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>回调记录保留期：窗口结束后再留 7 天，供健康检查与排障</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>过期记录清理间隔</summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    /// <summary>最后清理时间（静态：服务是 scoped，节流要跨请求共享）</summary>
    private static long _lastCleanupTicks = AppClock.Now.Ticks;

    /// <summary>严格解析金额：空串/空白/非法值一律失败</summary>
    public static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0m;
        return !string.IsNullOrWhiteSpace(text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>把「通道+关键字段」拼成稳定键（SHA-256 十六进制，定长 64）</summary>
    public static string BuildKey(string channel, params string?[] parts)
    {
        var raw = channel + "\u001f" + string.Join("\u001f", parts.Select(p => p ?? ""));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>
    /// 登记一份回调报文：窗口内首次出现返回 true（放行），重复出现返回 false（应忽略）。
    /// </summary>
    public async Task<bool> TryRegisterAsync(string channel, string? orderNo, string replayKey, CancellationToken ct = default)
    {
        var now = AppClock.Now;
        var expires = now.Add(Window);

        //① 过期记录允许重新登记：网关在窗口之后重推同一报文时，应能再次进入处理（入账侧幂等）
        object[] refreshArgs = [replayKey, now, now, expires];
        var refreshed = await db.Database.ExecuteSqlRawAsync("""
            UPDATE `pay_notify_log` SET `created_at` = {2}, `expires_at` = {3}
            WHERE `replay_key` = {0} AND `expires_at` < {1}
            """, refreshArgs, ct);
        if (refreshed > 0)
        {
            await MaybeCleanupAsync(now, ct);
            return true;
        }

        //② 新报文：唯一键冲突 = 窗口内已经处理过，直接判重复
        try
        {
            //order_no 允许为空。EF 的 ExecuteSqlRaw 无法从 DBNull 推断存储类型，
            //这里显式给出 VarChar，避免「没有单号」的回调直接报类型映射错误。
            var orderNoParam = new MySqlParameter("@orderNo", MySqlDbType.VarChar) { Value = (object?)orderNo ?? DBNull.Value };
            object[] insertArgs =
            [
                new MySqlParameter("@replayKey", replayKey),
                new MySqlParameter("@channel", channel),
                orderNoParam,
                new MySqlParameter("@createdAt", now),
                new MySqlParameter("@expiresAt", expires)
            ];
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO `pay_notify_log` (`replay_key`, `channel`, `order_no`, `created_at`, `expires_at`)
                VALUES (@replayKey, @channel, @orderNo, @createdAt, @expiresAt)
                """, insertArgs, ct);
            await MaybeCleanupAsync(now, ct);
            return true;
        }
        catch (Exception ex) when (ex.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("支付回调重复投递已忽略（通道 {Channel}，订单 {OrderNo}）", channel, orderNo ?? "(无)");
            return false;
        }
    }

    /// <summary>按固定间隔清理过期记录；清理失败不影响本次回调处理</summary>
    private async Task MaybeCleanupAsync(DateTime now, CancellationToken ct)
    {
        var last = Interlocked.Read(ref _lastCleanupTicks);
        if (now.Ticks - last < CleanupInterval.Ticks) return;
        //只让一个请求执行清理
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, last) != last) return;

        try
        {
            //注意这里按「保留期」删，而不是 expires_at < now：
            //过期只代表可以重新登记，记录要继续留着给健康检查统计回调量。
            object[] cleanupArgs = [now - Retention];
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM `pay_notify_log` WHERE `expires_at` < {0} LIMIT 1000", cleanupArgs, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清理支付回调去重记录失败（不影响本次回调处理）");
        }
    }
}
