using System.Text.Json.Serialization;

namespace ERental.DTOs;

public class StoreDto
{
    public int StoreId { get; init; }
    public string StoreName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string Status { get; init; } = string.Empty;
    public int VehicleCount { get; init; }
    public DateTime CreateTime { get; init; }
}
public class StoreInput
{
    [JsonRequired] public string StoreName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string Status { get; init; } = "启用";
}

public class AppointmentDto
{
    public int AppointmentId { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? ContactPhone { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public string? Brand { get; init; }
    public int? StoreId { get; init; }
    public string? StoreName { get; init; }
    public DateTime AppointTime { get; init; }
    public string? Note { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreateTime { get; init; }
}
public class AppointmentInput
{
    [JsonRequired] public int VehicleId { get; init; }
    [JsonRequired] public DateTime AppointTime { get; init; }
    public int? StoreId { get; init; }
    public string? ContactPhone { get; init; }
    public string? Note { get; init; }
}
public class StatusInput { public string Status { get; init; } = string.Empty; }

public class ReturnRequestDto
{
    public int RequestId { get; init; }
    public int OrderId { get; init; }
    public string? OrderNo { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? VehicleNo { get; init; }
    public DateTime ExpectTime { get; init; }
    public int Mileage { get; init; }
    public int BatteryPercent { get; init; }
    public string? Note { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreateTime { get; init; }
    public DateTime? HandleTime { get; init; }
}
public class ReturnRequestInput
{
    [JsonRequired] public int OrderId { get; init; }
    [JsonRequired] public DateTime ExpectTime { get; init; }
    public int Mileage { get; init; }
    public int BatteryPercent { get; init; }
    public string? Note { get; init; }
}

public class ReviewDto
{
    public int ReviewId { get; init; }
    public int OrderId { get; init; }
    public string? OrderNo { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public int Rating { get; init; }
    public string? Content { get; init; }
    public string? Reply { get; init; }
    public DateTime? ReplyTime { get; init; }
    public DateTime CreateTime { get; init; }
}
public class ReviewInput
{
    [JsonRequired] public int OrderId { get; init; }
    [JsonRequired] public int Rating { get; init; }
    public string? Content { get; init; }
}

public class SupportTicketDto
{
    public int TicketId { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? OrderId { get; init; }
    public string? OrderNo { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int? HandlerId { get; init; }
    public string? HandlerName { get; init; }
    public string? Reply { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime? CloseTime { get; init; }
}
public class TicketInput
{
    public int? OrderId { get; init; }
    public string Category { get; init; } = "其他";
    [JsonRequired] public string Content { get; init; } = string.Empty;
}
public class TicketReplyInput
{
    [JsonRequired] public string Reply { get; init; } = string.Empty;
    public bool Close { get; init; }
}

public class ReferralStatsDto
{
    public string ReferralCode { get; init; } = string.Empty;
    public int InvitedCount { get; init; }
    public decimal RewardTotal { get; init; }
    public List<ReferralDto> Items { get; init; } = [];
}
public class ReferralDto
{
    public int ReferralId { get; init; }
    public int ReferrerId { get; init; }
    public string? ReferrerName { get; init; }
    public int InviteeId { get; init; }
    public string? InviteeName { get; init; }
    public int? RewardCouponId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreateTime { get; init; }
}

public class CustomerCreditDto
{
    public int CustomerId { get; init; }
    public string? CompanyName { get; init; }
    public string? TaxNo { get; init; }
    public decimal CreditLimit { get; init; }
    public string RealNameStatus { get; init; } = "未认证";
    public string? IdCardNo { get; init; }
    public decimal DepositWaiverLimit { get; init; }
    public string? ReferralCode { get; init; }
    public DateTime? UpdateTime { get; init; }
    public bool IsCorporate { get; init; }
}
public class CustomerCreditInput
{
    public string? CompanyName { get; init; }
    public string? TaxNo { get; init; }
    public decimal CreditLimit { get; init; }
    public string RealNameStatus { get; init; } = "未认证";
    public string? IdCardNo { get; init; }
    public decimal DepositWaiverLimit { get; init; }
}

public class BatteryDto
{
    public int BatteryId { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public string SerialNo { get; init; } = string.Empty;
    public int HealthPercent { get; init; }
    public int CycleCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? LastCheckTime { get; init; }
    public bool LowHealth { get; init; }
}
public class BatteryInput
{
    [JsonRequired] public int VehicleId { get; init; }
    [JsonRequired] public string SerialNo { get; init; } = string.Empty;
    public int HealthPercent { get; init; } = 100;
    public int CycleCount { get; init; }
    public string Status { get; init; } = "在用";
}

public class InsuranceDto
{
    public int PolicyId { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public string Insurer { get; init; } = string.Empty;
    public string PolicyNo { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public decimal Premium { get; init; }
    public string Status { get; init; } = string.Empty;
    public int DaysLeft { get; init; }
    public bool Expiring { get; init; }
}
public class InsuranceInput
{
    [JsonRequired] public int VehicleId { get; init; }
    [JsonRequired] public string Insurer { get; init; } = string.Empty;
    [JsonRequired] public string PolicyNo { get; init; } = string.Empty;
    [JsonRequired] public DateTime StartDate { get; init; }
    [JsonRequired] public DateTime EndDate { get; init; }
    public decimal Premium { get; init; }
}

public class AccidentDto
{
    public int AccidentId { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public int? OrderId { get; init; }
    public string? OrderNo { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public DateTime AccidentTime { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal LossAmount { get; init; }
    public decimal ClaimAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreateTime { get; init; }
}
public class AccidentInput
{
    [JsonRequired] public int VehicleId { get; init; }
    public int? OrderId { get; init; }
    [JsonRequired] public DateTime AccidentTime { get; init; }
    [JsonRequired] public string Description { get; init; } = string.Empty;
    public decimal LossAmount { get; init; }
    public decimal ClaimAmount { get; init; }
    public string Status { get; init; } = "待处理";
}

public class VehicleCostDto
{
    public int CostId { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public string CostType { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime OccurDate { get; init; }
    public string? Note { get; init; }
}
public class VehicleCostInput
{
    [JsonRequired] public int VehicleId { get; init; }
    [JsonRequired] public string CostType { get; init; } = "其他";
    [JsonRequired] public decimal Amount { get; init; }
    [JsonRequired] public DateTime OccurDate { get; init; }
    public string? Note { get; init; }
}

public class SparePartDto
{
    public int PartId { get; init; }
    public string PartName { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public int StockQty { get; init; }
    public int LowThreshold { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool LowStock { get; init; }
}
public class SparePartInput
{
    [JsonRequired] public string PartName { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public int StockQty { get; init; }
    public int LowThreshold { get; init; }
    public string Unit { get; init; } = "个";
    public decimal Price { get; init; }
}

public class PricingRuleDto
{
    public int RuleId { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public string RuleType { get; init; } = string.Empty;
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public decimal Factor { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Remark { get; init; }
    public bool ActiveNow { get; init; }
}
public class PricingRuleInput
{
    [JsonRequired] public string RuleName { get; init; } = string.Empty;
    [JsonRequired] public string RuleType { get; init; } = "时段溢价";
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    [JsonRequired] public decimal Factor { get; init; } = 1.00m;
    public string Status { get; init; } = "启用";
    public string? Remark { get; init; }
}

public class GroupVoucherDto
{
    public int VoucherId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime ValidTo { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? UsedOrderId { get; init; }
    public DateTime? UsedTime { get; init; }
    public bool Expired { get; init; }
}
public class GroupVoucherInput
{
    [JsonRequired] public string Code { get; init; } = string.Empty;
    [JsonRequired] public string Title { get; init; } = string.Empty;
    [JsonRequired] public decimal Amount { get; init; }
    [JsonRequired] public DateTime ValidFrom { get; init; }
    [JsonRequired] public DateTime ValidTo { get; init; }
}
public class VoucherRedeemInput { public int? OrderId { get; init; } }

public class ApiKeyDto
{
    public int KeyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? LastUsed { get; init; }
    public DateTime CreateTime { get; init; }
}
public class ApiKeyCreatedDto
{
    public int KeyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
}
public class ApiKeyInput { [JsonRequired] public string Name { get; init; } = string.Empty; }

public class GpsDeviceDto
{
    public int DeviceId { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public string Imei { get; init; } = string.Empty;
    public string? SimNo { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal? LastLat { get; init; }
    public decimal? LastLng { get; init; }
    public DateTime? LastReport { get; init; }
}
public class GpsDeviceInput
{
    [JsonRequired] public int VehicleId { get; init; }
    [JsonRequired] public string Imei { get; init; } = string.Empty;
    public string? SimNo { get; init; }
    public string Status { get; init; } = "离线";
}
public class GpsReportInput
{
    [JsonRequired] public string Imei { get; init; } = string.Empty;
    [JsonRequired] public decimal Lat { get; init; }
    [JsonRequired] public decimal Lng { get; init; }
}

public class CouponCampaignDto
{
    public int CampaignId { get; init; }
    public int CouponId { get; init; }
    public string? CouponName { get; init; }
    public string CampaignName { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public int TotalQuota { get; init; }
    public int IssuedQty { get; init; }
    public string Status { get; init; } = string.Empty;
}
public class CouponCampaignInput
{
    [JsonRequired] public int CouponId { get; init; }
    [JsonRequired] public string CampaignName { get; init; } = string.Empty;
    [JsonRequired] public DateTime StartTime { get; init; }
    [JsonRequired] public DateTime EndTime { get; init; }
    public int TotalQuota { get; init; }
}

public class NotificationOutboxDto
{
    public int NotifyId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string ReceiverType { get; init; } = string.Empty;
    public int ReceiverId { get; init; }
    public string? ReceiverName { get; init; }
    public string Template { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int RetryCount { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime? SendTime { get; init; }
}

public class DamageAssessmentDto
{
    public int AssessmentId { get; init; }
    public int OrderId { get; init; }
    public string? OrderNo { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public int Severity { get; init; }
    public decimal EstimatedCost { get; init; }
    public string Findings { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreateTime { get; init; }
}
public class DamageInput
{
    [JsonRequired] public int OrderId { get; init; }
    [JsonRequired] public int Severity { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? Findings { get; init; }
}

public class ForecastPointDto
{
    public string Date { get; init; } = string.Empty;
    public int Orders { get; init; }
    public decimal Revenue { get; init; }
}
public class ForecastDto
{
    public int Days { get; init; }
    public decimal AvgOrders { get; init; }
    public string Trend { get; init; } = string.Empty;
    public List<ForecastPointDto> History { get; init; } = [];
    public List<ForecastPointDto> Forecast { get; init; } = [];
}

public class StaffPerformanceDto
{
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public int OrderCount { get; init; }
    public int SettleCount { get; init; }
    public decimal Revenue { get; init; }
    public decimal RefundAmount { get; init; }
}
