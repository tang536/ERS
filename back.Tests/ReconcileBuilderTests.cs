using ERental.DTOs;
using ERental.Models;
using ERental.Services;

namespace ERental.Tests;

/// <summary>
/// 对账差异表的纯运算口径：不连库，直接把几组构造好的订单/支付记录喂进计算器，
/// 覆盖「缺渠道流水 / 金额不符 / 到账未更新状态 / 退款超额 / 取消单有收款」五类异常。
/// </summary>
public class ReconcileBuilderTests
{
    private static RentalOrder Order(int id, string no, decimal rent, decimal deposit, string status)
    {
        var order = RentalOrder.Create(
            (no, 1, id, null),
            (DateTime.Today, DateTime.Today.AddDays(1)),
            (rent, deposit),
            status);
        typeof(RentalOrder).GetProperty(nameof(RentalOrder.OrderId))!.SetValue(order, id);
        return order;
    }

    private static PaymentRecord Paid(int orderId, decimal amount, string channel, string? tradeNo)
    {
        var pay = PaymentRecord.Create(orderId, amount, channel);
        pay.MarkPaid(channel, DateTime.Today.AddHours(9), tradeNo);
        return pay;
    }

    [Fact]
    public void ChannelsCompareSystemReceiptsWithExternalFlow()
    {
        var orders = new[]
        {
            Order(1, "ORD-1", 100m, 50m, "已支付"),
            Order(2, "ORD-2", 100m, 50m, "租赁中"),
        };
        var payments = new[]
        {
            Paid(1, 150m, "支付宝", "ALI-1"),
            Paid(2, 150m, "支付宝", null),          //已到账但没有渠道交易号
            Paid(2, 20m, "现金", null),
        };

        var result = ReconcileBuilder.Build(orders, payments, 0m, [], 0, DateTime.Today, DateTime.Today);

        var alipay = result.Channels.Single(c => c.Channel == "支付宝");
        Assert.True(alipay.IsElectronic);
        Assert.Equal(300m, alipay.SystemAmount);
        Assert.Equal(1, alipay.FlowCount);
        Assert.Equal(150m, alipay.FlowAmount);
        Assert.Equal(150m, alipay.Difference);
        Assert.Contains("缺渠道交易号", alipay.Note, StringComparison.Ordinal);

        var cash = result.Channels.Single(c => c.Channel == "现金");
        Assert.False(cash.IsElectronic);
        Assert.Equal(0m, cash.Difference);
        Assert.Contains("线下收款", cash.Note, StringComparison.Ordinal);

        //未标注渠道排最后
        Assert.Equal("支付宝", result.Channels[0].Channel);
        Assert.Equal("现金", result.Channels[^1].Channel);
    }

    [Fact]
    public void IssuesCoverAllMismatchKinds()
    {
        var orders = new[]
        {
            Order(1, "ORD-1", 100m, 50m, "已支付"),   //正常
            Order(2, "ORD-2", 100m, 50m, "租赁中"),   //缺渠道流水
            Order(3, "ORD-3", 100m, 50m, "待支付"),   //金额不符 + 到账未更新状态
            Order(4, "ORD-4", 100m, 0m, "已支付"),    //退款超额
        };
        var payments = new List<PaymentRecord>
        {
            Paid(1, 150m, "支付宝", "ALI-1"),
            Paid(2, 150m, "支付宝", null),
            Paid(3, 100m, "微信", "WX-1"),
            Paid(4, 100m, "银行卡", "BK-1"),
        };
        //退款超过原收款
        payments[3].ApplyRefund(120m);

        var cancelled = new[] { (Paid(99, 80m, "支付宝", "ALI-9"), "ORD-CANCELLED") };

        var result = ReconcileBuilder.Build(orders, payments, 30m, cancelled, 2, DateTime.Today, DateTime.Today);

        Assert.Equal(4, result.OrderCount);
        Assert.Equal(4, result.PaidCount);
        Assert.Equal(30m, result.TotalSupplement);
        Assert.Equal(2, result.SettleCount);

        var kinds = result.Issues.Select(i => i.Kind).ToList();
        Assert.Contains("缺渠道流水", kinds);
        Assert.Contains("收款金额不符", kinds);
        Assert.Contains("到账未更新状态", kinds);
        Assert.Contains("退款超额", kinds);
        Assert.Contains("已取消订单有收款", kinds);

        //正常单一笔不应出现在异常里
        Assert.DoesNotContain(result.Issues, i => i.OrderNo == "ORD-1");

        var cancelledIssue = result.Issues.Single(i => i.Kind == "已取消订单有收款");
        Assert.Equal("ORD-CANCELLED", cancelledIssue.OrderNo);
        Assert.Equal(80m, cancelledIssue.Amount);
    }

    [Fact]
    public void BalancedBookHasNoAnomaly()
    {
        var orders = new[] { Order(1, "ORD-1", 100m, 50m, "已支付") };
        var payments = new[] { Paid(1, 150m, "支付宝", "ALI-1") };

        var result = ReconcileBuilder.Build(orders, payments, 0m, [], 0, DateTime.Today, DateTime.Today);

        Assert.True(result.IsBalanced);
        Assert.Equal(0m, result.Difference);
        Assert.Empty(result.Issues);
    }
}
