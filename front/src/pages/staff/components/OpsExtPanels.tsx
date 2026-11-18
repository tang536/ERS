import { useEffect, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { extApi } from '@/api'
import Icon from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'
import type { Appointment, ReturnRequest, SupportTicket, PricingRule, SparePart, ApiKey } from '@/types'

const T = ({ children }: { children: React.ReactNode }) => <div className="ext-panel-title">{children}</div>

/** 到店预约：订单页顶部入口，只列待确认/今日预约，不单独占菜单。 */
export function AppointmentPanel({ onChanged }: { onChanged?: () => void }) {
  const { toast } = useUI()
  const [rows, setRows] = useState<Appointment[]>([])
  const [loading, setLoading] = useState(true)

  const load = () => {
    setLoading(true)
    extApi.appointments('待确认').then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '预约加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const setStatus = async (id: number, status: string) => {
    try { await extApi.setAppointmentStatus(id, status); toast(`预约已${status}`, 'success'); load(); onChanged?.() }
    catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="clock" size={15} chip />待到店预约<span className="ext-count">{rows.length}</span></T>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? (
        <div className="empty" style={{ padding: '18px 0' }}><Icon name="empty" size={28} chip /><div>暂无待确认预约</div></div>
      ) : (
        <div className="table-wrap">
          <table className="data-table compact-table">
            <thead><tr><th>到店时间</th><th>顾客</th><th>车辆</th><th>电话</th><th>备注</th><th>操作</th></tr></thead>
            <tbody>
              {rows.map(a => (
                <tr key={a.appointmentId}>
                  <td className="cell-time">{fmtDT(a.appointTime)}</td>
                  <td>{a.customerName || '—'}</td>
                  <td>{a.vehicleNo || '—'}</td>
                  <td className="mono">{a.contactPhone || '—'}</td>
                  <td className="cell-note">{a.note || '—'}</td>
                  <td>
                    <div className="btn-group">
                      <button className="btn-sm btn-sm-primary" onClick={() => setStatus(a.appointmentId, '已确认')}>确认</button>
                      <button className="btn-sm btn-sm-default" onClick={() => setStatus(a.appointmentId, '已取消')}>取消</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** 还车预登记：结算页入口，门店按登记时间提前备车、验车。 */
export function ReturnRequestPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<ReturnRequest[]>([])
  const [loading, setLoading] = useState(true)

  const load = () => {
    setLoading(true)
    extApi.returns('待处理').then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '还车登记加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const setStatus = async (id: number, status: string) => {
    try { await extApi.setReturnStatus(id, status); toast('状态已更新', 'success'); load() }
    catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="clock" size={15} chip />还车预登记<span className="ext-count">{rows.length}</span></T>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? (
        <div className="empty" style={{ padding: '18px 0' }}><Icon name="empty" size={28} chip /><div>暂无待处理登记</div></div>
      ) : (
        <div className="table-wrap">
          <table className="data-table compact-table">
            <thead><tr><th>预计到店</th><th>订单</th><th>顾客</th><th>里程</th><th>电量</th><th>备注</th><th>操作</th></tr></thead>
            <tbody>
              {rows.map(r => (
                <tr key={r.requestId}>
                  <td className="cell-time">{fmtDT(r.expectTime)}</td>
                  <td className="mono">{r.orderNo || '—'}</td>
                  <td>{r.customerName || '—'}</td>
                  <td className="num">{r.mileage} km</td>
                  <td className="num">{r.batteryPercent}%</td>
                  <td className="cell-note">{r.note || '—'}</td>
                  <td>
                    <div className="btn-group">
                      <button className="btn-sm btn-sm-primary" onClick={() => setStatus(r.requestId, '已受理')}>受理</button>
                      <button className="btn-sm btn-sm-default" onClick={() => setStatus(r.requestId, '已完成')}>完成</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** 客服工单：运营配置页签，回复即关闭。 */
export function TicketPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<SupportTicket[]>([])
  const [loading, setLoading] = useState(true)
  /** 自定义回复弹窗：不用浏览器原生 prompt，它不能换行、在手机上还会被键盘挡住 */
  const [replyTarget, setReplyTarget] = useState<SupportTicket | null>(null)
  const [replyText, setReplyText] = useState('')
  const [replySaving, setReplySaving] = useState(false)

  const load = () => {
    setLoading(true)
    extApi.tickets().then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '工单加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const openReply = (t: SupportTicket) => {
    setReplyTarget(t)
    setReplyText(t.reply || '')
  }

  const submitReply = async (close: boolean) => {
    if (!replyTarget) return
    const text = replyText.trim()
    if (!text) { toast('请填写回复内容', 'warning'); return }
    setReplySaving(true)
    try {
      await extApi.replyTicket(replyTarget.ticketId, text, close)
      toast(close ? '已回复并关闭工单' : '已回复', 'success')
      setReplyTarget(null)
      setReplyText('')
      load()
    } catch (e: any) { toast(e?.message || '回复失败', 'error') }
    finally { setReplySaving(false) }
  }

  return (
    <div>
      <T><Icon name="info" size={15} chip />客服工单<span className="ext-count">{rows.length}</span></T>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? (
        <div className="empty" style={{ padding: '18px 0' }}><Icon name="empty" size={28} chip /><div>暂无工单</div></div>
      ) : (
        <div className="ext-list">
          {rows.map(t => (
            <div key={t.ticketId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{t.customerName || `顾客 #${t.customerId}`} <span className={`status-tag ${t.status === '待处理' ? 'status-warning' : 'status-success'}`}>{t.status}</span></strong>
                <small>{t.category} · {t.orderNo || '无关联订单'} · {fmtDT(t.createTime)}</small>
                <p>{t.content}</p>
                {t.reply && <p className="ext-reply">回复：{t.reply}</p>}
              </div>
              {t.status !== '已关闭' && (
                <div className="btn-group">
                  <button className="btn-sm btn-sm-primary" onClick={() => openReply(t)}>回复</button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {replyTarget && (
        <div className="modal-overlay" onClick={() => setReplyTarget(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>回复工单 · {replyTarget.customerName || `顾客 #${replyTarget.customerId}`}</span>
              <button className="close-btn" onClick={() => setReplyTarget(null)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="ticket-reply-quote">
                <strong>{replyTarget.category}</strong>
                <p>{replyTarget.content}</p>
              </div>
              <div className="form-group" style={{ marginTop: 12 }}>
                <label>回复内容</label>
                <textarea
                  rows={5}
                  maxLength={500}
                  autoFocus
                  value={replyText}
                  onChange={e => setReplyText(e.target.value)}
                  placeholder="写清处理结果或下一步安排；顾客会在「消息通知」中看到这条回复"
                />
                <div className="field-hint">回复会同步推送到顾客的消息中心，不必让顾客反复回页刷新。</div>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => submitReply(false)} disabled={replySaving}>仅回复</button>
              <button className="btn-sm btn-sm-primary" onClick={() => submitReply(true)} disabled={replySaving}>
                {replySaving ? '提交中…' : '回复并关闭'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/** 动态定价：运营配置页签，只维护规则，不改订单历史价。 */
export function PricingPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<PricingRule[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ ruleName: '', ruleType: '时段溢价', factor: '1.2' })

  const load = () => {
    setLoading(true)
    extApi.pricing().then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '定价规则加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const create = async () => {
    if (!form.ruleName.trim()) { toast('请填写规则名称', 'warning'); return }
    const factor = Number(form.factor)
    if (!Number.isFinite(factor) || factor <= 0) { toast('系数必须大于 0', 'warning'); return }
    try {
      await extApi.createPricing({ ruleName: form.ruleName.trim(), ruleType: form.ruleType, factor, status: '启用' })
      toast('规则已新增', 'success')
      setForm({ ruleName: '', ruleType: '时段溢价', factor: '1.2' })
      load()
    } catch (e: any) { toast(e?.message || '新增失败', 'error') }
  }

  const toggle = async (r: PricingRule) => {
    try { await extApi.setPricingStatus(r.ruleId, r.status === '启用' ? '停用' : '启用'); load() }
    catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="settlements" size={15} chip />动态定价规则<span className="ext-count">{rows.length}</span></T>
      <div className="inline-form">
        <input placeholder="规则名称，如 周末溢价" value={form.ruleName} onChange={e => setForm({ ...form, ruleName: e.target.value })} />
        <select value={form.ruleType} onChange={e => setForm({ ...form, ruleType: e.target.value })}>
          <option>时段溢价</option><option>节假日</option><option>天气</option><option>库存紧张</option>
        </select>
        <input type="number" step="0.01" min="0.01" placeholder="系数" value={form.factor} onChange={e => setForm({ ...form, factor: e.target.value })} />
        <button className="btn-sm btn-sm-primary" onClick={create}>新增</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无规则，默认按车辆基础价计费。</div> : (
        <div className="ext-list">
          {rows.map(r => (
            <div key={r.ruleId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{r.ruleName} <span className="status-tag status-primary">{r.ruleType}</span></strong>
                <small>系数 ×{Number(r.factor).toFixed(2)} · {r.status}{r.activeNow ? ' · 当前生效' : ''}</small>
              </div>
              <button className="btn-sm btn-sm-default" onClick={() => toggle(r)}>{r.status === '启用' ? '停用' : '启用'}</button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 备件库存：维保页签内，低库存高亮，入库即加数。 */
export function SparePartPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<SparePart[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ partName: '', stockQty: '10', lowThreshold: '2', price: '0' })

  const load = () => {
    setLoading(true)
    extApi.parts().then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '备件加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const create = async () => {
    if (!form.partName.trim()) { toast('请填写备件名称', 'warning'); return }
    try {
      await extApi.createPart({ partName: form.partName.trim(), stockQty: Number(form.stockQty || 0), lowThreshold: Number(form.lowThreshold || 0), price: Number(form.price || 0) })
      toast('备件已入库', 'success')
      setForm({ partName: '', stockQty: '10', lowThreshold: '2', price: '0' })
      load()
    } catch (e: any) { toast(e?.message || '入库失败', 'error') }
  }

  const adjust = async (p: SparePart, delta: number) => {
    try { await extApi.updatePart(p.partId, { stockQty: Math.max(0, p.stockQty + delta) }); load() }
    catch (e: any) { toast(e?.message || '库存调整失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="addon" size={15} chip />配件库存<span className="ext-count">{rows.length}</span></T>
      <div className="panel-help">刹车片、轮胎等维修配件的库存；低于预警值会标红。</div>
      <div className="inline-form">
        <input placeholder="配件名称，如 刹车片" value={form.partName} onChange={e => setForm({ ...form, partName: e.target.value })} />
        <input type="number" min="0" placeholder="库存" value={form.stockQty} onChange={e => setForm({ ...form, stockQty: e.target.value })} />
        <input type="number" min="0" placeholder="预警值" value={form.lowThreshold} onChange={e => setForm({ ...form, lowThreshold: e.target.value })} />
        <input type="number" min="0" placeholder="单价" value={form.price} onChange={e => setForm({ ...form, price: e.target.value })} />
        <button className="btn-sm btn-sm-primary" onClick={create}>入库</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无备件记录。</div> : (
        <div className="table-wrap">
          <table className="data-table compact-table">
            <thead><tr><th>备件</th><th>SKU</th><th className="num">库存</th><th className="num">预警值</th><th className="num">单价</th><th>操作</th></tr></thead>
            <tbody>
              {rows.map(p => (
                <tr key={p.partId} className={p.lowStock ? 'row-warning' : ''}>
                  <td>{p.partName}{p.lowStock && <span className="status-tag status-warning" style={{ marginLeft: 6 }}>低库存</span>}</td>
                  <td className="mono">{p.sku || '—'}</td>
                  <td className="num">{p.stockQty}</td>
                  <td className="num">{p.lowThreshold}</td>
                  <td className="num">¥{Number(p.price).toFixed(2)}</td>
                  <td><div className="btn-group"><button className="btn-sm btn-sm-default" onClick={() => adjust(p, 1)}>+1</button><button className="btn-sm btn-sm-default" onClick={() => adjust(p, -1)}>-1</button></div></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** 开放 API：系统配置页，生成后只显示一次明文 Key。 */
export function ApiKeyPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<ApiKey[]>([])
  const [created, setCreated] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [loading, setLoading] = useState(true)

  const load = () => {
    setLoading(true)
    extApi.apiKeys().then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || 'API Key 加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const create = async () => {
    if (!name.trim()) { toast('请填写用途名称', 'warning'); return }
    try {
      const res: any = await extApi.createApiKey(name.trim())
      setCreated(res.data?.key || null)
      setName('')
      load()
    } catch (e: any) { toast(e?.message || '生成失败', 'error') }
  }

  const revoke = async (k: ApiKey) => {
    try { await extApi.revokeApiKey(k.keyId); toast('已吊销', 'success'); load() }
    catch (e: any) { toast(e?.message || '吊销失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="shield" size={15} chip />开放 API Key<span className="ext-count">{rows.length}</span></T>
      <div className="inline-form">
        <input placeholder="用途，如 第三方报表" value={name} onChange={e => setName(e.target.value)} />
        <button className="btn-sm btn-sm-primary" onClick={create}>生成 Key</button>
      </div>
      <div className="field-hint" style={{ marginBottom: 8 }}>只读接口 <code>GET /api/open/summary</code>，请求头带 <code>X-Api-Key</code>；泄露后立即吊销。</div>
      {created && (
        <div className="callout callout-warning" style={{ marginTop: 8 }}>
          <div><strong>新 Key 只显示一次</strong><p className="mono" style={{ wordBreak: 'break-all' }}>{created}</p></div>
          <button className="btn-sm btn-sm-default" onClick={() => { navigator.clipboard?.writeText(created); toast('已复制', 'success') }}>复制</button>
        </div>
      )}
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无 API Key。</div> : (
        <div className="ext-list">
          {rows.map(k => (
            <div key={k.keyId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{k.name} <span className="status-tag status-primary">{k.prefix}…</span></strong>
                <small>{k.status} · 最近使用 {k.lastUsed ? fmtDT(k.lastUsed) : '从未'}</small>
              </div>
              {k.status === '启用' && <button className="btn-sm btn-sm-danger" onClick={() => revoke(k)}>吊销</button>}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
