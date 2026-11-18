import { useEffect, useState } from 'react'
import { orderApi, orderChangeApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import DateRangeCalendar from '@/components/DateRangeCalendar'
import { fmtDT, toDatetimeInput } from '@/utils/datetime'
import type { Order, OrderChange, RentableVehicle } from '@/types'

/**
 * 订单变更弹窗：换车 / 改期。
 *
 * 只对「待支付 / 已支付」订单开放（后端同口径）：
 *   · 待支付：直接改写待付金额，车辆重新锁定；
 *   · 已支付：正差生成补缴单，负差原路退款。
 * 已起租后的换车属于事故处理，走结算与维保流程，不在这里办。
 */
export default function OrderChangeModal({ order, onClose, onDone }: {
  order: Order
  onClose: () => void
  onDone: () => void
}) {
  const { toast } = useUI()
  const [tab, setTab] = useState<'vehicle' | 'reschedule'>('vehicle')
  const [vehicles, setVehicles] = useState<RentableVehicle[]>([])
  const [loadingVehicles, setLoadingVehicles] = useState(true)
  const [newVehicleId, setNewVehicleId] = useState(0)
  const [leaseStart, setLeaseStart] = useState(() => toDatetimeInput(new Date(order.leaseStart)))
  const [leaseEnd, setLeaseEnd] = useState(() => toDatetimeInput(new Date(order.leaseEnd)))
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [history, setHistory] = useState<OrderChange[]>([])

  const loadHistory = () => {
    orderChangeApi.byOrder(order.orderId)
      .then((res: any) => setHistory(res.data || []))
      .catch(() => setHistory([]))
  }

  useEffect(() => {
    let alive = true
    orderApi.availableVehicles()
      .then((res: any) => { if (alive) setVehicles(res.data || []) })
      .catch(() => { if (alive) setVehicles([]) })
      .finally(() => { if (alive) setLoadingVehicles(false) })
    loadHistory()
    return () => { alive = false }
  }, [order.orderId])

  const submitVehicle = async () => {
    if (!newVehicleId) { toast('请选择要更换的车辆', 'warning'); return }
    setSaving(true)
    try {
      const res: any = await orderChangeApi.changeVehicle({
        orderId: order.orderId,
        newVehicleId,
        reason: reason.trim() || null,
      })
      const diff = Number(res?.data?.diffAmount || 0)
      toast(diff > 0 ? `换车已办理，需补缴 ¥${diff.toFixed(2)}` : diff < 0 ? `换车已办理，需退款 ¥${(-diff).toFixed(2)}` : '换车已办理，无差价', 'success')
      setNewVehicleId(0)
      setReason('')
      loadHistory()
      onDone()
    } catch (e: any) {
      toast(e?.message || '换车失败', 'error')
    } finally {
      setSaving(false)
    }
  }

  const submitReschedule = async () => {
    if (!leaseStart || !leaseEnd) { toast('请填写起租与到期时间', 'warning'); return }
    if (leaseEnd <= leaseStart) { toast('到期时间必须晚于起租时间', 'warning'); return }
    setSaving(true)
    try {
      const res: any = await orderChangeApi.reschedule({
        orderId: order.orderId,
        leaseStart,
        leaseEnd,
        reason: reason.trim() || null,
      })
      const diff = Number(res?.data?.diffAmount || 0)
      toast(diff > 0 ? `改期已办理，需补缴 ¥${diff.toFixed(2)}` : diff < 0 ? `改期已办理，需退款 ¥${(-diff).toFixed(2)}` : '改期已办理，无差价', 'success')
      setReason('')
      loadHistory()
      onDone()
    } catch (e: any) {
      toast(e?.message || '改期失败', 'error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={() => !saving && onClose()}>
      <div className="modal modal-lg" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>订单变更 · {order.orderNo}</span>
          <button className="close-btn" onClick={onClose}><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          <div className="callout callout-info">
            <Icon name="info" size={18} chip />
            <span>
              当前车辆 {order.vehicleNo}，租期 {fmtDT(order.leaseStart)} ~ {fmtDT(order.leaseEnd)}。
              待支付订单直接改写待付金额；已支付订单正差生成补缴单、负差原路退款。
            </span>
          </div>

          <div className="tab-switch" style={{ marginBottom: 12 }}>
            <button className={`tab-btn ${tab === 'vehicle' ? 'active' : ''}`} onClick={() => setTab('vehicle')}>换车</button>
            <button className={`tab-btn ${tab === 'reschedule' ? 'active' : ''}`} onClick={() => setTab('reschedule')}>改期</button>
          </div>

          {tab === 'vehicle' ? (
            <>
              <div className="form-group">
                <label>更换为 <span className="req">*</span></label>
                <select value={newVehicleId} onChange={e => setNewVehicleId(parseInt(e.target.value))}>
                  <option value={0}>{loadingVehicles ? '可租车辆加载中…' : '请选择车辆'}</option>
                  {vehicles.map(v => (
                    <option key={v.vehicleId} value={v.vehicleId}>
                      {v.vehicleNo} - {v.brand || '未知品牌'}（¥{v.hourlyRent}/小时 · 押金 ¥{v.deposit}）
                    </option>
                  ))}
                </select>
                {!loadingVehicles && vehicles.length === 0 && (
                  <div className="field-hint">当前没有可换的空闲车辆。</div>
                )}
              </div>
              <div className="form-group">
                <label>变更原因</label>
                <input value={reason} onChange={e => setReason(e.target.value)} placeholder="选填，如：原车电量不足" />
              </div>
              <div className="form-actions">
                <button className="btn-sm btn-sm-primary" onClick={submitVehicle} disabled={saving || !newVehicleId}>
                  {saving ? '办理中…' : '确认换车'}
                </button>
              </div>
            </>
          ) : (
            <>
              <div className="form-group">
                <label>新的租期 <span className="req">*</span></label>
                <DateRangeCalendar
                  withTime
                  start={leaseStart}
                  end={leaseEnd}
                  placeholder="选择起租与到期时间"
                  onChange={(s, e) => { setLeaseStart(s); setLeaseEnd(e) }}
                />
              </div>
              <div className="form-group">
                <label>变更原因</label>
                <input value={reason} onChange={e => setReason(e.target.value)} placeholder="选填，如：顾客行程调整" />
              </div>
              <div className="form-actions">
                <button className="btn-sm btn-sm-primary" onClick={submitReschedule} disabled={saving}>
                  {saving ? '办理中…' : '确认改期'}
                </button>
              </div>
            </>
          )}

          <div className="card-title" style={{ marginTop: 18 }}>
            <Icon name="orders" size={16} chip />变更历史
          </div>
          {history.length === 0 ? (
            <div className="field-hint">本单暂无变更记录。</div>
          ) : (
            <div className="table-wrap">
              <table className="data-table nowrap-table">
                <thead>
                  <tr>
                    <th>时间</th><th>类型</th><th>变更内容</th><th className="num">差价</th><th>原因</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map(c => (
                    <tr key={c.changeId}>
                      <td className="cell-time">{fmtDT(c.createTime)}</td>
                      <td>{c.changeType}</td>
                      <td>
                        {c.changeType === '换车'
                          ? `车辆 ${c.oldVehicleId ?? '—'} → ${c.newVehicleId ?? '—'}`
                          : `${fmtDT(c.oldStart)} ~ ${fmtDT(c.oldEnd)} → ${fmtDT(c.newStart)} ~ ${fmtDT(c.newEnd)}`}
                      </td>
                      <td className="num" style={{ color: c.diffAmount < 0 ? 'var(--success-text, #15803d)' : undefined }}>
                        {c.diffAmount === 0 ? '—' : c.diffAmount > 0 ? `+¥${c.diffAmount.toFixed(2)}` : `-¥${(-c.diffAmount).toFixed(2)}`}
                      </td>
                      <td>{c.reason || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
