import { useSyncExternalStore } from 'react'
import type { UserInfo } from '../types'
import { clearAccessToken, setAccessToken } from './authToken'

const STORAGE_KEY = 'user'

/** 系统角色常量（与后端 AuthService.Role* 保持一致） */
export const ROLE = {
  ADMIN: '管理员',
  RECEPTION: '前台',
  FINANCE: '财务',
  CUSTOMER: '顾客',
} as const

/** 各角色的默认落地页：登录 / 访问越权页面时统一走这里 */
export const HOME_BY_ROLE: Record<string, string> = {
  [ROLE.ADMIN]: '/dashboard',
  [ROLE.RECEPTION]: '/dashboard',
  [ROLE.FINANCE]: '/dashboard',
  [ROLE.CUSTOMER]: '/my-orders',
}

function readStorage(): UserInfo | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as UserInfo & { token?: string; refreshToken?: string }
    if (!parsed?.userId || !parsed?.roleName) return null
    // 兼容旧版本：旧包曾把 access/refresh token 明文放在 localStorage，读到后立即清掉。
    const { token: _legacyToken, refreshToken: _legacyRefresh, ...profile } = parsed
    if (_legacyToken || _legacyRefresh) localStorage.setItem(STORAGE_KEY, JSON.stringify(profile))
    return profile as UserInfo
  } catch {
    return null
  }
}

// ===== 轻量全局 store：让 Layout / App / 各页面共享同一份登录态 =====
let cache: UserInfo | null = readStorage()
const listeners = new Set<() => void>()

function emit() {
  listeners.forEach(l => l())
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function getSnapshot() {
  return cache
}

/**
 * 写入登录态。登录响应里的 token 只放进内存，其余资料落 localStorage。
 * 更新资料（会员等级、实名完成等）时不传 token，不能清掉当前会话的 access token。
 */
export function setAuth(info: UserInfo & { token?: string }) {
  const { token, ...profile } = info
  if (token) setAccessToken(token)
  localStorage.setItem(STORAGE_KEY, JSON.stringify(profile))
  cache = profile
  emit()
}

export function clearAuth() {
  clearAccessToken()
  localStorage.removeItem(STORAGE_KEY)
  cache = null
  emit()
}

/** 根据角色计算落地页 */
export function homePathOf(user: UserInfo | null | undefined): string {
  if (!user) return '/login'
  if (user.userType === 'customer') return HOME_BY_ROLE[ROLE.CUSTOMER]
  return HOME_BY_ROLE[user.roleName] || '/dashboard'
}

export function isCustomerRole(user: UserInfo | null | undefined) {
  return !!user && (user.userType === 'customer' || user.roleName === ROLE.CUSTOMER)
}

export function useAuth() {
  const user = useSyncExternalStore(subscribe, getSnapshot)

  return {
    user,
    /** 登录：写入登录态（会覆盖旧的残留信息） */
    login: setAuth,
    logout: clearAuth,
    isLoggedIn: !!user,
    isCustomer: isCustomerRole(user),
    isStaff: !!user && !isCustomerRole(user),
    homePath: homePathOf(user),
    hasRole: (roles: string[]) => !!user && roles.includes(user.roleName),
  }
}
