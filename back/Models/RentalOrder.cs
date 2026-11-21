using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ERental.Services;
using ERental.Data;

namespace ERental.Models;

[Table("rental_order")]
public class RentalOrder
{
    ///<summary>订单号长度上限</summary>
    private const int OrderNoMaxLength = 30;

    ///<summary>订单状态机白名单：待支付</summary>
    private static readonly string[] LegalStatuses = ["待支付", "已支付", "租赁中", "已完结", "已取消"];

    ///<summary>
    /// 合法状态迁移：只允许沿业务方向前进，禁止「已完结 → 租赁中」这类回退。
    /// 取消单独走 <see cref="Cancel"/>，因为它允许在待支付/已支付阶段发生。
    /// </summary>
    private static readonly Dictionary<string, string[]> NextStatuses = new(StringComparer.Ordinal)
    {
        ["待支付"] = ["已支付", "已取消"],
        ["已支付"] = ["租赁中", "已取消"],
        ["租赁中"] = ["已完结"],
        ["已完结"] = [],
        ["已取消"] = [],
    };

    ///<summary>状态值是否在白名单内</summary>
    private static bool IsValidStatus(string? value) => value is not null && Array.IndexOf(LegalStatuses, value) >= 0;

    [Key]
    [Column("order_id")]
    public int OrderId { get; init; }

    [Required, MaxLength(30)]
    [Column("order_no")]
    public string OrderNo { get; private set; } = string.Empty;

    [Column("customer_id")]
    public int CustomerId { get; private set; }

    [ForeignKey("CustomerId")]
    public Customer? Customer { get; init; }

    [Column("vehicle_id")]
    public int VehicleId { get; private set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; init; }

    [Column("lease_start")]
    public DateTime LeaseStart { get; private set; }

    [Column("lease_end")]
    public DateTime LeaseEnd { get; private set; }

    [Column("actual_return")]
    public DateTime? ActualReturn { get; private set; }

    [Column("total_rent", TypeName = "decimal(10,2)")]
    public decimal TotalRent { get; private set; }

    [Column("deposit", TypeName = "decimal(10,2)")]
    public decimal Deposit { get; private set; }

    ///<summary>优惠券 + 积分抵扣合计（券只抵租金，不抵押金）</summary>
    [Column("discount_amount", TypeName = "decimal(10,2)")]
    public decimal DiscountAmount { get; private set; }

    ///<summary>下单时的会员折扣率（固定下来，避免结算时按当前等级重算导致多退少补）</summary>
    [Column("rent_discount", TypeName = "decimal(3,2)")]
    public decimal RentDiscount { get; private set; } = 1.00m;

    ///<summary>增值服务费（保险/头盔/送车等），已并入 TotalRent</summary>
    [Column("addon_fee", TypeName = "decimal(10,2)")]
    public decimal AddonFee { get; private set; }

    ///<summary>
    /// 下单时固化的日租金快照。结算按它重算，车辆改价不会追溯修改已成立订单的账单。
    /// 可空仅为兼容历史订单（老单没有快照，只能回退当前价）。
    /// </summary>
    [Column("daily_rent", TypeName = "decimal(10,2)")]
    public decimal? DailyRent { get; private set; }

    ///<summary>
    /// 下单时生效的动态定价系数快照（1.00 = 无规则）。
    /// 下单时已乘进租金，结算必须按同一系数重算，否则活动期订单会按原价结算。
    /// </summary>
    [Column("price_factor", TypeName = "decimal(5,2)")]
    public decimal? PriceFactor { get; private set; }

    ///<summary>本单使用的顾客券（customer_coupon.id）</summary>
    [Column("customer_coupon_id")]
    public int? CustomerCouponId { get; private set; }

    ///<summary>本单抵扣的积分数</summary>
    [Column("points_used")]
    public int PointsUsed { get; private set; }

    ///<summary>实付租金 = 租金 + 增值服务 - 优惠抵扣，结算多退少补以它为基准</summary>
    [NotMapped]
    public decimal PrepaidRent => Math.Round(TotalRent - DiscountAmount, 2);

    ///<summary>订单状态</summary>
    [ConcurrencyCheck]
    [MaxLength(10)]
    [Column("order_status")]
    public string OrderStatus { get; private set; } = "待支付";

    [Column("create_time")]
    public DateTime CreateTime { get; init; } = AppClock.Now;

    /// <summary>客户端幂等键：同一请求重复提交时命中已有订单，防止双击/网络重试造成重复开单。</summary>
    [MaxLength(64)]
    [Column("request_key")]
    public string? RequestKey { get; private set; }

    [MaxLength(200)]
    [Column("cancel_reason")]
    public string? CancelReason { get; private set; }

    ///<summary>提前还车申请状态：null=未申请</summary>
    [MaxLength(20)]
    [Column("early_return_status")]
    public string? EarlyReturnStatus { get; private set; }

    [Column("operator_id")]
    public int? OperatorId { get; private set; }

    //支付/结算流水只给 EF 用
    private readonly List<PaymentRecord> _paymentRecords = [];
    [JsonIgnore] public IReadOnlyCollection<PaymentRecord> PaymentRecords => _paymentRecords;
    private readonly List<SettleRecord> _settleRecords = [];
    [JsonIgnore] public IReadOnlyCollection<SettleRecord> SettleRecords => _settleRecords;
    ///<summary>开单：订单号、客户、车辆、租期与金额在创建时一次定死</summary>
    public static RentalOrder Create(
        (string OrderNo, int CustomerId, int VehicleId, int? OperatorId) basic,
        (DateTime LeaseStart, DateTime LeaseEnd) lease,
        (decimal TotalRent, decimal Deposit) pricing,
        string orderStatus = "待支付")
    {
        var (orderNo, customerId, vehicleId, operatorId) = basic;
        var (leaseStart, leaseEnd) = lease;
        var (totalRent, deposit) = pricing;

        if (string.IsNullOrWhiteSpace(orderNo)) throw new BusinessException("订单号不能为空");
        if (orderNo.Trim().Length > OrderNoMaxLength)
            throw new BusinessException($"订单号不能超过 {OrderNoMaxLength} 位");
        if (customerId <= 0 || vehicleId <= 0) throw new BusinessException("订单必须关联有效的顾客与车辆");
        if (leaseEnd < leaseStart) throw new BusinessException("租期结束时间不能早于开始时间");
        if (totalRent < 0 || deposit < 0) throw new BusinessException("租金与押金不能为负数");
        if (!IsValidStatus(orderStatus)) throw new BusinessException("订单状态值非法：" + orderStatus);

        return new RentalOrder
        {
            OrderNo = orderNo.Trim(),
            CustomerId = customerId,
            VehicleId = vehicleId,
            LeaseStart = leaseStart,
            LeaseEnd = leaseEnd,
            TotalRent = totalRent,
            Deposit = deposit,
            OperatorId = operatorId,
            OrderStatus = orderStatus,
        };
    }

    ///<summary>绑定客户端幂等键；空值表示不使用幂等（例如后台脚本导入）。</summary>
    public void SetRequestKey(string? requestKey)
    {
        if (string.IsNullOrWhiteSpace(requestKey))
        {
            RequestKey = null;
            return;
        }

        var key = requestKey.Trim();
        if (key.Length > 64) throw new BusinessException("幂等键不能超过 64 位");
        RequestKey = key;
    }

    ///<summary>写入下单时的定价快照：日租金取车辆当前价（留空已回退全局配置），系数取下单时生效规则</summary>
    public void SetPricingSnapshot(decimal dailyRent, decimal priceFactor)
    {
        if (dailyRent < 0) throw new BusinessException("日租金不能为负数");
        if (priceFactor is < 0.01m or > 10m) throw new BusinessException("动态定价系数需在 0.01 ~ 10 之间");

        DailyRent = Math.Round(dailyRent, 2);
        PriceFactor = Math.Round(priceFactor, 2);
    }

    ///<summary>写入下单时的优惠与增值服务构成（券+积分只能抵租金，不能超过租金）</summary>
    public void ApplyBenefits(decimal discountAmount, decimal rentDiscount, decimal addonFee, int? customerCouponId, int pointsUsed)
    {
        if (discountAmount < 0 || addonFee < 0) throw new BusinessException("优惠与增值服务金额不能为负数");
        if (rentDiscount is < 0.5m or > 1m) throw new BusinessException("会员折扣需在 0.5 ~ 1 之间");
        if (pointsUsed < 0) throw new BusinessException("抵扣积分不能为负数");

        var rentOnly = Math.Round(TotalRent - addonFee, 2);
        if (rentOnly < 0) throw new BusinessException("增值服务费不能超过订单总额");
        if (discountAmount > rentOnly + 0.01m) throw new BusinessException("优惠抵扣不能超过租金");

        DiscountAmount = Math.Round(discountAmount, 2);
        RentDiscount = rentDiscount;
        AddonFee = Math.Round(addonFee, 2);
        CustomerCouponId = customerCouponId;
        PointsUsed = pointsUsed;
    }

    ///<summary>改期：重算租金，增值服务费保持不变</summary>
    public void Reschedule(DateTime start, DateTime end, decimal rentBeforeAddon)
    {
        if (end <= start) throw new BusinessException("还车时间必须晚于起租时间");
        LeaseStart = start;
        LeaseEnd = end;
        ApplyRent(rentBeforeAddon);
    }

    ///<summary>换车：只换车辆与重算租金，租期与优惠构成不动</summary>
    public void SwapVehicle(int vehicleId, decimal rentBeforeAddon)
    {
        if (vehicleId <= 0) throw new BusinessException("换车必须指定有效车辆");
        VehicleId = vehicleId;
        ApplyRent(rentBeforeAddon);
    }

    ///<summary>按「不含增值服务的租金」重建总额；优惠超过新租金时自动收敛到租金上限</summary>
    private void ApplyRent(decimal rentBeforeAddon)
    {
        if (rentBeforeAddon < 0) throw new BusinessException("租金不能为负数");
        TotalRent = Math.Round(rentBeforeAddon + AddonFee, 2);
        var rentOnly = Math.Round(TotalRent - AddonFee, 2);
        if (DiscountAmount > rentOnly) DiscountAmount = rentOnly;
    }

    ///<summary>状态迁移（待支付 → 已支付 → 租赁中 → 已完结/已取消），非法迁移直接拒绝</summary>
    public void ChangeStatus(string status)
    {
        if (!IsValidStatus(status)) throw new BusinessException("订单状态值非法：" + status);
        if (status == OrderStatus) return;
        var allowed = NextStatuses.GetValueOrDefault(OrderStatus) ?? [];
        if (!allowed.Contains(status, StringComparer.Ordinal))
            throw new BusinessException($"订单不能从「{OrderStatus}」直接变为「{status}」");
        OrderStatus = status;
    }

    ///<summary>取消订单：置「已取消」并留下原因，便于对账时区分「顾客取消」「超时回收」「支付失败」</summary>
    public void Cancel(string? reason)
    {
        OrderStatus = "已取消";
        CancelReason = string.IsNullOrWhiteSpace(reason) ? CancelReason : reason.Trim();
    }

    ///<summary>还车结算完成：写实际归还时间并把订单置为「已完结」（与结算单在同一事务内落库）</summary>
    public void MarkReturned(DateTime actualReturn)
    {
        ActualReturn = actualReturn;
        OrderStatus = "已完结";
    }

    ///<summary>顾客提交提前还车申请：未申请（null）→ 待审核</summary>
    public void RequestEarlyReturn() => EarlyReturnStatus = "待审核";

    ///<summary>前台审核提前还车：通过写空串（已处理，按实际用车时长结算）</summary>
    public void ReviewEarlyReturn(bool approve, string? rejectReason = null)
    {
        EarlyReturnStatus = approve ? string.Empty : null;
        if (!approve && !string.IsNullOrWhiteSpace(rejectReason)) CancelReason = rejectReason.Trim();
    }

    ///<summary>结算时清掉已处理的提前还车标记</summary>
    public void ClearEarlyReturnFlag() => EarlyReturnStatus = string.Empty;
}
