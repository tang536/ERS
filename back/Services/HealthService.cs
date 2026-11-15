using System.Diagnostics;
using System.Globalization;
using ERental.Data;
using Microsoft.EntityFrameworkCore;

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
/// /health 的检查逻辑：数据库、备份新鲜度、支付回调活跃度。
///
/// ⚠️ 这个接口匿名可读，**只回状态与一句话说明**：
/// 绝不带连接串、文件绝对路径、异常原文或任何密钥片段——健康检查不能变成信息泄露入口。
/// 具体异常只写服务端日志，并且同类告警每小时最多一次，避免监控高频轮询把日志刷爆。
/// </summary>
public sealed class HealthService(AppDbContext db, BackupService backups, PayConfigService payConfig, IConfiguration config, ILogger<HealthService> logger)
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";

    /// <summary>备份超过该小时数未更新即视为过期（可用 Backup:HealthMaxAgeHours 覆盖）</summary>
    private const int DefaultBackupMaxAgeHours = 48;

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
        IReadOnlyList<HealthItem> checks = [await CheckDatabaseAsync(ct), CheckBackups(), await CheckPayCallbacksAsync(ct)];

        var status = CombineStatus(checks);

        WarnIfNeeded(status, checks);
        return new HealthReport(status, AppClock.Now, checks);
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

    /// <summary>备份过期阈值（小时）：配置非法时回退默认值</summary>
    private int BackupMaxAgeHours() =>
        int.TryParse(config["Backup:HealthMaxAgeHours"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? hours
            : DefaultBackupMaxAgeHours;

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
