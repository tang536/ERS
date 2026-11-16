using System.Security.Cryptography;
using System.Text;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;

namespace ERental.Services;

///<summary>
/// 电子合同：按下单快照生成正文，顾客手写签名后连同 SHA-256 一起留档。
/// 签名图存 uploads/contract，正文与哈希存库——事后改订单不会改动已签合同。
///</summary>
public class ContractService(AppDbContext db, ImageStorage images, IHttpContextAccessor http)
{
    private const string Version = "v1";
    private const string SignaturePrefix = "data:image/png;base64,";
    private const int MaxSignatureChars = 1_500_000;

    private readonly AppDbContext _db = db;
    private readonly ImageStorage _images = images;
    private readonly IHttpContextAccessor _http = http;

    ///<summary>取合同：已签署返回留档正文；未签署返回按当前订单生成的预览</summary>
    public async Task<ContractDto> GetAsync(int orderId, int customerId)
    {
        var signed = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.OrderId == orderId);
        if (signed != null)
        {
            if (signed.CustomerId != customerId) throw new BusinessException("无权查看该合同");
            return ToDto(signed, signed.Content);
        }

        var content = await BuildContentAsync(orderId, customerId);
        return new ContractDto { OrderId = orderId, Version = Version, Content = content, Status = "未签署" };
    }

    ///<summary>签署：同一订单只能签一次，签名图落盘后写库</summary>
    public async Task<ContractDto> SignAsync(SignContractInput input, int customerId)
    {
        if (await _db.Contracts.AnyAsync(c => c.OrderId == input.OrderId))
            throw new BusinessException("该订单已签署合同，无需重复签署");

        var content = await BuildContentAsync(input.OrderId, customerId);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var signatureUrl = await SaveSignatureAsync(input.SignatureData);

        var order = await _db.RentalOrders.AsNoTracking().Where(o => o.OrderId == input.OrderId)
            .Select(o => new { o.OrderNo }).FirstAsync();
        var ip = ClientIp.Resolve(_http.HttpContext);
        var contract = Contract.Sign("HT" + order.OrderNo, input.OrderId, customerId, Version,
            content, hash, signatureUrl, ip, AppClock.Now);

        _db.Contracts.Add(contract);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbConcurrency.IsDuplicateKey(ex))
        {
            _images.Delete(signatureUrl);
            throw new BusinessException("该订单已签署合同，请刷新后查看");
        }
        return ToDto(contract, content);
    }

    private async Task<string> SaveSignatureAsync(string data)
    {
        if (string.IsNullOrWhiteSpace(data) || !data.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("签名格式不正确，请重新签名");
        if (data.Length > MaxSignatureChars) throw new BusinessException("签名图片过大，请重新签名");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(data[SignaturePrefix.Length..]); }
        catch (FormatException) { throw new BusinessException("签名图片解析失败，请重新签名"); }
        if (bytes.Length < 100) throw new BusinessException("签名内容为空，请先手写签名");

        using var ms = new MemoryStream(bytes);
        return await _images.SaveStreamAsync(ms, "contract", ".png");
    }

    private async Task<string> BuildContentAsync(int orderId, int customerId)
    {
        var order = await _db.RentalOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNo,
                o.CustomerId,
                o.Customer!.FullName,
                o.VehicleId,
                o.Vehicle!.VehicleNo,
                o.LeaseStart,
                o.LeaseEnd,
                o.TotalRent,
                o.Deposit,
                o.DiscountAmount,
                o.AddonFee,
                o.PointsUsed,
                o.OrderStatus,
            }).FirstOrDefaultAsync() ?? throw new BusinessException("订单不存在");
        if (order.CustomerId != customerId) throw new BusinessException("无权签署该订单的合同");

        var addons = await _db.OrderAddons.AsNoTracking().Where(a => a.OrderId == orderId)
            .Select(a => new { a.Name, a.Qty, a.Amount }).ToListAsync();
        var addonText = addons.Count == 0 ? "无" : string.Join("、", addons.Select(a => $"{a.Name}×{a.Qty}（¥{a.Amount:0.00}）"));

        var sb = new StringBuilder();
        sb.AppendLine("电动车租赁服务合同");
        sb.AppendLine($"合同版本：{Version}");
        sb.AppendLine($"订单号：{order.OrderNo}");
        sb.AppendLine($"承租方：{order.FullName}");
        sb.AppendLine($"租赁车辆：{order.VehicleNo}");
        sb.AppendLine($"租期：{order.LeaseStart:yyyy-MM-dd HH:mm} 至 {order.LeaseEnd:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"租金：¥{order.TotalRent - order.AddonFee:0.00}；增值服务：¥{order.AddonFee:0.00}（{addonText}）");
        sb.AppendLine($"优惠抵扣：¥{order.DiscountAmount:0.00}（含积分抵扣 {order.PointsUsed} 分）；押金：¥{order.Deposit:0.00}");
        sb.AppendLine($"实付合计：¥{order.TotalRent - order.DiscountAmount + order.Deposit:0.00}");
        sb.AppendLine();
        sb.AppendLine("第一条  承租方须实名租车，出示本人有效身份证件，并对所填资料的真实性负责。");
        sb.AppendLine("第二条  车辆仅限承租方本人按约定用途使用，不得转租、出借或用于违法活动。");
        sb.AppendLine("第三条  承租方应在取车时检查车辆状况；还车时经门店验车确认车况与电量。");
        sb.AppendLine("第四条  逾期归还按系统公示的超时倍数计收逾期费；车辆损坏按定损金额赔偿。");
        sb.AppendLine("第五条  押金在还车结算后按多退少补原则处理，原路退回或转为补缴款。");
        sb.AppendLine("第六条  本电子合同经承租方手写签名确认，与纸质合同具有同等效力。");
        sb.AppendLine();
        sb.AppendLine("（以下为签署区）");
        return sb.ToString();
    }

    private static ContractDto ToDto(Contract c, string content) => new()
    {
        ContractId = c.ContractId,
        ContractNo = c.ContractNo,
        OrderId = c.OrderId,
        Version = c.Version,
        ContentHash = c.ContentHash,
        SignatureUrl = c.SignatureUrl,
        SignTime = c.SignTime,
        Status = c.Status,
        Content = content,
    };
}
