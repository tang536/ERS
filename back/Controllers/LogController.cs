using System.Security.Claims;
using ERental.Data;
using ERental.Models;
using ERental.Services;
using ERental.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.AdminOnly)]
public class LogController(LogService svc, BackupService backup, LogArchiveService archive) : ControllerBase
{
    private readonly LogService _svc = svc;
    private readonly BackupService _backup = backup;
    private readonly LogArchiveService _archive = archive;

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private string? ClientIp => Services.ClientIp.Resolve(HttpContext);

    ///<summary>耗时文案</summary>
    private static string Elapsed(long ms) => ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:0.0}s";

    ///<summary>操作日志查询</summary>
    [HttpGet]
    public async Task<IActionResult> GetOperationLogs(int page = 1, int size = 20, int? userId = null, DateTime? from = null, DateTime? to = null, string? module = null, string? result = null, string? keyword = null) //NOSONAR S107：这 8 个形参是 HTTP 查询契约，前端逐个传 query 参数；若合并成值元组，ASP.NET Core 无法把 query string 绑定到元组元素，筛选会整体失效
    {
        //只为消除服务层的参数数量问题在这里装元组
        var (total, items) = await _svc.GetAll((page, size, userId, from, to, module, result, keyword));
        return Ok(new ApiResponse<object> { Data = new { Total = total, Page = page, Size = size, Items = items } });
    }

    ///<summary>日志里实际出现过的操作模块</summary>
    [HttpGet("modules")]
    public async Task<IActionResult> GetLogModules() => Ok(new ApiResponse<List<string>> { Data = await _svc.GetModules() });

    ///<summary>数据库备份</summary>
    [HttpPost("backup")]
    public async Task<IActionResult> BackupDatabase()
    {
        try
        {
            var r = await _backup.CreateAsync();
            await _svc.WriteLog(CurrentUserId,
                $"执行数据库备份，生成加密文件 {r.FileName}（{r.SizeBytes / 1024}KB，{r.TableCount} 张表，耗时 {r.ElapsedMs}ms）",
                "系统管理", "成功", ClientIp);
            return Ok(new ApiResponse<object>
            {
                Message = $"数据库备份成功，共 {r.TableCount} 张表（已加密，耗时 {Elapsed(r.ElapsedMs)}）",
                Data = new
                {
                    r.FileName,
                    SizeKB = Math.Round(r.SizeBytes / 1024.0, 2),
                    r.TableCount,
                    r.Encrypted,
                    r.ElapsedMs
                }
            });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = $"备份失败：{ErrorText.Safe(ex)}", Success = false });
        }
    }

    ///<summary>备份文件列表（新的在前），供管理员下载或选择恢复点</summary>
    [HttpGet("backups")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult GetBackupFiles() => Ok(new ApiResponse<object> { Data = _backup.ListFiles() });

    ///<summary>手动触发日志归档：先写 JSONL 落盘，成功后才删除库内到期记录</summary>
    [HttpPost("archive")]
    public async Task<IActionResult> ArchiveLogs(int? retentionDays = null)
    {
        try
        {
            var r = await _archive.ArchiveAsync(retentionDays);
            var message = r.Archived == 0
                ? $"没有超过 {_archive.RetentionDays} 天的日志需要归档"
                : $"已归档 {r.Archived} 条日志到 {r.FileName}（{r.SizeBytes / 1024}KB），清理库内 {r.Deleted} 条";

            await _svc.WriteLog(CurrentUserId, $"手动归档操作日志：{message}", "系统管理", "成功", ClientIp);
            return Ok(new ApiResponse<object> { Message = message, Data = r });
        }
        catch (Exception ex)
        {
            await _svc.WriteLog(CurrentUserId, $"手动归档操作日志失败（{ex.GetType().Name}）", "系统管理", "失败", ClientIp);
            return BadRequest(new ApiResponse<object> { Code = 400, Message = $"日志归档失败：{ErrorText.Safe(ex)}", Success = false });
        }
    }

    ///<summary>最近一次备份可恢复性校验结果；没有校验过时 Data 为 null</summary>
    [HttpGet("backups/verify-state")]
    public IActionResult GetBackupVerifyState() => Ok(new ApiResponse<object> { Data = _backup.ReadVerifyState() });

    ///<summary>手动校验指定备份：解密 + 解析 + 核对表清单与行数，不导入当前库</summary>
    [HttpPost("backups/{fileName}/verify")]
    public async Task<IActionResult> VerifyBackup(string fileName)
    {
        try
        {
            var r = await _backup.VerifyAsync(fileName);
            _backup.SaveVerifyState(new BackupVerifyState(r.FileName, AppClock.Now, true, r.TableCount, r.RowCount, null));

            var message = $"备份 {r.FileName} 校验通过（{r.TableCount} 张表 / {r.RowCount} 行，耗时 {Elapsed(r.ElapsedMs)}）";
            await _svc.WriteLog(CurrentUserId, message, "系统管理", "成功", ClientIp);
            return Ok(new ApiResponse<object> { Message = message, Data = r });
        }
        catch (Exception ex)
        {
            // 校验失败也落快照：/health 需要知道「最新一次校验没通过」，而不是继续沿用上一次的成功结果
            _backup.SaveVerifyState(new BackupVerifyState(Path.GetFileName(fileName), AppClock.Now, false, 0, 0, ex.GetType().Name));
            await _svc.WriteLog(CurrentUserId, $"备份 {Path.GetFileName(fileName)} 校验失败（{ex.GetType().Name}）", "系统管理", "失败", ClientIp);
            return BadRequest(new ApiResponse<object> { Code = 400, Message = $"备份校验失败：{ErrorText.Safe(ex)}", Success = false });
        }
    }

    ///<summary>下载备份文件</summary>
    [HttpGet("backups/{fileName}/download")]
    public async Task<IActionResult> DownloadBackupFile(string fileName)
    {
        try
        {
            var (name, content) = await _backup.ReadAsync(fileName);
            await _svc.WriteLog(CurrentUserId, $"下载数据库备份 {name}", "系统管理", "成功", ClientIp);
            return File(content, "application/octet-stream", name);
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    ///<summary>用备份恢复数据库</summary>
    [HttpPost("restore")]
    public async Task<IActionResult> RestoreDatabase([FromBody] RestoreRequest req)
    {
        if (!string.Equals(req.Confirm, "RESTORE", StringComparison.Ordinal))
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "危险操作：请在 confirm 字段填写 RESTORE 以确认覆盖当前数据库",
                Success = false
            });

        try
        {
            var r = await _backup.RestoreAsync(req.FileName);
            await _svc.WriteLog(CurrentUserId,
                $"从备份 {r.FileName} 恢复数据库（{r.TableCount} 张表 / {r.Rows} 行，耗时 {r.ElapsedMs}ms；恢复前快照 {r.SafetyBackup}）",
                "系统管理", "成功", ClientIp);
            return Ok(new ApiResponse<object>
            {
                Message = $"恢复完成：{r.Rows} 行数据，耗时 {Elapsed(r.ElapsedMs)}；恢复前快照已保存为 {r.SafetyBackup}",
                Data = r
            });
        }
        catch (Exception ex)
        {
            await _svc.WriteLog(CurrentUserId, $"从备份 {req.FileName} 恢复失败（{ex.GetType().Name}）",
                "系统管理", "失败", ClientIp);
            return BadRequest(new ApiResponse<object> { Code = 400, Message = $"恢复失败：{ErrorText.Safe(ex)}", Success = false });
        }
    }

    public sealed record RestoreRequest(string FileName, string? Confirm);
}
