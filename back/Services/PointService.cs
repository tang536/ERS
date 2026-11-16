using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

///<summary>
/// 顾客积分：结算实付租金 1 元得 1 分，100 分抵 1 元，单笔最多抵租金 10%。
/// 同时负责按累计已完成订单数自动升级会员——min_rent_count 即「达到该租车次数可升到该等级」。
///</summary>
public class PointService(AppDbContext db, ILogger<PointService> logger)
{
    ///<summary>多少分抵 1 元</summary>
    public const int PointsPerYuan = 100;
    ///<summary>单笔订单最多抵扣租金的比例</summary>
    public const decimal MaxRedeemRatio = 0.10m;

    private readonly AppDbContext _db = db;
    private readonly ILogger<PointService> _logger = logger;

    ///<summary>最多可用积分：余额与「租金 10% 折算分」取小</summary>
    public static int MaxUsablePoints(int balance, decimal rent) =>
        Math.Max(0, Math.Min(balance, (int)Math.Floor(rent * MaxRedeemRatio * PointsPerYuan)));

    public static decimal AmountOf(int points) => Math.Round(points / (decimal)PointsPerYuan, 2);

    public async Task<PointSummaryDto> GetSummaryAsync(int customerId, decimal? rent = null)
    {
        var balance = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => c.Points).FirstOrDefaultAsync();
        var logs = await _db.PointLogs.AsNoTracking().Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.Id).Take(50)
            .Select(p => new PointLogDto
            {
                ChangeAmount = p.ChangeAmount,
                Balance = p.Balance,
                Source = p.Source,
                Remark = p.Remark,
                OrderId = p.OrderId,
                CreateTime = p.CreateTime,
            }).ToListAsync();

        var maxPoints = rent.HasValue ? MaxUsablePoints(balance, rent.Value) : 0;
        return new PointSummaryDto
        {
            Balance = balance,
            MaxRedeem = maxPoints,
            MaxRedeemAmount = AmountOf(maxPoints),
            Logs = logs,
        };
    }

    ///<summary>下单页预览：前端只展示，最终抵扣以提交时服务端重算为准</summary>
    public async Task<PointRedeemPreviewDto> PreviewAsync(int customerId, decimal rent)
    {
        var balance = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => c.Points).FirstOrDefaultAsync();
        var max = MaxUsablePoints(balance, rent);
        return new PointRedeemPreviewDto { Points = 0, Amount = 0m, MaxPoints = max, MaxAmount = AmountOf(max) };
    }

    ///<summary>提交前校验：积分足够且不超过租金 10%</summary>
    public async Task<(int Points, decimal Amount)> ValidateAsync(int customerId, int points, decimal rent)
    {
        if (points <= 0) return (0, 0m);
        var balance = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => c.Points).FirstOrDefaultAsync();
        var max = MaxUsablePoints(balance, rent);
        if (points > max) throw new BusinessException($"本单最多可用 {max} 积分（余额 {balance} 分，单笔最多抵租金 10%）");
        return (points, AmountOf(points));
    }

    ///<summary>锁内扣减：条件更新保证余额不会被并发扣成负数</summary>
    public async Task DeductAsync(int customerId, int points, int orderId)
    {
        if (points <= 0) return;
        var affected = await _db.Customers
            .Where(c => c.CustomerId == customerId && c.Points >= points)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Points, c => c.Points - points));
        if (affected == 0) throw new BusinessException("积分不足，请重新选择抵扣数量");

        var balance = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => c.Points).FirstAsync();
        _db.PointLogs.Add(PointLog.Create(customerId, -points, balance, PointLog.SourceRedeem, orderId, "下单抵扣租金"));
        await _db.SaveChangesAsync();
    }

    ///<summary>结算奖励：按实付租金向下取整发分，并尝试自动升级</summary>
    public async Task AwardAsync(int customerId, decimal paidRent, int orderId)
    {
        var points = (int)Math.Floor(Math.Max(0m, paidRent));
        if (points <= 0) return;

        await _db.Customers.Where(c => c.CustomerId == customerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Points, c => c.Points + points));
        var balance = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => c.Points).FirstAsync();
        _db.PointLogs.Add(PointLog.Create(customerId, points, balance, PointLog.SourceSettle, orderId, $"订单结算实付 ¥{paidRent:0.00}"));
        await _db.SaveChangesAsync();

        await AutoUpgradeAsync(customerId);
    }

    ///<summary>订单取消：把本单抵扣的积分退回（奖励分不回收，已发生的消费不撤销）</summary>
    public async Task RefundByOrderAsync(int orderId)
    {
        var used = await _db.PointLogs.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Source == PointLog.SourceRedeem)
            .SumAsync(p => -p.ChangeAmount);
        if (used <= 0) return;

        var customerId = await _db.PointLogs.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Source == PointLog.SourceRedeem)
            .Select(p => p.CustomerId).FirstAsync();

        await RefundCoreAsync(customerId, orderId, used);
    }

    ///<summary>批量兜底：挂在「已取消」订单上的抵扣积分统一退回（超时回收走存储过程，拿不到具体订单号）</summary>
    public async Task RefundCancelledAsync()
    {
        var rows = await _db.PointLogs.AsNoTracking()
            .Where(p => p.Source == PointLog.SourceRedeem && p.OrderId != null
                        && _db.RentalOrders.Any(o => o.OrderId == p.OrderId && o.OrderStatus == "已取消")
                        && !_db.PointLogs.Any(q => q.OrderId == p.OrderId && q.Source == PointLog.SourceRefund))
            .Select(p => new { p.CustomerId, OrderId = p.OrderId!.Value, Points = -p.ChangeAmount })
            .Take(Paging.MaxUnpagedRows)
            .ToListAsync();

        foreach (var row in rows)
        {
            if (row.Points > 0) await RefundCoreAsync(row.CustomerId, row.OrderId, row.Points);
        }
    }

    ///<summary>退分本体：余额加回并写一条「取消退回」流水，保证同一订单只会退一次</summary>
    private async Task RefundCoreAsync(int customerId, int orderId, int points)
    {
        await _db.Customers.Where(c => c.CustomerId == customerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Points, c => c.Points + points));
        var balance = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => c.Points).FirstAsync();
        _db.PointLogs.Add(PointLog.Create(customerId, points, balance, PointLog.SourceRefund, orderId, "订单取消退回抵扣积分"));
        await _db.SaveChangesAsync();
    }

    ///<summary>按已完成订单数自动升级：只升不降，且只认折扣更优的等级</summary>
    private async Task AutoUpgradeAsync(int customerId)
    {
        var customer = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId)
            .Select(c => new { c.MemberLevel }).FirstOrDefaultAsync();
        if (customer == null) return;

        var completed = await _db.RentalOrders.CountAsync(o => o.CustomerId == customerId && o.OrderStatus == "已完结");
        var target = await _db.MemberRights.AsNoTracking()
            .Where(r => r.MinRentCount != null && r.MinRentCount <= completed && r.MemberLevel != customer.MemberLevel)
            .OrderBy(r => r.Discount).ThenByDescending(r => r.MinRentCount)
            .FirstOrDefaultAsync();
        if (target == null) return;

        var currentDiscount = await MemberDiscountLookup.OfLevelAsync(_db, customer.MemberLevel);
        if (target.Discount >= currentDiscount) return;

        await _db.Customers.Where(c => c.CustomerId == customerId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.MemberLevel, target.MemberLevel));
        _logger.LogInformation("顾客 {CustomerId} 完成 {Count} 单，自动升级为 {Level}", customerId, completed, target.MemberLevel);
    }
}
