using ERental.Services;

namespace ERental.Tests;

/// <summary>
/// 微信支付 APIv3 回调头自检：官方名称是 <c>Wechatpay-*</c>。
/// 之前写成 <c>Wechat-*</c> 时，真实回调取不到签名头，验签会全部失败——
/// 这是只有接真实商户号才会暴露的问题，用测试把四个名字钉死。
/// </summary>
public class WechatCallbackHeaderTests
{
    [Fact]
    public void NotifyHeaders_MatchOfficialApiV3Names()
    {
        Assert.Equal(
            ["Wechatpay-Timestamp", "Wechatpay-Nonce", "Wechatpay-Signature", "Wechatpay-Serial"],
            WechatPayService.NotifyHeaders);
    }
}
