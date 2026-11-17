using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;

namespace ERental.Models;

/// <summary>
/// 支付渠道对账流水：记录「渠道查单补账」和「对不上的差异」，供管理员事后核对。
///
/// 这是一张运维表，不参与业务备份清单（与 pay_notify_log 同口径）：
/// 它记的是「系统与通道之间的差异」，恢复业务数据时不需要把旧差异一并带回。
/// </summary>
[Table("pay_reconcile_log")]
public class PayReconcileLog
{
    private const int DetailMaxLength = 200;
    private const int ChannelMaxLength = 10;
    private const int OrderNoMaxLength = 64;
    private const int StatusMaxLength = 20;
    private const int ActionMaxLength = 20;

    [Key]
    [Column("id")]
    public long Id { get; init; }

    ///<summary>渠道名（支付宝/微信/银行卡）；巡检汇总行为「系统」</summary>
    [MaxLength(ChannelMaxLength)]
    [Column("channel")]
    public string Channel { get; private set; } = string.Empty;

    ///<summary>通道侧商户订单号（补缴单为 SP- 单号，首付款为租赁订单号）</summary>
    [MaxLength(OrderNoMaxLength)]
    [Column("order_no")]
    public string OrderNo { get; private set; } = string.Empty;

    ///<summary>本地支付流水 ID；巡检汇总行为空</summary>
    [Column("pay_id")]
    public int? PayId { get; private set; }

    ///<summary>处理前的本地状态</summary>
    [MaxLength(StatusMaxLength)]
    [Column("local_status")]
    public string LocalStatus { get; private set; } = string.Empty;

    ///<summary>渠道返回的状态（拿不到时为 NULL）</summary>
    [MaxLength(StatusMaxLength)]
    [Column("channel_status")]
    public string? ChannelStatus { get; private set; }

    [Column("local_amount", TypeName = "decimal(10,2)")]
    public decimal LocalAmount { get; private set; }

    [Column("channel_amount", TypeName = "decimal(10,2)")]
    public decimal? ChannelAmount { get; private set; }

    ///<summary>处理动作：已补账 / 待人工核对 / 巡检</summary>
    [MaxLength(ActionMaxLength)]
    [Column("action")]
    public string Action { get; private set; } = string.Empty;

    [MaxLength(DetailMaxLength)]
    [Column("detail")]
    public string Detail { get; private set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; private set; } = AppClock.Now;

    ///<summary>新建一条对账记录：文本列统一去空格并按列宽截断，避免脏数据把整批写入拖垮</summary>
    public static PayReconcileLog Create(string channel, string? orderNo, int? payId, string? localStatus,
        string? channelStatus, decimal localAmount, decimal? channelAmount, string action, string? detail) => new()
        {
            Channel = Cut(channel, ChannelMaxLength),
            OrderNo = Cut(orderNo, OrderNoMaxLength),
            PayId = payId,
            LocalStatus = Cut(localStatus, StatusMaxLength),
            ChannelStatus = string.IsNullOrWhiteSpace(channelStatus) ? null : Cut(channelStatus, StatusMaxLength),
            LocalAmount = localAmount,
            ChannelAmount = channelAmount,
            Action = Cut(action, ActionMaxLength),
            Detail = Cut(detail, DetailMaxLength),
            CreatedAt = AppClock.Now
        };

    private static string Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
