using System.Security.Cryptography;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

///<summary>
/// 交接班：开班登记备用金，交班时按班次时间区间汇总各渠道收款并核对现金。
/// 单店场景下不区分「谁收的款」，按时间区间统计即可；差额必须写原因，属资金安全的关键控制点。
///</summary>
public class ShiftService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    private sealed record Summary(decimal Cash, decimal Alipay, decimal Wechat, decimal Bank, int OrderCount, int SettleCount);

    public async Task<ShiftDto?> CurrentAsync(int operatorId)
    {
        var shift = await _db.Shifts.AsNoTracking()
            .Where(s => s.OperatorId == operatorId && s.Status == "进行中")
            .OrderByDescending(s => s.ShiftId).FirstOrDefaultAsync();
        if (shift == null) return null;

        var summary = await SummarizeAsync(shift.OpenTime, AppClock.Now);
        return ToDto(shift, summary, isCurrent: true);
    }

    public async Task<ShiftDto> OpenAsync(int operatorId, string operatorName, decimal openingCash)
    {
        var exists = await _db.Shifts.AnyAsync(s => s.OperatorId == operatorId && s.Status == "进行中");
        if (exists) throw new BusinessException("您还有未交班的班次，请先完成交班");

        var no = "SJ" + AppClock.Now.ToString("yyyyMMddHHmmss") + RandomNumberGenerator.GetHexString(4);
        var shift = Shift.Open(no, operatorId, operatorName, openingCash);
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();

        var summary = await SummarizeAsync(shift.OpenTime, AppClock.Now);
        return ToDto(shift, summary, isCurrent: true);
    }

    public async Task<ShiftDto> CloseAsync(CloseShiftInput input)
    {
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.ShiftId == input.ShiftId) ?? throw new BusinessException("班次不存在");
        if (shift.Status != "进行中") throw new BusinessException("该班次已交班，请勿重复操作");

        var now = AppClock.Now;
        var summary = await SummarizeAsync(shift.OpenTime, now);
        shift.Close(now, summary.Cash, summary.Alipay, summary.Wechat, summary.Bank,
            summary.OrderCount, summary.SettleCount, input.CashActual, input.Note);
        await _db.SaveChangesAsync();
        return ToDto(shift, summary, isCurrent: false);
    }

    public async Task<List<ShiftDto>> GetHistoryAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var rows = await _db.Shifts.AsNoTracking()
            .Where(s => s.Status == "已交班")
            .OrderByDescending(s => s.ShiftId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return [.. rows.Select(s => ToDto(s, null, false))];
    }

    ///<summary>按支付时间区间汇总：现金按净额（收款 - 退款）统计，其他渠道只做展示</summary>
    private async Task<Summary> SummarizeAsync(DateTime from, DateTime to)
    {
        var pays = await _db.PaymentRecords.AsNoTracking()
            .Where(p => p.PayTime != null && p.PayTime >= from && p.PayTime <= to
                        && (p.PayStatus == "已支付" || p.PayStatus == "部分退款"))
            .Select(p => new { p.PayType, p.PayAmount, p.RefundAmount, p.OrderId, p.BizType })
            .ToListAsync();

        decimal Sum(string channel) => Math.Round(pays.Where(p => p.PayType == channel)
            .Sum(p => p.PayAmount - p.RefundAmount), 2);

        var orderCount = pays.Where(p => p.BizType == "order").Select(p => p.OrderId).Distinct().Count();
        var settleCount = await _db.SettleRecords.CountAsync(s => s.SettleTime >= from && s.SettleTime <= to);
        return new Summary(Sum("现金"), Sum("支付宝"), Sum("微信"), Sum("银行卡"), orderCount, settleCount);
    }

    private static ShiftDto ToDto(Shift s, Summary? live, bool isCurrent) => new()
    {
        ShiftId = s.ShiftId,
        ShiftNo = s.ShiftNo,
        OperatorName = s.OperatorName,
        OpenTime = s.OpenTime,
        CloseTime = s.CloseTime,
        OpeningCash = s.OpeningCash,
        CashCollected = live?.Cash ?? s.CashCollected,
        CashActual = live == null ? s.CashActual : s.OpeningCash + live.Cash,
        Difference = isCurrent ? 0 : s.Difference,
        AlipayAmount = live?.Alipay ?? s.AlipayAmount,
        WechatAmount = live?.Wechat ?? s.WechatAmount,
        BankAmount = live?.Bank ?? s.BankAmount,
        OrderCount = live?.OrderCount ?? s.OrderCount,
        SettleCount = live?.SettleCount ?? s.SettleCount,
        Status = s.Status,
        Note = s.Note,
        IsCurrent = isCurrent,
    };
}
