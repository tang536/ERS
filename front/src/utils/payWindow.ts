/**
 * 打开收银台（真实网关或内置模拟收银台）。
 *
 * 为什么不用裸 `window.open`：付款请求是 `await` 之后才拿到 URL 的，
 * 此时已经脱离用户点击手势，Chrome / Edge 的弹窗拦截器会直接拦掉。
 * 原来的实现没有检查返回值，仍然弹「已打开模拟收银台」——
 * 用户什么也没看到，订单停在待支付，看起来就像支付被「绕过」了。
 *
 * 这里显式判断是否真的开窗：被拦截就退回同一窗口跳转。
 * 模拟收银台会自己跳回 /pay-result，不会把用户困在一个空白页里。
 *
 * @returns 是否成功新开窗口（false = 已改为当前窗口跳转）
 */
export function openCashier(url: string): boolean {
  if (!url) return false
  let win: Window | null = null
  try {
    win = window.open(url, '_blank', 'width=480,height=680')
  } catch {
    win = null
  }
  if (win) return true
  window.location.assign(url)
  return false
}
