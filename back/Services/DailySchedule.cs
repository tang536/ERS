using System.Globalization;

namespace ERental.Services;

/// <summary>
/// 每日定时任务的下一次执行时刻。后台任务（备份、备份校验、日志归档、支付对账）
/// 的调度方式完全一致，集中在这里，避免每个 Scheduler 各抄一份 HH:mm 解析。
/// </summary>
internal static class DailySchedule
{
    /// <summary>按配置的 HH:mm 计算下一次执行时刻；配置非法时回退到 <paramref name="fallback"/></summary>
    public static DateTime NextRun(DateTime now, string? raw, TimeOnly fallback)
    {
        var at = TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        var today = now.Date.Add(at.ToTimeSpan());
        return today > now ? today : today.AddDays(1);
    }
}
