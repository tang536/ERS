using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

/// <summary>
/// 预防性保养计划：按天数或里程触发。计划只负责「什么时候该保养」，
/// 真正的工单仍走 MaintenanceOrder，避免出现第二套维保流程。
/// </summary>
[Table("maintenance_plan")]
public class MaintenancePlan
{
    public const string TriggerDays = "按天数";
    public const string TriggerMileage = "按里程";
    public const string StatusEnabled = "启用";
    public const string StatusDisabled = "停用";

    [Key]
    [Column("plan_id")]
    public int PlanId { get; init; }

    [Column("vehicle_id")]
    public int VehicleId { get; private set; }

    [ForeignKey(nameof(VehicleId))]
    public Vehicle? Vehicle { get; init; }

    [Required, MaxLength(60)]
    [Column("plan_name")]
    public string PlanName { get; private set; } = string.Empty;

    [MaxLength(10)]
    [Column("trigger_type")]
    public string TriggerType { get; private set; } = TriggerDays;

    [Column("trigger_value")]
    public int TriggerValue { get; private set; }

    [Column("last_service_time")]
    public DateTime? LastServiceTime { get; private set; }

    [Column("last_mileage")]
    public int LastMileage { get; private set; }

    ///<summary>当前里程（门店手工维护），用于按里程判断是否到期</summary>
    [Column("current_mileage")]
    public int CurrentMileage { get; private set; }

    [Column("next_due_time")]
    public DateTime? NextDueTime { get; private set; }

    [MaxLength(20)]
    [Column("status")]
    public string Status { get; private set; } = StatusEnabled;

    [MaxLength(200)]
    [Column("remark")]
    public string? Remark { get; private set; }

    [Column("create_time")]
    public DateTime CreateTime { get; private set; } = AppClock.Now;

    public static MaintenancePlan Create(int vehicleId, string planName, string triggerType, int triggerValue,
        DateTime? lastServiceTime, int lastMileage, string? remark, int currentMileage = 0)
    {
        Validate(vehicleId, planName, triggerType, triggerValue, lastMileage);
        var plan = new MaintenancePlan
        {
            VehicleId = vehicleId,
            PlanName = planName.Trim(),
            TriggerType = triggerType,
            TriggerValue = triggerValue,
            LastServiceTime = lastServiceTime,
            LastMileage = lastMileage,
            CurrentMileage = Math.Max(lastMileage, currentMileage),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
        plan.RecalculateDue();
        return plan;
    }

    public void Update(string planName, string triggerType, int triggerValue, DateTime? lastServiceTime,
        int lastMileage, string? remark, int currentMileage = 0)
    {
        Validate(VehicleId, planName, triggerType, triggerValue, lastMileage);
        PlanName = planName.Trim();
        TriggerType = triggerType;
        TriggerValue = triggerValue;
        LastServiceTime = lastServiceTime;
        LastMileage = lastMileage;
        CurrentMileage = Math.Max(lastMileage, currentMileage);
        Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        RecalculateDue();
    }

    public void SetStatus(string status)
    {
        if (status is not (StatusEnabled or StatusDisabled)) throw new BusinessException("保养计划状态只能是「启用/停用」");
        Status = status;
    }

    public void MarkServiced(DateTime servicedAt, int mileage)
    {
        if (mileage < 0) throw new BusinessException("里程不能为负数");
        LastServiceTime = servicedAt;
        LastMileage = mileage;
        RecalculateDue();
    }

    /// <summary>按当前规则重算下次到期时间；按里程的计划没有日期，返回 null，由前端按里程差判断。</summary>
    public void RecalculateDue()
    {
        NextDueTime = TriggerType == TriggerDays
            ? (LastServiceTime ?? CreateTime).AddDays(Math.Max(1, TriggerValue))
            : null;
    }

    public bool IsDue(DateTime now, int currentMileage) => Status == StatusEnabled
        && (TriggerType == TriggerDays
            ? NextDueTime.HasValue && NextDueTime.Value <= now
            : currentMileage > 0 && currentMileage - LastMileage >= Math.Max(1, TriggerValue));

    private static void Validate(int vehicleId, string planName, string triggerType, int triggerValue, int lastMileage)
    {
        if (vehicleId <= 0) throw new BusinessException("保养计划必须关联车辆");
        if (string.IsNullOrWhiteSpace(planName)) throw new BusinessException("计划名称不能为空");
        if (triggerType is not (TriggerDays or TriggerMileage)) throw new BusinessException("触发方式只能是「按天数/按里程」");
        if (triggerValue <= 0) throw new BusinessException("触发值必须大于 0");
        if (lastMileage < 0) throw new BusinessException("里程不能为负数");
    }
}
