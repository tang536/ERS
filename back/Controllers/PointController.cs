using System.Security.Claims;
using ERental.Authorization;
using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class PointController(PointService svc) : ControllerBase
{
    private readonly PointService _svc = svc;
    private bool IsCustomer => User.IsInRole(AuthService.RoleCustomer);
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    ///<summary>积分概要：顾客看自己，工作人员可指定 customerId 查会员积分</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AdminOrReceptionOrCustomer)]
    public async Task<IActionResult> GetPoints(int? customerId, decimal? rent)
    {
        var id = IsCustomer ? CurrentUserId : customerId ?? 0;
        if (id <= 0) return BadRequest(new ApiResponse<object> { Code = 400, Message = "请指定客户", Success = false });
        try { return Ok(new ApiResponse<PointSummaryDto> { Data = await _svc.GetSummaryAsync(id, rent) }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>下单页积分预览：最多可抵多少分、折多少钱</summary>
    [HttpGet("preview")]
    [Authorize(Policy = Policies.CustomerOnly)]
    public async Task<IActionResult> Preview(decimal rent) =>
        Ok(new ApiResponse<PointRedeemPreviewDto> { Data = await _svc.PreviewAsync(CurrentUserId, rent) });
}
