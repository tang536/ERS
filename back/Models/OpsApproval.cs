using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

/// <summary>
/// 金额类操作审批：退款、免押、改价、损坏费减免等先落审批单，再执行资金动作。
/// 这里不直接改支付流水，审批通过由业务人员在原页面执行，保证原有账务口径不变。
/// </summary>
[Table("ops_approval")]
public class OpsApproval
{
    public const string StatusPending = "待审批";
    public const string StatusApproved = "已通过";
    public const string StatusRejected = "已驳回";
    public const string StatusCancelled = "已撤销";

    private static readonly string[] LegalStatuses = [StatusPending, StatusApproved, StatusRejected, StatusCancelled];

    [Key]
    [Column("approval_id")]
    public int ApprovalId { get; init; }

    [Required, MaxLength(32)]
    [Column("request_no")]
    public string RequestNo { get; private set; } = string.Empty;

    [Required, MaxLength(20)]
    [Column("biz_type")]
    public string BizType { get; private set; } = string.Empty;

    [MaxLength(20)]
    [Column("ref_type")]
    public string? RefType { get; private set; }

    [MaxLength(60)]
    [Column("ref_id")]
    public string? RefId { get; private set; }

    [Column("order_id")]
    public int? OrderId { get; private set; }

    [Column("amount", TypeName = "decimal(10,2)")]
    public decimal Amount { get; private set; }

    [MaxLength(500)]
    [Column("reason")]
    public string Reason { get; private set; } = string.Empty;

    [MaxLength(20)]
    [Column("status")]
    public string Status { get; private set; } = StatusPending;

    [Column("applicant_id")]
    public int ApplicantId { get; private set; }

    [Column("approver_id")]
    public int? ApproverId { get; private set; }

    [Column("approve_time")]
    public DateTime? ApproveTime { get; private set; }

    [MaxLength(500)]
    [Column("approve_note")]
    public string? ApproveNote { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; private set; } = AppClock.Now;

    public static OpsApproval Create(string requestNo, string bizType, decimal amount, string reason,
        int applicantId, int? orderId = null, string? refType = null, string? refId = null)
    {
        if (string.IsNullOrWhiteSpace(requestNo)) throw new BusinessException("审批单号不能为空");
        if (string.IsNullOrWhiteSpace(bizType)) throw new BusinessException("审批业务类型不能为空");
        if (amount < 0) throw new BusinessException("审批金额不能为负数");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("请填写审批原因");
        if (applicantId <= 0) throw new BusinessException("申请人无效");

        return new OpsApproval
        {
            RequestNo = requestNo.Trim(),
            BizType = bizType.Trim(),
            Amount = decimal.Round(amount, 2),
            Reason = reason.Trim(),
            ApplicantId = applicantId,
            OrderId = orderId,
            RefType = string.IsNullOrWhiteSpace(refType) ? null : refType.Trim(),
            RefId = string.IsNullOrWhiteSpace(refId) ? null : refId.Trim(),
        };
    }

    public void Decide(bool approve, int approverId, string? note)
    {
        if (Status != StatusPending) throw new BusinessException("该审批单已处理，不能重复操作");
        if (approverId <= 0) throw new BusinessException("审批人无效");
        Status = approve ? StatusApproved : StatusRejected;
        ApproverId = approverId;
        ApproveTime = AppClock.Now;
        ApproveNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public void Cancel()
    {
        if (Status != StatusPending) throw new BusinessException("只能撤销待审批的单据");
        Status = StatusCancelled;
    }

    public static bool IsLegalStatus(string status) => LegalStatuses.Contains(status);
}
