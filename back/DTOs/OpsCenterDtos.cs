using System.Text.Json.Serialization;

namespace ERental.DTOs;

/// <summary>运营中心待办：所有异常、催收、退款、审批、保养共用一条展示结构，前端只渲一张列表。</summary>
public class OpsTaskDto
{
    /// <summary>分类：payment / supplement / overdue / approval / maintenance / stock / invoice</summary>
    public string Kind { get; init; } = string.Empty;
    /// <summary>严重程度：danger / warning / info</summary>
    public string Severity { get; init; } = "info";
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public int? OrderId { get; init; }
    public string? OrderNo { get; init; }
    public string? RefId { get; init; }
    /// <summary>建议跳转的前端路径；没有就留空</summary>
    public string? Link { get; init; }
    public DateTime? DueTime { get; init; }
    public DateTime CreateTime { get; init; }
}

public class OpsTaskSummaryDto
{
    public int Total { get; init; }
    public int Danger { get; init; }
    public int Warning { get; init; }
    public Dictionary<string, int> ByKind { get; init; } = [];
    public List<OpsTaskDto> Items { get; init; } = [];
}

public class OpsApprovalDto
{
    public int ApprovalId { get; init; }
    public string RequestNo { get; init; } = string.Empty;
    public string BizType { get; init; } = string.Empty;
    public string? RefType { get; init; }
    public string? RefId { get; init; }
    public int? OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ApplicantId { get; init; }
    public int? ApproverId { get; init; }
    public DateTime? ApproveTime { get; init; }
    public string? ApproveNote { get; init; }
    public DateTime CreateTime { get; init; }
}

public class OpsApprovalInput
{
    [JsonRequired] public string BizType { get; init; } = string.Empty;
    [JsonRequired] public decimal Amount { get; init; }
    [JsonRequired] public string Reason { get; init; } = string.Empty;
    public int? OrderId { get; init; }
    public string? RefType { get; init; }
    public string? RefId { get; init; }
}

public class OpsApprovalDecisionInput
{
    public bool Approve { get; init; }
    public string? Note { get; init; }
}

public class OpsInvoiceDto
{
    public int InvoiceId { get; init; }
    public string InvoiceNo { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string? OrderNo { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string TaxNo { get; init; } = string.Empty;
    public string InvoiceType { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Remark { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime? IssueTime { get; init; }
}

public class OpsInvoiceInput
{
    [JsonRequired] public int OrderId { get; init; }
    [JsonRequired] public string Title { get; init; } = string.Empty;
    [JsonRequired] public string TaxNo { get; init; } = string.Empty;
    public string InvoiceType { get; init; } = "电子普通发票";
    [JsonRequired] public decimal Amount { get; init; }
    [JsonRequired] public string Email { get; init; } = string.Empty;
    public string? Remark { get; init; }
}

public class OpsInvoiceActionInput
{
    public string? Note { get; init; }
}

public class MaintenancePlanDto
{
    public int PlanId { get; init; }
    public int VehicleId { get; init; }
    public string? VehicleNo { get; init; }
    public string? Brand { get; init; }
    public string PlanName { get; init; } = string.Empty;
    public string TriggerType { get; init; } = string.Empty;
    public int TriggerValue { get; init; }
    public DateTime? LastServiceTime { get; init; }
    public int LastMileage { get; init; }
    public int CurrentMileage { get; init; }
    public DateTime? NextDueTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Remark { get; init; }
    public bool Due { get; init; }
    public int MileageSinceService { get; init; }
    public DateTime CreateTime { get; init; }
}

public class MaintenancePlanInput
{
    [JsonRequired] public int VehicleId { get; init; }
    [JsonRequired] public string PlanName { get; init; } = string.Empty;
    [JsonRequired] public string TriggerType { get; init; } = "按天数";
    [JsonRequired] public int TriggerValue { get; init; }
    public DateTime? LastServiceTime { get; init; }
    public int LastMileage { get; init; }
    public int CurrentMileage { get; init; }
    public string? Remark { get; init; }
}

public class ContractEvidenceDto
{
    public int ContractId { get; init; }
    public string ContractNo { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string? OrderNo { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string Version { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string? SignatureUrl { get; init; }
    public DateTime SignTime { get; init; }
    public string? SignIp { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool HashValid { get; init; }
    public int ContentLength { get; init; }
}
