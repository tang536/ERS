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
public class CouponController(CouponService svc) : ControllerBase
{
    private readonly CouponService _svc = svc;
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    ///<summary>券模板列表（运营配置页）</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> GetCoupons() => Ok(new ApiResponse<List<CouponDto>> { Data = await _svc.GetAllAsync() });

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> CreateCoupon([FromBody] CouponInput input)
    {
        try { return Ok(new ApiResponse<CouponDto> { Data = await _svc.CreateAsync(input), Message = "优惠券已创建" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> UpdateCoupon(int id, [FromBody] CouponInput input)
    {
        try { return Ok(new ApiResponse<CouponDto> { Data = await _svc.UpdateAsync(id, input), Message = "优惠券已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>启用/停用：停用只拦继续发放，已领出的券在有效期内仍可用</summary>
    [HttpPut("{id:int}/status")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> SetCouponStatus(int id, [FromBody] ChangeStatusDto dto)
    {
        try { await _svc.SetStatusAsync(id, dto.Status); return Ok(new ApiResponse<object> { Message = "状态已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>发券：不指定顾客则发给全部正常账号；返回实际发放张数</summary>
    [HttpPost("grant")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> GrantCoupon([FromBody] GrantCouponInput input)
    {
        try
        {
            var count = await _svc.GrantAsync(input);
            return Ok(new ApiResponse<object> { Data = new { count }, Message = $"已发放 {count} 张优惠券" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>顾客端「我的券」</summary>
    [HttpGet("mine")]
    [Authorize(Policy = Policies.CustomerOnly)]
    public async Task<IActionResult> MyCoupons() => Ok(new ApiResponse<List<MyCouponDto>> { Data = await _svc.MineAsync(CurrentUserId) });

    ///<summary>柜台开单时查该顾客持有的券（前台/管理员）：用于选择本单使用的券</summary>
    [HttpGet("customer/{customerId:int}")]
    [Authorize(Policy = Policies.AdminOrReception)]
    public async Task<IActionResult> CustomerCoupons(int customerId) =>
        Ok(new ApiResponse<List<MyCouponDto>> { Data = await _svc.MineAsync(customerId) });

    ///<summary>顾客输入兑换码领券（管理员在运营配置里为该券设置了码并开放领取）</summary>
    [HttpPost("redeem")]
    [Authorize(Policy = Policies.CustomerOnly)]
    public async Task<IActionResult> Redeem([FromBody] CouponRedeemInput input)
    {
        try
        {
            var coupon = await _svc.RedeemAsync(CurrentUserId, input.Code);
            return Ok(new ApiResponse<MyCouponDto> { Data = coupon, Message = $"已领取「{coupon.Name}」" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>顾客本人申请短时二维码令牌：绑定当前顾客与券，不暴露裸券码</summary>
    [HttpPost("{id:int}/qr")]
    [Authorize(Policy = Policies.CustomerOnly)]
    public async Task<IActionResult> CreateQrToken(int id)
    {
        try
        {
            var token = await _svc.CreateQrTokenAsync(CurrentUserId, id);
            return Ok(new ApiResponse<CouponQrTokenDto> { Data = token });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>前台扫码查券：二维码里是短时签名令牌，验证后带出持券人</summary>
    [HttpGet("verify")]
    [Authorize(Policy = Policies.AdminOrReception)]
    public async Task<IActionResult> Verify([FromQuery] string code)
    {
        try { return Ok(new ApiResponse<CouponVerifyDto> { Data = await _svc.VerifyByCodeAsync(code) }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>前台核销该券（置为已使用，核销后不可再用）</summary>
    [HttpPost("consume")]
    [Authorize(Policy = Policies.AdminOrReception)]
    public async Task<IActionResult> Consume([FromBody] CouponRedeemInput input)
    {
        try
        {
            var info = await _svc.ConsumeByCodeAsync(input.Code);
            return Ok(new ApiResponse<CouponVerifyDto> { Data = info, Message = $"券「{info.Name}」已核销" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }
}
