using System.Security.Cryptography;
using System.Text;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

/// <summary>
/// 第二批运营扩展服务：预约、还车预登记、评价、客服、推荐、企业客户、门店、
/// 电池、保险/事故、车辆成本、备件、动态定价、团购、API Key、GPS、通知出站、
/// 车损评估、经营预测与员工绩效。所有表都通过 ID 关联，不改核心表结构。
/// </summary>
public class OpsExtService(AppDbContext db, MessageService messages)
{
    private readonly AppDbContext _db = db;
    private readonly MessageService _messages = messages;

    // ── 门店 ──────────────────────────────────────────────

    public async Task<List<StoreDto>> ListStoresAsync()
    {
        var stores = await _db.Stores.OrderByDescending(s => s.StoreId).ToListAsync();
        var counts = await _db.VehicleStores.GroupBy(v => v.StoreId)
            .Select(g => new { StoreId = g.Key, Count = g.Count() }).ToListAsync();
        var map = counts.ToDictionary(x => x.StoreId, x => x.Count);
        return stores.Select(s => new StoreDto
        {
            StoreId = s.StoreId, StoreName = s.StoreName, Address = s.Address, Phone = s.Phone,
            Status = s.Status, VehicleCount = map.GetValueOrDefault(s.StoreId), CreateTime = s.CreateTime,
        }).ToList();
    }

    public async Task<StoreDto> SaveStoreAsync(int? id, StoreInput input)
    {
        if (string.IsNullOrWhiteSpace(input.StoreName)) throw new BusinessException("门店名称不能为空");
        Store store;
        if (id is > 0)
        {
            store = await _db.Stores.FindAsync(id) ?? throw new BusinessException("门店不存在");
        }
        else
        {
            store = new Store();
            _db.Stores.Add(store);
        }
        store.StoreName = input.StoreName.Trim();
        store.Address = NullIfEmpty(input.Address);
        store.Phone = NullIfEmpty(input.Phone);
        store.Status = string.IsNullOrWhiteSpace(input.Status) ? "启用" : input.Status;
        await _db.SaveChangesAsync();
        return (await ListStoresAsync()).First(x => x.StoreId == store.StoreId);
    }

    public async Task BindVehicleStoreAsync(int vehicleId, int storeId)
    {
        _ = await _db.Vehicles.FindAsync(vehicleId) ?? throw new BusinessException("车辆不存在");
        _ = await _db.Stores.FindAsync(storeId) ?? throw new BusinessException("门店不存在");
        var link = await _db.VehicleStores.FirstOrDefaultAsync(v => v.VehicleId == vehicleId);
        if (link is null)
        {
            link = new VehicleStore { VehicleId = vehicleId, StoreId = storeId };
            _db.VehicleStores.Add(link);
        }
        else link.StoreId = storeId;
        await _db.SaveChangesAsync();
    }

    // ── 预约 ──────────────────────────────────────────────

    public async Task<List<AppointmentDto>> ListAppointmentsAsync(string? status)
    {
        var q = _db.Appointments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        var rows = await q.OrderByDescending(a => a.AppointTime).Take(200).ToListAsync();
        return await ProjectAppointments(rows);
    }

    public async Task<List<AppointmentDto>> MyAppointmentsAsync(int customerId)
        => await ProjectAppointments(await _db.Appointments.AsNoTracking()
            .Where(a => a.CustomerId == customerId).OrderByDescending(a => a.AppointTime).Take(50).ToListAsync());

    private async Task<List<AppointmentDto>> ProjectAppointments(List<Appointment> rows)
    {
        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToArray();
        var vehicleIds = rows.Select(r => r.VehicleId).Distinct().ToArray();
        var storeIds = rows.Where(r => r.StoreId.HasValue).Select(r => r.StoreId!.Value).Distinct().ToArray();
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId);
        var vehicles = await _db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .ToDictionaryAsync(v => v.VehicleId);
        var stores = await _db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.StoreId))
            .ToDictionaryAsync(s => s.StoreId);
        return rows.Select(a => new AppointmentDto
        {
            AppointmentId = a.AppointmentId, CustomerId = a.CustomerId,
            CustomerName = customers.GetValueOrDefault(a.CustomerId)?.FullName,
            ContactPhone = customers.GetValueOrDefault(a.CustomerId)?.ContactPhone,
            VehicleId = a.VehicleId, VehicleNo = vehicles.GetValueOrDefault(a.VehicleId)?.VehicleNo,
            Brand = vehicles.GetValueOrDefault(a.VehicleId)?.Brand,
            StoreId = a.StoreId, StoreName = a.StoreId is null ? null : stores.GetValueOrDefault(a.StoreId.Value)?.StoreName,
            AppointTime = a.AppointTime, Note = a.Note, Status = a.Status, CreateTime = a.CreateTime,
        }).ToList();
    }

    public async Task<AppointmentDto> CreateAppointmentAsync(int customerId, AppointmentInput input)
    {
        _ = await _db.Vehicles.FindAsync(input.VehicleId) ?? throw new BusinessException("车辆不存在");
        if (input.AppointTime < AppClock.Now.AddMinutes(-5)) throw new BusinessException("预约时间不能早于当前时间");
        // 同一辆车前后 2 小时内只允许一张有效预约，避免到店后无车可交（预约锁车）
        var from = input.AppointTime.AddHours(-2);
        var to = input.AppointTime.AddHours(2);
        var conflict = await _db.Appointments.AnyAsync(a => a.VehicleId == input.VehicleId
            && a.Status != "已取消" && a.AppointTime >= from && a.AppointTime <= to);
        if (conflict) throw new BusinessException("该车辆在预约时段前后 2 小时内已被锁定，请换个时间或车辆");
        var entity = new Appointment
        {
            CustomerId = customerId, VehicleId = input.VehicleId, StoreId = input.StoreId,
            AppointTime = input.AppointTime, ContactPhone = NullIfEmpty(input.ContactPhone), Note = NullIfEmpty(input.Note),
        };
        _db.Appointments.Add(entity);
        await _db.SaveChangesAsync();
        return (await ProjectAppointments([entity])).First();
    }

    public async Task<AppointmentDto> SetAppointmentStatusAsync(int id, string status)
    {
        var entity = await _db.Appointments.FindAsync(id) ?? throw new BusinessException("预约不存在");
        if (status is not ("待确认" or "已确认" or "已到店" or "已取消")) throw new BusinessException("预约状态不支持");
        entity.Status = status;
        await _db.SaveChangesAsync();
        return (await ProjectAppointments([entity])).First();
    }

    // ── 还车预登记 ──────────────────────────────────────────

    public async Task<List<ReturnRequestDto>> ListReturnRequestsAsync(string? status)
    {
        var q = _db.ReturnRequests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        return await ProjectReturnRequests(await q.OrderByDescending(r => r.CreateTime).Take(100).ToListAsync());
    }

    public async Task<List<ReturnRequestDto>> MyReturnRequestsAsync(int customerId)
        => await ProjectReturnRequests(await _db.ReturnRequests.AsNoTracking()
            .Where(r => r.CustomerId == customerId).OrderByDescending(r => r.CreateTime).Take(50).ToListAsync());

    private async Task<List<ReturnRequestDto>> ProjectReturnRequests(List<ReturnRequest> rows)
    {
        var orderIds = rows.Select(r => r.OrderId).Distinct().ToArray();
        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToArray();
        var orders = await _db.RentalOrders.AsNoTracking().Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId);
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId);
        var vehicleIds = orders.Values.Select(o => o.VehicleId).Distinct().ToArray();
        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => vehicleIds.Contains(v.VehicleId)).ToDictionaryAsync(v => v.VehicleId);
        return rows.Select(r =>
        {
            var order = orders.GetValueOrDefault(r.OrderId);
            return new ReturnRequestDto
            {
                RequestId = r.RequestId, OrderId = r.OrderId, OrderNo = order?.OrderNo,
                CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId)?.FullName,
                VehicleNo = order is null ? null : vehicles.GetValueOrDefault(order.VehicleId)?.VehicleNo,
                ExpectTime = r.ExpectTime, Mileage = r.Mileage, BatteryPercent = r.BatteryPercent,
                Note = r.Note, Status = r.Status, CreateTime = r.CreateTime, HandleTime = r.HandleTime,
            };
        }).ToList();
    }

    public async Task<ReturnRequestDto> CreateReturnRequestAsync(int customerId, ReturnRequestInput input)
    {
        var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == input.OrderId)
            ?? throw new BusinessException("订单不存在");
        if (order.CustomerId != customerId) throw new BusinessException("只能登记自己的订单");
        if (order.OrderStatus is not ("已支付" or "租赁中")) throw new BusinessException("当前订单状态不能登记还车");
        var existing = await _db.ReturnRequests.FirstOrDefaultAsync(r => r.OrderId == input.OrderId && r.Status == "待处理");
        if (existing is not null) throw new BusinessException("该订单已有待处理的还车登记");
        var entity = new ReturnRequest
        {
            OrderId = input.OrderId, CustomerId = customerId, ExpectTime = input.ExpectTime,
            Mileage = Math.Max(0, input.Mileage), BatteryPercent = Math.Clamp(input.BatteryPercent, 0, 100),
            Note = NullIfEmpty(input.Note),
        };
        _db.ReturnRequests.Add(entity);
        await _db.SaveChangesAsync();
        return (await ProjectReturnRequests([entity])).First();
    }

    public async Task<ReturnRequestDto> SetReturnRequestStatusAsync(int id, string status)
    {
        var entity = await _db.ReturnRequests.FindAsync(id) ?? throw new BusinessException("还车登记不存在");
        if (status is not ("待处理" or "已受理" or "已取消")) throw new BusinessException("还车登记状态不支持");
        entity.Status = status;
        if (status != "待处理") entity.HandleTime = AppClock.Now;
        await _db.SaveChangesAsync();
        return (await ProjectReturnRequests([entity])).First();
    }

    // ── 评价 ──────────────────────────────────────────────

    public async Task<List<ReviewDto>> ListReviewsAsync(int? vehicleId)
    {
        var q = _db.Reviews.AsNoTracking();
        if (vehicleId is > 0) q = q.Where(r => r.VehicleId == vehicleId);
        return await ProjectReviews(await q.OrderByDescending(r => r.CreateTime).Take(100).ToListAsync());
    }

    private async Task<List<ReviewDto>> ProjectReviews(List<Review> rows)
    {
        var orderIds = rows.Select(r => r.OrderId).Distinct().ToArray();
        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToArray();
        var vehicleIds = rows.Select(r => r.VehicleId).Distinct().ToArray();
        var orders = await _db.RentalOrders.AsNoTracking().Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId);
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId);
        var vehicles = await _db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .ToDictionaryAsync(v => v.VehicleId);
        return rows.Select(r => new ReviewDto
        {
            ReviewId = r.ReviewId, OrderId = r.OrderId, OrderNo = orders.GetValueOrDefault(r.OrderId)?.OrderNo,
            CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId)?.FullName,
            VehicleId = r.VehicleId, VehicleNo = vehicles.GetValueOrDefault(r.VehicleId)?.VehicleNo,
            Rating = r.Rating, Content = r.Content, Reply = r.Reply, ReplyTime = r.ReplyTime, CreateTime = r.CreateTime,
        }).ToList();
    }

    public async Task<ReviewDto> CreateReviewAsync(int customerId, ReviewInput input)
    {
        var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == input.OrderId)
            ?? throw new BusinessException("订单不存在");
        if (order.CustomerId != customerId) throw new BusinessException("只能评价自己的订单");
        if (order.OrderStatus != "已完结") throw new BusinessException("订单完结后才能评价");
        if (input.Rating is < 1 or > 5) throw new BusinessException("评分必须是 1~5 星");
        if (await _db.Reviews.AnyAsync(r => r.OrderId == input.OrderId)) throw new BusinessException("该订单已评价");
        var entity = new Review
        {
            OrderId = order.OrderId, CustomerId = customerId, VehicleId = order.VehicleId,
            Rating = input.Rating, Content = NullIfEmpty(input.Content),
        };
        _db.Reviews.Add(entity);
        await _db.SaveChangesAsync();
        return (await ProjectReviews([entity])).First();
    }

    public async Task<ReviewDto> ReplyReviewAsync(int id, string reply)
    {
        var entity = await _db.Reviews.FindAsync(id) ?? throw new BusinessException("评价不存在");
        if (string.IsNullOrWhiteSpace(reply)) throw new BusinessException("回复内容不能为空");
        entity.Reply = reply.Trim();
        entity.ReplyTime = AppClock.Now;
        await _db.SaveChangesAsync();
        return (await ProjectReviews([entity])).First();
    }

    // ── 客服工单 ──────────────────────────────────────────

    public async Task<List<SupportTicketDto>> ListTicketsAsync(string? status)
    {
        var q = _db.SupportTickets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        return await ProjectTickets(await q.OrderByDescending(t => t.CreateTime).Take(100).ToListAsync());
    }

    public async Task<List<SupportTicketDto>> MyTicketsAsync(int customerId)
        => await ProjectTickets(await _db.SupportTickets.AsNoTracking()
            .Where(t => t.CustomerId == customerId).OrderByDescending(t => t.CreateTime).Take(50).ToListAsync());

    private async Task<List<SupportTicketDto>> ProjectTickets(List<SupportTicket> rows)
    {
        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToArray();
        var orderIds = rows.Where(r => r.OrderId is > 0).Select(r => r.OrderId!.Value).Distinct().ToArray();
        var handlerIds = rows.Where(r => r.HandlerId is > 0).Select(r => r.HandlerId!.Value).Distinct().ToArray();
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId);
        var orders = await _db.RentalOrders.AsNoTracking().Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId);
        var users = await _db.SystemUsers.AsNoTracking().Where(u => handlerIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId);
        return rows.Select(t => new SupportTicketDto
        {
            TicketId = t.TicketId, CustomerId = t.CustomerId, CustomerName = customers.GetValueOrDefault(t.CustomerId)?.FullName,
            OrderId = t.OrderId, OrderNo = t.OrderId is null ? null : orders.GetValueOrDefault(t.OrderId.Value)?.OrderNo,
            Category = t.Category, Content = t.Content, Status = t.Status, HandlerId = t.HandlerId,
            HandlerName = t.HandlerId is null ? null : users.GetValueOrDefault(t.HandlerId.Value)?.UserName,
            Reply = t.Reply, CreateTime = t.CreateTime, CloseTime = t.CloseTime,
        }).ToList();
    }

    public async Task<SupportTicketDto> CreateTicketAsync(int customerId, TicketInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Content)) throw new BusinessException("请描述要咨询的问题");
        if (input.OrderId is > 0)
        {
            var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == input.OrderId);
            if (order is null || order.CustomerId != customerId) throw new BusinessException("关联订单不存在");
        }
        var entity = new SupportTicket
        {
            CustomerId = customerId, OrderId = input.OrderId, Category = NullIfEmpty(input.Category) ?? "其他",
            Content = input.Content.Trim(),
        };
        _db.SupportTickets.Add(entity);
        await _db.SaveChangesAsync();
        return (await ProjectTickets([entity])).First();
    }

    public async Task<SupportTicketDto> ReplyTicketAsync(int id, int handlerId, TicketReplyInput input)
    {
        var entity = await _db.SupportTickets.FindAsync(id) ?? throw new BusinessException("工单不存在");
        if (entity.Status == "已关闭") throw new BusinessException("工单已关闭");
        if (string.IsNullOrWhiteSpace(input.Reply)) throw new BusinessException("回复内容不能为空");
        var reply = input.Reply.Trim();
        entity.Reply = reply;
        entity.HandlerId = handlerId;
        entity.Status = input.Close ? "已关闭" : "已回复";
        if (input.Close) entity.CloseTime = AppClock.Now;
        await _db.SaveChangesAsync();

        //客服回复必须推到顾客的消息中心：否则顾客只能回到工单页反复刷新，
        //在手机上几乎等于「回复了但看不到」。
        var preview = reply.Length > 120 ? reply[..120] + "…" : reply;
        await _messages.SendAsync(
            ("customer", entity.CustomerId),
            ("ticket_reply", entity.TicketId.ToString()),
            ("客服回复：" + entity.Category, preview),
            ("info", "/profile?ticket=1", false));

        return (await ProjectTickets([entity])).First();
    }

    // ── 推荐 / 企业与实名 ──────────────────────────────────

    public async Task<CustomerCreditDto> GetCreditAsync(int customerId)
    {
        var profile = await _db.CustomerCreditProfiles.FirstOrDefaultAsync(p => p.CustomerId == customerId);
        if (profile is null)
        {
            // 顾客首次打开个人中心会同时请求 /credit 与 /referral，两路并发都会走到这里；
            // uk_credit_customer 保证只落一条，输掉竞争的一路改为读取赢家写好的档案。
            var candidate = new CustomerCreditProfile { CustomerId = customerId, ReferralCode = NewReferralCode() };
            _db.CustomerCreditProfiles.Add(candidate);
            try
            {
                await _db.SaveChangesAsync();
                profile = candidate;
            }
            catch (DbUpdateException)
            {
                _db.Entry(candidate).State = EntityState.Detached;
                profile = await _db.CustomerCreditProfiles.FirstOrDefaultAsync(p => p.CustomerId == customerId);
                if (profile is null) throw;
            }
        }
        return ToCreditDto(profile);
    }

    public async Task<CustomerCreditDto> SaveCreditAsync(int customerId, CustomerCreditInput input)
    {
        _ = await _db.Customers.FindAsync(customerId) ?? throw new BusinessException("客户不存在");
        var profile = await _db.CustomerCreditProfiles.FirstOrDefaultAsync(p => p.CustomerId == customerId);
        if (profile is null)
        {
            profile = new CustomerCreditProfile { CustomerId = customerId, ReferralCode = NewReferralCode() };
            _db.CustomerCreditProfiles.Add(profile);
        }
        profile.CompanyName = NullIfEmpty(input.CompanyName);
        profile.TaxNo = NullIfEmpty(input.TaxNo);
        profile.CreditLimit = Math.Max(0, input.CreditLimit);
        profile.RealNameStatus = input.RealNameStatus is "已认证" or "认证中" ? input.RealNameStatus : "未认证";
        profile.IdCardNo = NullIfEmpty(input.IdCardNo);
        profile.DepositWaiverLimit = Math.Max(0, input.DepositWaiverLimit);
        profile.UpdateTime = AppClock.Now;
        await _db.SaveChangesAsync();
        return ToCreditDto(profile);
    }

    public async Task<ReferralStatsDto> ReferralStatsAsync(int customerId)
    {
        var profile = await GetCreditAsync(customerId);
        var rows = await _db.Referrals.AsNoTracking().Where(r => r.ReferrerId == customerId)
            .OrderByDescending(r => r.CreateTime).ToListAsync();
        var inviteeIds = rows.Select(r => r.InviteeId).Distinct().ToArray();
        var names = await _db.Customers.AsNoTracking().Where(c => inviteeIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.FullName);
        return new ReferralStatsDto
        {
            ReferralCode = profile.ReferralCode ?? NewReferralCode(),
            InvitedCount = rows.Count,
            RewardTotal = rows.Count * 10m,
            Items = rows.Select(r => new ReferralDto
            {
                ReferralId = r.ReferralId, ReferrerId = r.ReferrerId, InviteeId = r.InviteeId,
                InviteeName = names.GetValueOrDefault(r.InviteeId), RewardCouponId = r.RewardCouponId,
                Status = r.Status, CreateTime = r.CreateTime,
            }).ToList(),
        };
    }

    /// <summary>注册时按推荐码绑定推荐人；已有账号不重复绑定。</summary>
    public async Task BindReferralAsync(int inviteeId, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var referrer = await _db.CustomerCreditProfiles.FirstOrDefaultAsync(p => p.ReferralCode == code.Trim().ToUpperInvariant());
        if (referrer is null || referrer.CustomerId == inviteeId) return;
        if (await _db.Referrals.AnyAsync(r => r.InviteeId == inviteeId)) return;
        var profile = await _db.CustomerCreditProfiles.FirstOrDefaultAsync(p => p.CustomerId == inviteeId);
        if (profile is null)
        {
            profile = new CustomerCreditProfile { CustomerId = inviteeId, ReferralCode = NewReferralCode() };
            _db.CustomerCreditProfiles.Add(profile);
        }
        profile.ReferrerId = referrer.CustomerId;
        _db.Referrals.Add(new Referral { ReferrerId = referrer.CustomerId, InviteeId = inviteeId });
        await _db.SaveChangesAsync();
    }

    // ── 电池 / 保险 / 事故 / 成本 / 备件 / GPS ──────────────

    public async Task<List<BatteryDto>> ListBatteriesAsync()
    {
        var rows = await _db.Batteries.AsNoTracking().OrderByDescending(b => b.BatteryId).ToListAsync();
        var vehicles = await _db.Vehicles.AsNoTracking().ToDictionaryAsync(v => v.VehicleId, v => v.VehicleNo);
        return rows.Select(b => new BatteryDto
        {
            BatteryId = b.BatteryId, VehicleId = b.VehicleId, VehicleNo = vehicles.GetValueOrDefault(b.VehicleId),
            SerialNo = b.SerialNo, HealthPercent = b.HealthPercent, CycleCount = b.CycleCount,
            Status = b.Status, LastCheckTime = b.LastCheckTime, LowHealth = b.HealthPercent < 80,
        }).ToList();
    }

    public async Task<BatteryDto> SaveBatteryAsync(int? id, BatteryInput input)
    {
        _ = await _db.Vehicles.FindAsync(input.VehicleId) ?? throw new BusinessException("车辆不存在");
        if (string.IsNullOrWhiteSpace(input.SerialNo)) throw new BusinessException("电池编号不能为空");
        Battery entity;
        if (id is > 0) entity = await _db.Batteries.FindAsync(id) ?? throw new BusinessException("电池档案不存在");
        else
        {
            entity = new Battery();
            _db.Batteries.Add(entity);
        }
        entity.VehicleId = input.VehicleId;
        entity.SerialNo = input.SerialNo.Trim();
        entity.HealthPercent = Math.Clamp(input.HealthPercent, 0, 100);
        entity.CycleCount = Math.Max(0, input.CycleCount);
        entity.Status = input.Status;
        entity.LastCheckTime = AppClock.Now;
        await _db.SaveChangesAsync();
        return (await ListBatteriesAsync()).First(b => b.BatteryId == entity.BatteryId);
    }

    public async Task<List<InsuranceDto>> ListInsuranceAsync()
    {
        var rows = await _db.InsurancePolicies.AsNoTracking().OrderBy(i => i.EndDate).ToListAsync();
        var vehicles = await _db.Vehicles.AsNoTracking().ToDictionaryAsync(v => v.VehicleId, v => v.VehicleNo);
        var now = AppClock.Now;
        return rows.Select(i =>
        {
            var days = (i.EndDate.Date - now.Date).Days;
            return new InsuranceDto
            {
                PolicyId = i.PolicyId, VehicleId = i.VehicleId, VehicleNo = vehicles.GetValueOrDefault(i.VehicleId),
                Insurer = i.Insurer, PolicyNo = i.PolicyNo, StartDate = i.StartDate, EndDate = i.EndDate,
                Premium = i.Premium, Status = i.Status, DaysLeft = days, Expiring = days <= 30,
            };
        }).ToList();
    }

    public async Task<InsuranceDto> SaveInsuranceAsync(int? id, InsuranceInput input)
    {
        _ = await _db.Vehicles.FindAsync(input.VehicleId) ?? throw new BusinessException("车辆不存在");
        if (input.EndDate <= input.StartDate) throw new BusinessException("保险结束日期必须晚于开始日期");
        InsurancePolicy entity;
        if (id is > 0) entity = await _db.InsurancePolicies.FindAsync(id) ?? throw new BusinessException("保单不存在");
        else
        {
            entity = new InsurancePolicy();
            _db.InsurancePolicies.Add(entity);
        }
        entity.VehicleId = input.VehicleId;
        entity.Insurer = input.Insurer.Trim();
        entity.PolicyNo = input.PolicyNo.Trim();
        entity.StartDate = input.StartDate;
        entity.EndDate = input.EndDate;
        entity.Premium = Math.Max(0, input.Premium);
        entity.Status = input.EndDate < AppClock.Now ? "已过期" : "有效";
        await _db.SaveChangesAsync();
        return (await ListInsuranceAsync()).First(i => i.PolicyId == entity.PolicyId);
    }

    public async Task<List<AccidentDto>> ListAccidentsAsync()
    {
        var rows = await _db.AccidentRecords.AsNoTracking().OrderByDescending(a => a.AccidentTime).Take(100).ToListAsync();
        var vehicles = await _db.Vehicles.AsNoTracking().ToDictionaryAsync(v => v.VehicleId, v => v.VehicleNo);
        var orderIds = rows.Where(r => r.OrderId is > 0).Select(r => r.OrderId!.Value).Distinct().ToArray();
        var customerIds = rows.Where(r => r.CustomerId is > 0).Select(r => r.CustomerId!.Value).Distinct().ToArray();
        var orders = await _db.RentalOrders.AsNoTracking().Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId);
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.FullName);
        return rows.Select(a => new AccidentDto
        {
            AccidentId = a.AccidentId, VehicleId = a.VehicleId, VehicleNo = vehicles.GetValueOrDefault(a.VehicleId),
            OrderId = a.OrderId, OrderNo = a.OrderId is null ? null : orders.GetValueOrDefault(a.OrderId.Value)?.OrderNo,
            CustomerId = a.CustomerId, CustomerName = a.CustomerId is null ? null : customers.GetValueOrDefault(a.CustomerId.Value),
            AccidentTime = a.AccidentTime, Description = a.Description, LossAmount = a.LossAmount,
            ClaimAmount = a.ClaimAmount, Status = a.Status, CreateTime = a.CreateTime,
        }).ToList();
    }

    public async Task<AccidentDto> CreateAccidentAsync(AccidentInput input)
    {
        _ = await _db.Vehicles.FindAsync(input.VehicleId) ?? throw new BusinessException("车辆不存在");
        if (string.IsNullOrWhiteSpace(input.Description)) throw new BusinessException("请填写事故经过");
        int? customerId = null;
        if (input.OrderId is > 0)
        {
            var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == input.OrderId);
            if (order is null) throw new BusinessException("关联订单不存在");
            customerId = order.CustomerId;
        }
        var entity = new AccidentRecord
        {
            VehicleId = input.VehicleId, OrderId = input.OrderId, CustomerId = customerId,
            AccidentTime = input.AccidentTime, Description = input.Description.Trim(),
            LossAmount = Math.Max(0, input.LossAmount), ClaimAmount = Math.Max(0, input.ClaimAmount),
            Status = input.Status,
        };
        _db.AccidentRecords.Add(entity);
        await _db.SaveChangesAsync();
        return (await ListAccidentsAsync()).First(a => a.AccidentId == entity.AccidentId);
    }

    public async Task<List<VehicleCostDto>> ListVehicleCostsAsync(int? vehicleId)
    {
        var q = _db.VehicleCosts.AsNoTracking();
        if (vehicleId is > 0) q = q.Where(c => c.VehicleId == vehicleId);
        var rows = await q.OrderByDescending(c => c.OccurDate).Take(200).ToListAsync();
        var vehicleIds = rows.Select(r => r.VehicleId).Distinct().ToArray();
        var vehicles = await _db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .ToDictionaryAsync(v => v.VehicleId, v => v.VehicleNo);
        return rows.Select(c => new VehicleCostDto
        {
            CostId = c.CostId, VehicleId = c.VehicleId, VehicleNo = vehicles.GetValueOrDefault(c.VehicleId),
            CostType = c.CostType, Amount = c.Amount, OccurDate = c.OccurDate, Note = c.Note,
        }).ToList();
    }

    public async Task<VehicleCostDto> CreateVehicleCostAsync(VehicleCostInput input)
    {
        _ = await _db.Vehicles.FindAsync(input.VehicleId) ?? throw new BusinessException("车辆不存在");
        if (input.Amount <= 0) throw new BusinessException("成本金额必须大于 0");
        var entity = new VehicleCost
        {
            VehicleId = input.VehicleId, CostType = string.IsNullOrWhiteSpace(input.CostType) ? "其他" : input.CostType,
            Amount = input.Amount, OccurDate = input.OccurDate, Note = NullIfEmpty(input.Note),
        };
        _db.VehicleCosts.Add(entity);
        await _db.SaveChangesAsync();
        return (await ListVehicleCostsAsync(input.VehicleId)).First(c => c.CostId == entity.CostId);
    }

    public async Task<List<SparePartDto>> ListPartsAsync()
    {
        var rows = await _db.SpareParts.AsNoTracking().OrderBy(p => p.PartName).ToListAsync();
        return rows.Select(p => new SparePartDto
        {
            PartId = p.PartId, PartName = p.PartName, Sku = p.Sku, StockQty = p.StockQty,
            LowThreshold = p.LowThreshold, Unit = p.Unit, Price = p.Price, LowStock = p.StockQty <= p.LowThreshold,
        }).ToList();
    }

    public async Task<SparePartDto> SavePartAsync(int? id, SparePartInput input)
    {
        if (string.IsNullOrWhiteSpace(input.PartName)) throw new BusinessException("备件名称不能为空");
        SparePart entity;
        if (id is > 0) entity = await _db.SpareParts.FindAsync(id) ?? throw new BusinessException("备件不存在");
        else
        {
            entity = new SparePart();
            _db.SpareParts.Add(entity);
        }
        entity.PartName = input.PartName.Trim();
        entity.Sku = NullIfEmpty(input.Sku);
        entity.StockQty = Math.Max(0, input.StockQty);
        entity.LowThreshold = Math.Max(0, input.LowThreshold);
        entity.Unit = string.IsNullOrWhiteSpace(input.Unit) ? "个" : input.Unit;
        entity.Price = Math.Max(0, input.Price);
        await _db.SaveChangesAsync();
        return (await ListPartsAsync()).First(p => p.PartId == entity.PartId);
    }

    public async Task<List<GpsDeviceDto>> ListGpsAsync()
    {
        var rows = await _db.GpsDevices.AsNoTracking().OrderByDescending(g => g.DeviceId).ToListAsync();
        var vehicles = await _db.Vehicles.AsNoTracking().ToDictionaryAsync(v => v.VehicleId, v => v.VehicleNo);
        var now = AppClock.Now;
        return rows.Select(g => new GpsDeviceDto
        {
            DeviceId = g.DeviceId, VehicleId = g.VehicleId, VehicleNo = vehicles.GetValueOrDefault(g.VehicleId),
            Imei = g.Imei, SimNo = g.SimNo,
            Status = g.LastReport.HasValue && (now - g.LastReport.Value).TotalMinutes <= 10 ? "在线" : g.Status,
            LastLat = g.LastLat, LastLng = g.LastLng, LastReport = g.LastReport,
        }).ToList();
    }

    public async Task<GpsDeviceDto> SaveGpsAsync(int? id, GpsDeviceInput input)
    {
        _ = await _db.Vehicles.FindAsync(input.VehicleId) ?? throw new BusinessException("车辆不存在");
        if (string.IsNullOrWhiteSpace(input.Imei)) throw new BusinessException("IMEI 不能为空");
        GpsDevice entity;
        if (id is > 0) entity = await _db.GpsDevices.FindAsync(id) ?? throw new BusinessException("设备不存在");
        else
        {
            entity = new GpsDevice();
            _db.GpsDevices.Add(entity);
        }
        entity.VehicleId = input.VehicleId;
        entity.Imei = input.Imei.Trim();
        entity.SimNo = NullIfEmpty(input.SimNo);
        entity.Status = input.Status;
        await _db.SaveChangesAsync();
        return (await ListGpsAsync()).First(g => g.DeviceId == entity.DeviceId);
    }

    /// <summary>设备上报：按 IMEI 反查车辆，更新最后位置；未登记设备直接拒绝。</summary>
    public async Task<GpsDeviceDto> ReportGpsAsync(GpsReportInput input)
    {
        var entity = await _db.GpsDevices.FirstOrDefaultAsync(g => g.Imei == input.Imei)
            ?? throw new BusinessException("设备未登记");
        entity.LastLat = input.Lat;
        entity.LastLng = input.Lng;
        entity.LastReport = AppClock.Now;
        entity.Status = "在线";
        await _db.SaveChangesAsync();
        return (await ListGpsAsync()).First(g => g.DeviceId == entity.DeviceId);
    }

    // ── 动态定价 / 券活动 / 团购 ────────────────────────────

    public async Task<List<PricingRuleDto>> ListPricingRulesAsync()
    {
        var rows = await _db.PricingRules.AsNoTracking().OrderByDescending(r => r.RuleId).ToListAsync();
        var now = AppClock.Now;
        return rows.Select(r => new PricingRuleDto
        {
            RuleId = r.RuleId, RuleName = r.RuleName, RuleType = r.RuleType, StartTime = r.StartTime,
            EndTime = r.EndTime, Factor = r.Factor, Status = r.Status, Remark = r.Remark,
            ActiveNow = r.Status == "启用" && (r.StartTime is null || r.StartTime <= now) && (r.EndTime is null || r.EndTime >= now),
        }).ToList();
    }

    public async Task<PricingRuleDto> SavePricingRuleAsync(int? id, PricingRuleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.RuleName)) throw new BusinessException("规则名称不能为空");
        if (input.Factor is < 0.5m or > 3.0m) throw new BusinessException("价格系数应在 0.5~3.0 之间");
        PricingRule entity;
        if (id is > 0) entity = await _db.PricingRules.FindAsync(id) ?? throw new BusinessException("定价规则不存在");
        else
        {
            entity = new PricingRule();
            _db.PricingRules.Add(entity);
        }
        entity.RuleName = input.RuleName.Trim();
        entity.RuleType = input.RuleType;
        entity.StartTime = input.StartTime;
        entity.EndTime = input.EndTime;
        entity.Factor = input.Factor;
        entity.Status = input.Status;
        entity.Remark = NullIfEmpty(input.Remark);
        await _db.SaveChangesAsync();
        return (await ListPricingRulesAsync()).First(r => r.RuleId == entity.RuleId);
    }

    public async Task SetPricingRuleStatusAsync(int id, string status)
    {
        var entity = await _db.PricingRules.FindAsync(id) ?? throw new BusinessException("定价规则不存在");
        entity.Status = status == "启用" ? "启用" : "停用";
        await _db.SaveChangesAsync();
    }

    /// <summary>当前生效的最高系数：下单页可用于显示“当前溢价”。</summary>
    public async Task<decimal> CurrentPriceFactorAsync()
    {
        var now = AppClock.Now;
        var factors = await _db.PricingRules.Where(r => r.Status == "启用"
                && (r.StartTime == null || r.StartTime <= now) && (r.EndTime == null || r.EndTime >= now))
            .Select(r => r.Factor).ToListAsync();
        return factors.Count == 0 ? 1.00m : factors.Max();
    }

    /// <summary>免押额度：实名客户在额度内可免交押金；未建档返回 0。</summary>
    public async Task<decimal> DepositWaiverAsync(int customerId)
    {
        var profile = await _db.CustomerCreditProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CustomerId == customerId);
        return profile?.DepositWaiverLimit ?? 0m;
    }

    public async Task<List<CouponCampaignDto>> ListCampaignsAsync()
    {
        var rows = await _db.CouponCampaigns.AsNoTracking().OrderByDescending(c => c.CampaignId).ToListAsync();
        var couponIds = rows.Select(r => r.CouponId).Distinct().ToArray();
        var coupons = await _db.Coupons.AsNoTracking().Where(c => couponIds.Contains(c.CouponId))
            .ToDictionaryAsync(c => c.CouponId, c => c.Name);
        return rows.Select(c => new CouponCampaignDto
        {
            CampaignId = c.CampaignId, CouponId = c.CouponId, CouponName = coupons.GetValueOrDefault(c.CouponId),
            CampaignName = c.CampaignName, StartTime = c.StartTime, EndTime = c.EndTime,
            TotalQuota = c.TotalQuota, IssuedQty = c.IssuedQty, Status = c.Status,
        }).ToList();
    }

    public async Task<CouponCampaignDto> SaveCampaignAsync(int? id, CouponCampaignInput input)
    {
        _ = await _db.Coupons.FindAsync(input.CouponId) ?? throw new BusinessException("券模板不存在");
        if (input.EndTime <= input.StartTime) throw new BusinessException("活动结束时间必须晚于开始时间");
        CouponCampaign entity;
        if (id is > 0) entity = await _db.CouponCampaigns.FindAsync(id) ?? throw new BusinessException("活动不存在");
        else
        {
            entity = new CouponCampaign();
            _db.CouponCampaigns.Add(entity);
        }
        entity.CouponId = input.CouponId;
        entity.CampaignName = input.CampaignName.Trim();
        entity.StartTime = input.StartTime;
        entity.EndTime = input.EndTime;
        entity.TotalQuota = Math.Max(0, input.TotalQuota);
        entity.Status = input.EndTime < AppClock.Now ? "已结束" : "启用";
        await _db.SaveChangesAsync();
        return (await ListCampaignsAsync()).First(c => c.CampaignId == entity.CampaignId);
    }

    public async Task<List<GroupVoucherDto>> ListGroupVouchersAsync()
    {
        var rows = await _db.GroupVouchers.AsNoTracking().OrderByDescending(v => v.VoucherId).Take(200).ToListAsync();
        var now = AppClock.Now;
        return rows.Select(v => new GroupVoucherDto
        {
            VoucherId = v.VoucherId, Code = v.Code, Title = v.Title, Amount = v.Amount,
            ValidFrom = v.ValidFrom, ValidTo = v.ValidTo, Status = v.Status, UsedOrderId = v.UsedOrderId,
            UsedTime = v.UsedTime, Expired = v.ValidTo < now && v.Status == "未使用",
        }).ToList();
    }

    public async Task<GroupVoucherDto> CreateGroupVoucherAsync(GroupVoucherInput input)
    {
        if (input.ValidTo <= input.ValidFrom) throw new BusinessException("有效期结束必须晚于开始");
        if (await _db.GroupVouchers.AnyAsync(v => v.Code == input.Code.Trim()))
            throw new BusinessException("团购码已存在");
        var entity = new GroupVoucher
        {
            Code = input.Code.Trim().ToUpperInvariant(), Title = input.Title.Trim(), Amount = input.Amount,
            ValidFrom = input.ValidFrom, ValidTo = input.ValidTo,
        };
        _db.GroupVouchers.Add(entity);
        await _db.SaveChangesAsync();
        return (await ListGroupVouchersAsync()).First(v => v.VoucherId == entity.VoucherId);
    }

    public async Task<GroupVoucherDto> RedeemGroupVoucherAsync(string code, int? orderId, int operatorId)
    {
        var entity = await _db.GroupVouchers.FirstOrDefaultAsync(v => v.Code == code.Trim().ToUpperInvariant())
            ?? throw new BusinessException("团购码不存在");
        if (entity.Status == "已核销") throw new BusinessException("团购码已核销");
        if (entity.ValidTo < AppClock.Now) throw new BusinessException("团购码已过期");
        if (entity.ValidFrom > AppClock.Now) throw new BusinessException("团购码未到可用时间");
        if (orderId is > 0 && !await _db.RentalOrders.AnyAsync(o => o.OrderId == orderId))
            throw new BusinessException("关联订单不存在");
        entity.Status = "已核销";
        entity.UsedOrderId = orderId;
        entity.UsedTime = AppClock.Now;
        await _db.SaveChangesAsync();
        return (await ListGroupVouchersAsync()).First(v => v.VoucherId == entity.VoucherId);
    }

    // ── API Key / 通知出站 / 车损 / 预测 / 绩效 ──────────────

    public async Task<List<ApiKeyDto>> ListApiKeysAsync()
        => (await _db.ApiKeys.AsNoTracking().OrderByDescending(k => k.KeyId).ToListAsync()).Select(k => new ApiKeyDto
        {
            KeyId = k.KeyId, Name = k.Name, Prefix = k.Prefix, Status = k.Status,
            LastUsed = k.LastUsed, CreateTime = k.CreateTime,
        }).ToList();

    public async Task<ApiKeyCreatedDto> CreateApiKeyAsync(ApiKeyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new BusinessException("Key 名称不能为空");
        var raw = "ers_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var entity = new ApiKey
        {
            Name = input.Name.Trim(), KeyHash = Sha256(raw), Prefix = raw[..12],
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();
        return new ApiKeyCreatedDto { KeyId = entity.KeyId, Name = entity.Name, Key = raw };
    }

    public async Task RevokeApiKeyAsync(int id)
    {
        var entity = await _db.ApiKeys.FindAsync(id) ?? throw new BusinessException("Key 不存在");
        entity.Status = "已吊销";
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ValidateApiKeyAsync(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var hash = Sha256(raw);
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.Status == "启用");
        if (key is null) return false;
        key.LastUsed = AppClock.Now;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<NotificationOutboxDto>> ListNotificationsAsync(string? status)
    {
        var q = _db.NotificationOutbox.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(n => n.Status == status);
        var rows = await q.OrderByDescending(n => n.NotifyId).Take(200).ToListAsync();
        var customerIds = rows.Where(r => r.ReceiverType == "customer").Select(r => r.ReceiverId).Distinct().ToArray();
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.FullName);
        return rows.Select(n => new NotificationOutboxDto
        {
            NotifyId = n.NotifyId, Channel = n.Channel, ReceiverType = n.ReceiverType, ReceiverId = n.ReceiverId,
            ReceiverName = n.ReceiverType == "customer" ? customers.GetValueOrDefault(n.ReceiverId) : null,
            Template = n.Template, Content = n.Content, Status = n.Status, RetryCount = n.RetryCount,
            CreateTime = n.CreateTime, SendTime = n.SendTime,
        }).ToList();
    }

    /// <summary>业务事件写通知出站队列；外部通道未配置时保持「待发送」。</summary>
    public async Task EnqueueNotificationAsync(string channel, string receiverType, int receiverId, string template, string content)
    {
        _db.NotificationOutbox.Add(new NotificationOutbox
        {
            Channel = channel, ReceiverType = receiverType, ReceiverId = receiverId, Template = template, Content = content,
        });
        await _db.SaveChangesAsync();
    }

    public async Task<NotificationOutboxDto> MarkNotificationSentAsync(int id)
    {
        var entity = await _db.NotificationOutbox.FindAsync(id) ?? throw new BusinessException("通知不存在");
        entity.Status = "已发送";
        entity.SendTime = AppClock.Now;
        await _db.SaveChangesAsync();
        return (await ListNotificationsAsync(null)).First(n => n.NotifyId == id);
    }

    public async Task<List<DamageAssessmentDto>> ListDamageAssessmentsAsync(int? orderId)
    {
        var q = _db.DamageAssessments.AsNoTracking();
        if (orderId is > 0) q = q.Where(d => d.OrderId == orderId);
        var rows = await q.OrderByDescending(d => d.AssessmentId).Take(100).ToListAsync();
        var orderIds = rows.Select(r => r.OrderId).Distinct().ToArray();
        var vehicleIds = rows.Select(r => r.VehicleId).Distinct().ToArray();
        var orders = await _db.RentalOrders.AsNoTracking().Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId);
        var vehicles = await _db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .ToDictionaryAsync(v => v.VehicleId, v => v.VehicleNo);
        return rows.Select(d => new DamageAssessmentDto
        {
            AssessmentId = d.AssessmentId, OrderId = d.OrderId, OrderNo = orders.GetValueOrDefault(d.OrderId)?.OrderNo,
            VehicleId = d.VehicleId, VehicleNo = vehicles.GetValueOrDefault(d.VehicleId), Severity = d.Severity,
            EstimatedCost = d.EstimatedCost, Findings = d.Findings, Status = d.Status, CreateTime = d.CreateTime,
        }).ToList();
    }

    /// <summary>车损评估：按严重度给出可复算估价；后续接视觉模型时只替换这一处。</summary>
    public async Task<DamageAssessmentDto> CreateDamageAssessmentAsync(DamageInput input, int operatorId)
    {
        var order = await _db.RentalOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == input.OrderId)
            ?? throw new BusinessException("订单不存在");
        if (input.Severity is < 1 or > 5) throw new BusinessException("车损严重度必须是 1~5");
        var cost = input.EstimatedCost ?? input.Severity * 200m;
        var findings = NullIfEmpty(input.Findings) ?? $"系统初判：{input.Severity} 级车损，预计维修 ¥{cost:F2}，请人工复核照片。";
        var entity = new DamageAssessment
        {
            OrderId = order.OrderId, VehicleId = order.VehicleId, Severity = input.Severity,
            EstimatedCost = Math.Max(0, cost), Findings = findings,
        };
        _db.DamageAssessments.Add(entity);
        await _db.SaveChangesAsync();
        return (await ListDamageAssessmentsAsync(order.OrderId)).First(d => d.AssessmentId == entity.AssessmentId);
    }

    public async Task<ForecastDto> ForecastAsync(int days = 7)
    {
        days = Math.Clamp(days, 3, 30);
        var from = AppClock.Now.Date.AddDays(-30);
        var orders = await _db.RentalOrders.AsNoTracking().Where(o => o.CreateTime >= from).ToListAsync();
        var history = Enumerable.Range(0, 30).Select(i =>
        {
            var date = from.AddDays(i);
            var dayOrders = orders.Where(o => o.CreateTime.Date == date).ToList();
            return new ForecastPointDto
            {
                Date = date.ToString("MM-dd"), Orders = dayOrders.Count,
                Revenue = dayOrders.Sum(o => o.TotalRent),
            };
        }).ToList();
        var avgOrders = history.Count == 0 ? 0 : history.Average(h => h.Orders);
        var recent = history.TakeLast(7).Average(h => h.Orders);
        var previous = history.Take(23).DefaultIfEmpty(new ForecastPointDto()).Average(h => h.Orders);
        var trend = recent > previous * 1.15 ? "上升" : recent < previous * 0.85 ? "下降" : "平稳";
        var avgTicket = history.Count == 0 ? 0m : history.Average(h => h.Orders == 0 ? 0 : h.Revenue / h.Orders);
        var forecast = Enumerable.Range(1, days).Select(i =>
        {
            var date = AppClock.Now.Date.AddDays(i);
            var expected = Math.Max(0, Math.Round(recent, 1));
            return new ForecastPointDto
            {
                Date = date.ToString("MM-dd"), Orders = (int)Math.Round(expected),
                Revenue = Math.Round((decimal)expected * avgTicket, 2),
            };
        }).ToList();
        return new ForecastDto { Days = days, AvgOrders = Math.Round((decimal)avgOrders, 1), Trend = trend, History = history, Forecast = forecast };
    }

    public async Task<List<StaffPerformanceDto>> PerformanceAsync(DateTime? from, DateTime? to)
    {
        var start = (from ?? AppClock.Now.Date.AddDays(-30));
        var end = (to ?? AppClock.Now.Date.AddDays(1));
        var orders = await _db.RentalOrders.AsNoTracking()
            .Where(o => o.CreateTime >= start && o.CreateTime < end && o.OperatorId != null).ToListAsync();
        var settles = await _db.SettleRecords.AsNoTracking()
            .Where(s => s.SettleTime >= start && s.SettleTime < end && s.OperatorId != null).ToListAsync();
        var users = await _db.SystemUsers.AsNoTracking().ToListAsync();
        var roles = await _db.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleId, r => r.RoleName);
        return users.Select(u => new StaffPerformanceDto
        {
            UserId = u.UserId, UserName = u.UserName ?? u.LoginAccount, RoleName = roles.GetValueOrDefault(u.RoleId) ?? "",
            OrderCount = orders.Count(o => o.OperatorId == u.UserId),
            SettleCount = settles.Count(s => s.OperatorId == u.UserId),
            Revenue = orders.Where(o => o.OperatorId == u.UserId).Sum(o => o.TotalRent),
            RefundAmount = settles.Where(s => s.OperatorId == u.UserId).Sum(s => s.RefundAmount),
        }).OrderByDescending(p => p.Revenue).ToList();
    }

    // ── 工具 ──────────────────────────────────────────────

    private static CustomerCreditDto ToCreditDto(CustomerCreditProfile p) => new()
    {
        CustomerId = p.CustomerId, CompanyName = p.CompanyName, TaxNo = p.TaxNo, CreditLimit = p.CreditLimit,
        RealNameStatus = p.RealNameStatus, IdCardNo = MaskId(p.IdCardNo), DepositWaiverLimit = p.DepositWaiverLimit,
        ReferralCode = p.ReferralCode, UpdateTime = p.UpdateTime,
        IsCorporate = !string.IsNullOrWhiteSpace(p.CompanyName),
    };

    private string NewReferralCode()
    {
        for (var i = 0; i < 8; i++)
        {
            var code = "REF" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            if (!_db.CustomerCreditProfiles.Any(p => p.ReferralCode == code)) return code;
        }
        return "REF" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private static string? MaskId(string? id)
        => string.IsNullOrWhiteSpace(id) || id.Length < 8 ? id : id[..4] + "**********" + id[^4..];

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Sha256(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
