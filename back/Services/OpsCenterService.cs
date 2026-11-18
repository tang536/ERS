using System.Security.Cryptography;
using System.Text;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

/// <summary>
/// 运营中心：把分散在订单、支付、维保、券、合同里的待办收成一张列表，
/// 同时承载审批、发票、预防保养三个轻量工作流。页面只保留一个入口，避免侧边栏膨胀。
/// </summary>
public class OpsCenterService(AppDbContext db, MaintenanceService maintenance, PaymentService payments)
{
    private readonly AppDbContext _db = db;
    private readonly MaintenanceService _maintenance = maintenance;
    private readonly PaymentService _payments = payments;

    private const int PendingPayMinutes = 15;

    // ── 待办 ──────────────────────────────────────────────

    public async Task<OpsTaskSummaryDto> GetTasksAsync()
    {
        var now = AppClock.Now;
        var items = new List<OpsTaskDto>();

        // 超时未支付：待支付且创建超过 15 分钟，直接给出回收入口
        var pending = await (from o in _db.RentalOrders.AsNoTracking()
                             join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.CustomerId into customers
                             from c in customers.DefaultIfEmpty()
                             where o.OrderStatus == "待支付" && o.CreateTime < now.AddMinutes(-PendingPayMinutes)
                             orderby o.CreateTime
                             select new { o.OrderId, o.OrderNo, CustomerName = c != null ? c.FullName : null, o.CreateTime, o.TotalRent, o.Deposit })
            .Take(50).ToListAsync();
        foreach (var o in pending)
        {
            items.Add(new OpsTaskDto
            {
                Kind = "payment", Severity = "warning", Title = $"待支付超时 · {o.OrderNo}",
                Subtitle = $"{o.CustomerName ?? "客户"} 已等待超过 {PendingPayMinutes} 分钟，可回收车辆",
                Amount = o.TotalRent + o.Deposit, OrderId = o.OrderId, OrderNo = o.OrderNo,
                Link = "/orders", CreateTime = o.CreateTime,
            });
        }

        // 逾期未还：租赁中且已过租期
        var overdue = await (from o in _db.RentalOrders.AsNoTracking()
                             join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.CustomerId into customers
                             from c in customers.DefaultIfEmpty()
                             where o.OrderStatus == "租赁中" && o.LeaseEnd < now
                             orderby o.LeaseEnd
                             select new { o.OrderId, o.OrderNo, CustomerName = c != null ? c.FullName : null, o.LeaseEnd, o.TotalRent })
            .Take(50).ToListAsync();
        foreach (var o in overdue)
        {
            var hours = Math.Max(1, (int)Math.Ceiling((now - o.LeaseEnd).TotalHours));
            items.Add(new OpsTaskDto
            {
                Kind = "overdue", Severity = "danger", Title = $"逾期未还 · {o.OrderNo}",
                Subtitle = $"{o.CustomerName ?? "客户"} 已逾期约 {hours} 小时，应尽快联系归还",
                Amount = o.TotalRent, OrderId = o.OrderId, OrderNo = o.OrderNo,
                DueTime = o.LeaseEnd, CreateTime = o.LeaseEnd,
            });
        }

        // 未收清补缴：补缴单仍待支付
        var supplements = await _db.PaymentRecords.AsNoTracking()
            .Where(p => p.BizType == "supplement" && p.PayStatus == "待支付")
            .OrderByDescending(p => p.PayId).Take(50).ToListAsync();
        foreach (var p in supplements)
        {
            items.Add(new OpsTaskDto
            {
                Kind = "supplement", Severity = "warning", Title = "待收补缴款",
                Subtitle = $"补缴单 #{p.PayId} · 订单 #{p.OrderId}",
                Amount = p.PayAmount, OrderId = p.OrderId, RefId = p.PayId.ToString(),
                Link = "/settlements", CreateTime = p.PayTime ?? now,
            });
        }

        // 待审批金额
        var approvals = await _db.OpsApprovals.AsNoTracking()
            .Where(a => a.Status == OpsApproval.StatusPending)
            .OrderByDescending(a => a.CreateTime).Take(50).ToListAsync();
        foreach (var a in approvals)
        {
            items.Add(new OpsTaskDto
            {
                Kind = "approval", Severity = a.Amount >= 500 ? "danger" : "warning",
                Title = $"{a.BizType}待审批 · {a.RequestNo}",
                Subtitle = a.Reason, Amount = a.Amount, OrderId = a.OrderId,
                RefId = a.ApprovalId.ToString(), Link = "/ops?tab=approval", CreateTime = a.CreateTime,
            });
        }

        // 待处理维保工单
        var workOrders = await (from m in _db.MaintenanceOrders.AsNoTracking()
                                join v in _db.Vehicles.AsNoTracking() on m.VehicleId equals v.VehicleId into vehicles
                                from v in vehicles.DefaultIfEmpty()
                                where m.Status == "待处理"
                                orderby m.CreateTime
                                select new { m.WorkOrderId, m.WorkOrderNo, VehicleNo = v != null ? v.VehicleNo : null, m.FaultType, m.CreateTime })
            .Take(50).ToListAsync();
        foreach (var m in workOrders)
        {
            items.Add(new OpsTaskDto
            {
                Kind = "maintenance", Severity = "warning", Title = $"维保待处理 · {m.WorkOrderNo}",
                Subtitle = $"{m.VehicleNo ?? "车辆"} · {m.FaultType ?? "故障"}",
                RefId = m.WorkOrderId.ToString(), Link = "/vehicles?view=maintenance", CreateTime = m.CreateTime,
            });
        }

        // 预防保养到期
        var plans = await LoadPlansAsync();
        foreach (var p in plans.Where(p => p.Due))
        {
            items.Add(new OpsTaskDto
            {
                Kind = "maintenance", Severity = "warning", Title = $"保养到期 · {p.VehicleNo}",
                Subtitle = $"{p.PlanName}（{p.TriggerType} {p.TriggerValue}）",
                RefId = p.PlanId.ToString(), Link = "/ops?tab=vehicle", CreateTime = p.CreateTime,
            });
        }

        // 低库存增值服务
        var lowStock = await _db.AddonItems.AsNoTracking()
            .Where(a => a.Status == "启用" && a.StockQty >= 0 && a.LowStockThreshold > 0 && a.StockQty <= a.LowStockThreshold)
            .OrderBy(a => a.StockQty).Take(30).ToListAsync();
        foreach (var a in lowStock)
        {
            items.Add(new OpsTaskDto
            {
                Kind = "stock", Severity = a.StockQty == 0 ? "danger" : "warning",
                Title = $"库存不足 · {a.Name}", Subtitle = $"剩余 {a.StockQty} 件，阈值 {a.LowStockThreshold}",
                RefId = a.AddonId.ToString(), Link = "/operations", CreateTime = a.CreateTime,
            });
        }

        // 待开票
        var invoices = await _db.OpsInvoices.AsNoTracking()
            .Where(i => i.Status == OpsInvoice.StatusPending)
            .OrderByDescending(i => i.CreateTime).Take(30).ToListAsync();
        foreach (var i in invoices)
        {
            items.Add(new OpsTaskDto
            {
                Kind = "invoice", Severity = "info", Title = $"待开票 · {i.InvoiceNo}",
                Subtitle = $"{i.Title} · {i.Email}", Amount = i.Amount,
                RefId = i.InvoiceId.ToString(), Link = "/ops?tab=doc", CreateTime = i.CreateTime,
            });
        }

        var ordered = items.OrderByDescending(t => t.Severity == "danger")
            .ThenByDescending(t => t.CreateTime).ToList();
        return new OpsTaskSummaryDto
        {
            Total = ordered.Count,
            Danger = ordered.Count(t => t.Severity == "danger"),
            Warning = ordered.Count(t => t.Severity == "warning"),
            ByKind = ordered.GroupBy(t => t.Kind).ToDictionary(g => g.Key, g => g.Count()),
            Items = ordered,
        };
    }

    // ── 审批 ──────────────────────────────────────────────

    public async Task<List<OpsApprovalDto>> ListApprovalsAsync(string? status = null)
    {
        var q = _db.OpsApprovals.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        return await q.OrderByDescending(a => a.CreateTime).Take(Paging.MaxUnpagedRows)
            .Select(a => ToApprovalDto(a)).ToListAsync();
    }

    public async Task<OpsApprovalDto> CreateApprovalAsync(OpsApprovalInput input, int userId)
    {
        if (string.IsNullOrWhiteSpace(input.BizType)) throw new BusinessException("请选择审批业务类型");
        var approval = OpsApproval.Create(BusinessNumber.New("SP"), input.BizType, input.Amount,
            input.Reason, userId, input.OrderId, input.RefType, input.RefId);
        _db.OpsApprovals.Add(approval);
        await _db.SaveChangesAsync();
        return ToApprovalDto(approval);
    }

    public async Task<OpsApprovalDto> DecideApprovalAsync(int id, OpsApprovalDecisionInput input, int userId)
    {
        var approval = await _db.OpsApprovals.FindAsync(id) ?? throw new BusinessException("审批单不存在");
        // 审批通过即执行资金动作：refType=payment 时按支付流水原路退款。
        // 退款失败不落审批结果，避免出现「审批已通过但钱没退」的悬空状态。
        if (input.Approve && approval.RefType == "payment" && int.TryParse(approval.RefId, out var payId))
        {
            var refund = await _payments.Refund(payId, approval.Amount, approval.Reason);
            if (refund.RefundedTotal <= 0) throw new BusinessException(refund.Message);
        }
        approval.Decide(input.Approve, userId, input.Note);
        await _db.SaveChangesAsync();
        return ToApprovalDto(approval);
    }

    // ── 发票 ──────────────────────────────────────────────

    public async Task<List<OpsInvoiceDto>> ListInvoicesAsync(string? status = null)
    {
        var q = from i in _db.OpsInvoices.AsNoTracking()
                join o in _db.RentalOrders.AsNoTracking() on i.OrderId equals o.OrderId into orders
                from o in orders.DefaultIfEmpty()
                join c in _db.Customers.AsNoTracking() on i.CustomerId equals c.CustomerId into customers
                from c in customers.DefaultIfEmpty()
                select new { i, o, c };
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.i.Status == status);
        return await q.OrderByDescending(x => x.i.CreateTime).Take(Paging.MaxUnpagedRows)
            .Select(x => new OpsInvoiceDto
            {
                InvoiceId = x.i.InvoiceId, InvoiceNo = x.i.InvoiceNo, OrderId = x.i.OrderId,
                OrderNo = x.o != null ? x.o.OrderNo : null,
                CustomerId = x.i.CustomerId, CustomerName = x.c != null ? x.c.FullName : null,
                Title = x.i.Title, TaxNo = x.i.TaxNo, InvoiceType = x.i.InvoiceType,
                Amount = x.i.Amount, Email = x.i.Email, Status = x.i.Status, Remark = x.i.Remark,
                CreateTime = x.i.CreateTime, IssueTime = x.i.IssueTime,
            }).ToListAsync();
    }

    public async Task<OpsInvoiceDto> CreateInvoiceAsync(OpsInvoiceInput input)
    {
        var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == input.OrderId)
            ?? throw new BusinessException("订单不存在");
        var invoice = OpsInvoice.Create(BusinessNumber.New("FP"), input.OrderId, order.CustomerId,
            input.Title, input.TaxNo, input.InvoiceType, input.Amount, input.Email, input.Remark);
        _db.OpsInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return (await ListInvoicesAsync()).First(x => x.InvoiceId == invoice.InvoiceId);
    }

    public async Task<OpsInvoiceDto> IssueInvoiceAsync(int id)
    {
        var invoice = await _db.OpsInvoices.FindAsync(id) ?? throw new BusinessException("发票申请不存在");
        invoice.Issue(AppClock.Now);
        await _db.SaveChangesAsync();
        return (await ListInvoicesAsync()).First(x => x.InvoiceId == id);
    }

    public async Task<OpsInvoiceDto> RejectInvoiceAsync(int id, string? note)
    {
        var invoice = await _db.OpsInvoices.FindAsync(id) ?? throw new BusinessException("发票申请不存在");
        invoice.Reject(note);
        await _db.SaveChangesAsync();
        return (await ListInvoicesAsync()).First(x => x.InvoiceId == id);
    }

    public async Task<OpsInvoiceDto> RedInvoiceAsync(int id)
    {
        var invoice = await _db.OpsInvoices.FindAsync(id) ?? throw new BusinessException("发票申请不存在");
        invoice.Red();
        await _db.SaveChangesAsync();
        return (await ListInvoicesAsync()).First(x => x.InvoiceId == id);
    }

    // ── 预防保养 ──────────────────────────────────────────

    public async Task<List<MaintenancePlanDto>> ListPlansAsync()
    {
        var plans = await LoadPlansAsync();
        return plans;
    }

    private async Task<List<MaintenancePlanDto>> LoadPlansAsync()
    {
        var now = AppClock.Now;
        var plans = await _db.MaintenancePlans.AsNoTracking()
            .Include(p => p.Vehicle)
            .OrderBy(p => p.Status).ThenBy(p => p.NextDueTime).Take(Paging.MaxUnpagedRows)
            .ToListAsync();
        return plans.Select(p => new MaintenancePlanDto
        {
            PlanId = p.PlanId, VehicleId = p.VehicleId, VehicleNo = p.Vehicle?.VehicleNo,
            Brand = p.Vehicle?.Brand, PlanName = p.PlanName, TriggerType = p.TriggerType,
            TriggerValue = p.TriggerValue, LastServiceTime = p.LastServiceTime,
            LastMileage = p.LastMileage, CurrentMileage = p.CurrentMileage, NextDueTime = p.NextDueTime,
            Status = p.Status, Remark = p.Remark,
            Due = p.IsDue(now, p.CurrentMileage),
            MileageSinceService = Math.Max(0, p.CurrentMileage - p.LastMileage),
            CreateTime = p.CreateTime,
        }).ToList();
    }

    public async Task<MaintenancePlanDto> CreatePlanAsync(MaintenancePlanInput input)
    {
        var plan = MaintenancePlan.Create(input.VehicleId, input.PlanName, input.TriggerType,
            input.TriggerValue, input.LastServiceTime, input.LastMileage, input.Remark, input.CurrentMileage);
        _db.MaintenancePlans.Add(plan);
        await _db.SaveChangesAsync();
        return (await ListPlansAsync()).First(p => p.PlanId == plan.PlanId);
    }

    public async Task<MaintenancePlanDto> UpdatePlanAsync(int id, MaintenancePlanInput input)
    {
        var plan = await _db.MaintenancePlans.FindAsync(id) ?? throw new BusinessException("保养计划不存在");
        plan.Update(input.PlanName, input.TriggerType, input.TriggerValue, input.LastServiceTime,
            input.LastMileage, input.Remark, input.CurrentMileage);
        await _db.SaveChangesAsync();
        return (await ListPlansAsync()).First(p => p.PlanId == id);
    }

    public async Task SetPlanStatusAsync(int id, string status)
    {
        var plan = await _db.MaintenancePlans.FindAsync(id) ?? throw new BusinessException("保养计划不存在");
        plan.SetStatus(status);
        await _db.SaveChangesAsync();
    }

    public async Task<string> GenerateWorkOrderAsync(int id, int? userId)
    {
        var plan = await _db.MaintenancePlans.Include(p => p.Vehicle).FirstOrDefaultAsync(p => p.PlanId == id)
            ?? throw new BusinessException("保养计划不存在");
        if (plan.Vehicle == null) throw new BusinessException("计划关联车辆不存在");
        var req = new CreateMaintenanceRequest
        {
            VehicleId = plan.VehicleId,
            FaultType = "预防保养",
            FaultDescription = $"{plan.PlanName}（{plan.TriggerType} {plan.TriggerValue}）到期保养",
        };
        var order = await _maintenance.Create(req, userId);
        plan.MarkServiced(AppClock.Now, plan.CurrentMileage);
        await _db.SaveChangesAsync();
        return order.WorkOrderNo;
    }

    // ── 合同存证 ──────────────────────────────────────────

    public async Task<List<ContractEvidenceDto>> ListContractsAsync(string? keyword = null)
    {
        var q = from c in _db.Contracts.AsNoTracking()
                join o in _db.RentalOrders.AsNoTracking() on c.OrderId equals o.OrderId into orders
                from o in orders.DefaultIfEmpty()
                join cu in _db.Customers.AsNoTracking() on c.CustomerId equals cu.CustomerId into customers
                from cu in customers.DefaultIfEmpty()
                select new { c, o, cu };
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(x => x.c.ContractNo.Contains(kw) || (x.o != null && x.o.OrderNo.Contains(kw))
                || (x.cu != null && x.cu.FullName.Contains(kw)));
        }
        var rows = await q.OrderByDescending(x => x.c.SignTime).Take(Paging.MaxUnpagedRows).ToListAsync();
        return rows.Select(x =>
        {
            var valid = HashOf(x.c.Content) == x.c.ContentHash;
            return new ContractEvidenceDto
            {
                ContractId = x.c.ContractId, ContractNo = x.c.ContractNo, OrderId = x.c.OrderId,
                OrderNo = x.o?.OrderNo, CustomerId = x.c.CustomerId, CustomerName = x.cu?.FullName,
                Version = x.c.Version, ContentHash = x.c.ContentHash, SignatureUrl = x.c.SignatureUrl,
                SignTime = x.c.SignTime, SignIp = x.c.SignIp, Status = x.c.Status,
                HashValid = valid, ContentLength = x.c.Content.Length,
            };
        }).ToList();
    }

    public async Task<bool> VerifyContractAsync(int id)
    {
        var c = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == id)
            ?? throw new BusinessException("合同不存在");
        return HashOf(c.Content) == c.ContentHash;
    }

    private static string HashOf(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static OpsApprovalDto ToApprovalDto(OpsApproval a) => new()
    {
        ApprovalId = a.ApprovalId, RequestNo = a.RequestNo, BizType = a.BizType,
        RefType = a.RefType, RefId = a.RefId, OrderId = a.OrderId, Amount = a.Amount,
        Reason = a.Reason, Status = a.Status, ApplicantId = a.ApplicantId,
        ApproverId = a.ApproverId, ApproveTime = a.ApproveTime, ApproveNote = a.ApproveNote,
        CreateTime = a.CreateTime,
    };
}
