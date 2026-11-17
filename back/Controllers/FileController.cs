using System.Security.Claims;
using ERental.Authorization;
using ERental.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERental.Controllers;

/// <summary>
/// 私有上传文件的唯一读取入口。
/// 公开静态目录只保留 vehicle / avatars；维保照片、交接照片、合同签名与 AI 成品
/// 都含有顾客个人信息或经营证据，必须按角色鉴权后读取。
/// access token 同时写入 HttpOnly Cookie，且 JWT 只在 GET/HEAD 时接受 Cookie，
/// 因此前端的 &lt;img src&gt; 可以直接加载，而写接口仍然只认 Bearer。
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class FileController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private const string ContractCategory = "contract";
    private const string MaintenanceCategory = "maintenance";
    private const string HandoverCategory = "handover";
    private const string AiCategory = "ai";

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ContractCategory, MaintenanceCategory, HandoverCategory, AiCategory
    };

    private readonly AppDbContext _db = db;
    private readonly IWebHostEnvironment _env = env;

    private bool IsCustomer => User.IsInRole(RoleNames.Customer);
    private bool IsAdmin => User.IsInRole(RoleNames.Admin);
    private bool IsReception => User.IsInRole(RoleNames.Reception);
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("{category}/{**path}")]
    public async Task<IActionResult> GetPrivateFile(string category, string path)
    {
        return await ServeAsync(category, path);
    }

    /// <summary>
    /// 兼容历史数据里的 /uploads/{category}/... 链接：静态中间件已不再公开这些目录，
    /// 但老合同、交接照片的数据库字段仍存旧路径，统一转到同一个鉴权入口。
    /// </summary>
    [HttpGet("~/uploads/{category}/{**path}")]
    public async Task<IActionResult> GetPrivateFileLegacy(string category, string path)
    {
        return await ServeAsync(category, path);
    }

    private async Task<IActionResult> ServeAsync(string category, string path)
    {
        if (!AllowedCategories.Contains(category)) return NotFound();

        var relative = path.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            return NotFound();

        var root = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", category));
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        if (!await CanReadAsync(category, relative)) return Forbid();

        var contentType = ContentTypeOf(fullPath);
        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(fullPath, contentType);
    }

    private async Task<bool> CanReadAsync(string category, string relative)
    {
        // 维保与交接照片是门店内部证据，只给管理员/前台；财务对账不需要看现场照片。
        if (category is MaintenanceCategory or HandoverCategory) return IsAdmin || IsReception;

        // AI 成品属于管理端素材，只给管理员。
        if (category is AiCategory) return IsAdmin;

        // 合同签名：管理员/前台可查；顾客只能查本人签署的那一份。
        if (category is ContractCategory)
        {
            var url = $"/uploads/{category}/{relative.Replace(Path.DirectorySeparatorChar, '/')}";
            var apiUrl = $"/api/files/{category}/{relative.Replace(Path.DirectorySeparatorChar, '/')}";
            var contract = await _db.Contracts.AsNoTracking()
                .Where(c => c.SignatureUrl == url || c.SignatureUrl == apiUrl)
                .Select(c => new { c.CustomerId })
                .FirstOrDefaultAsync();
            if (contract == null) return false;
            if (IsCustomer) return contract.CustomerId == CurrentUserId;
            return IsAdmin || IsReception;
        }

        return false;
    }

    private static string ContentTypeOf(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };
}
