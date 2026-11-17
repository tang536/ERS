using ERental.Data;
using ERental.Models;
using ERental.Services;

namespace ERental.Tests;

/// <summary>
/// 订单状态机必须只允许业务正向迁移：
/// 已支付不能直接跳过租赁中进入已完结（结算走 MarkReturned 专用方法），
/// 租赁中不能取消（取消只允许待支付/已支付）。
/// </summary>
public class OrderStateMachineTests
{
    private static RentalOrder Order(string status = "待支付") => RentalOrder.Create(
        ("ZL-STATE-1", 1, 1, null),
        (new DateTime(2026, 11, 17, 9, 0, 0), new DateTime(2026, 11, 18, 9, 0, 0)),
        (60m, 500m),
        status);

    [Fact]
    public void ChangeStatus_AllowsOnlyForwardTransitions()
    {
        var order = Order();

        order.ChangeStatus("已支付");
        Assert.Equal("已支付", order.OrderStatus);

        // 结算使用 MarkReturned，普通 ChangeStatus 不允许跳过租赁中。
        Assert.Throws<BusinessException>(() => order.ChangeStatus("已完结"));

        order.ChangeStatus("租赁中");
        Assert.Throws<BusinessException>(() => order.ChangeStatus("已取消"));
        Assert.Throws<BusinessException>(() => order.ChangeStatus("已支付")); // 禁止回退
    }

    [Fact]
    public void CancelAndMarkReturned_KeepDedicatedTerminalPaths()
    {
        var pending = Order();
        pending.ChangeStatus("已支付");
        pending.Cancel("顾客临时取消");
        Assert.Equal("已取消", pending.OrderStatus);

        var rented = Order();
        rented.ChangeStatus("已支付");
        rented.ChangeStatus("租赁中");
        rented.MarkReturned(new DateTime(2026, 11, 18, 10, 0, 0));
        Assert.Equal("已完结", rented.OrderStatus);
    }
}
