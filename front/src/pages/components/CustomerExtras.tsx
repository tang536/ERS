import { useEffect, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { myExtApi } from '@/api'
import Icon from '@/components/Icon'
import { nowDatetimeInput, afterDaysDatetimeInput } from '@/utils/datetime'
import type { Order, RentableVehicle } from '@/types'

/** 顾客还车预登记：只登记预计到店时间和表显数据，实际结算仍由门店验车。 */
export function ReturnRequestModal({ order, onClose, onDone }: { order: Order; onClose: () => void; onDone?: () => void }) {
  const { toast } = useUI()
  const [form, setForm] = useState({
    expectTime: afterDaysDatetimeInput(1),
    mileage: '',
    batteryPercent: '50',
    note: '',
  })
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    if (!form.expectTime) { toast('请选择预计到店时间', 'warning'); return }
    setSaving(true)
    try {
      await myExtApi.createReturn({
        orderId: order.orderId,
        expectTime: form.expectTime,
        mileage: Number(form.mileage || 0),
        batteryPercent: Number(form.batteryPercent || 0),
        note: form.note.trim() || null,
      })
      toast('还车登记已提交，请按预约时间到店', 'success')
      onDone?.()
      onClose()
    } catch (e: any) { toast(e?.message || '还车登记失败', 'error') }
    finally { setSaving(false) }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>还车预登记 · {order.orderNo}</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          <div className="form-row">
            <div className="form-group">
              <label>预计到店时间</label>
              <input type="datetime-local" value={form.expectTime} onChange={e => setForm({ ...form, expectTime: e.target.value })} />
            </div>
            <div className="form-group">
              <label>表显里程（km）</label>
              <input type="number" min="0" value={form.mileage} onChange={e => setForm({ ...form, mileage: e.target.value })} placeholder="选填" />
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>剩余电量（%）</label>
              <input type="number" min="0" max="100" value={form.batteryPercent} onChange={e => setForm({ ...form, batteryPercent: e.target.value })} />
            </div>
            <div className="form-group">
              <label>备注</label>
              <input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} placeholder="如车辆异常、随车物品" />
            </div>
          </div>
          <div className="field-hint">登记只用于门店提前准备，实际租金、逾期费和损坏费以到店验车结算为准。</div>
        </div>
        <div className="modal-footer">
          <button className="btn-sm btn-sm-default" onClick={onClose}>取消</button>
          <button className="btn-sm btn-sm-primary" onClick={submit} disabled={saving}>{saving ? '提交中…' : '提交登记'}</button>
        </div>
      </div>
    </div>
  )
}

/** 订单评价：完结订单可评一次，5 星 + 文字。 */
export function ReviewModal({ order, onClose, onDone }: { order: Order; onClose: () => void; onDone?: () => void }) {
  const { toast } = useUI()
  const [rating, setRating] = useState(5)
  const [content, setContent] = useState('')
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setSaving(true)
    try {
      await myExtApi.createReview({ orderId: order.orderId, rating, content: content.trim() || null })
      toast('评价已提交，感谢反馈', 'success')
      onDone?.()
      onClose()
    } catch (e: any) { toast(e?.message || '评价提交失败', 'error') }
    finally { setSaving(false) }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>评价订单 · {order.orderNo}</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          <div className="form-group">
            <label>本次体验</label>
            <div className="rating-row">
              {[1, 2, 3, 4, 5].map(n => (
                <button key={n} type="button" className={`rating-star ${rating >= n ? 'active' : ''}`} onClick={() => setRating(n)} aria-label={`${n} 星`}>★</button>
              ))}
              <span className="field-hint" style={{ marginLeft: 8 }}>{rating} 星</span>
            </div>
          </div>
          <div className="form-group">
            <label>评价内容</label>
            <textarea rows={4} maxLength={500} value={content} onChange={e => setContent(e.target.value)} placeholder="车辆状况、门店服务、还车体验…" />
          </div>
        </div>
        <div className="modal-footer">
          <button className="btn-sm btn-sm-default" onClick={onClose}>取消</button>
          <button className="btn-sm btn-sm-primary" onClick={submit} disabled={saving}>{saving ? '提交中…' : '提交评价'}</button>
        </div>
      </div>
    </div>
  )
}

/** 客服工单：顾客提交问题，员工在运营配置的客服页签回复。 */
export function TicketModal({ order, onClose, onDone }: { order?: Order | null; onClose: () => void; onDone?: () => void }) {
  const { toast } = useUI()
  const [form, setForm] = useState({ category: '租车咨询', content: '' })
  const [saving, setSaving] = useState(false)
  /** 历史工单（含客服回复）：顾客不必再凭记忆判断“客服到底回没回” */
  const [items, setItems] = useState<any[]>([])

  const loadTickets = () => {
    myExtApi.tickets()
      .then((res: any) => setItems(res.data || []))
      .catch(() => { /* 历史加载失败不影响新建工单 */ })
  }

  useEffect(() => { loadTickets() }, [])

  const submit = async () => {
    if (!form.content.trim()) { toast('请描述要咨询的问题', 'warning'); return }
    setSaving(true)
    try {
      await myExtApi.createTicket({ orderId: order?.orderId ?? null, category: form.category, content: form.content.trim() })
      toast('已发送给客服，回复会出现在消息通知', 'success')
      setForm({ ...form, content: '' })
      loadTickets()
      onDone?.()
    } catch (e: any) { toast(e?.message || '发送失败', 'error') }
    finally { setSaving(false) }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-lg" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>联系客服{order ? ` · ${order.orderNo}` : ''}</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          {items.length > 0 && (
            <div className="ticket-history">
              <div className="ticket-history-title">历史消息与回复</div>
              {items.slice(0, 8).map(t => (
                <div className="ticket-history-item" key={t.ticketId}>
                  <div className="ticket-history-head">
                    <strong>{t.category}</strong>
                    <span className={`status-tag ${t.status === '已关闭' ? 'status-default' : t.status === '已回复' ? 'status-success' : 'status-warning'}`}>{t.status}</span>
                  </div>
                  <p className="ticket-history-q">{t.content}</p>
                  {t.reply ? (
                    <div className="ticket-history-reply">
                      <span>客服回复</span>
                      <p>{t.reply}</p>
                    </div>
                  ) : <div className="ticket-history-wait">等待客服回复</div>}
                </div>
              ))}
            </div>
          )}

          <div className="form-group">
            <label>问题类型</label>
            <div className="choice-chips">
              {['租车咨询', '还车结算', '退款问题', '车辆故障', '其他'].map(c => (
                <button
                  type="button"
                  key={c}
                  className={`choice-chip ${form.category === c ? 'is-active' : ''}`}
                  onClick={() => setForm({ ...form, category: c })}
                >{c}</button>
              ))}
            </div>
          </div>
          <div className="form-group">
            <label>问题描述</label>
            <textarea rows={4} maxLength={500} value={form.content} onChange={e => setForm({ ...form, content: e.target.value })} placeholder="请描述具体情况，便于客服快速定位" />
          </div>
        </div>
        <div className="modal-footer ticket-modal-footer">
          <span className="field-hint">客服回复后会同步到「消息通知」</span>
          <button className="btn-sm btn-sm-primary" onClick={submit} disabled={saving}>{saving ? '发送中…' : '发送给客服'}</button>
        </div>
      </div>
    </div>
  )
}

/** 到店预约：车辆详情页只填时间/电话，车辆和门店由页面带出。 */
export function AppointmentModal({ vehicle, storeId, onClose, onDone }: {
  vehicle: RentableVehicle
  storeId?: number | null
  onClose: () => void
  onDone?: () => void
}) {
  const { toast } = useUI()
  const [form, setForm] = useState({
    appointTime: afterDaysDatetimeInput(1),
    phone: '',
    note: '',
  })
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    if (!form.appointTime) { toast('请选择到店时间', 'warning'); return }
    setSaving(true)
    try {
      await myExtApi.createAppointment({
        vehicleId: vehicle.vehicleId,
        appointTime: form.appointTime,
        storeId: storeId ?? null,
        contactPhone: form.phone.trim() || null,
        note: form.note.trim() || null,
      })
      toast('预约已提交，门店会尽快确认', 'success')
      onDone?.()
      onClose()
    } catch (e: any) { toast(e?.message || '预约提交失败', 'error') }
    finally { setSaving(false) }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>预约到店 · {vehicle.vehicleNo}</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          <div className="form-row">
            <div className="form-group">
              <label>到店时间</label>
              <input type="datetime-local" value={form.appointTime} onChange={e => setForm({ ...form, appointTime: e.target.value })} />
            </div>
            <div className="form-group">
              <label>联系电话</label>
              <input value={form.phone} onChange={e => setForm({ ...form, phone: e.target.value })} placeholder="便于门店确认" />
            </div>
          </div>
          <div className="form-group">
            <label>备注</label>
            <input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} placeholder="如想试骑、需要头盔" />
          </div>
          <div className="field-hint">预约不锁定车辆和价格；到店后按当时库存与计价规则开单。</div>
        </div>
        <div className="modal-footer">
          <button className="btn-sm btn-sm-default" onClick={onClose}>取消</button>
          <button className="btn-sm btn-sm-primary" onClick={submit} disabled={saving}>{saving ? '提交中…' : '提交预约'}</button>
        </div>
      </div>
    </div>
  )
}
