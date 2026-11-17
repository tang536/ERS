using ERental.Data;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

/// <summary>
/// 租期提醒后台服务：每 10 分钟扫描一次「已逾期 / 24 小时内到期」的活跃订单，
/// 写进管理员与前台的站内消息队列。消息按订单号做幂等（uk_msg_biz），
/// 重复扫描只刷新内容，不重复堆积；已读状态保持不变，避免每 10 分钟把消息重新标未读。
/// </summary>
public class OrderReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderReminderBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private const int WarnHours = 24;
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(ScanInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                //后台任务不能因为一次数据库抖动把整个进程带崩，下一轮继续
                logger.LogWarning(ex, "租期提醒扫描失败，将在下一轮重试");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messages = scope.ServiceProvider.GetRequiredService<MessageService>();

        var now = AppClock.Now;
        var soon = now.AddHours(WarnHours);

        var staffIds = await db.SystemUsers.AsNoTracking()
            .Where(u => u.UserStatus != 0 && u.Role != null
                && (u.Role.RoleName == "管理员" || u.Role.RoleName == "前台"))
            .Select(u => u.UserId)
            .ToListAsync(ct);
        if (staffIds.Count == 0) return;

        // 按到期时间分页扫描全部匹配订单。不能只 Take(50)：逾期订单会一直留在结果集里，
        // 每轮都占满前 50 个名额，后面的订单永远收不到提醒。
        var offset = 0;
        var processed = 0;
        while (true)
        {
            var warnings = await db.OrderOverviews.AsNoTracking()
                .Where(o => (o.OrderStatus == "租赁中" || o.OrderStatus == "已支付")
                    && o.LeaseEnd <= soon)
                .OrderBy(o => o.LeaseEnd)
                .Skip(offset)
                .Take(BatchSize)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNo,
                    o.CustomerName,
                    o.VehicleNo,
                    o.LeaseEnd,
                })
                .ToListAsync(ct);

            if (warnings.Count == 0) break;

            foreach (var warning in warnings)
            {
                var overdue = warning.LeaseEnd < now;
                var span = overdue ? now - warning.LeaseEnd : warning.LeaseEnd - now;
                var title = overdue
                    ? $"订单 {warning.OrderNo} 已逾期 {DescribeSpan(span)}"
                    : $"订单 {warning.OrderNo} 将在 {DescribeSpan(span)} 内到期";
                var content = $"客户：{warning.CustomerName ?? "未知"}　车辆：{warning.VehicleNo ?? "未知"}　到期：{warning.LeaseEnd:MM-dd HH:mm}，请及时联系顾客确认归还或续租。";
                var link = $"/orders?orderNo={Uri.EscapeDataString(warning.OrderNo)}";

                foreach (var staffId in staffIds)
                {
                    await messages.SendAsync(
                        ("staff", staffId),
                        (overdue ? "overdue" : "expiring", warning.OrderId.ToString()),
                        (title, content),
                        (overdue ? "danger" : "warning", link, true));
                }
            }

            processed += warnings.Count;
            offset += warnings.Count;
            if (warnings.Count < BatchSize) break;
        }

        logger.LogInformation("租期提醒：本轮处理 {Count} 单，覆盖 {StaffCount} 名员工", processed, staffIds.Count);
    }

    private static string DescribeSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} 天 {span.Hours} 小时";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} 小时";
        return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} 分钟";
    }
}
