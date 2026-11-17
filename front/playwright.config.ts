import { defineConfig, devices } from '@playwright/test'

/**
 * 前端视觉/布局回归。
 *
 * 运行方式（需要本地前后端已启动，且准备测试账号）：
 *   E2E_STAFF_ACCOUNT=... E2E_STAFF_PASSWORD=... \
 *   E2E_CUSTOMER_ACCOUNT=... E2E_CUSTOMER_PASSWORD=... \
 *   npm run test:e2e
 *
 * 不把账号密码写进仓库：没有账号时对应用例跳过，不会用假登录态污染页面。
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  // 登录接口有防刷限流，多个 project 并行登录同一测试账号会触发 429；
  // 回归用例本身很快，串行跑更稳定，也便于截图按顺序归档。
  workers: 1,
  timeout: 45_000,
  expect: { timeout: 8_000 },
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL || 'http://localhost:5173',
    channel: process.env.E2E_CHANNEL || 'chromium',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'mobile-390', use: { ...devices['iPhone 13'], browserName: 'chromium', viewport: { width: 390, height: 844 } } },
    { name: 'tablet-768', use: { ...devices['iPad Mini'], browserName: 'chromium', viewport: { width: 768, height: 1024 } } },
    { name: 'desktop-1280', use: { ...devices['Desktop Chrome'], browserName: 'chromium', viewport: { width: 1280, height: 900 } } },
  ],
})
