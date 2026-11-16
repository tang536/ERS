using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

///<summary>
/// 顾客持有的券：从模板发放出来后是独立一行，模板改价不影响已领出的券。
/// 状态只允许 未使用 → 已使用 / 已过期，使用后不可撤销（取消订单时由服务层释放回未使用）。
///</summary>
[Table("customer_coupon")]
public class CustomerCoupon
{
    public const string StatusUnused = "未使用";
    public const string StatusUsed = "已使用";
    public const string StatusExpired = "已过期";

    [Key]
    [Column("id")]
    public int Id { get; init; }

    [Column("coupon_id")]
    public int CouponId { get; private set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; init; }

    [Column("customer_id")]
    public int CustomerId { get; private set; }

    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; init; }

    [MaxLength(10)]
    [Column("status")]
    public string Status { get; private set; } = StatusUnused;

    [Column("receive_time")]
    public DateTime ReceiveTime { get; init; } = AppClock.Now;

    [Column("expire_time")]
    public DateTime ExpireTime { get; private set; }

    [Column("use_time")]
    public DateTime? UseTime { get; private set; }

    [Column("order_id")]
    public int? OrderId { get; private set; }

    ///<summary>
    /// 单张券的唯一码值：顾客端据此渲染二维码，前台扫码核销。
    /// 因为码值直接挂在 customer_coupon 这一行上，扫码解出来的券天然带 customer_id，
    /// 「有二维码的券一定绑定顾客」由数据模型保证，而不是靠界面约定。
    ///</summary>
    [MaxLength(24)]
    [Column("code")]
    public string? Code { get; private set; }

    ///<summary>发券：过期时间取模板失效时间；码值由服务层统一生成（唯一索引兜底）</summary>
    public static CustomerCoupon Issue(int couponId, int customerId, DateTime expireTime, string? code = null) => new()
    {
        CouponId = couponId,
        CustomerId = customerId,
        ExpireTime = expireTime,
        Status = StatusUnused,
        Code = code,
    };

    ///<summary>补码：历史数据没有码值时按规则补上（只在服务层调用）</summary>
    public void AssignCode(string code) => Code = code;

    ///<summary>核销：订单提交时调用，记录用券订单与时间</summary>
    public void Use(int orderId, DateTime now)
    {
        if (Status != StatusUnused) throw new BusinessException("该券已使用或已过期");
        Status = StatusUsed;
        OrderId = orderId;
        UseTime = now;
    }

    ///<summary>订单取消/超时回收：退回未使用，过期时间沿用原值</summary>
    public void Release()
    {
        if (Status != StatusUsed) return;
        Status = StatusUnused;
        OrderId = null;
        UseTime = null;
    }

    ///<summary>到期作废（读取时顺带刷新，避免依赖定时任务）</summary>
    public void ExpireIfNeeded(DateTime now)
    {
        if (Status == StatusUnused && ExpireTime < now) Status = StatusExpired;
    }
}
