using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

/// <summary>
/// 订单活动聚合：时间线与车辆可用日历。只读、不参与下单事务，
/// 因此单独成服务，避免把 OrderService 继续堆大。
/// </summary>
public class OrderActivityService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>订单完整时间线，按发生时间正序；缺时间的历史事件排在最后，不伪造时刻。</summary>
    public async Task<OrderTimelineDto> GetTimelineAsync(int orderId)
    {
        var order = await _db.RentalOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNo,
                o.OrderStatus,
                o.CreateTime,
                o.LeaseStart,
                o.LeaseEnd,
                o.CancelReason,
            })
            .FirstOrDefaultAsync()
            ?? throw new BusinessException("订单不存在");

        var events = new List<OrderTimelineEventDto>
        {
            new()
            {
                Kind = "created",
                Title = "订单创建",
                Detail = $"租期 {order.LeaseStart:yyyy-MM-dd HH:mm} 至 {order.LeaseEnd:yyyy-MM-dd HH:mm}",
                Time = order.CreateTime,
            },
        };

        var payments = await _db.PaymentRecords.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.PayTime != null)
            .OrderBy(p => p.PayTime)
            .Select(p => new { p.PayStatus, p.PayAmount, p.RefundAmount, p.PayType, p.PayTime })
            .ToListAsync();

        foreach (var payment in payments)
        {
            var refunded = payment.RefundAmount > 0;
            events.Add(new OrderTimelineEventDto
            {
                Kind = refunded ? "refund" : "payment",
                Title = refunded ? "退款/部分退款" : "支付成功",
                Detail = $"通道：{payment.PayType ?? "未记录"}"
                    + (refunded ? $"；累计退款 ¥{payment.RefundAmount:0.00}" : string.Empty),
                Time = payment.PayTime,
                Amount = refunded ? -payment.RefundAmount : payment.PayAmount,
            });
        }

        var handovers = await _db.OrderHandovers.AsNoTracking()
            .Where(h => h.OrderId == orderId)
            .OrderBy(h => h.CreateTime)
            .Select(h => new { h.Phase, h.CreateTime, h.BatteryLevel, h.ConditionNote })
            .ToListAsync();

        foreach (var handover in handovers)
        {
            events.Add(new OrderTimelineEventDto
            {
                Kind = handover.Phase == OrderHandover.PhasePickup ? "pickup" : "return",
                Title = handover.Phase == OrderHandover.PhasePickup ? "取车验车" : "还车验车",
                Detail = $"电量 {handover.BatteryLevel?.ToString() ?? "未记录"}%"
                    + (string.IsNullOrWhiteSpace(handover.ConditionNote) ? string.Empty : $"；{handover.ConditionNote}"),
                Time = handover.CreateTime,
            });
        }

        var changes = await _db.OrderChanges.AsNoTracking()
            .Where(c => c.OrderId == orderId)
            .OrderBy(c => c.CreateTime)
            .Select(c => new { c.ChangeType, c.CreateTime, c.DiffAmount, c.Reason })
            .ToListAsync();

        foreach (var change in changes)
        {
            events.Add(new OrderTimelineEventDto
            {
                Kind = change.ChangeType == OrderChange.TypeVehicle ? "change-vehicle" : "change-schedule",
                Title = change.ChangeType,
                Detail = change.Reason,
                Time = change.CreateTime,
                Amount = change.DiffAmount == 0 ? null : change.DiffAmount,
            });
        }

        var settles = await _db.SettleRecords.AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.SettleTime)
            .Select(s => new { s.SettleTime, s.ActualRent, s.DamageFee, s.OverdueFee, s.RefundAmount, s.SupplementAmount })
            .ToListAsync();

        foreach (var settle in settles)
        {
            events.Add(new OrderTimelineEventDto
            {
                Kind = "settled",
                Title = "归还结算",
                Detail = $"实收租金 ¥{settle.ActualRent:0.00}；损坏费 ¥{settle.DamageFee:0.00}；逾期费 ¥{settle.OverdueFee:0.00}",
                Time = settle.SettleTime,
                Amount = settle.RefundAmount > 0 ? -settle.RefundAmount : settle.SupplementAmount > 0 ? settle.SupplementAmount : null,
            });
        }

        if (order.OrderStatus == "已取消")
        {
            events.Add(new OrderTimelineEventDto
            {
                Kind = "cancelled",
                Title = "订单已取消",
                Detail = order.CancelReason,
                Time = null,
            });
        }

        return new OrderTimelineDto
        {
            OrderId = order.OrderId,
            OrderNo = order.OrderNo,
            OrderStatus = order.OrderStatus,
            Events = events.OrderBy(e => e.Time ?? DateTime.MaxValue).ToList(),
        };
    }

    /// <summary>
    /// 车辆可用日历：返回每台车在时间窗内的活跃档期（待支付/已支付/租赁中）。
    /// 时间窗含首尾两天，最长 31 天，避免一次查询扫全表。
    /// </summary>
    public async Task<VehicleCalendarResultDto> GetCalendarAsync(DateTime? from, DateTime? to, int? vehicleId)
    {
        var localToday = AppClock.Now.Date;
        var start = (from?.Date) ?? localToday;
        var end = (to?.Date) ?? start.AddDays(7);
        if (end < start) throw new BusinessException("结束日期不能早于开始日期");
        if ((end - start).TotalDays > 31) throw new BusinessException("车辆日历一次最多查询 31 天");
        var windowEnd = end.AddDays(1);

        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => vehicleId == null || v.VehicleId == vehicleId)
            .OrderBy(v => v.VehicleNo)
            .Select(v => new { v.VehicleId, v.VehicleNo, v.Brand, v.VehicleStatus })
            .ToListAsync();

        var orders = await _db.RentalOrders.AsNoTracking()
            .Where(o => (o.OrderStatus == "待支付" || o.OrderStatus == "已支付" || o.OrderStatus == "租赁中")
                && o.LeaseStart < windowEnd && o.LeaseEnd > start)
            .OrderBy(o => o.LeaseStart)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNo,
                o.VehicleId,
                CustomerName = o.Customer != null ? o.Customer.FullName : string.Empty,
                o.LeaseStart,
                o.LeaseEnd,
                o.OrderStatus,
            })
            .ToListAsync();

        var slotsByVehicle = orders
            .GroupBy(o => o.VehicleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(o => new VehicleCalendarSlotDto
                {
                    OrderId = o.OrderId,
                    OrderNo = o.OrderNo,
                    CustomerName = o.CustomerName,
                    LeaseStart = o.LeaseStart,
                    LeaseEnd = o.LeaseEnd,
                    OrderStatus = o.OrderStatus,
                }).ToList());

        return new VehicleCalendarResultDto
        {
            From = start,
            To = end,
            Vehicles = vehicles.Select(v => new VehicleCalendarDto
            {
                VehicleId = v.VehicleId,
                VehicleNo = v.VehicleNo,
                Model = v.Brand ?? string.Empty,
                VehicleStatus = v.VehicleStatus,
                Slots = slotsByVehicle.GetValueOrDefault(v.VehicleId) ?? [],
            }).ToList(),
        };
    }
}
