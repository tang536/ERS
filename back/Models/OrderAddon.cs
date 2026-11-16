using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERental.Models;

///<summary>
/// 订单增值服务快照：名称与单价在下单时固化，后台改价不影响历史订单。
///</summary>
[Table("order_addon")]
public class OrderAddon
{
    [Key]
    [Column("id")]
    public int Id { get; init; }

    [Column("order_id")]
    public int OrderId { get; private set; }

    [ForeignKey(nameof(OrderId))]
    public RentalOrder? RentalOrder { get; init; }

    [Column("addon_id")]
    public int AddonId { get; private set; }

    [MaxLength(30)]
    [Column("name")]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(10)]
    [Column("unit")]
    public string Unit { get; private set; } = AddonItem.UnitOnce;

    [Column("price", TypeName = "decimal(10,2)")]
    public decimal Price { get; private set; }

    ///<summary>计费数量：按天项为天数，一次性项为 1</summary>
    [Column("qty")]
    public int Qty { get; private set; } = 1;

    [Column("amount", TypeName = "decimal(10,2)")]
    public decimal Amount { get; private set; }

    ///<summary>下单时固化服务项快照并算出金额</summary>
    public static OrderAddon Snapshot(int orderId, AddonItem item, int billableDays)
    {
        var qty = item.IsDaily ? Math.Max(1, billableDays) : 1;
        return new OrderAddon
        {
            OrderId = orderId,
            AddonId = item.AddonId,
            Name = item.Name,
            Unit = item.Unit,
            Price = item.Price,
            Qty = qty,
            Amount = item.AmountFor(billableDays),
        };
    }
}
