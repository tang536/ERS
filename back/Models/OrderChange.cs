using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;

namespace ERental.Models;

///<summary>
/// 订单变更单：记录换车 / 改期的前后快照与差价。
/// 变更不覆盖原订单的历史，而是留一行「谁在什么时候把哪台车换成了哪台车」。
///</summary>
[Table("order_change")]
public class OrderChange
{
    public const string TypeVehicle = "换车";
    public const string TypeSchedule = "改期";

    [Key]
    [Column("change_id")]
    public int ChangeId { get; init; }

    [Column("order_id")]
    public int OrderId { get; private set; }

    [ForeignKey(nameof(OrderId))]
    public RentalOrder? RentalOrder { get; init; }

    [MaxLength(10)]
    [Column("change_type")]
    public string ChangeType { get; private set; } = string.Empty;

    [Column("old_vehicle_id")]
    public int? OldVehicleId { get; private set; }

    [Column("new_vehicle_id")]
    public int? NewVehicleId { get; private set; }

    [Column("old_start")]
    public DateTime? OldStart { get; private set; }

    [Column("old_end")]
    public DateTime? OldEnd { get; private set; }

    [Column("new_start")]
    public DateTime? NewStart { get; private set; }

    [Column("new_end")]
    public DateTime? NewEnd { get; private set; }

    [Column("old_rent", TypeName = "decimal(10,2)")]
    public decimal OldRent { get; private set; }

    [Column("new_rent", TypeName = "decimal(10,2)")]
    public decimal NewRent { get; private set; }

    ///<summary>差价：正数需补缴、负数需退款</summary>
    [Column("diff_amount", TypeName = "decimal(10,2)")]
    public decimal DiffAmount { get; private set; }

    [MaxLength(200)]
    [Column("reason")]
    public string? Reason { get; private set; }

    [Column("operator_id")]
    public int? OperatorId { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; init; } = AppClock.Now;

    ///<summary>换车变更</summary>
    public static OrderChange ForVehicle(int orderId, int oldVehicleId, int newVehicleId,
        decimal oldRent, decimal newRent, string? reason, int? operatorId) => new()
        {
            OrderId = orderId,
            ChangeType = TypeVehicle,
            OldVehicleId = oldVehicleId,
            NewVehicleId = newVehicleId,
            OldRent = oldRent,
            NewRent = newRent,
            DiffAmount = Math.Round(newRent - oldRent, 2),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OperatorId = operatorId,
        };

    ///<summary>改期变更</summary>
    public static OrderChange ForSchedule(int orderId, DateTime oldStart, DateTime oldEnd,
        DateTime newStart, DateTime newEnd, decimal oldRent, decimal newRent, string? reason, int? operatorId) => new()
        {
            OrderId = orderId,
            ChangeType = TypeSchedule,
            OldStart = oldStart,
            OldEnd = oldEnd,
            NewStart = newStart,
            NewEnd = newEnd,
            OldRent = oldRent,
            NewRent = newRent,
            DiffAmount = Math.Round(newRent - oldRent, 2),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OperatorId = operatorId,
        };
}
