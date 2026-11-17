/**
 * 下单幂等键：优先用浏览器原生 UUID；旧浏览器降级为「时间戳 + 随机数」。
 * 同一笔提交在用户双击/网络重试时必须复用同一个值，因此调用方要用 useRef 持有，
 * 成功后再换新的，而不是每次进入提交函数都重新生成。
 */
export function newRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}
