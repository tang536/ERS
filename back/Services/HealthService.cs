using System.Diagnostics;
using System.Globalization;
using ERental.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERental.Services;

/// <summary>单项健康检查结果</summary>
public sealed record HealthItem(string Name, string Status, string Detail);

/// <summary>
/// 健康检查报告。<see cref="Status"/> 取 Healthy / Degraded / Unhealthy：
///   Healthy   = 一切正常；
///   Degraded  = 服务可用但有需要人看一眼的问题（备份过期、回调缺失）；
///   Unhealthy = 核心依赖不可用（数据库连不上）。
/// </summary>
public sealed record HealthReport(string Status, DateTime CheckedAt, IReadOnlyList<HealthItem> Checks);

/// <summary>
/// /health 的检查逻辑：数据库、备份新鲜度、备份可恢复性、支付回调活跃度、支付渠道对账。
///
/// ⚠️ 这个接口匿名可读，**只回状态与一句话说明**：
/// 绝不带连接串、文件绝对路径、异常原文或任何密钥片段——健康检查不能变成信息泄露入口。
/// 具体异常只写服务端日志，并且同类告警每小时最多一次，避免监控高频轮询把日志刷爆。
///
/// 报告在进程内缓存 10 秒：监控通常每 15~30 秒轮询一次，而每份报告要打 4~5 次数据库；
/// 缓存把连续轮询合并掉，同时保留「最多晚 10 秒发现故障」的可接受时效。
/// </summary>
public sealed class HealthService(AppDbContext db, BackupService backups, PayConfigService payConfig, IConfiguration config, IMemoryCache cache, ILogger<HealthService> logger)
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";

    /// <summary>报告缓存键与有效时长</summary>
    private const string CacheKey = "erental:health:report";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    /// <summary>备份超过该小时数未更新即视为过期（可用 Backup:HealthMaxAgeHours 覆盖）</summary>
    private const int DefaultBackupMaxAgeHours = 48;

    /// <summary>备份超过该小时数未校验即视为过期（可用 Backup:VerifyMaxAgeHours 覆盖）</summary>
    private const int DefaultVerifyMaxAgeHours = 72;

    /// <summary>对账任务超过该小时数未执行即视为过期（可用 Payment:ReconcileMaxAgeHours 覆盖）</summary>
    private const int DefaultReconcileMaxAgeHours = 48;

    /// <summary>回调活跃度的统计窗口（小时）</summary>
    private const int CallbackWindowHours = 24;

    /// <summary>同类告警的最小间隔（分钟）</summary>
    private const int WarnIntervalMinutes = 60;

    private static long _lastWarnTicks;

    /// <summary>有外部流水可核的线上渠道</summary>
    private static readonly string[] ElectronicPayTypes = ["支付宝", "微信", "银行卡"];

    /// <summary>执行全部检查；任何一项都不抛异常，失败折成 Unhealthy 项</summary>
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out HealthReport? cached) && cached is not null)
            return cached;

        IReadOnlyList<HealthItem> checks =
        [
            await CheckDatabaseAsync(ct),
            CheckBackups(),
            CheckBackupVerify(),
            await CheckPayCallbacksAsync(ct),
            await CheckPayReconcileAsync(ct)
        ];

        var status = CombineStatus(checks);

        WarnIfNeeded(status, checks);
        var report = new HealthReport(status, AppClock.Now, checks);
        cache.Set(CacheKey, report, CacheTtl);
        return report;
    }

    /// <summary>汇总单项状态：任一项 Unhealthy → Unhealthy；否则任一项 Degraded → Degraded</summary>
    public static string CombineStatus(IReadOnlyCollection<HealthItem> checks) =>
        checks.Any(c => c.Status == Unhealthy) ? Unhealthy
        : checks.Any(c => c.Status == Degraded) ? Degraded
        : Healthy;

    private async Task<HealthItem> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var ok = await db.Database.CanConnectAsync(ct);
            sw.Stop();
            return ok
                ? new HealthItem("database", Healthy, $"连接正常（{sw.ElapsedMilliseconds} ms）")
                : new HealthItem("database", Unhealthy, "数据库连接不可用，请检查服务状态与连接配置");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "健康检查：数据库连接失败");
            return new HealthItem("database", Unhealthy, "数据库连接不可用，请检查服务状态与连接配置");
        }
    }

    /// <summary>备份新鲜度：没有备份或最近一份太旧都算降级，但不影响服务可用性</summary>
    private HealthItem CheckBackups()
    {
        try
        {
            var files = backups.ListFiles();
            if (files.Count == 0)
                return new HealthItem("backup", Degraded, "尚未生成任何备份，请检查自动备份配置与备份目录权限");

            var latest = files.Max(f => f.CreatedAt);
            var maxHours = BackupMaxAgeHours();
            var maxAge = TimeSpan.FromHours(maxHours);
            var age = AppClock.Now - latest;
            return age <= maxAge
                ? new HealthItem("backup", Healthy, $"最近备份 {latest:yyyy-MM-dd HH:mm}，共 {files.Count} 份")
                : new HealthItem("backup", Degraded, $"最近备份已超过 {maxHours} 小时（{latest:yyyy-MM-dd HH:mm}），请检查备份任务");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "健康检查：读取备份目录失败");
            return new HealthItem("backup", Unhealthy, "备份目录不可读，请检查目录权限");
        }
    }

    /// <summary>
    /// 回调活跃度：近 24 小时有线上到账却一条回调记录都没有，说明回调地址/验签很可能坏了。
    /// 模拟模式不产生真实回调，直接跳过判定，避免演示环境一直报降级。
    /// </summary>
    private async Task<HealthItem> CheckPayCallbacksAsync(CancellationToken ct)
    {
        try
        {
            var mode = (await payConfig.ResolveAsync()).Mode;
            if (mode is PayConfigService.ModeMock)
                return new HealthItem("pay-callback", Healthy, "当前为模拟支付模式，不校验真实渠道回调");

            var since = AppClock.Now.AddHours(-CallbackWindowHours);
            var callbacks = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS `Value` FROM `pay_notify_log` WHERE `created_at` >= {0}", since)
                .SingleAsync(ct);

            var electronic = await db.PaymentRecords.AsNoTracking().CountAsync(p =>
                PayStatuses.MoneyArrived.Contains(p.PayStatus)
                && p.PayTime.HasValue && p.PayTime >= since
                && ElectronicPayTypes.Contains(p.PayType), ct);

            if (electronic > 0 && callbacks == 0)
                return new HealthItem("pay-callback", Degraded, $"近 {CallbackWindowHours} 小时有 {electronic} 笔线上到账，但没有任何回调记录，请检查回调地址与验签配置");

            return new HealthItem("pay-callback", Healthy, $"近 {CallbackWindowHours} 小时回调 {callbacks} 条，线上到账 {electronic} 笔");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "健康检查：统计支付回调失败");
            return new HealthItem("pay-callback", Unhealthy, "回调统计失败，请检查数据库与支付配置");
        }
    }

    /// <summary>
    /// 备份可恢复性：只认后台校验任务写的快照。
    /// 备份文件存在不等于能恢复——密钥换了、文件写了一半都会留下一个「看起来正常」的文件，
    /// 所以这里检查的是最近一次「解密 + 解析 + 核对表/行数」的结果，而不是文件大小。
    /// </summary>
    private HealthItem CheckBackupVerify()
    {
        try
        {
            var state = backups.ReadVerifyState();
            if (state is null)
                return new HealthItem("backup-verify", Degraded, "尚未完成备份可恢复性校验，请确认后台校验任务已启用");

            if (!state.Ok)
                return new HealthItem("backup-verify", Degraded,
                    $"最近一次备份校验失败（{state.Error ?? "未知原因"}），请检查备份文件与密钥是否匹配");

            var maxHours = ConfigHours("Backup:VerifyMaxAgeHours", DefaultVerifyMaxAgeHours);
            var age = AppClock.Now - state.VerifiedAt;
            return age <= TimeSpan.FromHours(maxHours)
                ? new HealthItem("backup-verify", Healthy,
                    $"最近校验 {state.FileName}（{state.TableCount} 张表 / {state.RowCount} 行，{state.VerifiedAt:yyyy-MM-dd HH:mm}）")
                : new HealthItem("backup-verify", Degraded,
                    $"备份已 {age.TotalHours:0} 小时未校验（{state.VerifiedAt:yyyy-MM-dd HH:mm}），请检查校验任务");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "健康检查：读取备份校验快照失败");
            return new HealthItem("backup-verify", Degraded, "备份校验快照不可读，请检查文件权限");
        }
    }

    /// <summary>
    /// 支付渠道对账活跃度：真实模式下超过阈值没有「巡检」行，说明对账任务没跑起来。
    /// 模拟模式没有真实交易可对，直接跳过，避免演示环境一直报降级。
    /// </summary>
    private async Task<HealthItem> CheckPayReconcileAsync(CancellationToken ct)
    {
        try
        {
            var mode = (await payConfig.ResolveAsync()).Mode;
            if (mode is PayConfigService.ModeMock)
                return new HealthItem("pay-reconcile", Healthy, "当前为模拟支付模式，不校验真实渠道对账");

            var last = await db.PayReconcileLogs.AsNoTracking()
                .Where(l => l.Action == PayReconcileActions.Run)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => (DateTime?)l.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (last is null)
                return new HealthItem("pay-reconcile", Degraded, "尚未执行支付渠道对账，请检查对账任务配置");

            var maxHours = ConfigHours("Payment:ReconcileMaxAgeHours", DefaultReconcileMaxAgeHours);
            var age = AppClock.Now - last.Value;
            if (age > TimeSpan.FromHours(maxHours))
                return new HealthItem("pay-reconcile", Degraded,
                    $"最近对账已超过 {maxHours} 小时（{last.Value:yyyy-MM-dd HH:mm}），请检查对账任务");

            var differences = await db.PayReconcileLogs.AsNoTracking()
                .CountAsync(l => l.Action != PayReconcileActions.Run && l.CreatedAt >= AppClock.Now.AddDays(-7), ct);
            return new HealthItem("pay-reconcile", Healthy,
                $"最近对账 {last.Value:yyyy-MM-dd HH:mm}，近 7 天差异 {differences} 条");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "健康检查：统计支付渠道对账失败");
            return new HealthItem("pay-reconcile", Unhealthy, "支付对账统计失败，请检查数据库与对账配置");
        }
    }

    /// <summary>备份过期阈值（小时）：配置非法时回退默认值</summary>
    private int BackupMaxAgeHours() => ConfigHours("Backup:HealthMaxAgeHours", DefaultBackupMaxAgeHours);

    /// <summary>读取正整数小时配置：缺失、非数字或非正数都回退默认值</summary>
    private int ConfigHours(string key, int fallback) =>
        int.TryParse(config[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? hours
            : fallback;

    /// <summary>日志里的告警节流：监控每 30 秒轮询一次，同一类问题每小时记一次就够</summary>
    private void WarnIfNeeded(string status, IReadOnlyList<HealthItem> checks)
    {
        if (status == Healthy) return;

        var now = AppClock.Now.Ticks;
        var last = Interlocked.Read(ref _lastWarnTicks);
        if (now - last < TimeSpan.FromMinutes(WarnIntervalMinutes).Ticks) return;
        if (Interlocked.CompareExchange(ref _lastWarnTicks, now, last) != last) return;

        var problems = string.Join("；", checks
            .Where(c => c.Status != Healthy)
            .Select(c => $"{c.Name}={c.Detail}"));
        logger.LogWarning("健康检查异常（{Status}）：{Problems}", status, problems);
    }
}
