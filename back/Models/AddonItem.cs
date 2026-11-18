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

    ///<summary>服务图片：下单页展示用，保存 ImageStorage 生成的 /uploads/addon/… 相对 URL</summary>
    [MaxLength(255)]
    [Column("image_url")]
    public string? ImageUrl { get; private set; }

    [MaxLength(10)]
    [Column("status")]
    public string Status { get; private set; } = "启用";

    ///<summary>实物类增值服务库存；-1 表示不限制库存（送车上门、保险等虚拟服务）</summary>
    [Column("stock_qty")]
    public int StockQty { get; private set; } = -1;

    ///<summary>低库存提醒阈值；0 表示不提醒</summary>
    [Column("low_stock_threshold")]
    public int LowStockThreshold { get; private set; }

    [Column("sort_no")]
    public int SortNo { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; init; } = AppClock.Now;

    [NotMapped]
    public bool IsDaily => Unit == UnitDaily;

    public static AddonItem Create(string name, decimal price, string unit, string? description, int sortNo,
        int stockQty = -1, int lowStockThreshold = 0)
    {
        Validate(name, price, unit);
        ValidateStock(stockQty, lowStockThreshold);
        return new AddonItem
        {
            Name = name.Trim(),
            Price = price,
            Unit = unit,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SortNo = sortNo,
            StockQty = stockQty,
            LowStockThreshold = lowStockThreshold,
        };
    }

    public void Update(string name, decimal price, string unit, string? description, int sortNo,
        int stockQty = -1, int lowStockThreshold = 0)
    {
        Validate(name, price, unit);
        ValidateStock(stockQty, lowStockThreshold);
        Name = name.Trim();
        Price = price;
        Unit = unit;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SortNo = sortNo;
        StockQty = stockQty;
        LowStockThreshold = lowStockThreshold;
    }

    public void SetStatus(string status)
    {
        if (status is not ("启用" or "停用")) throw new BusinessException("服务项状态只能是「启用/停用」");
        Status = status;
    }

    ///<summary>设置或清除服务图片：只接受本系统 /uploads/addon/ 下的地址</summary>
    public void SetImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ImageUrl = null;
            return;
        }
        var trimmed = url.Trim();
        if (!ImageStorage.IsOwnedUrl(trimmed, "addon"))
            throw new BusinessException("服务图片只支持本站上传的 /uploads/addon/ 地址");
        ImageUrl = trimmed;
    }

    ///<summary>按租期计算费用：按天项以「向上取整的天数」计，一次性项只收一次</summary>
    public decimal AmountFor(int billableDays) => IsDaily ? Math.Round(Price * Math.Max(1, billableDays), 2) : Price;

    ///<summary>下单占用 1 件库存；-1 表示不限库存。库存不足直接拒绝，避免超卖。</summary>
    public void ConsumeOne()
    {
        if (StockQty < 0) return;
        if (StockQty <= 0) throw new BusinessException($"「{Name}」库存不足，请先补货或取消勾选");
        StockQty--;
    }

    ///<summary>订单取消时归还 1 件库存；不限库存的项不动。</summary>
    public void RestoreOne()
    {
        if (StockQty >= 0) StockQty++;
    }

    public bool IsLowStock => StockQty >= 0 && LowStockThreshold > 0 && StockQty <= LowStockThreshold;

    private static void ValidateStock(int stockQty, int lowStockThreshold)
    {
        if (stockQty < -1) throw new BusinessException("库存只能是 -1（不限）或非负整数");
        if (lowStockThreshold < 0) throw new BusinessException("低库存阈值不能为负数");
    }

    private static void Validate(string name, decimal price, string unit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("服务名称不能为空");
        if (price < 0) throw new BusinessException("服务价格不能为负数");
        if (unit is not (UnitOnce or UnitDaily)) throw new BusinessException("计价方式只能是「一次性/按天」");
    }
}
