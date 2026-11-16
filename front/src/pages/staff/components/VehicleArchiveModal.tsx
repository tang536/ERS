import { useEffect, useState } from 'react'
import { vehicleApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'
import type { VehicleArchive } from '@/types'

const TYPE_TONE: Record<string, string> = {
  '租赁': 'status-primary',
  '维保': 'status-warning',
  '盘点': 'status-default',
  '报废': 'status-danger',
  '验车': 'status-success',
}

/**
 * 车辆档案时间线（只读聚合）：把租赁、维保、盘点、验车、报废按时间倒序串起来，
 * 并汇总累计营收与维保成本。数据全部来自现有业务表，不额外建表。
 */
export default function VehicleArchiveModal({ vehicleId, vehicleNo, onClose }: {
  vehicleId: number
  vehicleNo: string
  onClose: () => void
}) {
  const { toast } = useUI()
  const [data, setData] = useState<VehicleArchive | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let alive = true
    vehicleApi.archive(vehicleId)
      .then((res: any) => { if (alive) setData(res.data) })
      .catch((e: any) => { if (alive) { toast(e?.message || '车辆档案加载失败', 'error'); onClose() } })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
    // onClose 是父组件每次渲染新建的内联函数，放进依赖会导致重复拉取；它在错误分支里只用于收尾
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [vehicleId, toast])

  const net = data ? data.totalRevenue - data.maintenanceCost : 0

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-lg" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>车辆档案 · {vehicleNo}</span>
          <button className="close-btn" onClick={onClose}><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          {loading ? (
            <div className="loading-line"><Icon name="refresh" size={18} />档案加载中…</div>
          ) : data ? (
            <>
              <div className="callout callout-info">
                <Icon name="info" size={18} chip />
                <span>
                  {data.brand || '未知品牌'} · 当前状态 {data.vehicleStatus}
                  {data.buyTime ? ` · 购入于 ${String(data.buyTime).slice(0, 10)}` : ''}
                  {data.dailyRent != null ? ` · 日租金 ¥${data.dailyRent}` : ' · 日租金按默认价'}
                  {data.deposit != null ? ` · 押金 ¥${data.deposit}` : ''}
                </span>
              </div>

              <div className="stats-grid" style={{ marginBottom: 16 }}>
                <div className="stat-card tone-sky">
                  <div className="label">累计营收</div>
                  <div className="value">¥{data.totalRevenue.toFixed(2)}</div>
                </div>
                <div className="stat-card tone-amber">
                  <div className="label">维保成本</div>
                  <div className="value">¥{data.maintenanceCost.toFixed(2)}</div>
                </div>
                <div className="stat-card tone-emerald">
                  <div className="label">净收益</div>
                  <div className="value" style={{ color: net < 0 ? 'var(--danger-text, #C0392B)' : undefined }}>¥{net.toFixed(2)}</div>
                </div>
                <div className="stat-card tone-violet">
                  <div className="label">累计订单</div>
                  <div className="value">{data.orderCount}</div>
                </div>
              </div>

              <div className="card-title" style={{ marginTop: 4 }}>
                <Icon name="clock" size={16} chip />生命周期时间线
                <span className="card-title-count">{data.events.length} 条</span>
              </div>

              {data.events.length === 0 ? (
                <div className="empty"><Icon name="empty" size={40} chip /><div>该车暂无业务记录</div></div>
              ) : (
                <div style={{ maxHeight: 420, overflow: 'auto' }}>
                  <table className="data-table">
                    <thead>
                      <tr><th>时间</th><th>类型</th><th>事项</th><th className="num">金额</th><th>状态</th></tr>
                    </thead>
                    <tbody>
                      {data.events.map((e, i) => (
                        <tr key={`${e.type}-${i}-${e.time}`}>
                          <td className="cell-time">{fmtDT(e.time)}</td>
                          <td><span className={`status-tag ${TYPE_TONE[e.type] || 'status-default'}`}>{e.type}</span></td>
                          <td>
                            <strong>{e.title}</strong>
                            {e.detail && <div className="field-hint">{e.detail}</div>}
                          </td>
                          <td className="num">{e.amount != null ? `¥${Number(e.amount).toFixed(2)}` : '—'}</td>
                          <td>{e.status || '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          ) : null}
        </div>
        <div className="modal-footer">
          <button className="btn-sm btn-sm-default" onClick={onClose}>关闭</button>
        </div>
      </div>
    </div>
  )
}
