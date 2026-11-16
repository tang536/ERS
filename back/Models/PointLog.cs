using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;

namespace ERental.Models;

///<summary>
/// 积分流水：只增不改，余额随每次变动落快照，便于对账与顾客申诉。
/// 规则：结算实付租金 1 元得 1 分；100 分抵 1 元，单笔最多抵订单租金的 10%。
///</summary>
[Table("point_log")]
public class PointLog
{
    public const string SourceSettle = "结算奖励";
    public const string SourceRedeem = "下单抵扣";
    public const string SourceRefund = "取消退回";
    public const string SourceAdjust = "人工调整";

    [Key]
    [Column("id")]
    public int Id { get; init; }

    [Column("customer_id")]
    public int CustomerId { get; private set; }

    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; init; }

    ///<summary>正数获得、负数消耗</summary>
    [Column("change_amount")]
    public int ChangeAmount { get; private set; }

    ///<summary>本次变动后的余额</summary>
    [Column("balance")]
    public int Balance { get; private set; }

    [MaxLength(20)]
    [Column("source")]
    public string Source { get; private set; } = SourceSettle;

    [Column("order_id")]
    public int? OrderId { get; private set; }

    [MaxLength(200)]
    [Column("remark")]
    public string? Remark { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; init; } = AppClock.Now;

    public static PointLog Create(int customerId, int change, int balance, string source, int? orderId, string? remark) => new()
    {
        CustomerId = customerId,
        ChangeAmount = change,
        Balance = balance,
        Source = source,
        OrderId = orderId,
        Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
    };
}
