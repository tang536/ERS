using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

///<summary>
/// 增值服务项：骑行保险、头盔、儿童座椅、送车上门等。
/// 计价方式只有「一次性」与「按天」两种，避免把定价模型做成配置引擎。
///</summary>
[Table("addon_item")]
public class AddonItem
{
    public const string UnitOnce = "一次性";
    public const string UnitDaily = "按天";

    [Key]
    [Column("addon_id")]
    public int AddonId { get; init; }

    [Required, MaxLength(30)]
    [Column("name")]
    public string Name { get; private set; } = string.Empty;

    [Column("price", TypeName = "decimal(10,2)")]
    public decimal Price { get; private set; }

    [MaxLength(10)]
    [Column("unit")]
    public string Unit { get; private set; } = UnitOnce;

    [MaxLength(200)]
    [Column("description")]
    public string? Description { get; private set; }

    [MaxLength(10)]
    [Column("status")]
    public string Status { get; private set; } = "启用";

    [Column("sort_no")]
    public int SortNo { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; init; } = AppClock.Now;

    [NotMapped]
    public bool IsDaily => Unit == UnitDaily;

    public static AddonItem Create(string name, decimal price, string unit, string? description, int sortNo)
    {
        Validate(name, price, unit);
        return new AddonItem
        {
            Name = name.Trim(),
            Price = price,
            Unit = unit,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SortNo = sortNo,
        };
    }

    public void Update(string name, decimal price, string unit, string? description, int sortNo)
    {
        Validate(name, price, unit);
        Name = name.Trim();
        Price = price;
        Unit = unit;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SortNo = sortNo;
    }

    public void SetStatus(string status)
    {
        if (status is not ("启用" or "停用")) throw new BusinessException("服务项状态只能是「启用/停用」");
        Status = status;
    }

    ///<summary>按租期计算费用：按天项以「向上取整的天数」计，一次性项只收一次</summary>
    public decimal AmountFor(int billableDays) => IsDaily ? Math.Round(Price * Math.Max(1, billableDays), 2) : Price;

    private static void Validate(string name, decimal price, string unit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("服务名称不能为空");
        if (price < 0) throw new BusinessException("服务价格不能为负数");
        if (unit is not (UnitOnce or UnitDaily)) throw new BusinessException("计价方式只能是「一次性/按天」");
    }
}
