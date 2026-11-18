using System.Security.Claims;
using ERental.Authorization;
using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

/// <summary>
/// 运营中心：一个入口承载待办、金额审批、电子发票、预防保养和合同存证。
/// 页面不拆成多个菜单，接口仍按业务动作分别授权。
/// </summary>
[ApiController]
[Route("api/ops")]
[Authorize(Policy = Policies.AnyStaff)]
public class OpsCenterController(OpsCenterService svc) : ControllerBase
{
    private readonly OpsCenterService _svc = svc;
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    ///<summary>统一待办：超时未付、逾期、补缴、审批、维保、低库存、待开票</summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks()
        => Ok(new ApiResponse<OpsTaskSummaryDto> { Data = await _svc.GetTasksAsync() });

    // ── 审批 ──────────────────────────────────────────────

    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals([FromQuery] string? status)
        => Ok(new ApiResponse<List<OpsApprovalDto>> { Data = await _svc.ListApprovalsAsync(status) });

    [HttpPost("approvals")]
    [Authorize(Roles = "管理员,前台,财务")]
    public async Task<IActionResult> CreateApproval([FromBody] OpsApprovalInput input)
    {
        try
        {
            var data = await _svc.CreateApprovalAsync(input, CurrentUserId);
            return Ok(new ApiResponse<OpsApprovalDto> { Data = data, Message = "审批单已提交" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPost("approvals/{id:int}/decide")]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> DecideApproval(int id, [FromBody] OpsApprovalDecisionInput input)
    {
        try
        {
            var data = await _svc.DecideApprovalAsync(id, input, CurrentUserId);
            return Ok(new ApiResponse<OpsApprovalDto> { Data = data, Message = input.Approve ? "已通过" : "已驳回" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    // ── 发票 ──────────────────────────────────────────────

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices([FromQuery] string? status)
        => Ok(new ApiResponse<List<OpsInvoiceDto>> { Data = await _svc.ListInvoicesAsync(status) });

    [HttpPost("invoices")]
    [Authorize(Roles = "管理员,前台,财务")]
    public async Task<IActionResult> CreateInvoice([FromBody] OpsInvoiceInput input)
    {
        try
        {
            var data = await _svc.CreateInvoiceAsync(input);
            return Ok(new ApiResponse<OpsInvoiceDto> { Data = data, Message = "开票申请已提交" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPost("invoices/{id:int}/issue")]
    [Authorize(Roles = "管理员,财务")]
    public async Task<IActionResult> IssueInvoice(int id)
    {
        try
        {
            var data = await _svc.IssueInvoiceAsync(id);
            return Ok(new ApiResponse<OpsInvoiceDto> { Data = data, Message = "已标记开票" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPost("invoices/{id:int}/reject")]
    [Authorize(Roles = "管理员,财务")]
    public async Task<IActionResult> RejectInvoice(int id, [FromBody] OpsInvoiceActionInput input)
    {
        try
        {
            var data = await _svc.RejectInvoiceAsync(id, input.Note);
            return Ok(new ApiResponse<OpsInvoiceDto> { Data = data, Message = "已驳回" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPost("invoices/{id:int}/red")]
    [Authorize(Roles = "管理员,财务")]
    public async Task<IActionResult> RedInvoice(int id)
    {
        try
        {
            var data = await _svc.RedInvoiceAsync(id);
            return Ok(new ApiResponse<OpsInvoiceDto> { Data = data, Message = "已标记红冲" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    // ── 预防保养 ──────────────────────────────────────────

    [HttpGet("plans")]
    public async Task<IActionResult> Plans()
        => Ok(new ApiResponse<List<MaintenancePlanDto>> { Data = await _svc.ListPlansAsync() });

    [HttpPost("plans")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> CreatePlan([FromBody] MaintenancePlanInput input)
    {
        try
        {
            var data = await _svc.CreatePlanAsync(input);
            return Ok(new ApiResponse<MaintenancePlanDto> { Data = data, Message = "保养计划已创建" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPut("plans/{id:int}")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> UpdatePlan(int id, [FromBody] MaintenancePlanInput input)
    {
        try
        {
            var data = await _svc.UpdatePlanAsync(id, input);
            return Ok(new ApiResponse<MaintenancePlanDto> { Data = data, Message = "保养计划已更新" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPost("plans/{id:int}/status")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> SetPlanStatus(int id, [FromBody] OpsInvoiceActionInput input)
    {
        try
        {
            await _svc.SetPlanStatusAsync(id, input.Note == "停用" ? "停用" : "启用");
            return Ok(new ApiResponse<object> { Message = "状态已更新" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPost("plans/{id:int}/generate")]
    [Authorize(Roles = "管理员,前台")]
    public async Task<IActionResult> GenerateWorkOrder(int id)
    {
        try
        {
            var no = await _svc.GenerateWorkOrderAsync(id, CurrentUserId);
            return Ok(new ApiResponse<object> { Data = new { workOrderNo = no }, Message = "维保工单已生成" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    // ── 合同存证 ──────────────────────────────────────────

    [HttpGet("contracts")]
    public async Task<IActionResult> Contracts([FromQuery] string? q)
        => Ok(new ApiResponse<List<ContractEvidenceDto>> { Data = await _svc.ListContractsAsync(q) });

    [HttpGet("contracts/{id:int}/verify")]
    public async Task<IActionResult> VerifyContract(int id)
    {
        try
        {
            var ok = await _svc.VerifyContractAsync(id);
            return Ok(new ApiResponse<object> { Data = new { valid = ok }, Message = ok ? "哈希校验通过" : "哈希不一致" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }
}

