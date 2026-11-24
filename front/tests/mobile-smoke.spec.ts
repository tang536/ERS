import { test, expect, request } from '@playwright/test'

/**
 * 移动端视觉回归基线（结构断言 + 整页截图留证）。
 *
 * 截图基线受订单/结算等会变化的业务数据影响，先做与数据无关的结构断言：
 * 横向溢出、表格卡片字段名、页面可达性。截图只作为失败时的证据，
 * 避免把大量图片和随机业务数据提交进仓库。
 */
const API = process.env.E2E_API_URL || 'http://127.0.0.1:5105'

type Account = { account: string; password: string; roleType?: string; kind: 'staff' | 'customer' }

const staffAccount: Account | null = process.env.E2E_STAFF_ACCOUNT && process.env.E2E_STAFF_PASSWORD
  ? { account: process.env.E2E_STAFF_ACCOUNT, password: process.env.E2E_STAFF_PASSWORD, roleType: process.env.E2E_STAFF_ROLE || 'admin', kind: 'staff' }
  : null

const receptionAccount: Account | null = process.env.E2E_RECEPTION_ACCOUNT && process.env.E2E_RECEPTION_PASSWORD
  ? { account: process.env.E2E_RECEPTION_ACCOUNT, password: process.env.E2E_RECEPTION_PASSWORD, roleType: 'reception', kind: 'staff' }
  : null

const customerAccount: Account | null = process.env.E2E_CUSTOMER_ACCOUNT && process.env.E2E_CUSTOMER_PASSWORD
  ? { account: process.env.E2E_CUSTOMER_ACCOUNT, password: process.env.E2E_CUSTOMER_PASSWORD, kind: 'customer' }
  : null

async function loginByApi(account: Account) {
  const api = await request.newContext({ baseURL: API })
  const path = account.kind === 'staff' ? '/api/auth/staff-login' : '/api/auth/customer-login'
  const payload = account.kind === 'staff'
    ? { loginAccount: account.account, password: account.password, roleType: account.roleType }
    : { account: account.account, password: account.password }
  const res = await api.post(path, { data: payload })
  expect(res.ok(), `${account.account} 登录失败：${await res.text()}`).toBeTruthy()
  const body = await res.json()
  expect(body.data?.token, '登录响应缺少 token').toBeTruthy()
  await api.dispose()
  return body.data
}

async function assertPageHealth(page: any, route: string, testInfo: any) {
  await expect(page.locator('body')).toBeVisible()

  // 只有列表页需要等表格；纯表单/报表页不要白等 5 秒。
  const tableCount = await page.locator('table.data-table').count()
  if (tableCount > 0) {
    await page.locator('table.data-table tbody tr').first().waitFor({ state: 'attached', timeout: 5000 }).catch(() => {})
    await page.waitForFunction(() => {
      const cells = Array.from(document.querySelectorAll('table.data-table tbody td')) as HTMLElement[]
      const visible = cells.filter((cell) => cell.offsetParent !== null)
      return visible.length > 0 && visible.every((cell) => (cell.getAttribute('data-label') || '').trim())
    }, null, { timeout: 5000 }).catch(() => {})
  }

  // 1) 不允许出现横向滚动条（手机端最常见的整页错位信号）
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }))
  expect(overflow.scrollWidth, `${route} 出现横向溢出`).toBeLessThanOrEqual(overflow.clientWidth + 1)

  // 2) 表格卡片：有表头且可见的单元格必须有 data-label
  const missingLabels = await page.locator('table.data-table tbody td').evaluateAll((cells) =>
    cells
      .filter((cell) => {
        const el = cell as HTMLElement
        return el.offsetParent !== null && !el.closest('thead')
      })
      .filter((cell) => !(cell.getAttribute('data-label') || '').trim())
      .map((cell) => (cell.textContent || '').trim().slice(0, 24),
      ))
  expect(missingLabels, `${route} 存在没有字段名的卡片单元格`).toEqual([])

  await testInfo.attach(`${route.replace(/\W+/g, '_')}-${testInfo.project.name}.png`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  })
}

// 仅前台角色可进：券核销已收紧到前台，管理员不再出现在 STAFF_ROUTES 中。
const STAFF_ROUTES = [
  '/dashboard', '/orders', '/settlements', '/customers', '/vehicles',
  '/operations', '/reports', '/users', '/config', '/dict',
]

const RECEPTION_ROUTES = ['/coupon-verify']

const CUSTOMER_ROUTES = ['/my-orders', '/rent', '/coupons', '/profile', '/membership']

let staffUser: any = null
let receptionUser: any = null
let customerUser: any = null

test.describe('员工端关键路由', () => {
  test.skip(!staffAccount, '未配置 E2E_STAFF_ACCOUNT / E2E_STAFF_PASSWORD，跳过员工端回归')

  // 登录接口有防刷限流，一个 project 只登录一次，后续用例复用 token。
  test.beforeAll(async () => {
    if (staffAccount) staffUser = await loginByApi(staffAccount)
  })

  test.beforeEach(async ({ page }) => {
    if (!staffUser) return
    await page.addInitScript((u) => localStorage.setItem('user', JSON.stringify(u)), staffUser)
  })

  for (const route of STAFF_ROUTES) {
    test(`布局健康 ${route}`, async ({ page }, testInfo) => {
      await page.goto(route)
      await page.waitForTimeout(1200)
      await assertPageHealth(page, route, testInfo)
    })
  }
})

test.describe('前台专属路由', () => {
  test.skip(!receptionAccount, '未配置 E2E_RECEPTION_ACCOUNT / E2E_RECEPTION_PASSWORD，跳过前台端回归')

  test.beforeAll(async () => {
    if (receptionAccount) receptionUser = await loginByApi(receptionAccount)
  })

  test.beforeEach(async ({ page }) => {
    if (!receptionUser) return
    await page.addInitScript((u) => localStorage.setItem('user', JSON.stringify(u)), receptionUser)
  })

  for (const route of RECEPTION_ROUTES) {
    test(`布局健康 ${route}`, async ({ page }, testInfo) => {
      await page.goto(route)
      await page.waitForTimeout(1200)
      await assertPageHealth(page, route, testInfo)
    })
  }
})

test.describe('顾客端关键路由', () => {
  test.skip(!customerAccount, '未配置 E2E_CUSTOMER_ACCOUNT / E2E_CUSTOMER_PASSWORD，跳过顾客端回归')

  test.beforeAll(async () => {
    if (customerAccount) customerUser = await loginByApi(customerAccount)
  })

  test.beforeEach(async ({ page }) => {
    if (!customerUser) return
    await page.addInitScript((u) => localStorage.setItem('user', JSON.stringify(u)), customerUser)
  })

  for (const route of CUSTOMER_ROUTES) {
    test(`布局健康 ${route}`, async ({ page }, testInfo) => {
      await page.goto(route)
      await page.waitForTimeout(1200)
      await assertPageHealth(page, route, testInfo)
    })
  }
})
