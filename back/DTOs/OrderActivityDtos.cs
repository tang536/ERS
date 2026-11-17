namespace ERental.DTOs;

/// <summary>
/// 订单时间线事件。时间允许为空：历史数据只有状态没有落库时间时，前端仍能展示，
/// 但不会伪造一个精确时刻（例如早期取消单没有 cancel_reason 的写入时间）。
/// </summary>
public class OrderTimelineEventDto
{
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public DateTime? Time { get; init; }
    public decimal? Amount { get; init; }
}

/// <summary>订单完整时间线：下单、支付、取还车、换车/改期、结算、取消按时间正序聚合。</summary>
public class OrderTimelineDto
{
    public int OrderId { get; init; }
    public string OrderNo { get; init; } = string.Empty;
    public string OrderStatus { get; init; } = string.Empty;
    public List<OrderTimelineEventDto> Events { get; init; } = [];
}

/// <summary>车辆占用日历中的一条订单档期（只给员工端，含顾客姓名便于柜台核对）。</summary>
public class VehicleCalendarSlotDto
{
    public int OrderId { get; init; }
    public string OrderNo { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public DateTime LeaseStart { get; init; }
    public DateTime LeaseEnd { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
}

/// <summary>单台车辆在指定时间窗内的档期与当前状态。</summary>
public class VehicleCalendarDto
{
    public int VehicleId { get; init; }
    public string VehicleNo { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string VehicleStatus { get; init; } = string.Empty;
    public List<VehicleCalendarSlotDto> Slots { get; init; } = [];
}

/// <summary>车辆可用日历查询结果：时间窗回显，避免前端按本地时区自行推断。</summary>
public class VehicleCalendarResultDto
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public List<VehicleCalendarDto> Vehicles { get; init; } = [];
}
