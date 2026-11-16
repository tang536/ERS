using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ERental.Services;

/// <summary>
/// 模拟收银台的一次性签名：Nginx 反向代理后 loopback 判定会失真，
/// 若只靠来源 IP，匿名用户可直接伪造 /api/payment/mock/notify 把订单改成已支付。
/// 服务端在生成 /mock-pay 链接时按「订单号 + 金额 + 支付方式」签名，
/// 通知回传时校验同一个签名；没有服务端密钥就无法为一笔新订单伪造通知。
/// </summary>
public static class MockPayToken
{
    private const string PayloadSeparator = "\n";

    /// <summary>生成签名（十六进制小写）</summary>
    public static string Sign(IConfiguration configuration, string orderNo, decimal amount, string payType)
    {
        var secret = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16) return string.Empty;

        var payload = string.Concat(
            orderNo, PayloadSeparator,
            amount.ToString("0.00", CultureInfo.InvariantCulture), PayloadSeparator,
            payType);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    /// <summary>恒定时间校验签名；格式错误一律视为无效</summary>
    public static bool Validate(IConfiguration configuration, string orderNo, decimal amount, string payType, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var expected = Sign(configuration, orderNo, amount, payType);
        if (expected.Length == 0 || token.Length != expected.Length) return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(token),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
