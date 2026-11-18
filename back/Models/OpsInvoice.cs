using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

/// <summary>
/// 电子发票申请：门店只登记抬头、税号、金额与接收邮箱，实际开票由财务在外部开票系统完成。
/// 状态保留「待开票 / 已开票 / 已驳回 / 已红冲」，不伪造发票号码。
/// </summary>
[Table("ops_invoice")]
public class OpsInvoice
{
    public const string StatusPending = "待开票";
    public const string StatusIssued = "已开票";
    public const string StatusRejected = "已驳回";
    public const string StatusRed = "已红冲";

    private static readonly string[] LegalStatuses = [StatusPending, StatusIssued, StatusRejected, StatusRed];

    [Key]
    [Column("invoice_id")]
    public int InvoiceId { get; init; }

    [Required, MaxLength(40)]
    [Column("invoice_no")]
    public string InvoiceNo { get; private set; } = string.Empty;

    [Column("order_id")]
    public int OrderId { get; private set; }

    [ForeignKey(nameof(OrderId))]
    public RentalOrder? RentalOrder { get; init; }

    [Column("customer_id")]
    public int CustomerId { get; private set; }

    [MaxLength(100)]
    [Column("title")]
    public string Title { get; private set; } = string.Empty;

    [MaxLength(50)]
    [Column("tax_no")]
    public string TaxNo { get; private set; } = string.Empty;

    [MaxLength(20)]
    [Column("invoice_type")]
    public string InvoiceType { get; private set; } = "电子普通发票";

    [Column("amount", TypeName = "decimal(10,2)")]
    public decimal Amount { get; private set; }

    [MaxLength(120)]
    [Column("email")]
    public string Email { get; private set; } = string.Empty;

    [MaxLength(20)]
    [Column("status")]
    public string Status { get; private set; } = StatusPending;

    [MaxLength(300)]
    [Column("remark")]
    public string? Remark { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; private set; } = AppClock.Now;

    [Column("issue_time")]
    public DateTime? IssueTime { get; private set; }

    public static OpsInvoice Create(string invoiceNo, int orderId, int customerId, string title, string taxNo,
        string invoiceType, decimal amount, string email, string? remark)
    {
        if (string.IsNullOrWhiteSpace(invoiceNo)) throw new BusinessException("发票申请单号不能为空");
        if (orderId <= 0) throw new BusinessException("发票必须关联订单");
        if (customerId <= 0) throw new BusinessException("发票必须关联客户");
        if (string.IsNullOrWhiteSpace(title)) throw new BusinessException("请填写发票抬头");
        if (invoiceType is not ("电子普通发票" or "增值税专用发票")) throw new BusinessException("发票类型不支持");
        // 个人电子普票可以没有税号；专用发票必须填纳税人识别号
        if (invoiceType == "增值税专用发票" && string.IsNullOrWhiteSpace(taxNo))
            throw new BusinessException("专用发票必须填写纳税人识别号");
        if (amount <= 0) throw new BusinessException("开票金额必须大于 0");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw new BusinessException("请填写有效的接收邮箱");

        return new OpsInvoice
        {
            InvoiceNo = invoiceNo.Trim(),
            OrderId = orderId,
            CustomerId = customerId,
            Title = title.Trim(),
            TaxNo = taxNo.Trim(),
            InvoiceType = invoiceType,
            Amount = decimal.Round(amount, 2),
            Email = email.Trim(),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    public void Issue(DateTime time)
    {
        if (Status != StatusPending) throw new BusinessException("只有待开票的申请可以开票");
        Status = StatusIssued;
        IssueTime = time;
    }

    public void Reject(string? reason)
    {
        if (Status != StatusPending) throw new BusinessException("只有待开票的申请可以驳回");
        Status = StatusRejected;
        Remark = string.IsNullOrWhiteSpace(reason) ? Remark : reason.Trim();
    }

    public void Red()
    {
        if (Status != StatusIssued) throw new BusinessException("只有已开票的申请可以红冲");
        Status = StatusRed;
    }

    public static bool IsLegalStatus(string status) => LegalStatuses.Contains(status);
}
