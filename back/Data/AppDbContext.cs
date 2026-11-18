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
    public DbSet<SessionState> Sessions => Set<SessionState>();
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

    //运营中心扩展：审批、发票、预防保养
    public DbSet<OpsApproval> OpsApprovals => Set<OpsApproval>();
    public DbSet<OpsInvoice> OpsInvoices => Set<OpsInvoice>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();

    //第二批运营扩展：预约、还车、评价、客服、推荐、企业客户、门店、电池、保险/事故、成本、备件、定价、团购、API、GPS、通知、车损
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<VehicleStore> VehicleStores => Set<VehicleStore>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<CustomerCreditProfile> CustomerCreditProfiles => Set<CustomerCreditProfile>();
    public DbSet<Battery> Batteries => Set<Battery>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<AccidentRecord> AccidentRecords => Set<AccidentRecord>();
    public DbSet<VehicleCost> VehicleCosts => Set<VehicleCost>();
    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<GroupVoucher> GroupVouchers => Set<GroupVoucher>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<GpsDevice> GpsDevices => Set<GpsDevice>();
    public DbSet<CouponCampaign> CouponCampaigns => Set<CouponCampaign>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<DamageAssessment> DamageAssessments => Set<DamageAssessment>();

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
            e.HasIndex(o => new { o.OrderStatus, o.LeaseEnd }).HasDatabaseName("idx_status_lease_end");
        });

        modelBuilder.Entity<SessionState>(e =>
        {
            e.HasIndex(s => s.SessionKey).IsUnique().HasDatabaseName("uk_session_key");
            e.HasIndex(s => s.UpdateTime).HasDatabaseName("idx_session_update");
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

        modelBuilder.Entity<OpsApproval>(e =>
        {
            e.HasIndex(a => a.RequestNo).IsUnique().HasDatabaseName("uk_ops_approval_no");
            e.HasIndex(a => new { a.Status, a.CreateTime }).HasDatabaseName("idx_ops_approval_status");
            e.HasIndex(a => new { a.BizType, a.RefId }).HasDatabaseName("idx_ops_approval_biz");
        });

        modelBuilder.Entity<OpsInvoice>(e =>
        {
            e.HasIndex(i => i.InvoiceNo).IsUnique().HasDatabaseName("uk_ops_invoice_no");
            e.HasIndex(i => new { i.Status, i.CreateTime }).HasDatabaseName("idx_ops_invoice_status");
            e.HasIndex(i => i.OrderId).HasDatabaseName("idx_ops_invoice_order");
            e.HasOne(i => i.RentalOrder).WithMany().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MaintenancePlan>(e =>
        {
            e.HasIndex(p => new { p.VehicleId, p.Status }).HasDatabaseName("idx_plan_vehicle_status");
            e.HasOne(p => p.Vehicle).WithMany().HasForeignKey(p => p.VehicleId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── 第二批扩展表索引 ───────────────────────────────
        modelBuilder.Entity<VehicleStore>(e =>
            e.HasIndex(v => v.VehicleId).IsUnique().HasDatabaseName("uk_vehicle_store_vehicle"));

        modelBuilder.Entity<Appointment>(e =>
            e.HasIndex(a => new { a.Status, a.AppointTime }).HasDatabaseName("idx_appointment_status_time"));

        modelBuilder.Entity<ReturnRequest>(e =>
        {
            e.HasIndex(r => new { r.OrderId, r.Status }).HasDatabaseName("idx_return_order_status");
            e.HasIndex(r => new { r.Status, r.ExpectTime }).HasDatabaseName("idx_return_status_time");
        });

        modelBuilder.Entity<Review>(e =>
        {
            e.HasIndex(r => r.OrderId).IsUnique().HasDatabaseName("uk_review_order");
            e.HasIndex(r => new { r.VehicleId, r.CreateTime }).HasDatabaseName("idx_review_vehicle_time");
        });

        modelBuilder.Entity<SupportTicket>(e =>
            e.HasIndex(t => new { t.Status, t.CreateTime }).HasDatabaseName("idx_ticket_status_time"));

        modelBuilder.Entity<Referral>(e =>
            e.HasIndex(r => r.InviteeId).IsUnique().HasDatabaseName("uk_referral_invitee"));

        modelBuilder.Entity<CustomerCreditProfile>(e =>
            e.HasIndex(p => p.CustomerId).IsUnique().HasDatabaseName("uk_credit_customer"));
        modelBuilder.Entity<CustomerCreditProfile>().HasIndex(p => p.ReferralCode);

        modelBuilder.Entity<Battery>(e =>
        {
            e.HasIndex(b => b.VehicleId).HasDatabaseName("idx_battery_vehicle");
            e.HasIndex(b => b.SerialNo).IsUnique().HasDatabaseName("uk_battery_serial");
        });

        modelBuilder.Entity<InsurancePolicy>(e =>
            e.HasIndex(i => new { i.VehicleId, i.EndDate }).HasDatabaseName("idx_insurance_vehicle_end"));

        modelBuilder.Entity<AccidentRecord>(e =>
            e.HasIndex(a => new { a.VehicleId, a.AccidentTime }).HasDatabaseName("idx_accident_vehicle_time"));

        modelBuilder.Entity<VehicleCost>(e =>
            e.HasIndex(c => new { c.VehicleId, c.OccurDate }).HasDatabaseName("idx_cost_vehicle_date"));

        modelBuilder.Entity<SparePart>(e =>
            e.HasIndex(p => p.PartName).HasDatabaseName("idx_part_name"));

        modelBuilder.Entity<PricingRule>(e =>
            e.HasIndex(r => new { r.Status, r.StartTime, r.EndTime }).HasDatabaseName("idx_pricing_status_time"));

        modelBuilder.Entity<GroupVoucher>(e =>
            e.HasIndex(v => v.Code).IsUnique().HasDatabaseName("uk_group_voucher_code"));

        modelBuilder.Entity<ApiKey>(e =>
            e.HasIndex(k => k.KeyHash).IsUnique().HasDatabaseName("uk_api_key_hash"));

        modelBuilder.Entity<GpsDevice>(e =>
        {
            e.HasIndex(g => g.VehicleId).IsUnique().HasDatabaseName("uk_gps_vehicle");
            e.HasIndex(g => g.Imei).IsUnique().HasDatabaseName("uk_gps_imei");
        });

        modelBuilder.Entity<CouponCampaign>(e =>
            e.HasIndex(c => new { c.Status, c.StartTime, c.EndTime }).HasDatabaseName("idx_campaign_status_time"));

        modelBuilder.Entity<NotificationOutbox>(e =>
            e.HasIndex(n => new { n.Status, n.CreateTime }).HasDatabaseName("idx_notify_status_time"));

        modelBuilder.Entity<DamageAssessment>(e =>
            e.HasIndex(d => new { d.OrderId, d.CreateTime }).HasDatabaseName("idx_damage_order_time"));

        //系统日志 — 操作人：日志只增不删，账号删除后置空 user_id，日志内容保留
        modelBuilder.Entity<SystemLog>(e =>
        {
            e.HasOne(l => l.Operator).WithMany()
                .HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
