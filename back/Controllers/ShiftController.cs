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
[Authorize(Policy = Policies.AdminOrReception)]
public class ShiftController(ShiftService svc) : ControllerBase
{
    private readonly ShiftService _svc = svc;
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private string CurrentUserName => User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "门店员工";

    ///<summary>当前进行中的班次（含实时汇总，未开班返回 null）</summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentShift() =>
        Ok(new ApiResponse<ShiftDto?> { Data = await _svc.CurrentAsync(CurrentUserId) });

    ///<summary>开班：登记备用金</summary>
    [HttpPost("open")]
    public async Task<IActionResult> OpenShift([FromBody] OpenShiftInput input)
    {
        try { return Ok(new ApiResponse<ShiftDto> { Data = await _svc.OpenAsync(CurrentUserId, CurrentUserName, input.OpeningCash), Message = "已开班" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>交班：系统汇总各渠道收款，前台录入实点现金，差额必须写原因</summary>
    [HttpPost("close")]
    public async Task<IActionResult> CloseShift([FromBody] CloseShiftInput input)
    {
        try { return Ok(new ApiResponse<ShiftDto> { Data = await _svc.CloseAsync(input), Message = "已完成交班" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>交班历史</summary>
    [HttpGet("history")]
    [Authorize(Policy = Policies.SettleRead)]
    public async Task<IActionResult> GetHistory(int page = 1, int pageSize = 20) =>
        Ok(new ApiResponse<List<ShiftDto>> { Data = await _svc.GetHistoryAsync(page, pageSize) });
}
