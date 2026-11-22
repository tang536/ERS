/**
 * 全站共享的货币格式：「¥ + 2 位小数」。
 *
 * 入参以 unknown：后端 DTO 字段在转发链路上可能为 null，统一在这里容错；
 * 调用方不再自己写 `Number(v || 0).toFixed(2)` 或 `v.toFixed(2)` 的变体。
 */
export function money(v: unknown): string {
  const n = Number(v ?? 0)
  return `¥${Number.isFinite(n) ? n.toFixed(2) : '0.00'}`
}
