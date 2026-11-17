using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERental.Models;

///<summary>
/// 电子合同：订单确认后生成一份带内容哈希的合同，顾客手写签名后留档。
/// 哈希用于证明「这份合同从签署后没有被改过」，签名图存 uploads/contract。
///</summary>
[Table("contract")]
public class Contract
{
    public const string StatusSigned = "已签署";

    [Key]
    [Column("contract_id")]
    public int ContractId { get; init; }

    [Required, MaxLength(40)]
    [Column("contract_no")]
    public string ContractNo { get; private set; } = string.Empty;

    [Column("order_id")]
    public int OrderId { get; private set; }

    [ForeignKey(nameof(OrderId))]
    public RentalOrder? RentalOrder { get; init; }

    [Column("customer_id")]
    public int CustomerId { get; private set; }

    [MaxLength(20)]
    [Column("version")]
    public string Version { get; private set; } = "v1";

    ///<summary>合同正文的 SHA-256（十六进制），用于校验留档后未被篡改</summary>
    [MaxLength(64)]
    [Column("content_hash")]
    public string ContentHash { get; private set; } = string.Empty;

    ///<summary>签署时的合同正文快照：之后订单变更也不影响已签合同</summary>
    [Column("content", TypeName = "text")]
    public string Content { get; private set; } = string.Empty;

    ///<summary>手写签名图相对路径</summary>
    [MaxLength(255)]
    [Column("signature_url")]
    public string? SignatureUrl { get; private set; }

    [Column("sign_time")]
    public DateTime SignTime { get; private set; }

    [MaxLength(45)]
    [Column("sign_ip")]
    public string? SignIp { get; private set; }

    [MaxLength(10)]
    [Column("status")]
    public string Status { get; private set; } = StatusSigned;

    ///<summary>签署：签名图与时间落库，状态固定为「已签署」</summary>
    public static Contract Sign(string contractNo, int orderId, int customerId, string version,
        string content, string contentHash, string signatureUrl, string? signIp, DateTime signTime) => new()
        {
            ContractNo = contractNo,
            OrderId = orderId,
            CustomerId = customerId,
            Version = version,
            Content = content,
            ContentHash = contentHash,
            SignatureUrl = signatureUrl,
            SignTime = signTime,
            SignIp = signIp,
            Status = StatusSigned,
        };
}
