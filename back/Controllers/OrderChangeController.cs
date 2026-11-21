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
public class OrderChangeController(OrderChangeService svc) : ControllerBase
{
    private readonly OrderChangeService _svc = svc;
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    ///<summary>换车：已支付订单正差生成补缴单、负差原路退款</summary>
    [HttpPost("vehicle")]
    public async Task<IActionResult> ChangeVehicle([FromBody] ChangeVehicleInput input)
    {
        try { return Ok(new ApiResponse<OrderChangeDto> { Data = await _svc.ChangeVehicleAsync(input, CurrentUserId), Message = "换车已办理" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>改期：重算租金并处理差价</summary>
    [HttpPost("reschedule")]
    public async Task<IActionResult> Reschedule([FromBody] RescheduleInput input)
    {
        try { return Ok(new ApiResponse<OrderChangeDto> { Data = await _svc.RescheduleAsync(input, CurrentUserId), Message = "改期已办理" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>换车试算：只返回租金变化与差价处置方式，不占车、不落库</summary>
    [HttpPost("preview/vehicle")]
    public async Task<IActionResult> PreviewVehicle([FromBody] ChangeVehicleInput input)
    {
        try { return Ok(new ApiResponse<OrderChangePreviewDto> { Data = await _svc.PreviewVehicleAsync(input.OrderId, input.NewVehicleId) }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>改期试算：只返回租金变化与差价处置方式，不落库</summary>
    [HttpPost("preview/reschedule")]
    public async Task<IActionResult> PreviewReschedule([FromBody] RescheduleInput input)
    {
        try { return Ok(new ApiResponse<OrderChangePreviewDto> { Data = await _svc.PreviewRescheduleAsync(input.OrderId, input.LeaseStart, input.LeaseEnd) }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>某订单的变更历史</summary>
    [HttpGet("order/{orderId:int}")]
    public async Task<IActionResult> GetChanges(int orderId) =>
        Ok(new ApiResponse<List<OrderChangeDto>> { Data = await _svc.ListByOrderAsync(orderId) });
}
