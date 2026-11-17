/**
 * access token 只放内存，不落 localStorage：
 * - XSS 拿不到可重放的长期凭证；刷新令牌由后端写在 HttpOnly Cookie，前端完全读不到。
 * - 页面刷新后内存令牌丢失，但浏览器仍持有 HttpOnly Cookie；首个需要鉴权的请求遇到 401 时
 *   api 拦截器会自动调用 /auth/refresh 换新令牌并重放原请求。
 */
let accessToken: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null | undefined): void {
  accessToken = typeof token === 'string' && token.length > 0 ? token : null
}

export function clearAccessToken(): void {
  accessToken = null
}
