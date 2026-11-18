using ERental.Data;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERental.Controllers;

/// <summary>
/// 对外开放 API：只暴露只读汇总，用 X-Api-Key 认证。
/// Key 在「系统配置 → 开放 API」生成，吊销后立即失效。
/// </summary>
[ApiController]
[Route("api/open")]
public class OpenApiController(AppDbContext db, OpsExtService ext) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        if (!await ext.ValidateApiKeyAsync(apiKey))
            return Unauthorized(new ApiResponse<object> { Code = 401, Message = "API Key 无效或已吊销", Success = false });

        var today = AppClock.Now.Date;
        var data = new
        {
            vehicleTotal = await db.Vehicles.CountAsync(),
            vehicleRenting = await db.RentalOrders.CountAsync(o => o.OrderStatus == "租赁中"),
            activeOrders = await db.RentalOrders.CountAsync(o => o.OrderStatus == "租赁中" || o.OrderStatus == "已支付"),
            customerTotal = await db.Customers.CountAsync(),
            todayOrders = await db.RentalOrders.CountAsync(o => o.CreateTime >= today),
            todayRevenue = await db.SettleRecords
                .Where(s => s.SettleTime >= today)
                .SumAsync(s => (decimal?)s.ActualRent) ?? 0,
        };
        return Ok(new ApiResponse<object> { Data = data });
    }
}
