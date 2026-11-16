using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ERental.Services;

///<summary>
/// 优惠券：管理员发券、顾客用券、订单取消时退券。
/// 券模板与顾客持券分表——模板改价不影响已发出去的券，已领的券在有效期内继续可用。
///</summary>
public class CouponService(AppDbContext db, ILogger<CouponService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<CouponService> _logger = logger;

    private static readonly System.Linq.Expressions.Expression<Func<Coupon, CouponDto>> Projection = c => new CouponDto
    {
        CouponId = c.CouponId,
        Name = c.Name,
        Amount = c.Amount,
        MinAmount = c.MinAmount,
        ValidFrom = c.ValidFrom,
        ValidTo = c.ValidTo,
        TotalQty = c.TotalQty,
        IssuedQty = c.IssuedQty,
        PerLimit = c.PerLimit,
        Status = c.Status,
        CreateTime = c.CreateTime,
        RedeemCode = c.RedeemCode,
        RedeemOpen = c.RedeemOpen,
    };

    public Task<List<CouponDto>> GetAllAsync() =>
        _db.Coupons.AsNoTracking().OrderByDescending(c => c.CouponId).Take(Paging.MaxUnpagedRows).Select(Projection).ToListAsync();

    public async Task<CouponDto> CreateAsync(CouponInput input)
    {
        var coupon = Coupon.Create(input.Name, input.Amount, input.MinAmount, input.ValidFrom, input.ValidTo,
            input.TotalQty, input.PerLimit, input.RedeemCode, input.RedeemOpen);
        if (coupon.RedeemCode != null && await _db.Coupons.AnyAsync(c => c.RedeemCode == coupon.RedeemCode))
            throw new BusinessException($"兑换码 {coupon.RedeemCode} 已被其他券占用，请换一个");
        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync();
        return await _db.Coupons.AsNoTracking().Where(c => c.CouponId == coupon.CouponId).Select(Projection).FirstAsync();
    }

    public async Task<CouponDto> UpdateAsync(int couponId, CouponInput input)
    {
        var coupon = await _db.Coupons.FindAsync(couponId) ?? throw new BusinessException("优惠券不存在");
        coupon.Update(input.Name, input.Amount, input.MinAmount, input.ValidFrom, input.ValidTo,
            input.TotalQty, input.PerLimit, input.RedeemCode, input.RedeemOpen);
        if (coupon.RedeemCode != null && await _db.Coupons.AnyAsync(c => c.RedeemCode == coupon.RedeemCode && c.CouponId != couponId))
            throw new BusinessException($"兑换码 {coupon.RedeemCode} 已被其他券占用，请换一个");
        await _db.SaveChangesAsync();
        return await _db.Coupons.AsNoTracking().Where(c => c.CouponId == couponId).Select(Projection).FirstAsync();
    }

    public async Task SetStatusAsync(int couponId, string status)
    {
        var coupon = await _db.Coupons.FindAsync(couponId) ?? throw new BusinessException("优惠券不存在");
        coupon.SetStatus(status);
        await _db.SaveChangesAsync();
    }

    ///<summary>发券：不指定顾客则发给全部正常账号；库存与每人限领在同一事务内校验</summary>
    public async Task<int> GrantAsync(GrantCouponInput input)
    {
        var coupon = await _db.Coupons.FindAsync(input.CouponId) ?? throw new BusinessException("优惠券不存在");
        if (coupon.Status != "启用") throw new BusinessException("券已停用，不能继续发放");

        var customerIds = input.CustomerIds.Count > 0
            ? await _db.Customers.Where(c => input.CustomerIds.Contains(c.CustomerId) && c.AccountStatus == "正常").Select(c => c.CustomerId).ToListAsync()
            : await _db.Customers.Where(c => c.AccountStatus == "正常").Select(c => c.CustomerId).Take(Paging.MaxUnpagedRows).ToListAsync();
        if (customerIds.Count == 0) throw new BusinessException("没有可发放的顾客");

        //已领数量按顾客统计，超过每人限领的跳过
        var held = await _db.CustomerCoupons.AsNoTracking()
            .Where(c => c.CouponId == coupon.CouponId && customerIds.Contains(c.CustomerId))
            .GroupBy(c => c.CustomerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);

        var targets = customerIds.Where(id => held.GetValueOrDefault(id) < coupon.PerLimit).ToList();
        if (targets.Count == 0) throw new BusinessException("所选顾客均已达到领取上限");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            //库存兜底：不限量（total_qty=0）或剩余量足够才放行，避免并发发券超发
            if (coupon.TotalQty > 0)
            {
                var affected = await _db.Coupons
                    .Where(c => c.CouponId == coupon.CouponId && c.IssuedQty + targets.Count <= c.TotalQty)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IssuedQty, c => c.IssuedQty + targets.Count));
                if (affected == 0) throw new BusinessException("券库存不足，请减少发放数量");
            }
            else
            {
                await _db.Coupons.Where(c => c.CouponId == coupon.CouponId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IssuedQty, c => c.IssuedQty + targets.Count));
            }

            //每张发出的券都有唯一码值：顾客端凭码出二维码，前台扫码即可定位到人
            var codes = await GenerateUniqueCodesAsync(targets.Count);
            for (var i = 0; i < targets.Count; i++)
                _db.CustomerCoupons.Add(CustomerCoupon.Issue(coupon.CouponId, targets[i], coupon.ValidTo, codes[i]));

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            _logger.LogInformation("券 {CouponId} 发放 {Count} 张", coupon.CouponId, targets.Count);
            return targets.Count;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    ///<summary>顾客端「我的券」：读取前顺带把过期券标记掉，不依赖定时任务</summary>
    public async Task<List<MyCouponDto>> MineAsync(int customerId)
    {
        var now = AppClock.Now;
        await _db.CustomerCoupons
            .Where(c => c.CustomerId == customerId && c.Status == CustomerCoupon.StatusUnused && c.ExpireTime < now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CustomerCoupon.StatusExpired));

        //历史数据兜底：早期发出的券没有码值，读取时补上，保证每张券都能出二维码
        await EnsureCodesAsync(customerId);

        return await _db.CustomerCoupons.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .OrderBy(c => c.Status).ThenBy(c => c.ExpireTime)
            .Take(Paging.MaxUnpagedRows)
            .Select(c => new MyCouponDto
            {
                Id = c.Id,
                CouponId = c.CouponId,
                Name = c.Coupon!.Name,
                Amount = c.Coupon!.Amount,
                MinAmount = c.Coupon!.MinAmount,
                ExpireTime = c.ExpireTime,
                Status = c.Status,
                ReceiveTime = c.ReceiveTime,
                OrderId = c.OrderId,
                Code = c.Code,
            }).ToListAsync();
    }

    ///<summary>顾客输入兑换码自助领券：码必须由管理员开放，库存与每人限领仍按模板约束</summary>
    public async Task<MyCouponDto> RedeemAsync(int customerId, string rawCode)
    {
        var code = (rawCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0) throw new BusinessException("请输入兑换码");

        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.RedeemCode == code && c.RedeemOpen)
            ?? throw new BusinessException("兑换码无效，或该券未开放领取");
        if (coupon.Status != "启用") throw new BusinessException("该券已停用，暂不能领取");

        var now = AppClock.Now;
        if (now < coupon.ValidFrom) throw new BusinessException($"该券 {coupon.ValidFrom:yyyy-MM-dd HH:mm} 才生效，请稍后再领");
        if (now > coupon.ValidTo) throw new BusinessException("该券已过期，无法领取");

        var held = await _db.CustomerCoupons.CountAsync(c => c.CouponId == coupon.CouponId && c.CustomerId == customerId);
        if (held >= coupon.PerLimit) throw new BusinessException($"该券每人限领 {coupon.PerLimit} 张，你已领满");

        var customer = await _db.Customers.FindAsync(customerId) ?? throw new BusinessException("账号不存在");
        if (customer.AccountStatus != "正常") throw new BusinessException("账号状态异常，暂不能领券");

        var newCode = (await GenerateUniqueCodesAsync(1))[0];

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            //库存用条件更新抢：并发兑换时超出总量的那一笔直接失败，不会超发
            if (coupon.TotalQty > 0)
            {
                var affected = await _db.Coupons
                    .Where(c => c.CouponId == coupon.CouponId && c.IssuedQty < c.TotalQty)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IssuedQty, c => c.IssuedQty + 1));
                if (affected == 0) throw new BusinessException("该券已被领完");
            }
            else
            {
                await _db.Coupons.Where(c => c.CouponId == coupon.CouponId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IssuedQty, c => c.IssuedQty + 1));
            }

            var row = CustomerCoupon.Issue(coupon.CouponId, customerId, coupon.ValidTo, newCode);
            _db.CustomerCoupons.Add(row);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("顾客 {CustomerId} 用兑换码领取券 {CouponId}", customerId, coupon.CouponId);
            return new MyCouponDto
            {
                Id = row.Id,
                CouponId = coupon.CouponId,
                Name = coupon.Name,
                Amount = coupon.Amount,
                MinAmount = coupon.MinAmount,
                ExpireTime = row.ExpireTime,
                Status = row.Status,
                ReceiveTime = row.ReceiveTime,
                Code = row.Code,
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    ///<summary>前台扫码查券：按码值定位到具体顾客的那张券，不做任何写操作</summary>
    public async Task<CouponVerifyDto> VerifyByCodeAsync(string rawCode)
    {
        //二维码里带前缀（顾客端 buildCouponQrText），扫码枪/手输两种来源都要能吃：
        //这里统一剥前缀再按码值查，避免同一张券在两种入口上表现不一致
        var code = NormalizeScannedCode(rawCode);
        if (code.Length == 0) throw new BusinessException("请扫码或输入券码");

        var row = await _db.CustomerCoupons.AsNoTracking()
            .Include(c => c.Coupon)
            .Include(c => c.Customer)
            .FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new BusinessException("未找到该券，请确认二维码是否为本系统券码");

        var template = row.Coupon ?? throw new BusinessException("券模板已删除，无法核销");
        var now = AppClock.Now;
        //状态是静态字段，过期判断要在读取时算：券可能已过期但还没被任何读操作刷新过
        var effectiveStatus = row.Status == CustomerCoupon.StatusUnused && row.ExpireTime < now
            ? CustomerCoupon.StatusExpired
            : row.Status;
        var idCard = row.Customer?.IdCard;
        return new CouponVerifyDto
        {
            CustomerCouponId = row.Id,
            CouponId = row.CouponId,
            Code = code,
            Name = template.Name,
            Amount = template.Amount,
            MinAmount = template.MinAmount,
            Status = effectiveStatus,
            ExpireTime = row.ExpireTime,
            UseTime = row.UseTime,
            OrderId = row.OrderId,
            CustomerId = row.CustomerId,
            CustomerName = row.Customer?.FullName ?? "（未登记）",
            ContactPhone = row.Customer?.ContactPhone,
            IdCardMasked = MaskIdCard(idCard),
            MemberLevel = row.Customer?.MemberLevel ?? string.Empty,
            Usable = effectiveStatus == CustomerCoupon.StatusUnused && row.ExpireTime >= now && template.Status == "启用",
        };
    }

    ///<summary>
    /// 前台核销：把券置为已使用。与下单用券共用「条件更新」口径，
    /// 同一张券被两个前台同时扫到时只有一次会成功。
    ///</summary>
    public async Task<CouponVerifyDto> ConsumeByCodeAsync(string rawCode)
    {
        var info = await VerifyByCodeAsync(rawCode);
        if (info.Status != CustomerCoupon.StatusUnused) throw new BusinessException($"该券状态为「{info.Status}」，不能核销");
        if (info.ExpireTime < AppClock.Now) throw new BusinessException("该券已过期，不能核销");
        if (!info.Usable) throw new BusinessException("该券所属活动已停用，不能核销");

        var now = AppClock.Now;
        var affected = await _db.CustomerCoupons
            .Where(c => c.Id == info.CustomerCouponId && c.Status == CustomerCoupon.StatusUnused && c.ExpireTime >= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CustomerCoupon.StatusUsed)
                .SetProperty(c => c.UseTime, now));
        if (affected == 0) throw new BusinessException("该券刚刚已被核销");

        _logger.LogInformation("券 {Id} 前台扫码核销", info.CustomerCouponId);
        return await VerifyByCodeAsync(rawCode);
    }

    ///<summary>码值生成：CP + 16 位随机十六进制，批量生成后一次性查重，冲突再补</summary>
    private async Task<List<string>> GenerateUniqueCodesAsync(int count)
    {
        var result = new List<string>(count);
        while (result.Count < count)
        {
            var batch = Enumerable.Range(0, count - result.Count)
                .Select(_ => "CP" + RandomNumberGenerator.GetHexString(16).ToUpperInvariant())
                .ToList();
            var exists = await _db.CustomerCoupons.AsNoTracking()
                .Where(c => c.Code != null && batch.Contains(c.Code!))
                .Select(c => c.Code!)
                .ToListAsync();
            result.AddRange(batch.Where(b => !exists.Contains(b)));
        }
        return result;
    }

    ///<summary>给该顾客名下缺少码值的券补码（历史数据兼容）</summary>
    private async Task EnsureCodesAsync(int customerId)
    {
        var missing = await _db.CustomerCoupons
            .Where(c => c.CustomerId == customerId && c.Code == null)
            .Take(Paging.MaxUnpagedRows)
            .ToListAsync();
        if (missing.Count == 0) return;

        var codes = await GenerateUniqueCodesAsync(missing.Count);
        for (var i = 0; i < missing.Count; i++) missing[i].AssignCode(codes[i]);
        await _db.SaveChangesAsync();
    }

    ///<summary>券码规范化：去掉二维码前缀，统一大写；与前端 normalize 保持同一口径</summary>
    private static string NormalizeScannedCode(string? raw)
    {
        var t = (raw ?? string.Empty).Trim();
        const string prefix = "ERENTAL:COUPON:";
        if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) t = t[prefix.Length..].Trim();
        return t.ToUpperInvariant();
    }

    ///<summary>身份证脱敏：前台只能看后 4 位，避免核销页面泄露完整证件号</summary>
    private static string? MaskIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length < 8) return null;
        return idCard[..4] + new string('*', idCard.Length - 8) + idCard[^4..];
    }

    ///<summary>下单前校验：券归属、状态、有效期、门槛，返回可抵扣金额（不超过租金）</summary>
    public async Task<(int Id, decimal Amount)> ValidateAsync(int customerId, int customerCouponId, decimal rent)
    {
        var row = await _db.CustomerCoupons.AsNoTracking()
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.Id == customerCouponId && c.CustomerId == customerId)
            ?? throw new BusinessException("优惠券不存在，或不属于当前顾客");
        if (row.Status != CustomerCoupon.StatusUnused) throw new BusinessException("该券已使用或已过期");
        if (row.ExpireTime < AppClock.Now) throw new BusinessException("该券已过期");
        var template = row.Coupon ?? throw new BusinessException("券模板已删除，无法使用");
        if (template.Status != "启用") throw new BusinessException("该券已停用");
        if (rent < template.MinAmount) throw new BusinessException($"该券需订单租金满 ¥{template.MinAmount:0.00} 才能使用");
        return (row.Id, Math.Min(template.Amount, rent));
    }

    ///<summary>核销：条件更新保证同一张券只会被一个订单用掉，返回 0 行说明已被并发使用</summary>
    public async Task ConsumeAsync(int customerCouponId, int orderId)
    {
        var now = AppClock.Now;
        var affected = await _db.CustomerCoupons
            .Where(c => c.Id == customerCouponId && c.Status == CustomerCoupon.StatusUnused && c.ExpireTime >= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CustomerCoupon.StatusUsed)
                .SetProperty(c => c.UseTime, now)
                .SetProperty(c => c.OrderId, orderId));
        if (affected == 0) throw new BusinessException("该优惠券刚刚被使用，请重新选择");
    }

    ///<summary>订单取消/超时回收：把本单用掉的券退回未使用（已过期的券不再恢复可用）</summary>
    public Task<int> ReleaseByOrderAsync(int orderId) =>
        _db.CustomerCoupons
            .Where(c => c.OrderId == orderId && c.Status == CustomerCoupon.StatusUsed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CustomerCoupon.StatusUnused)
                .SetProperty(c => c.OrderId, (int?)null)
                .SetProperty(c => c.UseTime, (DateTime?)null));

    ///<summary>批量兜底：把挂在「已取消」订单上的券统一退回（超时回收走存储过程，拿不到具体订单号）</summary>
    public Task<int> ReleaseOrphanedAsync() =>
        _db.CustomerCoupons
            .Where(c => c.Status == CustomerCoupon.StatusUsed && c.OrderId != null
                        && _db.RentalOrders.Any(o => o.OrderId == c.OrderId && o.OrderStatus == "已取消"))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CustomerCoupon.StatusUnused)
                .SetProperty(c => c.OrderId, (int?)null)
                .SetProperty(c => c.UseTime, (DateTime?)null));
}
