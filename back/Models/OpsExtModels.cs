using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;

namespace ERental.Models;

// ═══════════════════════════════════════════════════════════════
// 运营扩展（第二批）：预约、还车预登记、评价、客服、推荐、企业客户、
// 门店、电池、保险/事故、车辆成本、备件、动态定价、团购、API Key、GPS
// 原则：不修改既有核心表，全部用独立表 + 外键 ID 关联，避免老库迁移风险。
// ═══════════════════════════════════════════════════════════════

[Table("store")]
public class Store
{
    [Key, Column("store_id")] public int StoreId { get; set; }
    [Required, MaxLength(60), Column("store_name")] public string StoreName { get; set; } = string.Empty;
    [MaxLength(200), Column("address")] public string? Address { get; set; }
    [MaxLength(30), Column("phone")] public string? Phone { get; set; }
    [MaxLength(20), Column("status")] public string Status { get; set; } = "启用";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

/// <summary>车辆归属门店：一车一店，独立映射表，避免给 vehicle 加列。</summary>
[Table("vehicle_store")]
public class VehicleStore
{
    [Key, Column("id")] public int Id { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Column("store_id")] public int StoreId { get; set; }
    [Column("bind_time")] public DateTime BindTime { get; set; } = AppClock.Now;
}

[Table("appointment")]
public class Appointment
{
    [Key, Column("appointment_id")] public int AppointmentId { get; set; }
    [Column("customer_id")] public int CustomerId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Column("store_id")] public int? StoreId { get; set; }
    [Column("appoint_time")] public DateTime AppointTime { get; set; }
    [MaxLength(30), Column("contact_phone")] public string? ContactPhone { get; set; }
    [MaxLength(300), Column("note")] public string? Note { get; set; }
    /// <summary>待确认 / 已确认 / 已到店 / 已取消</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "待确认";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("return_request")]
public class ReturnRequest
{
    [Key, Column("request_id")] public int RequestId { get; set; }
    [Column("order_id")] public int OrderId { get; set; }
    [Column("customer_id")] public int CustomerId { get; set; }
    [Column("expect_time")] public DateTime ExpectTime { get; set; }
    [Column("mileage")] public int Mileage { get; set; }
    [Column("battery_percent")] public int BatteryPercent { get; set; }
    [MaxLength(300), Column("note")] public string? Note { get; set; }
    /// <summary>待处理 / 已受理 / 已取消</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "待处理";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
    [Column("handle_time")] public DateTime? HandleTime { get; set; }
}

[Table("review")]
public class Review
{
    [Key, Column("review_id")] public int ReviewId { get; set; }
    [Column("order_id")] public int OrderId { get; set; }
    [Column("customer_id")] public int CustomerId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Column("rating")] public int Rating { get; set; }
    [MaxLength(500), Column("content")] public string? Content { get; set; }
    [MaxLength(500), Column("reply")] public string? Reply { get; set; }
    [Column("reply_time")] public DateTime? ReplyTime { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("support_ticket")]
public class SupportTicket
{
    [Key, Column("ticket_id")] public int TicketId { get; set; }
    [Column("customer_id")] public int CustomerId { get; set; }
    [Column("order_id")] public int? OrderId { get; set; }
    [MaxLength(30), Column("category")] public string Category { get; set; } = "其他";
    [Required, MaxLength(500), Column("content")] public string Content { get; set; } = string.Empty;
    /// <summary>待处理 / 处理中 / 已回复 / 已关闭</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "待处理";
    [Column("handler_id")] public int? HandlerId { get; set; }
    [MaxLength(500), Column("reply")] public string? Reply { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
    [Column("close_time")] public DateTime? CloseTime { get; set; }
}

[Table("referral")]
public class Referral
{
    [Key, Column("referral_id")] public int ReferralId { get; set; }
    [Column("referrer_id")] public int ReferrerId { get; set; }
    [Column("invitee_id")] public int InviteeId { get; set; }
    [Column("reward_coupon_id")] public int? RewardCouponId { get; set; }
    [MaxLength(20), Column("status")] public string Status { get; set; } = "已注册";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

/// <summary>客户信用/企业/实名扩展：不往 customer 表加列，按 customer_id 一对一挂接。</summary>
[Table("customer_credit_profile")]
public class CustomerCreditProfile
{
    [Key, Column("profile_id")] public int ProfileId { get; set; }
    [Column("customer_id")] public int CustomerId { get; set; }
    [MaxLength(80), Column("company_name")] public string? CompanyName { get; set; }
    [MaxLength(50), Column("tax_no")] public string? TaxNo { get; set; }
    [Column("credit_limit", TypeName = "decimal(10,2)")] public decimal CreditLimit { get; set; }
    [MaxLength(20), Column("real_name_status")] public string RealNameStatus { get; set; } = "未认证";
    [MaxLength(30), Column("id_card_no")] public string? IdCardNo { get; set; }
    [Column("deposit_waiver_limit", TypeName = "decimal(10,2)")] public decimal DepositWaiverLimit { get; set; }
    [MaxLength(12), Column("referral_code")] public string? ReferralCode { get; set; }
    [Column("referrer_id")] public int? ReferrerId { get; set; }
    [Column("update_time")] public DateTime UpdateTime { get; set; } = AppClock.Now;
}

[Table("battery")]
public class Battery
{
    [Key, Column("battery_id")] public int BatteryId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Required, MaxLength(40), Column("serial_no")] public string SerialNo { get; set; } = string.Empty;
    [Column("health_percent")] public int HealthPercent { get; set; } = 100;
    [Column("cycle_count")] public int CycleCount { get; set; }
    /// <summary>在用 / 备用 / 维修 / 报废</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "在用";
    [Column("last_check_time")] public DateTime? LastCheckTime { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("insurance_policy")]
public class InsurancePolicy
{
    [Key, Column("policy_id")] public int PolicyId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [MaxLength(60), Column("insurer")] public string Insurer { get; set; } = string.Empty;
    [MaxLength(50), Column("policy_no")] public string PolicyNo { get; set; } = string.Empty;
    [Column("start_date")] public DateTime StartDate { get; set; }
    [Column("end_date")] public DateTime EndDate { get; set; }
    [Column("premium", TypeName = "decimal(10,2)")] public decimal Premium { get; set; }
    [MaxLength(20), Column("status")] public string Status { get; set; } = "有效";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("accident_record")]
public class AccidentRecord
{
    [Key, Column("accident_id")] public int AccidentId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Column("order_id")] public int? OrderId { get; set; }
    [Column("customer_id")] public int? CustomerId { get; set; }
    [Column("accident_time")] public DateTime AccidentTime { get; set; }
    [Required, MaxLength(500), Column("description")] public string Description { get; set; } = string.Empty;
    [Column("loss_amount", TypeName = "decimal(10,2)")] public decimal LossAmount { get; set; }
    [Column("claim_amount", TypeName = "decimal(10,2)")] public decimal ClaimAmount { get; set; }
    /// <summary>待处理 / 理赔中 / 已结案</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "待处理";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("vehicle_cost")]
public class VehicleCost
{
    [Key, Column("cost_id")] public int CostId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [MaxLength(30), Column("cost_type")] public string CostType { get; set; } = "其他";
    [Column("amount", TypeName = "decimal(10,2)")] public decimal Amount { get; set; }
    [Column("occur_date")] public DateTime OccurDate { get; set; }
    [MaxLength(300), Column("note")] public string? Note { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("spare_part")]
public class SparePart
{
    [Key, Column("part_id")] public int PartId { get; set; }
    [Required, MaxLength(60), Column("part_name")] public string PartName { get; set; } = string.Empty;
    [MaxLength(40), Column("sku")] public string? Sku { get; set; }
    [Column("stock_qty")] public int StockQty { get; set; }
    [Column("low_threshold")] public int LowThreshold { get; set; }
    [MaxLength(10), Column("unit")] public string Unit { get; set; } = "个";
    [Column("price", TypeName = "decimal(10,2)")] public decimal Price { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("pricing_rule")]
public class PricingRule
{
    [Key, Column("rule_id")] public int RuleId { get; set; }
    [Required, MaxLength(80), Column("rule_name")] public string RuleName { get; set; } = string.Empty;
    /// <summary>时段溢价 / 节假日 / 会员日 / 库存紧张</summary>
    [MaxLength(20), Column("rule_type")] public string RuleType { get; set; } = "时段溢价";
    [Column("start_time")] public DateTime? StartTime { get; set; }
    [Column("end_time")] public DateTime? EndTime { get; set; }
    [Column("factor", TypeName = "decimal(5,2)")] public decimal Factor { get; set; } = 1.00m;
    [MaxLength(20), Column("status")] public string Status { get; set; } = "启用";
    [MaxLength(200), Column("remark")] public string? Remark { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("group_voucher")]
public class GroupVoucher
{
    [Key, Column("voucher_id")] public int VoucherId { get; set; }
    [Required, MaxLength(30), Column("code")] public string Code { get; set; } = string.Empty;
    [MaxLength(80), Column("title")] public string Title { get; set; } = string.Empty;
    [Column("amount", TypeName = "decimal(10,2)")] public decimal Amount { get; set; }
    [Column("valid_from")] public DateTime ValidFrom { get; set; }
    [Column("valid_to")] public DateTime ValidTo { get; set; }
    /// <summary>未使用 / 已核销 / 已过期</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "未使用";
    [Column("used_order_id")] public int? UsedOrderId { get; set; }
    [Column("used_time")] public DateTime? UsedTime { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("api_key")]
public class ApiKey
{
    [Key, Column("key_id")] public int KeyId { get; set; }
    [MaxLength(60), Column("name")] public string Name { get; set; } = string.Empty;
    [MaxLength(64), Column("key_hash")] public string KeyHash { get; set; } = string.Empty;
    [MaxLength(12), Column("prefix")] public string Prefix { get; set; } = string.Empty;
    [MaxLength(20), Column("status")] public string Status { get; set; } = "启用";
    [Column("last_used")] public DateTime? LastUsed { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("gps_device")]
public class GpsDevice
{
    [Key, Column("device_id")] public int DeviceId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [MaxLength(30), Column("imei")] public string Imei { get; set; } = string.Empty;
    [MaxLength(30), Column("sim_no")] public string? SimNo { get; set; }
    /// <summary>在线 / 离线 / 停用</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "离线";
    [Column("last_lat", TypeName = "decimal(10,6)")] public decimal? LastLat { get; set; }
    [Column("last_lng", TypeName = "decimal(10,6)")] public decimal? LastLng { get; set; }
    [Column("last_report")] public DateTime? LastReport { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

[Table("coupon_campaign")]
public class CouponCampaign
{
    [Key, Column("campaign_id")] public int CampaignId { get; set; }
    [Column("coupon_id")] public int CouponId { get; set; }
    [Required, MaxLength(80), Column("campaign_name")] public string CampaignName { get; set; } = string.Empty;
    [Column("start_time")] public DateTime StartTime { get; set; }
    [Column("end_time")] public DateTime EndTime { get; set; }
    [Column("total_quota")] public int TotalQuota { get; set; }
    [Column("issued_qty")] public int IssuedQty { get; set; }
    [MaxLength(20), Column("status")] public string Status { get; set; } = "启用";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}

/// <summary>短信/微信通知出站队列：外部通道未配置时保留为「待发送」，可在系统配置接通道。</summary>
[Table("notification_outbox")]
public class NotificationOutbox
{
    [Key, Column("notify_id")] public int NotifyId { get; set; }
    [MaxLength(20), Column("channel")] public string Channel { get; set; } = "站内";
    [MaxLength(30), Column("receiver_type")] public string ReceiverType { get; set; } = "customer";
    [Column("receiver_id")] public int ReceiverId { get; set; }
    [MaxLength(30), Column("template")] public string Template { get; set; } = string.Empty;
    [MaxLength(500), Column("content")] public string Content { get; set; } = string.Empty;
    /// <summary>待发送 / 已发送 / 失败</summary>
    [MaxLength(20), Column("status")] public string Status { get; set; } = "待发送";
    [Column("retry_count")] public int RetryCount { get; set; }
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
    [Column("send_time")] public DateTime? SendTime { get; set; }
}

/// <summary>AI 车损评估：当前为可复算的规则引擎结果，后续可替换为视觉模型。</summary>
[Table("damage_assessment")]
public class DamageAssessment
{
    [Key, Column("assessment_id")] public int AssessmentId { get; set; }
    [Column("order_id")] public int OrderId { get; set; }
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [MaxLength(20), Column("source")] public string Source { get; set; } = "图片";
    [Column("severity")] public int Severity { get; set; }
    [Column("estimated_cost", TypeName = "decimal(10,2)")] public decimal EstimatedCost { get; set; }
    [MaxLength(500), Column("findings")] public string Findings { get; set; } = string.Empty;
    [MaxLength(20), Column("status")] public string Status { get; set; } = "待复核";
    [Column("create_time")] public DateTime CreateTime { get; set; } = AppClock.Now;
}
