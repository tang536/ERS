using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ERental.Data;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

/// <summary>日志归档结果：FileName 为空表示没有到期记录</summary>
public sealed record LogArchiveResult(string FileName, int Archived, int Deleted, long SizeBytes);

/// <summary>
/// 操作日志的归档与清理。
///
/// 需求 7.1 要求「数据不可删除、全流程可追溯」，但 system_log 只增不删会无限膨胀：
/// 列表变慢、备份变大，最后连排障都要翻几十万行。这里采用「先归档、后删除」：
///   ① 按保留期把到期日志流式写入 JSONL（一行一条，便于 grep/导入）；
///   ② 归档文件成功落盘后才删除库内行，写文件失败则一行都不删；
///   ③ 用 log_id 上界把「本次归档的行」和「归档期间新写入的行」分开，不会误删。
/// </summary>
public sealed class LogArchiveService(AppDbContext db, IConfiguration config, IHostEnvironment env, ILogger<LogArchiveService> logger)
{
    private const int DefaultRetentionDays = 365;
    private const int MinRetentionDays = 30;
    private const int MaxRetentionDays = 3650;
    private const int BatchSize = 1000;

    private readonly AppDbContext _db = db;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 归档文件是给人 grep 的：中文按原文写出，不转成 \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    /// <summary>归档目录：默认程序目录 App_Data/log-archive，可用 Log:ArchiveDirectory 改到独立磁盘</summary>
    public string ArchiveDirectory
    {
        get
        {
            var configured = config["Log:ArchiveDirectory"];
            var dir = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(env.ContentRootPath, "App_Data", "log-archive")
                : configured.Trim();
            return Path.GetFullPath(dir);
        }
    }

    /// <summary>保留天数：配置非法时回退 365 天</summary>
    public int RetentionDays => ClampRetention(int.TryParse(config["Log:RetentionDays"], out var days) ? days : DefaultRetentionDays);

    /// <summary>归档并删除到期日志；<paramref name="retentionDays"/> 仅供管理员手动触发时覆盖配置</summary>
    public async Task<LogArchiveResult> ArchiveAsync(int? retentionDays = null, CancellationToken ct = default)
    {
        var days = ClampRetention(retentionDays ?? RetentionDays);
        var cutoff = AppClock.Now.AddDays(-days);

        // 先记下本次要归档的最大 log_id：归档期间新写入的日志 id 更大，不会被下面的 DELETE 带走
        var maxId = await _db.SystemLogs.Where(l => l.OperateTime < cutoff).Select(l => (int?)l.LogId).MaxAsync(ct) ?? 0;
        var total = await _db.SystemLogs.CountAsync(l => l.LogId <= maxId && l.OperateTime < cutoff, ct);
        if (total == 0) return new LogArchiveResult("", 0, 0, 0);

        Directory.CreateDirectory(ArchiveDirectory);
        var baseName = $"system-log-{AppClock.Now:yyyyMMdd_HHmmss}";
        var fileName = baseName + ".jsonl";
        // 同一秒内被手动触发两次时不能覆盖上一份：那份对应的库内行可能已经删掉了
        for (var i = 2; File.Exists(Path.Combine(ArchiveDirectory, fileName)); i++)
            fileName = $"{baseName}-{i}.jsonl";
        var path = Path.Combine(ArchiveDirectory, fileName);
        var tmp = path + ".tmp";

        long size;
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                // 第一行是元数据，其余每行一条日志：标准 JSONL，便于 grep、按行导入或流式处理
                var meta = new
                {
                    type = "meta",
                    format = "erental-log-archive",
                    version = 1,
                    archivedAt = AppClock.Now,
                    cutoff,
                    count = total
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(meta, JsonOpts));

                var lastId = 0;
                while (true)
                {
                    var batch = await _db.SystemLogs.AsNoTracking()
                        .Where(l => l.LogId > lastId && l.LogId <= maxId && l.OperateTime < cutoff)
                        .OrderBy(l => l.LogId)
                        .Take(BatchSize)
                        .ToListAsync(ct);
                    if (batch.Count == 0) break;

                    foreach (var log in batch)
                    {
                        var line = JsonSerializer.Serialize(new
                        {
                            logId = log.LogId,
                            userId = log.UserId,
                            customerId = log.CustomerId,
                            operateContent = log.OperateContent,
                            operateModule = log.OperateModule,
                            operateResult = log.OperateResult,
                            ipAddress = log.IpAddress,
                            operateTime = log.OperateTime
                        }, JsonOpts);
                        await writer.WriteLineAsync(line);
                    }
                    lastId = batch[^1].LogId;
                }

                await writer.FlushAsync(ct);
                await stream.FlushAsync(ct);
            }

            // 临时文件改名后才算归档成功：中途失败不会留下“看起来完整、实际截断”的归档
            File.Move(tmp, path, overwrite: true);
            size = new FileInfo(path).Length;
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* 清理失败不盖住原始异常 */ }
            throw;
        }

        var deleted = 0;
        while (true)
        {
            var affected = await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM `system_log` WHERE `log_id` <= {0} AND `operate_time` < {1} LIMIT 5000",
                [maxId, cutoff], ct);
            deleted += affected;
            if (affected == 0) break;
        }

        logger.LogInformation("日志归档完成：{File}（{Archived} 条 / {Size} KB），已清理库内 {Deleted} 条（保留 {Days} 天）",
            fileName, total, size / 1024, deleted, days);
        return new LogArchiveResult(fileName, total, deleted, size);
    }

    /// <summary>保留期兜底：太小会把刚发生的审计记录清掉，太大则失去归档意义</summary>
    private static int ClampRetention(int days) => Math.Clamp(days, MinRetentionDays, MaxRetentionDays);
}

/// <summary>
/// 每日日志归档：默认 Log:DailyAt（03:10）执行，多实例靠 MySQL 命名锁选主。
/// 归档必须先落盘再删库，因此这里不做任何「先删后写」的快捷路径。
/// </summary>
public class LogRetentionSchedulerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<LogRetentionSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = false;
            try
            {
                await ArchiveOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                failed = true;
                logger.LogError(ex, "日志归档失败，{Minutes} 分钟后重试", RetryDelay.TotalMinutes);
            }

            var wait = failed
                ? RetryDelay
                : DailySchedule.NextRun(AppClock.Now, config["Log:DailyAt"], new TimeOnly(3, 10)) - AppClock.Now;

            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ArchiveOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var locks = scope.ServiceProvider.GetRequiredService<DbLockService>();
        await using var handle = await locks.TryAcquireAsync("erental_log_archive", TimeSpan.Zero, ct);
        if (handle is null)
        {
            logger.LogInformation("日志归档已由其他实例执行，本实例跳过");
            return;
        }

        var archive = scope.ServiceProvider.GetRequiredService<LogArchiveService>();
        var result = await archive.ArchiveAsync(ct: ct);
        if (result.Archived == 0)
            logger.LogInformation("日志归档任务：没有超过 {Days} 天的记录", archive.RetentionDays);
    }
}
