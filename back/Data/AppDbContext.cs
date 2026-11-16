using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MemberRights> MemberRights => Set<MemberRights>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<RentalOrder> RentalOrders => Set<RentalOrder>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<SettleRecord> SettleRecords => Set<SettleRecord>();
    public DbSet<OrderHandover> OrderHandovers => Set<OrderHandover>();
    public DbSet<MaintenanceOrder> MaintenanceOrders => Set<MaintenanceOrder>();
    public DbSet<ScrapRecord> ScrapRecords => Set<ScrapRecord>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<PayReconcileLog> PayReconcileLogs => Set<PayReconcileLog>();
    public DbSet<RenewalRecord> RenewalRecords => Set<RenewalRecord>();
    public DbSet<CarCheckList> CarCheckLists => Set<CarCheckList>();
    public DbSet<CheckReport> CheckReports => Set<CheckReport>();
    public DbSet<AppMessage> Messages => Set<AppMessage>();

    //运营与顾客权益：券、增值服务、积分、交接班、订单变更、电子合同
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CustomerCoupon> CustomerCoupons => Set<CustomerCoupon>();
    public DbSet<AddonItem> AddonItems => Set<AddonItem>();
    public DbSet<OrderAddon> OrderAddons => Set<OrderAddon>();
    public DbSet<PointLog> PointLogs => Set<PointLog>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<OrderChange> OrderChanges => Set<OrderChange>();
    public DbSet<Contract> Contracts => Set<Contract>();

    //数据库视图：只读查询模型，不参与建表
    public DbSet<OrderOverviewView> OrderOverviews => Set<OrderOverviewView>();
    public DbSet<SettlementBillView> SettlementBills => Set<SettlementBillView>();
    public DbSet<VehicleMaintenanceDueView> VehicleMaintenanceDues => Set<VehicleMaintenanceDueView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        //TPH继承：AdminUser/ReceptionUser/FinanceUser 都映射到 system_user 表
        modelBuilder.Entity<SystemUser>().HasDiscriminator<string>("user_type").HasValue<SystemUser>("staff").HasValue<AdminUser>("admin").HasValue<ReceptionUser>("reception").HasValue<FinanceUser>("finance");

        //视图映射：HasNoKey 明确告诉 EF 这是只读结果集，EnsureCreated 不会尝试建表
        modelBuilder.Entity<OrderOverviewView>().HasNoKey().ToView("v_order_overview");
        modelBuilder.Entity<SettlementBillView>().HasNoKey().ToView("v_settlement_bill");
        modelBuilder.Entity<VehicleMaintenanceDueView>().HasNoKey().ToView("v_vehicle_maintenance_due");

        //支付记录
        modelBuilder.Entity<PaymentRecord>(e =>
        {
            e.Property(p => p.BizType).IsRequired().HasDefaultValue("order");
            e.Property(p => p.OutTradeNo).IsRequired(false);
            e.HasIndex(p => new { p.OrderId, p.BizType, p.PayStatus }).HasDatabaseName("idx_pay_order_biz_status");
            // 通道商户单号是回调查单的唯一锚点，必须唯一；NULL 允许重复（未走线上通道的记录）
            e.HasIndex(p => p.OutTradeNo).IsUnique().HasDatabaseName("uk_pay_out_trade_no");
        });

        modelBuilder.Entity<Coupon>(e =>
        {
            // 兑换码是顾客领券的公开凭据，重复会让「按码找券」产生歧义
            e.HasIndex(c => c.RedeemCode).IsUnique().HasDatabaseName("uk_coupon_redeem_code");
        });

        modelBuilder.Entity<OrderAddon>(e =>
        {
            e.HasIndex(a => new { a.OrderId, a.AddonId }).IsUnique().HasDatabaseName("uk_order_addon");
        });

        modelBuilder.Entity<AppMessage>(e =>
        {
            e.HasIndex(m => new { m.ReceiverType, m.ReceiverId, m.IsRead }).HasDatabaseName("idx_msg_receiver");
            e.HasIndex(m => new { m.ReceiverType, m.ReceiverId, m.RefType, m.RefId }).HasDatabaseName("uk_msg_biz").IsUnique();
            // 接收人只用多态字段 receiver_type + receiver_id 表达；不再冗余两列互斥外键
        });

        modelBuilder.Entity<MemberRights>(e =>
        {
            // 等级名是客户表外键的自然键，必须唯一
            e.HasIndex(r => r.MemberLevel).IsUnique().HasDatabaseName("uk_member_level");
        });

        //角色-权限多对多（RBAC）：关系在代码层维护

        //盘点：报表 1 — 0..* 明细
        modelBuilder.Entity<CheckReport>(e =>
        {
            e.HasMany(r => r.Details).WithOne(c => c.Report)
                .HasForeignKey(c => c.ReportId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(r => r.Operator).WithMany()
                .HasForeignKey(r => r.OperatorId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CarCheckList>(e =>
        {
            e.HasOne(c => c.Operator).WithMany()
                .HasForeignKey(c => c.OperatorId).OnDelete(DeleteBehavior.SetNull);
        });

        //客户 — 会员权益：以等级名作为自然外键，客户表不再冗余 member_rights_id
        modelBuilder.Entity<Customer>(e =>
        {
            e.HasOne(c => c.MemberRights).WithMany(r => r.Customers)
                .HasForeignKey(c => c.MemberLevel)
                .HasPrincipalKey(r => r.MemberLevel)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.MemberLevel).HasDatabaseName("idx_customer_level");
            // 空库由 EnsureCreated 建表时也要带上 schema.sql 里的 DEFAULT 1：
            // 演示数据等直接写 SQL 的插入会省略 version，缺默认值会报 1364。
            e.Property(c => c.Version).HasDefaultValue(1);
            e.Property(c => c.Points).HasDefaultValue(0);
        });

        //订单金额构成：老订单缺省为「无优惠、无增值服务」，结算口径保持不变
        modelBuilder.Entity<RentalOrder>(e =>
        {
            e.Property(o => o.DiscountAmount).HasDefaultValue(0m);
            e.Property(o => o.RentDiscount).HasDefaultValue(1.00m);
            e.Property(o => o.AddonFee).HasDefaultValue(0m);
            e.Property(o => o.PointsUsed).HasDefaultValue(0);
        });

        modelBuilder.Entity<CustomerCoupon>(e =>
        {
            e.HasOne(c => c.Coupon).WithMany().HasForeignKey(c => c.CouponId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.CustomerId, c.Status }).HasDatabaseName("idx_coupon_customer_status");
            e.HasIndex(c => c.OrderId).HasDatabaseName("idx_coupon_order");
        });

        modelBuilder.Entity<OrderAddon>(e =>
        {
            e.HasOne(a => a.RentalOrder).WithMany().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.OrderId).HasDatabaseName("idx_addon_order");
        });

        modelBuilder.Entity<OrderChange>(e =>
        {
            e.HasOne(c => c.RentalOrder).WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.OrderId).HasDatabaseName("idx_order_change_order");
        });

        modelBuilder.Entity<PointLog>(e =>
        {
            e.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.CustomerId, p.CreateTime }).HasDatabaseName("idx_point_customer_time");
        });

        modelBuilder.Entity<Contract>(e =>
        {
            e.HasOne(c => c.RentalOrder).WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.OrderId).HasDatabaseName("uk_contract_order").IsUnique();
        });

        modelBuilder.Entity<Shift>(e =>
        {
            e.HasIndex(s => s.OperatorId).HasDatabaseName("idx_shift_operator");
            e.HasIndex(s => s.OpenTime).HasDatabaseName("idx_shift_open_time");
        });

        //系统日志 — 操作人：日志只增不删，账号删除后置空 user_id，日志内容保留
        modelBuilder.Entity<SystemLog>(e =>
        {
            e.HasOne(l => l.Operator).WithMany()
                .HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
