using System.Net;

namespace ERental.Services;

/// <summary>
/// 客户端地址的统一口径。
///
/// 全站已经有 <c>ForwardedHeaders</c> 中间件按「仅信任本机反代」把 X-Forwarded-For
/// 还原进 <c>Connection.RemoteIpAddress</c>，因此业务代码只应读这个属性；
/// 各控制器再各自拼 <c>RemoteIpAddress?.ToString()</c> 会产生两种偏差：
///   · 双栈监听时拿到 <c>::ffff:127.0.0.1</c>，与 IPv4 审计记录对不上；
///   · 取不到地址时有的写 null、有的写 "unknown"，限流分区与日志来源各说各话。
/// 这里集中处理映射与兜底，调用方不再重复这段判断。
/// </summary>
public static class ClientIp
{
    /// <summary>当前请求的客户端 IP；取不到时返回 null</summary>
    public static string? Resolve(HttpContext? http) => Resolve(http?.Connection.RemoteIpAddress);

    /// <summary>当前请求的客户端 IP；取不到时返回 "unknown"（限流分区/日志需要稳定键时用）</summary>
    public static string ResolveOrUnknown(HttpContext? http) => Resolve(http) ?? "unknown";

    /// <summary>把 IPv4-mapped IPv6 还原成 IPv4 文本，其余地址原样输出</summary>
    public static string? Resolve(IPAddress? address)
    {
        if (address is null) return null;
        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }
}
