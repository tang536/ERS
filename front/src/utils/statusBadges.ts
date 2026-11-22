/**
 * 全站共享的状态徽标颜色地图：员工端、顾客端与仪表报表共用同一份。
 * 同一状态在任何页面显示的颜色一致；调用方读取不存在的键仍用 fallback（`|| 'status-default'` 等）。
 */
export const ORDER_BADGE: Record<string, string> = {
  '待支付': 'status-warning',
  '已支付': 'status-primary',
  '租赁中': 'status-primary',
  '已完结': 'status-success',
  '已完成': 'status-success',
  '待续租': 'status-warning',
  '已取消': 'status-default',
  '已逾期': 'status-danger',
}

export const PAYMENT_BADGE: Record<string, string> = {
  '待支付': 'status-warning',
  '已支付': 'status-success',
  '已退款': 'status-danger',
  '部分退款': 'status-warning',
  '已取消': 'status-default',
}

export const VEHICLE_BADGE: Record<string, string> = {
  '空闲': 'status-success',
  '在租': 'status-primary',
  '维保': 'status-warning',
  '报废': 'status-danger',
}
