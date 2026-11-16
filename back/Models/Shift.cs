using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

///<summary>
/// 交接班：前台开班时登记备用金，交班时系统按班次区间汇总各渠道收款，
/// 前台只需点清现金，差额必须写原因——这是门店「日结」的最小闭环。
///</summary>
[Table("shift")]
public class Shift
{
    [Key]
    [Column("shift_id")]
    public int ShiftId { get; init; }

    [Required, MaxLength(30)]
    [Column("shift_no")]
    public string ShiftNo { get; private set; } = string.Empty;

    [Column("operator_id")]
    public int? OperatorId { get; private set; }

    [MaxLength(20)]
    [Column("operator_name")]
    public string OperatorName { get; private set; } = string.Empty;

    [Column("open_time")]
    public DateTime OpenTime { get; private set; }

    [Column("close_time")]
    public DateTime? CloseTime { get; private set; }

    ///<summary>开班备用金（元）</summary>
    [Column("opening_cash", TypeName = "decimal(10,2)")]
    public decimal OpeningCash { get; private set; }

    ///<summary>本班现金净收入（收款减退款）</summary>
    [Column("cash_collected", TypeName = "decimal(10,2)")]
    public decimal CashCollected { get; private set; }

    ///<summary>交班时实际点清的现金</summary>
    [Column("cash_actual", TypeName = "decimal(10,2)")]
    public decimal CashActual { get; private set; }

    ///<summary>实收 - (备用金 + 现金净收入)，正数长款、负数短款</summary>
    [Column("difference", TypeName = "decimal(10,2)")]
    public decimal Difference { get; private set; }

    [Column("alipay_amount", TypeName = "decimal(10,2)")]
    public decimal AlipayAmount { get; private set; }

    [Column("wechat_amount", TypeName = "decimal(10,2)")]
    public decimal WechatAmount { get; private set; }

    [Column("bank_amount", TypeName = "decimal(10,2)")]
    public decimal BankAmount { get; private set; }

    [Column("order_count")]
    public int OrderCount { get; private set; }

    [Column("settle_count")]
    public int SettleCount { get; private set; }

    [MaxLength(10)]
    [Column("status")]
    public string Status { get; private set; } = "进行中";

    [MaxLength(200)]
    [Column("note")]
    public string? Note { get; private set; }

    ///<summary>开班：同一个人同时只能有一个进行中的班次（服务层校验）</summary>
    public static Shift Open(string shiftNo, int? operatorId, string operatorName, decimal openingCash)
    {
        if (string.IsNullOrWhiteSpace(shiftNo)) throw new BusinessException("班次号不能为空");
        if (openingCash < 0) throw new BusinessException("备用金不能为负数");
        return new Shift
        {
            ShiftNo = shiftNo.Trim(),
            OperatorId = operatorId,
            OperatorName = string.IsNullOrWhiteSpace(operatorName) ? "门店员工" : operatorName.Trim(),
            OpenTime = AppClock.Now,
            OpeningCash = openingCash,
            Status = "进行中",
        };
    }

    ///<summary>交班：写入系统汇总值、前台实点数与差额说明</summary>
    public void Close(DateTime now, decimal cashCollected, decimal alipay, decimal wechat, decimal bank,
        int orderCount, int settleCount, decimal cashActual, string? note)
    {
        if (Status != "进行中") throw new BusinessException("该班次已交班，请勿重复操作");
        if (cashActual < 0) throw new BusinessException("实收现金不能为负数");
        if (cashCollected < 0 || alipay < 0 || wechat < 0 || bank < 0) throw new BusinessException("收款金额不能为负数");

        CashCollected = cashCollected;
        AlipayAmount = alipay;
        WechatAmount = wechat;
        BankAmount = bank;
        OrderCount = orderCount;
        SettleCount = settleCount;
        CashActual = cashActual;
        Difference = Math.Round(cashActual - (OpeningCash + cashCollected), 2);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CloseTime = now;
        Status = "已交班";
    }
}
