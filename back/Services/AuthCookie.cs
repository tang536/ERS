namespace ERental.Services;

/// <summary>
/// 认证 Cookie 的唯一约定。
/// access token 只用于 **GET/HEAD** 的图片等资源请求（JWT 事件里按方法限定），
/// refresh token 只走 <c>/api/auth</c> 路径；两者都是 HttpOnly + SameSite=Strict，
/// 前端 JavaScript 不读取，避免 XSS 直接偷走令牌。
/// </summary>
public static class AuthCookie
{
    public const string AccessName = "erental_access";
    public const string RefreshName = "erental_refresh";
    public const string RefreshPath = "/api/auth";

    /// <summary>访问令牌 Cookie：路径为根，供私有图片等 GET 资源携带。</summary>
    public static CookieOptions Access(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = TimeSpan.FromMinutes(AuthService.AccessTokenMinutes),
    };

    /// <summary>续期令牌 Cookie：只发给 /api/auth，减少每个业务请求都携带长期凭证。</summary>
    public static CookieOptions Refresh(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = RefreshPath,
        MaxAge = TimeSpan.FromDays(AuthService.RefreshTokenDays),
    };

    /// <summary>退出登录时按同一路径删除两个 Cookie。</summary>
    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(AccessName, new CookieOptions { Path = "/" });
        response.Cookies.Delete(RefreshName, new CookieOptions { Path = RefreshPath });
    }
}
