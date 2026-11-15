using ERental.DTOs;
using ERental.Models;

namespace ERental.Services;

/// <summary>
/// 财务对账的差异计算：把「订单应收」「支付记录」「已取消订单的到账记录」三组数据
/// 折算成汇总口径 + 分渠道对照 + 逐笔异常。
///
/// 为什么单独抽出来：对账最容易出错的是口径（哪些状态算钱到账、渠道怎么归一），
/// 而这部分完全是纯运算，不碰数据库就能用固定数据把所有分支跑一遍；
/// 查询留在 <see cref="SettlementService.Reconcile"/>，本类只负责算账，两边都好维护。
///
/// 口径说明：
///   · 系统收款 = payment_record 中「钱到过账」的状态（已支付 / 部分退款 / 已退款）；
///   · 渠道流水 = 上述记录里带外部交易号（transaction_no）的部分，代表通道侧确有回执；
///   · 现金等线下渠道本来就没有外部流水，不进差异口径，只在说明里标注。
/// </summary>
internal static class ReconcileBuilder
{
    /// <summary>没有记录收款渠道时统一显示的名字</summary>
    private const string ChannelUnknown = "未标注";

    /// <summary>有外部流水可核的线上渠道</summary>
    private static readonly HashSet<string> ElectronicChannels = new(StringComparer.Ordinal)
    {
        "支付宝", "微信", "银行卡"
    };

    /// <summary>渠道归一：空值与下单占位值都归到「未标注」</summary>
    private static string NormalizeChannel(string? payType) =>
        string.IsNullOrWhiteSpace(payType) || payType == PayStatuses.ChannelPlaceholder
            ? ChannelUnknown
            : payType.Trim();

    /// <summary>把已到账的支付记录按渠道聚合成对照行</summary>
    public static List<ReconcileChannelDto> BuildChannels(IEnumerable<PaymentRecord> payments) =>
        payments
            .GroupBy(p => NormalizeChannel(p.PayType), StringComparer.Ordinal)
            .Select(g =>
            {
                var electronic = ElectronicChannels.Contains(g.Key);
                var systemAmount = g.Sum(p => p.PayAmount);
                var flow = g.Where(p => !string.IsNullOrWhiteSpace(p.TransactionNo)).ToList();
                var flowAmount = flow.Sum(p => p.PayAmount);
                var refund = g.Sum(p => p.RefundAmount);
                var difference = electronic ? Math.Round(systemAmount - flowAmount, 2) : 0m;
                var missing = electronic ? g.Count() - flow.Count : 0;

                return new ReconcileChannelDto
                {
                    Channel = g.Key,
                    IsElectronic = electronic,
                    SystemCount = g.Count(),
                    SystemAmount = Math.Round(systemAmount, 2),
                    FlowCount = flow.Count,
                    FlowAmount = Math.Round(flowAmount, 2),
                    RefundAmount = Math.Round(refund, 2),
                    NetAmount = Math.Round(systemAmount - refund, 2),
                    Difference = difference,
                    Note = g.Key switch
                    {
                        ChannelUnknown => "未记录收款渠道，无法归类核对",
                        _ when electronic && missing > 0 => $"{missing} 笔已到账记录缺渠道交易号",
                        _ when electronic => "渠道流水齐全",
                        _ => "线下收款，无外部渠道流水"
                    }
                };
            })
            //未标注的排最后；线上渠道优先，其余按名称稳定排序
            .OrderBy(c => c.Channel == ChannelUnknown ? 1 : 0)
            .ThenByDescending(c => c.IsElectronic)
            .ThenBy(c => c.Channel, StringComparer.Ordinal)
            .ToList();

    /// <summary>逐笔异常：按「缺凭据 → 退款 → 金额 → 状态 → 取消单」的顺序列出</summary>
    public static List<ReconcileIssueDto> BuildIssues(IEnumerable<RentalOrder> orders, IEnumerable<PaymentRecord> payments, IEnumerable<(PaymentRecord Pay, string OrderNo)> cancelledPaid)
    {
        List<ReconcileIssueDto> issues = [];
        var orderList = orders.ToList();
        var orderNoById = orderList.ToDictionary(o => o.OrderId, o => o.OrderNo);
        var paymentsByOrder = payments.GroupBy(p => p.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var order in orderList)
        {
            if (!paymentsByOrder.TryGetValue(order.OrderId, out var rows)) continue;
            var paid = rows.Sum(p => p.PayAmount);

            foreach (var p in rows)
            {
                var channel = NormalizeChannel(p.PayType);
                if (ElectronicChannels.Contains(channel) && string.IsNullOrWhiteSpace(p.TransactionNo))
                {
                    issues.Add(new ReconcileIssueDto
                    {
                        OrderNo = order.OrderNo,
                        Channel = channel,
                        Kind = "缺渠道流水",
                        Amount = p.PayAmount,
                        Detail = "记录已到账但交易号为空，无法与渠道账单核对"
                    });
                }

                if (p.RefundAmount > p.PayAmount + 0.01m)
                {
                    issues.Add(new ReconcileIssueDto
                    {
                        OrderNo = order.OrderNo,
                        Channel = channel,
                        Kind = "退款超额",
                        Amount = p.RefundAmount,
                        Expected = p.PayAmount,
                        Detail = "累计退款金额超过原收款金额"
                    });
                }
            }

            var expected = order.TotalRent + order.Deposit;
            if (Math.Abs(paid - expected) > 0.01m)
            {
                issues.Add(new ReconcileIssueDto
                {
                    OrderNo = order.OrderNo,
                    Channel = string.Join("/", rows.Select(p => NormalizeChannel(p.PayType)).Distinct(StringComparer.Ordinal)),
                    Kind = "收款金额不符",
                    Amount = Math.Round(paid, 2),
                    Expected = Math.Round(expected, 2),
                    Detail = "已到账合计与订单应收（租金+押金）不一致"
                });
            }

            if (order.OrderStatus == "待支付" && paid > 0.01m)
            {
                issues.Add(new ReconcileIssueDto
                {
                    OrderNo = order.OrderNo,
                    Channel = string.Join("/", rows.Select(p => NormalizeChannel(p.PayType)).Distinct(StringComparer.Ordinal)),
                    Kind = "到账未更新状态",
                    Amount = Math.Round(paid, 2),
                    Detail = "已有到账记录，订单状态仍为「待支付」"
                });
            }
        }

        foreach (var (pay, orderNo) in cancelledPaid)
        {
            issues.Add(new ReconcileIssueDto
            {
                OrderNo = orderNo,
                Channel = NormalizeChannel(pay.PayType),
                Kind = "已取消订单有收款",
                Amount = pay.PayAmount,
                Detail = "订单已取消但支付记录仍为已到账，需确认是否应退款"
            });
        }

        //固定排序：同类型内按订单号，保证同一批数据每次打开顺序一致
        issues.Sort((a, b) =>
        {
            var byKind = string.CompareOrdinal(a.Kind, b.Kind);
            return byKind != 0 ? byKind : string.CompareOrdinal(a.OrderNo, b.OrderNo);
        });
        return issues;
    }

    /// <summary>汇总口径：与原有对账返回字段保持一致，额外带上渠道对照与异常明细</summary>
    public static ReconcileDto Build(IReadOnlyCollection<RentalOrder> orders, IReadOnlyCollection<PaymentRecord> orderPayments, decimal totalSupplement, IEnumerable<(PaymentRecord Pay, string OrderNo)> cancelledPaid, int settleCount, DateTime startDate, DateTime endDate)
    {
        var expectedRent = orders.Sum(o => o.TotalRent);
        var expectedDeposit = orders.Sum(o => o.Deposit);
        var expectedTotal = expectedRent + expectedDeposit;

        var actualReceived = orderPayments.Sum(p => p.PayAmount);
        var totalRefund = orderPayments.Sum(p => p.RefundAmount);
        var netIncome = Math.Round(actualReceived - totalRefund + totalSupplement, 2);
        var difference = Math.Round(expectedTotal - actualReceived, 2);

        //差异文案单独算：三级判断用 switch 表达式，既不嵌套三元（Sonar S3358）
        var remark = difference switch
        {
            _ when Math.Abs(difference) < 0.01m => "账目平衡",
            > 0 => $"存在 {difference:C} 差额（应收大于实收）",
            _ => $"存在 {-difference:C} 差额（实收大于应收）"
        };

        return new ReconcileDto
        {
            StartDate = startDate.ToString("yyyy-MM-dd"),
            EndDate = endDate.ToString("yyyy-MM-dd"),
            OrderCount = orders.Count,
            PaidCount = orderPayments.Count,
            SettleCount = settleCount,
            ExpectedRent = Math.Round(expectedRent, 2),
            ExpectedDeposit = Math.Round(expectedDeposit, 2),
            ExpectedTotal = Math.Round(expectedTotal, 2),
            ActualReceived = Math.Round(actualReceived, 2),
            TotalRefund = Math.Round(totalRefund, 2),
            TotalSupplement = Math.Round(totalSupplement, 2),
            NetIncome = netIncome,
            Difference = difference,
            IsBalanced = Math.Abs(difference) < 0.01m,
            Remark = remark,
            Channels = BuildChannels(orderPayments),
            Issues = BuildIssues(orders, orderPayments, cancelledPaid)
        };
    }
}
