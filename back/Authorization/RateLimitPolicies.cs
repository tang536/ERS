namespace ERental.Authorization;

/// <summary>
/// 限流策略名。与 <c>Program.cs</c> 里 <c>AddRateLimiter</c> 注册的具名策略一一对应，
/// 控制器侧用 <c>[EnableRateLimiting(RateLimitPolicies.Auth)]</c> 引用，
/// 避免「注册名」与「引用名」各写一份字符串、改一处漏一处。
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>认证类接口：员工登录 / 顾客登录 / 注册 / 修改密码。每 IP 每分钟 10 次。</summary>
    public const string Auth = "auth";

    /// <summary>
    /// 滑动拼图挑战生成：单独限流，每 IP 每分钟 30 次。
    /// 不能和 Auth 共用：登录失败、重新验证都会换图，10 次额度很容易被正常用户耗尽，
    /// 结果是人被挡在「操作过于频繁」外面，反而登录不了。
    /// </summary>
    public const string Captcha = "captcha";

    /// <summary>匿名支付状态轮询：订单号虽已加随机熵，仍要挡住批量枚举。每 IP 每分钟 30 次。</summary>
    public const string PayStatus = "pay-status";
}
