using System.Text.Json.Serialization;

namespace ERental.DTOs;

// ── 优惠券 ──────────────────────────────────────────────

public class CouponDto
{
    public int CouponId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal MinAmount { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime ValidTo { get; init; }
    public int TotalQty { get; init; }
    public int IssuedQty { get; init; }
    public int PerLimit { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreateTime { get; init; }
    ///<summary>兑换码（口令领券）；未设置为 null</summary>
    public string? RedeemCode { get; init; }
    ///<summary>是否开放兑换</summary>
    public bool RedeemOpen { get; init; }
}

public class CouponInput
{
    [JsonRequired] public string Name { get; init; } = string.Empty;
    [JsonRequired] public decimal Amount { get; init; }
    public decimal MinAmount { get; init; }
    [JsonRequired] public DateTime ValidFrom { get; init; }
    [JsonRequired] public DateTime ValidTo { get; init; }
    ///<summary>0 = 不限量</summary>
    public int TotalQty { get; init; }
    public int PerLimit { get; init; } = 1;
    ///<summary>兑换码：4~20 位字母数字，留空表示不开放口令领取</summary>
    public string? RedeemCode { get; init; }
    ///<summary>是否开放顾客自助兑换</summary>
    public bool RedeemOpen { get; init; }
}

///<summary>顾客输入兑换码领券</summary>
public class CouponRedeemInput
{
    [JsonRequired] public string Code { get; init; } = string.Empty;
}

///<summary>前台扫码查券：二维码里是 customer_coupon.code，查到即带出持券顾客</summary>
public class CouponVerifyDto
{
    public int CustomerCouponId { get; init; }
    public int CouponId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal MinAmount { get; init; }
    ///<summary>未使用 / 已使用 / 已过期</summary>
    public string Status { get; init; } = string.Empty;
    public DateTime ExpireTime { get; init; }
    public DateTime? UseTime { get; init; }
    public int? OrderId { get; init; }
    ///<summary>持券顾客（有码必有主）</summary>
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? ContactPhone { get; init; }
    public string? IdCardMasked { get; init; }
    public string MemberLevel { get; init; } = string.Empty;
    ///<summary>此刻能否核销（未使用且在有效期内）</summary>
    public bool Usable { get; init; }
}

public class GrantCouponInput
{
    [JsonRequired] public int CouponId { get; init; }
    ///<summary>为空表示发给全部正常状态顾客</summary>
    public List<int> CustomerIds { get; init; } = [];
}

///<summary>顾客端「我的券」：同时带模板信息，避免前端二次查询</summary>
public class MyCouponDto
{
    public int Id { get; init; }
    public int CouponId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal MinAmount { get; init; }
    public DateTime ExpireTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime ReceiveTime { get; init; }
    public int? OrderId { get; init; }
    ///<summary>单张券唯一码值：顾客端据此生成核销二维码</summary>
    public string? Code { get; init; }
}

// ── 增值服务 ────────────────────────────────────────────

public class AddonDto
{
    public int AddonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public int SortNo { get; init; }
}

public class AddonInput
{
    [JsonRequired] public string Name { get; init; } = string.Empty;
    [JsonRequired] public decimal Price { get; init; }
    [JsonRequired] public string Unit { get; init; } = "一次性";
    public string? Description { get; init; }
    public int SortNo { get; init; }
}

public class OrderAddonDto
{
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Qty { get; init; }
    public decimal Amount { get; init; }
}

// ── 积分 ────────────────────────────────────────────────

public class PointSummaryDto
{
    public int Balance { get; init; }
    ///<summary>本次最多可抵扣的积分数（按下单租金 10% 封顶）</summary>
    public int MaxRedeem { get; init; }
    public decimal MaxRedeemAmount { get; init; }
    public string Rule { get; init; } = "结算实付租金 1 元得 1 分；100 分抵 1 元，单笔最多抵租金的 10%";
    public List<PointLogDto> Logs { get; init; } = [];
}

public class PointLogDto
{
    public int ChangeAmount { get; init; }
    public int Balance { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? Remark { get; init; }
    public int? OrderId { get; init; }
    public DateTime CreateTime { get; init; }
}

public class PointRedeemPreviewDto
{
    public int Points { get; init; }
    public decimal Amount { get; init; }
    public int MaxPoints { get; init; }
    public decimal MaxAmount { get; init; }
}

// ── 交接班 ──────────────────────────────────────────────

public class ShiftDto
{
    public int ShiftId { get; init; }
    public string ShiftNo { get; init; } = string.Empty;
    public string OperatorName { get; init; } = string.Empty;
    public DateTime OpenTime { get; init; }
    public DateTime? CloseTime { get; init; }
    public decimal OpeningCash { get; init; }
    public decimal CashCollected { get; init; }
    public decimal CashActual { get; init; }
    public decimal Difference { get; init; }
    public decimal AlipayAmount { get; init; }
    public decimal WechatAmount { get; init; }
    public decimal BankAmount { get; init; }
    public int OrderCount { get; init; }
    public int SettleCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Note { get; init; }
    ///<summary>进行中的班次：系统实时汇总（未交班时 CashCollected 等即为预估值）</summary>
    public bool IsCurrent { get; init; }
}

public class OpenShiftInput
{
    public decimal OpeningCash { get; init; }
}

public class CloseShiftInput
{
    [JsonRequired] public int ShiftId { get; init; }
    [JsonRequired] public decimal CashActual { get; init; }
    public string? Note { get; init; }
}

// ── 订单变更 ────────────────────────────────────────────

public class ChangeVehicleInput
{
    [JsonRequired] public int OrderId { get; init; }
    [JsonRequired] public int NewVehicleId { get; init; }
    public string? Reason { get; init; }
}

public class RescheduleInput
{
    [JsonRequired] public int OrderId { get; init; }
    [JsonRequired] public DateTime LeaseStart { get; init; }
    [JsonRequired] public DateTime LeaseEnd { get; init; }
    public string? Reason { get; init; }
}

public class OrderChangeDto
{
    public int ChangeId { get; init; }
    public int OrderId { get; init; }
    public string ChangeType { get; init; } = string.Empty;
    public int? OldVehicleId { get; init; }
    public int? NewVehicleId { get; init; }
    public DateTime? OldStart { get; init; }
    public DateTime? OldEnd { get; init; }
    public DateTime? NewStart { get; init; }
    public DateTime? NewEnd { get; init; }
    public decimal OldRent { get; init; }
    public decimal NewRent { get; init; }
    public decimal DiffAmount { get; init; }
    public string? Reason { get; init; }
    public DateTime CreateTime { get; init; }
}

// ── 车辆档案 ────────────────────────────────────────────

public class VehicleArchiveDto
{
    public int VehicleId { get; init; }
    public string VehicleNo { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string VehicleStatus { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string? FrameNumber { get; init; }
    public DateTime? BuyTime { get; init; }
    public decimal? BuyPrice { get; init; }
    public decimal? DailyRent { get; init; }
    public decimal? Deposit { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal MaintenanceCost { get; init; }
    public int OrderCount { get; init; }
    public List<VehicleArchiveEventDto> Events { get; init; } = [];
}

public class VehicleArchiveEventDto
{
    public DateTime Time { get; init; }
    ///<summary>租赁 / 维保 / 盘点 / 报废 / 验车</summary>
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public decimal? Amount { get; init; }
    public string? Status { get; init; }
}

// ── 电子合同 ────────────────────────────────────────────

public class ContractDto
{
    public int ContractId { get; init; }
    public string ContractNo { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string Version { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string? SignatureUrl { get; init; }
    public DateTime SignTime { get; init; }
    public string Status { get; init; } = string.Empty;
    ///<summary>合同正文（按当前订单快照生成，签署后以哈希为准）</summary>
    public string Content { get; init; } = string.Empty;
}

public class SignContractInput
{
    [JsonRequired] public int OrderId { get; init; }
    ///<summary>手写签名 PNG 的 data URL</summary>
    [JsonRequired] public string SignatureData { get; init; } = string.Empty;
}

///<summary>通用「启用/停用」状态变更入参</summary>
public class ChangeStatusDto
{
    [JsonRequired] public string Status { get; init; } = string.Empty;
}
