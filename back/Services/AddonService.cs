using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

///<summary>
/// 增值服务：骑行保险、头盔、送车上门等。按「一次性/按天」两种方式计价，
/// 下单时把名称与单价固化进 order_addon，后台改价不影响历史订单。
///</summary>
public class AddonService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    ///<summary>服务项报价：Item + 数量 + 金额，供订单落快照</summary>
    public sealed record AddonQuote(AddonItem Item, int Qty, decimal Amount);

    private static readonly System.Linq.Expressions.Expression<Func<AddonItem, AddonDto>> Projection = a => new AddonDto
    {
        AddonId = a.AddonId,
        Name = a.Name,
        Price = a.Price,
        Unit = a.Unit,
        Description = a.Description,
        Status = a.Status,
        SortNo = a.SortNo,
    };

    public Task<List<AddonDto>> GetAllAsync(bool onlyEnabled = false)
    {
        var q = _db.AddonItems.AsNoTracking();
        if (onlyEnabled) q = q.Where(a => a.Status == "启用");
        return q.OrderBy(a => a.SortNo).ThenBy(a => a.AddonId).Take(Paging.MaxUnpagedRows).Select(Projection).ToListAsync();
    }

    public async Task<AddonDto> CreateAsync(AddonInput input)
    {
        var item = AddonItem.Create(input.Name, input.Price, input.Unit, input.Description, input.SortNo);
        _db.AddonItems.Add(item);
        await _db.SaveChangesAsync();
        return await _db.AddonItems.AsNoTracking().Where(a => a.AddonId == item.AddonId).Select(Projection).FirstAsync();
    }

    public async Task<AddonDto> UpdateAsync(int addonId, AddonInput input)
    {
        var item = await _db.AddonItems.FindAsync(addonId) ?? throw new BusinessException("增值服务不存在");
        item.Update(input.Name, input.Price, input.Unit, input.Description, input.SortNo);
        await _db.SaveChangesAsync();
        return await _db.AddonItems.AsNoTracking().Where(a => a.AddonId == addonId).Select(Projection).FirstAsync();
    }

    public async Task SetStatusAsync(int addonId, string status)
    {
        var item = await _db.AddonItems.FindAsync(addonId) ?? throw new BusinessException("增值服务不存在");
        item.SetStatus(status);
        await _db.SaveChangesAsync();
    }

    ///<summary>按勾选的服务项报价；已停用/不存在的项直接拒绝，避免下单金额与页面不一致</summary>
    public async Task<(List<AddonQuote> Quotes, decimal Total)> QuoteAsync(IEnumerable<int>? addonIds, int billableDays)
    {
        var ids = (addonIds ?? []).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return ([], 0m);

        var items = await _db.AddonItems.AsNoTracking().Where(a => ids.Contains(a.AddonId)).ToListAsync();
        if (items.Count != ids.Count) throw new BusinessException("部分增值服务不存在，请刷新后重新选择");
        if (items.Any(a => a.Status != "启用")) throw new BusinessException("所选增值服务已下架，请刷新后重新选择");

        var quotes = items.Select(a => new AddonQuote(a, a.IsDaily ? Math.Max(1, billableDays) : 1, a.AmountFor(billableDays))).ToList();
        return (quotes, Math.Round(quotes.Sum(q => q.Amount), 2));
    }

    ///<summary>创建订单服务快照行（调用方已拿到订单主键）</summary>
    public IEnumerable<OrderAddon> Snapshot(int orderId, IReadOnlyCollection<AddonQuote> quotes) =>
        quotes.Select(q => OrderAddon.Snapshot(orderId, q.Item, q.Qty));

    ///<summary>订单服务明细（订单详情/结算展示用）</summary>
    public Task<List<OrderAddonDto>> OfOrderAsync(int orderId) =>
        _db.OrderAddons.AsNoTracking().Where(a => a.OrderId == orderId)
            .OrderBy(a => a.Id)
            .Select(a => new OrderAddonDto { Name = a.Name, Unit = a.Unit, Price = a.Price, Qty = a.Qty, Amount = a.Amount })
            .ToListAsync();
}
