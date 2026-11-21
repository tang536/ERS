using System.Data;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

///<summary>
/// 订单变更：换车与改期。变更不动原订单的历史，只追加一张变更单；
/// 已支付订单的差价走「补缴单 / 原路退款」，待支付订单直接改待付金额。
///</summary>
public class OrderChangeService(AppDbContext db, PaymentService payment, MessageService messages, OpsExtService ops, ILogger<OrderChangeService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly PaymentService _payment = payment;
    private readonly MessageService _messages = messages;
    private readonly OpsExtService _ops = ops;
    private readonly ILogger<OrderChangeService> _logger = logger;

    ///<summary>
    /// 变更单的计价基准：改期沿用车下单时的价格快照（同一辆车，不应因运营改价而悄悄涨跌），
    /// 只有历史订单没有快照时才回退车辆当前价。
    ///</summary>
    private async Task<decimal> OrderHourlyRentAsync(RentalOrder order)
    {
        if (order.DailyRent.HasValue)
            return PricingResolver.HourlyRent(order.DailyRent.Value) * (order.PriceFactor ?? 1m);

        var (daily, _, _) = await PricingResolver.ResolveHourlyAsync(_db, order.VehicleId);
        return PricingResolver.HourlyRent(daily);
    }

    public async Task<OrderChangeDto> ChangeVehicleAsync(ChangeVehicleInput input, int operatorId)
    {
        decimal refund;
        int payId;
        OrderChange change;
        string orderNo;
        int customerId;

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await DbRowLock.PrepareAsync(_db);
            var order = await DbRowLock.LockOrderAsync(_db, input.OrderId) ?? throw new BusinessException("订单不存在");
            EnsureChangeable(order);

            if (order.VehicleId == input.NewVehicleId) throw new BusinessException("新车辆与原车辆相同，无需换车");
            var target = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.VehicleId == input.NewVehicleId)
                ?? throw new BusinessException("新车辆不存在");
            if (target.VehicleStatus != "空闲") throw new BusinessException($"车辆 {target.VehicleNo} 当前为「{target.VehicleStatus}」，不能换入");

            var oldRent = Math.Round(order.TotalRent - order.AddonFee, 2);
            var oldVehicleId = order.VehicleId;
            //换车后计价基准会变：取新车的当前日租金 + 变更时生效的动态系数，并写回订单快照
            var (newDailyRent, _, _) = await PricingResolver.ResolveHourlyAsync(_db, target.VehicleId);
            var newFactor = await _ops.CurrentPriceFactorAsync();
            var newHourly = PricingResolver.HourlyRent(newDailyRent) * newFactor;
            var newRent = Math.Round(PricingResolver.CalcHours(order.LeaseStart, order.LeaseEnd) * newHourly * order.RentDiscount, 2);

            //先释放旧车再占新车：两步都在本事务内，占新车失败时旧车状态随回滚恢复
            await VehicleLeaseGuard.TryReleaseAsync(_db, order.VehicleId, order.OrderId);
            if (!await VehicleLeaseGuard.TryClaimAsync(_db, target.VehicleId))
                throw new BusinessException($"车辆 {target.VehicleNo} 刚被他人租走，请重新选择");

            order.SwapVehicle(target.VehicleId, newRent);
            order.SetPricingSnapshot(newDailyRent, newFactor);
            change = OrderChange.ForVehicle(order.OrderId, oldVehicleId, target.VehicleId, oldRent, newRent, input.Reason, operatorId);
            _db.OrderChanges.Add(change);

            (refund, payId) = await AdjustMoneyAsync(order, newRent - oldRent);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            orderNo = order.OrderNo;
            customerId = order.CustomerId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (refund > 0)
        {
            var refundResult = await SafeRefundAsync(payId, refund, $"换车差价退款（{orderNo}）", orderNo);
            await NotifyChangeAsync(customerId, orderNo, change, refund, refundResult);
        }
        else
        {
            await NotifyChangeAsync(customerId, orderNo, change, refund, null);
        }
        return ToDto(change);
    }

    public async Task<OrderChangeDto> RescheduleAsync(RescheduleInput input, int operatorId)
    {
        decimal refund;
        int payId;
        OrderChange change;
        string orderNo;
        int customerId;

        if (input.LeaseEnd <= input.LeaseStart) throw new BusinessException("还车时间必须晚于起租时间");
        if (input.LeaseStart < AppClock.Now.AddMinutes(-1)) throw new BusinessException("起租时间不能早于当前时间");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await DbRowLock.PrepareAsync(_db);
            var order = await DbRowLock.LockOrderAsync(_db, input.OrderId) ?? throw new BusinessException("订单不存在");
            EnsureChangeable(order);

            var oldRent = Math.Round(order.TotalRent - order.AddonFee, 2);
            var hourly = await OrderHourlyRentAsync(order);
            var newRent = Math.Round(PricingResolver.CalcHours(input.LeaseStart, input.LeaseEnd) * hourly * order.RentDiscount, 2);

            var oldStart = order.LeaseStart;
            var oldEnd = order.LeaseEnd;
            order.Reschedule(input.LeaseStart, input.LeaseEnd, newRent);
            change = OrderChange.ForSchedule(order.OrderId, oldStart, oldEnd, input.LeaseStart, input.LeaseEnd,
                oldRent, newRent, input.Reason, operatorId);
            _db.OrderChanges.Add(change);

            (refund, payId) = await AdjustMoneyAsync(order, newRent - oldRent);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            orderNo = order.OrderNo;
            customerId = order.CustomerId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (refund > 0)
        {
            var refundResult = await SafeRefundAsync(payId, refund, $"改期差价退款（{orderNo}）", orderNo);
            await NotifyChangeAsync(customerId, orderNo, change, refund, refundResult);
        }
        else
        {
            await NotifyChangeAsync(customerId, orderNo, change, refund, null);
        }
        return ToDto(change);
    }

    public async Task<List<OrderChangeDto>> ListByOrderAsync(int orderId) =>
        await _db.OrderChanges.AsNoTracking().Where(c => c.OrderId == orderId)
            .OrderByDescending(c => c.ChangeId).Take(Paging.MaxUnpagedRows)
            .Select(c => new OrderChangeDto
            {
                ChangeId = c.ChangeId,
                OrderId = c.OrderId,
                ChangeType = c.ChangeType,
                OldVehicleId = c.OldVehicleId,
                NewVehicleId = c.NewVehicleId,
                OldStart = c.OldStart,
                OldEnd = c.OldEnd,
                NewStart = c.NewStart,
                NewEnd = c.NewEnd,
                OldRent = c.OldRent,
                NewRent = c.NewRent,
                DiffAmount = c.DiffAmount,
                Reason = c.Reason,
                CreateTime = c.CreateTime,
            }).ToListAsync();

    ///<summary>换车试算：只算钱，不占车、不写变更单</summary>
    public async Task<OrderChangePreviewDto> PreviewVehicleAsync(int orderId, int newVehicleId)
    {
        var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new BusinessException("订单不存在");
        EnsureChangeable(order);
        if (order.VehicleId == newVehicleId) throw new BusinessException("新车辆与原车辆相同，无需换车");

        var target = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.VehicleId == newVehicleId)
            ?? throw new BusinessException("新车辆不存在");
        if (target.VehicleStatus != "空闲") throw new BusinessException($"车辆 {target.VehicleNo} 当前为「{target.VehicleStatus}」，不能换入");

        var oldRent = Math.Round(order.TotalRent - order.AddonFee, 2);
        var (newDailyRent, _, _) = await PricingResolver.ResolveHourlyAsync(_db, target.VehicleId);
        var newFactor = await _ops.CurrentPriceFactorAsync();
        var hours = PricingResolver.CalcHours(order.LeaseStart, order.LeaseEnd);
        var newRent = Math.Round(hours * PricingResolver.HourlyRent(newDailyRent) * newFactor * order.RentDiscount, 2);
        return BuildPreview(order, "换车", oldRent, newRent, newDailyRent, newFactor, hours);
    }

    ///<summary>改期试算：沿用车下单时的价格快照，只算钱，不写变更单</summary>
    public async Task<OrderChangePreviewDto> PreviewRescheduleAsync(int orderId, DateTime leaseStart, DateTime leaseEnd)
    {
        if (leaseEnd <= leaseStart) throw new BusinessException("还车时间必须晚于起租时间");
        if (leaseStart < AppClock.Now.AddMinutes(-1)) throw new BusinessException("起租时间不能早于当前时间");

        var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId)
            ?? throw new BusinessException("订单不存在");
        EnsureChangeable(order);

        var oldRent = Math.Round(order.TotalRent - order.AddonFee, 2);
        var hourly = await OrderHourlyRentAsync(order);
        var hours = PricingResolver.CalcHours(leaseStart, leaseEnd);
        var newRent = Math.Round(hours * hourly * order.RentDiscount, 2);

        //试算展示与正式办理同口径：有快照就展示快照价，没有就展示车辆当前价
        var daily = order.DailyRent ?? (await PricingResolver.ResolveHourlyAsync(_db, order.VehicleId)).DailyRent;
        var factor = order.PriceFactor ?? 1m;
        return BuildPreview(order, "改期", oldRent, newRent, daily, factor, hours);
    }

    private static OrderChangePreviewDto BuildPreview(RentalOrder order, string changeType,
        decimal oldRent, decimal newRent, decimal dailyRent, decimal priceFactor, int hours)
    {
        var diff = Math.Round(newRent - oldRent, 2);
        return new OrderChangePreviewDto
        {
            OrderId = order.OrderId,
            ChangeType = changeType,
            OldRent = oldRent,
            NewRent = newRent,
            DiffAmount = diff,
            NewDailyRent = dailyRent,
            NewPriceFactor = priceFactor,
            NewHourlyRent = PricingResolver.HourlyRent(dailyRent) * priceFactor,
            BillableHours = hours,
            SettleMode = order.OrderStatus == "待支付"
                ? "直接改写待付金额"
                : diff > 0.01m ? "生成补缴单" : diff < -0.01m ? "原路退款" : "无差价",
        };
    }

    ///<summary>只有「待支付/已支付」可变更：已起租后换车属事故处理，走结算与维保流程</summary>
    private static void EnsureChangeable(RentalOrder order)
    {
        if (order.OrderStatus is not ("待支付" or "已支付"))
            throw new BusinessException($"订单当前状态为「{order.OrderStatus}」，只有待支付或待取车的订单可以换车/改期");
    }

    ///<summary>
    /// 差价处理：待支付单直接改待付金额；已支付单正差生成补缴单、负差登记为待退款。
    /// 返回 (需退款金额, 原支付记录 ID)。
    ///</summary>
    private async Task<(decimal Refund, int PayId)> AdjustMoneyAsync(RentalOrder order, decimal diff)
    {
        if (order.OrderStatus == "待支付")
        {
            var pending = await _db.PaymentRecords
                .Where(p => p.OrderId == order.OrderId && p.BizType == "order" && p.PayStatus == "待支付")
                .OrderByDescending(p => p.PayId).FirstOrDefaultAsync();
            pending?.PreparePayment(pending.PayType ?? "微信", Math.Round(order.PrepaidRent + order.Deposit, 2));
            return (0m, 0);
        }

        if (diff > 0.01m)
        {
            var outTradeNo = BusinessNumber.New("CHG");
            _db.PaymentRecords.Add(PaymentRecord.Create(order.OrderId, Math.Round(diff, 2), null, "supplement", outTradeNo));
            return (0m, 0);
        }

        if (diff < -0.01m)
        {
            var paid = await _db.PaymentRecords
                .Where(p => p.OrderId == order.OrderId && p.BizType == "order" && (p.PayStatus == "已支付" || p.PayStatus == "部分退款"))
                .OrderByDescending(p => p.PayId).FirstOrDefaultAsync()
                ?? throw new BusinessException("找不到该订单的原支付流水，无法办理退款；请先到支付流水核对后再改单");
            return (Math.Round(-diff, 2), paid.PayId);
        }

        return (0m, 0);
    }

    ///<summary>退款失败不能把已提交的变更回滚：给财务留人工待办，并把失败结果带回去如实告知顾客</summary>
    private async Task<PaymentService.RefundResult?> SafeRefundAsync(int payId, decimal amount, string reason, string orderNo)
    {
        try
        {
            return await _payment.Refund(payId, amount, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订单变更退款失败：支付流水 {PayId}，金额 {Amount}", payId, amount);
            await NotifyManualRefundAsync(payId, amount, reason, orderNo);
            return null;
        }
    }

    ///<summary>自动退款失败时给管理员/财务写待办：顾客已经看到「退款失败」，门店必须有人接手</summary>
    private async Task NotifyManualRefundAsync(int payId, decimal amount, string reason, string orderNo)
    {
        try
        {
            var staffIds = await _db.SystemUsers.AsNoTracking()
                .Where(u => u.UserStatus != 0 && u.Role != null
                    && (u.Role.RoleName == "管理员" || u.Role.RoleName == "财务"))
                .Select(u => u.UserId)
                .ToListAsync();
            foreach (var staffId in staffIds)
            {
                await _messages.SendAsync(
                    ("staff", staffId),
                    ("refund", $"change-{payId}"),
                    ("订单变更退款失败，需人工处理",
                        $"订单 {orderNo} 的差价退款 ¥{amount:0.00} 自动退款失败（支付流水 #{payId}，{reason}）。请核对支付流水后人工退款。"),
                    ("danger", $"/reports?orderNo={Uri.EscapeDataString(orderNo)}", true));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "订单变更退款失败待办通知写入失败（变更本身已成功）");
        }
    }

    private async Task NotifyChangeAsync(int customerId, string orderNo, OrderChange change, decimal refund,
        PaymentService.RefundResult? refundResult)
    {
        try
        {
            var tail = change.DiffAmount > 0.01m
                ? $"需补缴差价 ¥{change.DiffAmount:0.00}，可在「我的订单 → 待补缴」在线支付或到店缴纳"
                : refund <= 0
                    ? "金额无变化"
                    : refundResult == null
                        ? $"应退差价 ¥{refund:0.00}，自动退款未成功，门店财务将尽快人工处理，请留意到账"
                        : refundResult.RealMoney
                            ? $"应退差价 ¥{refund:0.00} 已原路退回，预计 1-3 个工作日到账"
                            : $"应退差价 ¥{refund:0.00}，已登记为门店当面退款，请到店领取";
            await _messages.SendAsync(
                ("customer", customerId),
                ("order-change", change.ChangeId.ToString()),
                ($"订单{change.ChangeType}通知", $"订单 {orderNo} 已办理{change.ChangeType}，{tail}。"),
                ("info", "/my-orders", false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "订单变更通知写入失败（变更本身已成功）");
        }
    }

    private static OrderChangeDto ToDto(OrderChange c) => new()
    {
        ChangeId = c.ChangeId,
        OrderId = c.OrderId,
        ChangeType = c.ChangeType,
        OldVehicleId = c.OldVehicleId,
        NewVehicleId = c.NewVehicleId,
        OldStart = c.OldStart,
        OldEnd = c.OldEnd,
        NewStart = c.NewStart,
        NewEnd = c.NewEnd,
        OldRent = c.OldRent,
        NewRent = c.NewRent,
        DiffAmount = c.DiffAmount,
        Reason = c.Reason,
        CreateTime = c.CreateTime,
    };
}
