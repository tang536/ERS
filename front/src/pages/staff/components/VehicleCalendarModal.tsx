import { useCallback, useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { orderApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import DateRangeCalendar from '@/components/DateRangeCalendar'
import { todayInput, shiftDateInput, fmtDT } from '@/utils/datetime'
import { VEHICLE_BADGE } from '@/utils/statusBadges'

interface VehicleCalendarModalProps {
  onClose: () => void
}

interface Slot {
  orderId: number
  orderNo: string
  customerName: string
  leaseStart: string
  leaseEnd: string
  orderStatus: string
}

interface CalendarVehicle {
  vehicleId: number
  vehicleNo: string
  model?: string
  vehicleStatus: string
  slots: Slot[]
}



/**
 * 车辆可用日历：按时间窗查看每台车已有的「待支付 / 已支付 / 租赁中」档期。
 * 柜台开单前先看一眼，避免把同一台车在同一时段卖两次；后端 calendar 接口
 * 已按租期重叠计算，这里只负责展示，不做二次推断。
 */
export default function VehicleCalendarModal({ onClose }: VehicleCalendarModalProps) {
  const { toast } = useUI()
  const [from, setFrom] = useState(todayInput())
  const [to, setTo] = useState(shiftDateInput(todayInput(), 7))
  const [vehicles, setVehicles] = useState<CalendarVehicle[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const res: any = await orderApi.calendar({ from, to })
      setVehicles(res?.data?.vehicles || [])
    } catch (e: any) {
      toast(e?.message || '车辆日历加载失败', 'error')
      setVehicles([])
    } finally {
      setLoading(false)
    }
  }, [from, to, toast])

  useEffect(() => { load() }, [load])

  return createPortal(
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-wide" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>车辆可用日历</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>

        <div className="modal-body">
          <div className="form-row form-row-inline" style={{ gap: 10, alignItems: 'end', flexWrap: 'wrap' }}>
            <label className="field field-grow">
              <span className="field-label">查询日期</span>
              <DateRangeCalendar
                start={from}
                end={to}
                placeholder="选择可用日历区间"
                onChange={(s, e) => { setFrom(s); setTo(e) }}
              />
            </label>
            <button className="btn-sm btn-sm-default btn-icon-sm" onClick={load} disabled={loading}>
              <Icon name="refresh" size={15} className={loading ? 'spin' : ''} /><span className="btn-label">刷新</span>
            </button>
          </div>
          <p className="field-hint">只显示「待支付 / 已支付 / 租赁中」的占用档期，一次最多查询 31 天。</p>

          <div className="table-wrap" style={{ marginTop: 12 }}>
            <table className="data-table">
              <thead>
                <tr><th>车辆</th><th>当前状态</th><th>占用档期</th></tr>
              </thead>
              <tbody>
                {vehicles.map(v => (
                  <tr key={v.vehicleId}>
                    <td>
                      <strong>{v.vehicleNo}</strong>
                      {v.model ? <span className="cell-sub">{v.model}</span> : null}
                    </td>
                    <td><span className={`status-tag ${VEHICLE_BADGE[v.vehicleStatus] || 'status-warning'}`}>{v.vehicleStatus}</span></td>
                    <td>
                      {v.slots.length === 0
                        ? <span className="status-tag status-success">该时段空闲</span>
                        : <div className="calendar-slots">
                            {v.slots.map(s => (
                              <span key={s.orderId} className="calendar-slot" title={`${s.orderNo} · ${s.customerName}`}>
                                <span className="mono">{fmtDT(s.leaseStart)} → {fmtDT(s.leaseEnd)}</span>
                                <span className={`status-tag ${s.orderStatus === '租赁中' ? 'status-primary' : 'status-warning'}`}>{s.orderStatus}</span>
                              </span>
                            ))}
                          </div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {!loading && vehicles.length === 0 && (
            <div className="empty"><Icon name="empty" size={40} chip />
              <div>该时间窗内没有可展示的车辆</div>
            </div>
          )}
        </div>
      </div>
    </div>,
    document.body
  )
}
