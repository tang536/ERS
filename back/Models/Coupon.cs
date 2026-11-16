using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

///<summary>
/// 优惠券模板：门店配置「满减」「新客券」等营销活动。
/// 券只抵扣租金，不抵押金；一单最多用一张，过期自动作废。
///</summary>
[Table("coupon")]
public class Coupon
{
    private static readonly string[] LegalStatuses = ["启用", "停用"];

    [Key]
    [Column("coupon_id")]
    public int CouponId { get; init; }

    [Required, MaxLength(40)]
    [Column("name")]
    public string Name { get; private set; } = string.Empty;

    ///<summary>抵扣金额（元）</summary>
    [Column("amount", TypeName = "decimal(10,2)")]
    public decimal Amount { get; private set; }

    ///<summary>使用门槛：订单租金达到该金额才可用，0 表示无门槛</summary>
    [Column("min_amount", TypeName = "decimal(10,2)")]
    public decimal MinAmount { get; private set; }

    [Column("valid_from")]
    public DateTime ValidFrom { get; private set; }

    [Column("valid_to")]
    public DateTime ValidTo { get; private set; }

    ///<summary>发放总量，0 表示不限量</summary>
    [Column("total_qty")]
    public int TotalQty { get; private set; }

    [Column("issued_qty")]
    public int IssuedQty { get; private set; }

    ///<summary>同一顾客最多可领张数</summary>
    [Column("per_limit")]
    public int PerLimit { get; private set; } = 1;

    [MaxLength(10)]
    [Column("status")]
    public string Status { get; private set; } = "启用";

    ///<summary>
    /// 兑换码：管理员开放的「口令领券」凭据。顾客在顾客端输入该码即可自助领券。
    /// 与顾客持券的二维码码值是两件事：兑换码是模板级的、可反复使用（受库存与限领约束）；
    /// customer_coupon.code 是每张券唯一的核销码，只属于某一位顾客。
    ///</summary>
    [MaxLength(20)]
    [Column("redeem_code")]
    public string? RedeemCode { get; private set; }

    ///<summary>是否开放兑换：关闭后兑换码立即失效</summary>
    [Column("redeem_open")]
    public bool RedeemOpen { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; init; } = AppClock.Now;

    [NotMapped]
    public bool IsActive => Status == "启用";

    ///<summary>新建券模板：金额、门槛、有效期与限领数量一次校验</summary>
    public static Coupon Create(string name, decimal amount, decimal minAmount,
        DateTime validFrom, DateTime validTo, int totalQty, int perLimit,
        string? redeemCode = null, bool redeemOpen = false)
    {
        Validate(name, amount, minAmount, validFrom, validTo, totalQty, perLimit);
        return new Coupon
        {
            Name = name.Trim(),
            Amount = amount,
            MinAmount = minAmount,
            ValidFrom = validFrom,
            ValidTo = validTo,
            TotalQty = totalQty,
            PerLimit = perLimit,
            Status = "启用",
            RedeemCode = NormalizeCode(redeemCode),
            RedeemOpen = redeemOpen && !string.IsNullOrWhiteSpace(redeemCode),
        };
    }

    ///<summary>编辑券模板（已发放的券保持原样，只影响后续发放与使用判定）</summary>
    public void Update(string name, decimal amount, decimal minAmount,
        DateTime validFrom, DateTime validTo, int totalQty, int perLimit,
        string? redeemCode = null, bool redeemOpen = false)
    {
        Validate(name, amount, minAmount, validFrom, validTo, totalQty, perLimit);
        Name = name.Trim();
        Amount = amount;
        MinAmount = minAmount;
        ValidFrom = validFrom;
        ValidTo = validTo;
        TotalQty = totalQty;
        PerLimit = perLimit;
        RedeemCode = NormalizeCode(redeemCode);
        //没有码就谈不上开放兑换：避免出现「开关打开但顾客无码可输」的悬空配置
        RedeemOpen = redeemOpen && RedeemCode != null;
    }

    ///<summary>兑换开关：只改开关，不动码值</summary>
    public void SetRedeemOpen(bool open)
    {
        if (open && RedeemCode == null) throw new BusinessException("请先为该券设置兑换码，再开放领取");
        RedeemOpen = open;
    }

    ///<summary>兑换码规范化：统一大写去空格；允许 4~20 位字母数字，避免顾客输入中文/符号造成匹配歧义</summary>
    public static string? NormalizeCode(string? code)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (c.Length == 0) return null;
        if (c.Length is < 4 or > 20) throw new BusinessException("兑换码需为 4~20 位字母或数字");
        if (!c.All(char.IsAsciiLetterOrDigit)) throw new BusinessException("兑换码只能包含字母和数字");
        return c;
    }

    ///<summary>启用/停用：停用只拦「继续发放」，已领出的券在有效期内仍可使用</summary>
    public void SetStatus(string status)
    {
        if (Array.IndexOf(LegalStatuses, status) < 0) throw new BusinessException("券状态只能是「启用/停用」");
        Status = status;
    }

    ///<summary>发放量 +1（调用方需保证总量校验与自增在同一事务内）</summary>
    public void MarkIssued() => IssuedQty++;

    ///<summary>指定时间点是否在有效期内</summary>
    public bool ValidAt(DateTime at) => at >= ValidFrom && at <= ValidTo;

    ///<summary>是否还有库存可发</summary>
    public bool HasStock => TotalQty == 0 || IssuedQty < TotalQty;

    private static void Validate(string name, decimal amount, decimal minAmount,
        DateTime validFrom, DateTime validTo, int totalQty, int perLimit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("券名称不能为空");
        if (amount <= 0) throw new BusinessException("券面额必须大于 0");
        if (minAmount < 0) throw new BusinessException("使用门槛不能为负数");
        if (validTo <= validFrom) throw new BusinessException("失效时间必须晚于生效时间");
        if (totalQty < 0) throw new BusinessException("发放总量不能为负数");
        if (perLimit < 1) throw new BusinessException("每人限领至少 1 张");
    }
}
