using System.Security.Claims;
using ERental.Authorization;
using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

/// <summary>员工侧扩展接口：预约、还车、评价、工单、客户信用、资产与运营规则。</summary>
[ApiController]
[Route("api/ext")]
[Authorize(Policy = Policies.AnyStaff)]
public class OpsExtController(OpsExtService svc) : ControllerBase
{
    private readonly OpsExtService _svc = svc;
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private static ApiResponse<object> Fail(Exception ex) => new()
    {
        Code = 400, Message = ErrorText.Safe(ex), Success = false,
    };

    // ── 门店 ──────────────────────────────────────────────
    [HttpGet("stores")]
    public async Task<IActionResult> Stores()
        => Ok(new ApiResponse<List<StoreDto>> { Data = await _svc.ListStoresAsync() });

    [HttpPost("stores")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> CreateStore([FromBody] StoreInput input)
    {
        try { return Ok(new ApiResponse<StoreDto> { Data = await _svc.SaveStoreAsync(null, input), Message = "门店已创建" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPut("stores/{id:int}")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> UpdateStore(int id, [FromBody] StoreInput input)
    {
        try { return Ok(new ApiResponse<StoreDto> { Data = await _svc.SaveStoreAsync(id, input), Message = "门店已保存" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPost("stores/{id:int}/bind-vehicle/{vehicleId:int}")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> BindVehicle(int id, int vehicleId)
    {
        try { await _svc.BindVehicleStoreAsync(vehicleId, id); return Ok(new ApiResponse<object> { Message = "车辆已归属门店" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 预约 ──────────────────────────────────────────────
    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] string? status)
        => Ok(new ApiResponse<List<AppointmentDto>> { Data = await _svc.ListAppointmentsAsync(status) });

    [HttpPost("appointments/{id:int}/status")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> AppointmentStatus(int id, [FromBody] StatusInput input)
    {
        try { return Ok(new ApiResponse<AppointmentDto> { Data = await _svc.SetAppointmentStatusAsync(id, input.Status), Message = "预约状态已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 还车预登记 ─────────────────────────────────────────
    [HttpGet("returns")]
    public async Task<IActionResult> Returns([FromQuery] string? status)
        => Ok(new ApiResponse<List<ReturnRequestDto>> { Data = await _svc.ListReturnRequestsAsync(status) });

    [HttpPost("returns/{id:int}/status")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> ReturnStatus(int id, [FromBody] StatusInput input)
    {
        try { return Ok(new ApiResponse<ReturnRequestDto> { Data = await _svc.SetReturnRequestStatusAsync(id, input.Status), Message = "还车登记已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 评价 ──────────────────────────────────────────────
    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews([FromQuery] int? vehicleId)
        => Ok(new ApiResponse<List<ReviewDto>> { Data = await _svc.ListReviewsAsync(vehicleId) });

    [HttpPost("reviews/{id:int}/reply")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> ReplyReview(int id, [FromBody] TicketReplyInput input)
    {
        try { return Ok(new ApiResponse<ReviewDto> { Data = await _svc.ReplyReviewAsync(id, input.Reply), Message = "评价已回复" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 客服工单 ──────────────────────────────────────────
    [HttpGet("tickets")]
    public async Task<IActionResult> Tickets([FromQuery] string? status)
        => Ok(new ApiResponse<List<SupportTicketDto>> { Data = await _svc.ListTicketsAsync(status) });

    [HttpPost("tickets/{id:int}/reply")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> ReplyTicket(int id, [FromBody] TicketReplyInput input)
    {
        try { return Ok(new ApiResponse<SupportTicketDto> { Data = await _svc.ReplyTicketAsync(id, CurrentUserId, input), Message = input.Close ? "工单已关闭" : "工单已回复" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 客户信用 / 企业 / 实名 ─────────────────────────────
    [HttpGet("customers/{id:int}/credit")]
    public async Task<IActionResult> Credit(int id)
        => Ok(new ApiResponse<CustomerCreditDto> { Data = await _svc.GetCreditAsync(id) });

    [HttpPut("customers/{id:int}/credit")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> SaveCredit(int id, [FromBody] CustomerCreditInput input)
    {
        try { return Ok(new ApiResponse<CustomerCreditDto> { Data = await _svc.SaveCreditAsync(id, input), Message = "客户信用资料已保存" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 电池 / 保险 / 事故 / 成本 / 备件 / GPS ──────────────
    [HttpGet("batteries")]
    public async Task<IActionResult> Batteries()
        => Ok(new ApiResponse<List<BatteryDto>> { Data = await _svc.ListBatteriesAsync() });

    [HttpPost("batteries")]
    public async Task<IActionResult> CreateBattery([FromBody] BatteryInput input)
    {
        try { return Ok(new ApiResponse<BatteryDto> { Data = await _svc.SaveBatteryAsync(null, input), Message = "电池档案已建立" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPut("batteries/{id:int}")]
    public async Task<IActionResult> UpdateBattery(int id, [FromBody] BatteryInput input)
    {
        try { return Ok(new ApiResponse<BatteryDto> { Data = await _svc.SaveBatteryAsync(id, input), Message = "电池档案已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("insurance")]
    public async Task<IActionResult> Insurance()
        => Ok(new ApiResponse<List<InsuranceDto>> { Data = await _svc.ListInsuranceAsync() });

    [HttpPost("insurance")]
    public async Task<IActionResult> CreateInsurance([FromBody] InsuranceInput input)
    {
        try { return Ok(new ApiResponse<InsuranceDto> { Data = await _svc.SaveInsuranceAsync(null, input), Message = "保单已登记" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPut("insurance/{id:int}")]
    public async Task<IActionResult> UpdateInsurance(int id, [FromBody] InsuranceInput input)
    {
        try { return Ok(new ApiResponse<InsuranceDto> { Data = await _svc.SaveInsuranceAsync(id, input), Message = "保单已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("accidents")]
    public async Task<IActionResult> Accidents()
        => Ok(new ApiResponse<List<AccidentDto>> { Data = await _svc.ListAccidentsAsync() });

    [HttpPost("accidents")]
    public async Task<IActionResult> CreateAccident([FromBody] AccidentInput input)
    {
        try { return Ok(new ApiResponse<AccidentDto> { Data = await _svc.CreateAccidentAsync(input), Message = "事故已登记" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("vehicle-costs")]
    public async Task<IActionResult> VehicleCosts([FromQuery] int? vehicleId)
        => Ok(new ApiResponse<List<VehicleCostDto>> { Data = await _svc.ListVehicleCostsAsync(vehicleId) });

    [HttpPost("vehicle-costs")]
    public async Task<IActionResult> CreateVehicleCost([FromBody] VehicleCostInput input)
    {
        try { return Ok(new ApiResponse<VehicleCostDto> { Data = await _svc.CreateVehicleCostAsync(input), Message = "成本已记账" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("parts")]
    public async Task<IActionResult> Parts()
        => Ok(new ApiResponse<List<SparePartDto>> { Data = await _svc.ListPartsAsync() });

    [HttpPost("parts")]
    public async Task<IActionResult> CreatePart([FromBody] SparePartInput input)
    {
        try { return Ok(new ApiResponse<SparePartDto> { Data = await _svc.SavePartAsync(null, input), Message = "备件已入库" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPut("parts/{id:int}")]
    public async Task<IActionResult> UpdatePart(int id, [FromBody] SparePartInput input)
    {
        try { return Ok(new ApiResponse<SparePartDto> { Data = await _svc.SavePartAsync(id, input), Message = "备件已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("gps")]
    public async Task<IActionResult> Gps()
        => Ok(new ApiResponse<List<GpsDeviceDto>> { Data = await _svc.ListGpsAsync() });

    [HttpPost("gps")]
    public async Task<IActionResult> CreateGps([FromBody] GpsDeviceInput input)
    {
        try { return Ok(new ApiResponse<GpsDeviceDto> { Data = await _svc.SaveGpsAsync(null, input), Message = "设备已绑定" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPut("gps/{id:int}")]
    public async Task<IActionResult> UpdateGps(int id, [FromBody] GpsDeviceInput input)
    {
        try { return Ok(new ApiResponse<GpsDeviceDto> { Data = await _svc.SaveGpsAsync(id, input), Message = "设备已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    /// <summary>GPS 终端上报入口：设备侧按 IMEI 上报，无需员工登录。</summary>
    [HttpPost("gps/report")]
    [AllowAnonymous]
    public async Task<IActionResult> ReportGps([FromBody] GpsReportInput input)
    {
        try { return Ok(new ApiResponse<GpsDeviceDto> { Data = await _svc.ReportGpsAsync(input) }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── 定价 / 券活动 / 团购 ────────────────────────────────
    [HttpGet("pricing")]
    public async Task<IActionResult> Pricing()
        => Ok(new ApiResponse<List<PricingRuleDto>> { Data = await _svc.ListPricingRulesAsync() });

    [HttpPost("pricing")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> CreatePricing([FromBody] PricingRuleInput input)
    {
        try { return Ok(new ApiResponse<PricingRuleDto> { Data = await _svc.SavePricingRuleAsync(null, input), Message = "定价规则已保存" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPut("pricing/{id:int}")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> UpdatePricing(int id, [FromBody] PricingRuleInput input)
    {
        try { return Ok(new ApiResponse<PricingRuleDto> { Data = await _svc.SavePricingRuleAsync(id, input), Message = "定价规则已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPost("pricing/{id:int}/status")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> PricingStatus(int id, [FromBody] StatusInput input)
    {
        try { await _svc.SetPricingRuleStatusAsync(id, input.Status); return Ok(new ApiResponse<object> { Message = "规则状态已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns()
        => Ok(new ApiResponse<List<CouponCampaignDto>> { Data = await _svc.ListCampaignsAsync() });

    [HttpPost("campaigns")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> CreateCampaign([FromBody] CouponCampaignInput input)
    {
        try { return Ok(new ApiResponse<CouponCampaignDto> { Data = await _svc.SaveCampaignAsync(null, input), Message = "券活动已创建" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("vouchers")]
    public async Task<IActionResult> Vouchers()
        => Ok(new ApiResponse<List<GroupVoucherDto>> { Data = await _svc.ListGroupVouchersAsync() });

    [HttpPost("vouchers")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> CreateVoucher([FromBody] GroupVoucherInput input)
    {
        try { return Ok(new ApiResponse<GroupVoucherDto> { Data = await _svc.CreateGroupVoucherAsync(input), Message = "团购码已生成" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPost("vouchers/{code}/redeem")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> RedeemVoucher(string code, [FromBody] VoucherRedeemInput input)
    {
        try { return Ok(new ApiResponse<GroupVoucherDto> { Data = await _svc.RedeemGroupVoucherAsync(code, input.OrderId), Message = "团购码已核销" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    // ── API Key / 通知 / 车损 / 预测 / 绩效 ──────────────────
    [HttpGet("api-keys")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> ApiKeys()
        => Ok(new ApiResponse<List<ApiKeyDto>> { Data = await _svc.ListApiKeysAsync() });

    [HttpPost("api-keys")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> CreateApiKey([FromBody] ApiKeyInput input)
    {
        try { return Ok(new ApiResponse<ApiKeyCreatedDto> { Data = await _svc.CreateApiKeyAsync(input), Message = "Key 仅显示这一次，请立即保存" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpPost("api-keys/{id:int}/revoke")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> RevokeApiKey(int id)
    {
        try { await _svc.RevokeApiKeyAsync(id); return Ok(new ApiResponse<object> { Message = "Key 已吊销" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] string? status)
        => Ok(new ApiResponse<List<NotificationOutboxDto>> { Data = await _svc.ListNotificationsAsync(status) });

    [HttpPost("notifications/{id:int}/sent")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> MarkSent(int id)
    {
        try { return Ok(new ApiResponse<NotificationOutboxDto> { Data = await _svc.MarkNotificationSentAsync(id), Message = "已标记发送" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("damage")]
    public async Task<IActionResult> Damage([FromQuery] int? orderId)
        => Ok(new ApiResponse<List<DamageAssessmentDto>> { Data = await _svc.ListDamageAssessmentsAsync(orderId) });

    [HttpPost("damage")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> CreateDamage([FromBody] DamageInput input)
    {
        try { return Ok(new ApiResponse<DamageAssessmentDto> { Data = await _svc.CreateDamageAssessmentAsync(input), Message = "车损评估已生成" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("forecast")]
    public async Task<IActionResult> Forecast([FromQuery] int days = 7)
        => Ok(new ApiResponse<ForecastDto> { Data = await _svc.ForecastAsync(days) });

    [HttpGet("performance")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Performance([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(new ApiResponse<List<StaffPerformanceDto>> { Data = await _svc.PerformanceAsync(from, to) });
}

/// <summary>顾客侧扩展接口：我的预约、还车登记、评价、工单、信用与推荐码。</summary>
[ApiController]
[Route("api/my-ext")]
[Authorize(Policy = Policies.CustomerOnly)]
public class CustomerExtController(OpsExtService svc) : ControllerBase
{
    private readonly OpsExtService _svc = svc;
    private int CurrentCustomerId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private static ApiResponse<object> Fail(Exception ex) => new()
    {
        Code = 400, Message = ErrorText.Safe(ex), Success = false,
    };

    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments()
        => Ok(new ApiResponse<List<AppointmentDto>> { Data = await _svc.MyAppointmentsAsync(CurrentCustomerId) });

    [HttpPost("appointments")]
    public async Task<IActionResult> CreateAppointment([FromBody] AppointmentInput input)
    {
        try { return Ok(new ApiResponse<AppointmentDto> { Data = await _svc.CreateAppointmentAsync(CurrentCustomerId, input), Message = "预约已提交，到店前请保持电话畅通" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("returns")]
    public async Task<IActionResult> Returns()
        => Ok(new ApiResponse<List<ReturnRequestDto>> { Data = await _svc.MyReturnRequestsAsync(CurrentCustomerId) });

    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn([FromBody] ReturnRequestInput input)
    {
        try { return Ok(new ApiResponse<ReturnRequestDto> { Data = await _svc.CreateReturnRequestAsync(CurrentCustomerId, input), Message = "还车登记已提交，请按预约时间到店" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("reviews")]
    public async Task<IActionResult> MyReviews()
    {
        var reviews = await _svc.ListReviewsAsync(null);
        return Ok(new ApiResponse<List<ReviewDto>> { Data = reviews.Where(r => r.CustomerId == CurrentCustomerId).ToList() });
    }

    [HttpPost("reviews")]
    public async Task<IActionResult> CreateReview([FromBody] ReviewInput input)
    {
        try { return Ok(new ApiResponse<ReviewDto> { Data = await _svc.CreateReviewAsync(CurrentCustomerId, input), Message = "评价已提交" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> Tickets()
        => Ok(new ApiResponse<List<SupportTicketDto>> { Data = await _svc.MyTicketsAsync(CurrentCustomerId) });

    [HttpPost("tickets")]
    public async Task<IActionResult> CreateTicket([FromBody] TicketInput input)
    {
        try { return Ok(new ApiResponse<SupportTicketDto> { Data = await _svc.CreateTicketAsync(CurrentCustomerId, input), Message = "工单已提交，客服会尽快回复" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(Fail(ex)); }
    }

    [HttpGet("credit")]
    public async Task<IActionResult> Credit()
        => Ok(new ApiResponse<CustomerCreditDto> { Data = await _svc.GetCreditAsync(CurrentCustomerId) });

    [HttpGet("referral")]
    public async Task<IActionResult> Referral()
        => Ok(new ApiResponse<ReferralStatsDto> { Data = await _svc.ReferralStatsAsync(CurrentCustomerId) });

    [HttpGet("price-factor")]
    public async Task<IActionResult> PriceFactor()
        => Ok(new ApiResponse<decimal> { Data = await _svc.CurrentPriceFactorAsync() });
}
