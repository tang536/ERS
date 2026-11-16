using System.Diagnostics;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

/// <summary>对账动作取值：巡检行只用于健康检查判断「任务有没有跑」，列表默认不展示</summary>
internal static class PayReconcileActions
{
    public const string Run = "巡检";
    public const string Repaired = "已补账";
    public const string ManualCheck = "待人工核对";
}

///<summary>一次渠道对账的汇总（管理员手动触发时直接回显）</summary>
public sealed record PaymentReconcileSummary(DateTime StartedAt, int Scanned, int Repaired, int Differences, long ElapsedMs, string Message);

///<summary>对账差异行的响应投影（实体不出服务层）</summary>
public sealed record PayReconcileLogDto(long Id, string Channel, string OrderNo, int? PayId, string LocalStatus,
    string? ChannelStatus, decimal LocalAmount, decimal? ChannelAmount, string Action, string Detail, DateTime CreatedAt);

/// <summary>
/// 支付渠道对账：扫描电子渠道的待支付/已到账记录，调用各渠道查单接口，
/// 把「渠道侧已成功、本地还没入账」的单子补上，并把差异落进 pay_reconcile_log。
///
/// 为什么复用各渠道的 <c>QueryTradeStatusAsync</c>：查单成功后要走的金额校验、
/// 订单状态推进、补缴单入账都在同一条既有链路上；对账再写一套入账逻辑，
/// 只会多出一份可能和回调口径不一致的实现。这里只负责「扫哪些单、补完记什么」。
/// </summary>
public sealed class PaymentReconcileService(
    AppDbContext db,
    AlipayService alipay,
    WechatPayService wechat,
    BankPayService bank,
    PayConfigService payConfig,
    ILogger<PaymentReconcileService> logger)
{
    /// <summary>单次最多扫描的记录数：对账是兜底任务，不应在凌晨把渠道接口打成压测</summary>
    private const int MaxScan = 200;

    /// <summary>待支付记录只回看 7 天：更早的单子要么已超时回收，要么早已人工处理</summary>
    private const int PendingDays = 7;

    /// <summary>已到账记录回看 30 天：用于发现「到账但缺渠道交易号」的脏数据</summary>
    private const int PaidDays = 30;

    private static readonly string[] ElectronicPayTypes = ["支付宝", "微信", "银行卡"];

    private readonly AppDbContext _db = db;
    private readonly AlipayService _alipay = alipay;
    private readonly WechatPayService _wechat = wechat;
    private readonly BankPayService _bank = bank;
    private readonly PayConfigService _payConfig = payConfig;
    private readonly ILogger<PaymentReconcileService> _logger = logger;

    /// <summary>
    /// 执行对账。<paramref name="orderNo"/> 为空时按时间窗批量扫描；
    /// 指定单号时忽略时间窗，只查这一单（管理员排障用）。
    /// </summary>
    public async Task<PaymentReconcileSummary> RunAsync(string? orderNo = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = AppClock.Now;

        // 模拟模式的本地状态就是真相，向渠道查单没有意义，直接跳过
        var mode = (await _payConfig.ResolveAsync()).Mode;
        if (mode is PayConfigService.ModeMock)
            return new PaymentReconcileSummary(startedAt, 0, 0, 0, 0, "当前为模拟支付模式，未执行真实渠道对账");

        var rows = await LoadCandidatesAsync(orderNo, ct);
        var repaired = 0;
        var differences = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var channel = row.PayType;

            // 已到账：不再向渠道重复查单，只检查本地是否留下可追溯的渠道流水号
            if (PayStatuses.IsMoneyArrived(row.PayStatus))
            {
                if (!string.IsNullOrWhiteSpace(row.TransactionNo)) continue;

                differences++;
                await AddAsync(PayReconcileLog.Create(channel, row.OrderNo, row.PayId, row.PayStatus, null,
                    row.PayAmount, null, PayReconcileActions.ManualCheck, "已到账但缺少渠道交易号，无法与通道流水核对"), ct);
                continue;
            }

            // 已取消等终态不参与补账
            if (row.PayStatus != "待支付") continue;

            var merchantNo = row.BizType == "supplement" ? row.OutTradeNo : row.OrderNo;
            if (string.IsNullOrWhiteSpace(merchantNo))
            {
                differences++;
                await AddAsync(PayReconcileLog.Create(channel, row.OrderNo, row.PayId, row.PayStatus, null,
                    row.PayAmount, null, PayReconcileActions.ManualCheck, "缺少通道商户订单号，无法查单"), ct);
                continue;
            }

            try
            {
                await QueryChannelAsync(channel, merchantNo, ct);
            }
            catch (Exception ex)
            {
                differences++;
                await AddAsync(PayReconcileLog.Create(channel, merchantNo, row.PayId, row.PayStatus, null,
                    row.PayAmount, null, PayReconcileActions.ManualCheck, $"渠道查单失败（{ex.GetType().Name}）"), ct);
                continue;
            }

            // 查单入库后重新读取：只有状态真的推进了，才记一条「已补账」
            var current = await _db.PaymentRecords.AsNoTracking().FirstOrDefaultAsync(p => p.PayId == row.PayId, ct);
            if (current is null || !PayStatuses.IsMoneyArrived(current.PayStatus)) continue;

            repaired++;
            await AddAsync(PayReconcileLog.Create(channel, merchantNo, row.PayId, row.PayStatus, current.PayStatus,
                row.PayAmount, current.PayAmount, PayReconcileActions.Repaired, $"渠道查单确认已到账：{row.PayStatus} → {current.PayStatus}"), ct);
        }

        var message = $"扫描 {rows.Count} 笔，补账 {repaired} 笔，差异 {differences} 条";

        // 巡检行是「任务跑过」的心跳：没有差异时也要留痕，否则 /health 无法区分「没差异」和「没执行」
        await AddAsync(PayReconcileLog.Create("系统", null, null, null, null, 0, null, PayReconcileActions.Run, message), ct);

        sw.Stop();
        _logger.LogInformation("支付渠道对账完成：{Message}，耗时 {Elapsed} ms", message, sw.ElapsedMilliseconds);
        return new PaymentReconcileSummary(startedAt, rows.Count, repaired, differences, sw.ElapsedMilliseconds, message);
    }

    /// <summary>对账差异列表（不含巡检心跳行），供管理员端按时间窗查看</summary>
    public async Task<PagedResult<PayReconcileLogDto>> ListAsync(int days = 30, int page = 1, int size = 50)
    {
        var (p, s, skip, take) = Paging.Normalize(page, size > 0 ? size : 50);
        var since = AppClock.Now.AddDays(-Math.Clamp(days, 1, 365));

        var q = _db.PayReconcileLogs.AsNoTracking()
            .Where(l => l.CreatedAt >= since && l.Action != PayReconcileActions.Run);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip(skip).Take(take)
            .Select(l => new PayReconcileLogDto(l.Id, l.Channel, l.OrderNo, l.PayId, l.LocalStatus,
                l.ChannelStatus, l.LocalAmount, l.ChannelAmount, l.Action, l.Detail, l.CreatedAt))
            .ToListAsync();

        return new PagedResult<PayReconcileLogDto>(items, total, p, s);
    }

    /// <summary>按时间窗装载候选（或按单号精确定位），只取对账需要的列</summary>
    private async Task<List<ReconcileRow>> LoadCandidatesAsync(string? orderNo, CancellationToken ct)
    {
        var q = _db.PaymentRecords.AsNoTracking()
            .Where(p => p.PayType != null && ElectronicPayTypes.Contains(p.PayType));

        if (!string.IsNullOrWhiteSpace(orderNo))
        {
            var no = orderNo.Trim();
            q = q.Where(p => p.OutTradeNo == no || p.RentalOrder!.OrderNo == no);
        }
        else
        {
            var pendingSince = AppClock.Now.AddDays(-PendingDays);
            var paidSince = AppClock.Now.AddDays(-PaidDays);
            q = q.Where(p => (p.PayStatus == "待支付" && p.RentalOrder!.CreateTime >= pendingSince)
                || (PayStatuses.MoneyArrived.Contains(p.PayStatus) && p.PayTime >= paidSince));
        }

        var rows = await q.OrderByDescending(p => p.PayId).Take(MaxScan)
            .Select(p => new
            {
                p.PayId,
                p.OutTradeNo,
                p.BizType,
                PayType = p.PayType ?? string.Empty,
                p.PayStatus,
                p.PayAmount,
                p.TransactionNo,
                p.RentalOrder!.OrderNo
            })
            .ToListAsync(ct);

        return rows.Select(r => new ReconcileRow(r.PayId, r.OutTradeNo, r.BizType, r.PayType,
            r.PayStatus, r.PayAmount, r.TransactionNo, r.OrderNo)).ToList();
    }

    ///<summary>把查单交给对应渠道：三个通道的查单成功都会走同一条入账链路</summary>
    private async Task QueryChannelAsync(string channel, string merchantNo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (channel)
        {
            case "支付宝": await _alipay.QueryTradeStatusAsync(merchantNo); break;
            case "微信": await _wechat.QueryTradeStatusAsync(merchantNo); break;
            case "银行卡": await _bank.QueryTradeStatusAsync(merchantNo); break;
            default: throw new BusinessException($"未知支付渠道：{channel}");
        }
    }

    private async Task AddAsync(PayReconcileLog log, CancellationToken ct)
    {
        _db.PayReconcileLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    ///<summary>候选行（匿名投影在内存里落成元组，避免把实体带出查询）</summary>
    private sealed record ReconcileRow(int PayId, string? OutTradeNo, string BizType, string PayType,
        string PayStatus, decimal PayAmount, string? TransactionNo, string OrderNo);
}

/// <summary>
/// 每日支付渠道对账：默认 Payment:ReconcileAt（03:15）执行，多实例靠 MySQL 命名锁选主。
/// </summary>
public class PaymentReconcileSchedulerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<PaymentReconcileSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动 3 分钟后先补一次：部署或重启当天不必等到次日凌晨才有对账记录
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = false;
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                failed = true;
                logger.LogError(ex, "支付渠道对账失败，{Minutes} 分钟后重试", RetryDelay.TotalMinutes);
            }

            var wait = failed
                ? RetryDelay
                : DailySchedule.NextRun(AppClock.Now, config["Payment:ReconcileAt"], new TimeOnly(3, 15)) - AppClock.Now;

            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var locks = scope.ServiceProvider.GetRequiredService<DbLockService>();
        await using var handle = await locks.TryAcquireAsync("erental_pay_reconcile", TimeSpan.Zero, ct);
        if (handle is null)
        {
            logger.LogInformation("支付渠道对账已由其他实例执行，本实例跳过");
            return;
        }

        var service = scope.ServiceProvider.GetRequiredService<PaymentReconcileService>();
        var result = await service.RunAsync(ct: ct);
        logger.LogInformation("支付渠道对账任务：{Message}（耗时 {Elapsed} ms）", result.Message, result.ElapsedMs);
    }
}
