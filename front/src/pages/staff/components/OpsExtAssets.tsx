import { useEffect, useMemo, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { extApi, vehicleApi, couponApi, settleApi, supplementApi, reportApi } from '@/api'
import { useNavigate } from 'react-router-dom'
import Icon from '@/components/Icon'
import DateRangeCalendar from '@/components/DateRangeCalendar'
import { fmtDT } from '@/utils/datetime'
import type { Store, Battery, Insurance, Accident, VehicleCost, GpsDevice, CouponCampaign, GroupVoucher, NotificationOutbox, DamageAssessment, StaffPerformance, Forecast } from '@/types'

const T = ({ children }: { children: React.ReactNode }) => <div className="ext-panel-title">{children}</div>
const today = () => new Date().toISOString().slice(0, 10)
/** 本地 datetime-local 字符串，避免 toISOString 的 UTC 偏移 */
const dtLocal = (d: Date) => {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 门店与网点：系统配置里的轻量区块，车辆按门店归集，不新开顶级菜单。 */
export function StorePanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<Store[]>([])
  const [vehicles, setVehicles] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ storeName: '', address: '', phone: '' })
  const [bind, setBind] = useState({ storeId: '', vehicleId: '' })

  const load = () => {
    setLoading(true)
    Promise.all([extApi.stores(), vehicleApi.page({ page: 1, pageSize: 200 })])
      .then(([s, v]: any[]) => { setRows(s.data || []); setVehicles(v.data?.items || []) })
      .catch((e: any) => toast(e?.message || '门店加载失败', 'error'))
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const create = async () => {
    if (!form.storeName.trim()) { toast('请填写门店名称', 'warning'); return }
    try {
      await extApi.createStore({ ...form, status: '启用' })
      toast('门店已新增', 'success')
      setForm({ storeName: '', address: '', phone: '' })
      load()
    } catch (e: any) { toast(e?.message || '新增失败', 'error') }
  }

  const doBind = async () => {
    if (!bind.storeId || !bind.vehicleId) { toast('请选择门店和车辆', 'warning'); return }
    try {
      await extApi.bindVehicleStore(Number(bind.storeId), Number(bind.vehicleId))
      toast('车辆已归入门店', 'success')
      load()
    } catch (e: any) { toast(e?.message || '绑定失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="config" size={15} chip />门店与网点<span className="ext-count">{rows.length}</span></T>
      <div className="inline-form">
        <input placeholder="门店名称，如 西乡塘旗舰店" value={form.storeName} onChange={e => setForm({ ...form, storeName: e.target.value })} />
        <input placeholder="地址（选填）" value={form.address} onChange={e => setForm({ ...form, address: e.target.value })} />
        <input placeholder="电话（选填）" value={form.phone} onChange={e => setForm({ ...form, phone: e.target.value })} />
        <button className="btn-sm btn-sm-primary" onClick={create}>新增门店</button>
      </div>
      <div className="inline-form">
        <select value={bind.storeId} onChange={e => setBind({ ...bind, storeId: e.target.value })}>
          <option value="">选择门店</option>
          {rows.map(s => <option key={s.storeId} value={s.storeId}>{s.storeName}</option>)}
        </select>
        <select value={bind.vehicleId} onChange={e => setBind({ ...bind, vehicleId: e.target.value })}>
          <option value="">选择车辆</option>
          {vehicles.map((v: any) => <option key={v.vehicleId} value={v.vehicleId}>{v.vehicleNo} · {v.brand}</option>)}
        </select>
        <button className="btn-sm btn-sm-default" onClick={doBind}>归入门店</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无门店，系统按单店模式运行。</div> : (
        <div className="ext-list">
          {rows.map(s => (
            <div key={s.storeId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{s.storeName} <span className={`status-tag ${s.status === '启用' ? 'status-success' : 'status-danger'}`}>{s.status}</span></strong>
                <small>{s.address || '未填地址'} · {s.phone || '未填电话'} · {s.vehicleCount} 台车</small>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 车辆资产档案：电池/保险/GPS/事故/成本，落在维保页签内，切子页不新增页面。 */
export function VehicleAssetPanel() {
  const { toast } = useUI()
  const [vehicles, setVehicles] = useState<any[]>([])
  const [vehicleId, setVehicleId] = useState('')
  const [tab, setTab] = useState<'battery' | 'insurance' | 'gps' | 'accident' | 'cost'>('battery')
  const [rows, setRows] = useState<Array<Battery | Insurance | GpsDevice | Accident | VehicleCost>>([])
  const [loading, setLoading] = useState(false)
  const [form, setForm] = useState<Record<string, string>>({})

  useEffect(() => {
    vehicleApi.page({ page: 1, pageSize: 200 }).then((r: any) => setVehicles(r.data?.items || [])).catch(() => {})
  }, [])

  const load = () => {
    setLoading(true)
    const call = tab === 'battery' ? extApi.batteries() : tab === 'insurance' ? extApi.insurance() : tab === 'gps' ? extApi.gps() : tab === 'accident' ? extApi.accidents() : extApi.vehicleCosts()
    call.then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '资产档案加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [tab])

  const filtered = useMemo(() => vehicleId ? rows.filter((r: any) => String(r.vehicleId) === vehicleId) : rows, [rows, vehicleId])

  const create = async () => {
    if (!vehicleId) { toast('请先选择车辆', 'warning'); return }
    const base = { vehicleId: Number(vehicleId) }
    try {
      if (tab === 'battery') {
        if (!form.serialNo?.trim()) { toast('请填写电池编号', 'warning'); return }
        await extApi.createBattery({ ...base, serialNo: form.serialNo.trim(), healthPercent: Number(form.healthPercent || 100), cycleCount: Number(form.cycleCount || 0) })
      } else if (tab === 'insurance') {
        if (!form.insurer?.trim() || !form.policyNo?.trim()) { toast('请填写保险公司与保单号', 'warning'); return }
        await extApi.createInsurance({ ...base, insurer: form.insurer.trim(), policyNo: form.policyNo.trim(), startDate: form.startDate || today(), endDate: form.endDate || today(), premium: Number(form.premium || 0) })
      } else if (tab === 'gps') {
        if (!form.imei?.trim()) { toast('请填写设备 IMEI', 'warning'); return }
        await extApi.createGps({ ...base, imei: form.imei.trim(), simNo: form.simNo || null })
      } else if (tab === 'accident') {
        if (!form.description?.trim()) { toast('请填写事故经过', 'warning'); return }
        await extApi.createAccident({ ...base, accidentTime: form.accidentTime || new Date().toISOString(), description: form.description.trim(), lossAmount: Number(form.lossAmount || 0), claimAmount: Number(form.claimAmount || 0) })
      } else {
        await extApi.createVehicleCost({ ...base, costType: form.costType || '其他', amount: Number(form.amount || 0), occurDate: form.occurDate || today(), note: form.note || null })
      }
      toast('档案已保存', 'success')
      setForm({})
      load()
    } catch (e: any) { toast(e?.message || '保存失败', 'error') }
  }

  const tabs = [['battery', '电池'], ['insurance', '保险'], ['gps', 'GPS'], ['accident', '事故'], ['cost', '成本']] as const

  return (
    <div>
      <T><Icon name="vehicles" size={15} chip />车辆档案<span className="ext-count">{filtered.length}</span></T>
      <div className="panel-help">电池、保险、GPS、事故与费用记录，用于判断车辆是否继续出租。</div>
      <div className="inline-form">
        <select value={vehicleId} onChange={e => setVehicleId(e.target.value)}>
          <option value="">全部车辆</option>
          {vehicles.map((v: any) => <option key={v.vehicleId} value={v.vehicleId}>{v.vehicleNo} · {v.brand}</option>)}
        </select>
        <div className="btn-group">
          {tabs.map(([k, label]) => <button key={k} className={`btn-sm ${tab === k ? 'btn-sm-primary' : 'btn-sm-default'}`} onClick={() => setTab(k)}>{label}</button>)}
        </div>
      </div>
      <div className="inline-form">
        {tab === 'battery' && <>
          <input placeholder="电池编号" value={form.serialNo || ''} onChange={e => setForm({ ...form, serialNo: e.target.value })} />
          <input type="number" min="0" max="100" placeholder="健康度 %" value={form.healthPercent || '100'} onChange={e => setForm({ ...form, healthPercent: e.target.value })} />
          <input type="number" min="0" placeholder="循环次数" value={form.cycleCount || '0'} onChange={e => setForm({ ...form, cycleCount: e.target.value })} />
        </>}
        {tab === 'insurance' && <>
          <input placeholder="保险公司" value={form.insurer || ''} onChange={e => setForm({ ...form, insurer: e.target.value })} />
          <input placeholder="保单号" value={form.policyNo || ''} onChange={e => setForm({ ...form, policyNo: e.target.value })} />
          <DateRangeCalendar
            start={form.startDate || ''}
            end={form.endDate || ''}
            placeholder="保险期间"
            onChange={(s, e) => setForm({ ...form, startDate: s, endDate: e })}
          />
          <input type="number" min="0" placeholder="保费" value={form.premium || '0'} onChange={e => setForm({ ...form, premium: e.target.value })} />
        </>}
        {tab === 'gps' && <>
          <input placeholder="设备 IMEI" value={form.imei || ''} onChange={e => setForm({ ...form, imei: e.target.value })} />
          <input placeholder="SIM 卡号（选填）" value={form.simNo || ''} onChange={e => setForm({ ...form, simNo: e.target.value })} />
        </>}
        {tab === 'accident' && <>
          <input type="datetime-local" value={form.accidentTime || ''} onChange={e => setForm({ ...form, accidentTime: e.target.value })} />
          <input placeholder="事故经过" value={form.description || ''} onChange={e => setForm({ ...form, description: e.target.value })} />
          <input type="number" min="0" placeholder="损失金额" value={form.lossAmount || '0'} onChange={e => setForm({ ...form, lossAmount: e.target.value })} />
          <input type="number" min="0" placeholder="保险赔付" value={form.claimAmount || '0'} onChange={e => setForm({ ...form, claimAmount: e.target.value })} />
        </>}
        {tab === 'cost' && <>
          <input placeholder="成本类型，如 上牌" value={form.costType || ''} onChange={e => setForm({ ...form, costType: e.target.value })} />
          <input type="number" min="0" placeholder="金额" value={form.amount || '0'} onChange={e => setForm({ ...form, amount: e.target.value })} />
          <input type="date" value={form.occurDate || today()} onChange={e => setForm({ ...form, occurDate: e.target.value })} />
          <input placeholder="备注（选填）" value={form.note || ''} onChange={e => setForm({ ...form, note: e.target.value })} />
        </>}
        <button className="btn-sm btn-sm-primary" onClick={create}>新增</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : filtered.length === 0 ? <div className="field-hint">暂无{tab === 'battery' ? '电池' : tab === 'insurance' ? '保单' : tab === 'gps' ? 'GPS 设备' : tab === 'accident' ? '事故' : '成本'}记录。</div> : (
        <div className="ext-list">
          {filtered.map((r: any) => (
            <div key={r.batteryId || r.policyId || r.deviceId || r.accidentId || r.costId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{r.vehicleNo || '车辆'} · {
                  tab === 'battery' ? `${r.serialNo} ${r.lowHealth ? '· 健康度偏低' : ''}`
                    : tab === 'insurance' ? `${r.insurer} ${r.policyNo}`
                      : tab === 'gps' ? r.imei
                        : tab === 'accident' ? r.description
                          : `${r.costType} ¥${Number(r.amount).toFixed(2)}`
                }</strong>
                <small>{
                  tab === 'battery' ? `健康度 ${r.healthPercent}% · 循环 ${r.cycleCount} 次 · ${r.status}`
                    : tab === 'insurance' ? `保费 ¥${Number(r.premium).toFixed(2)} · ${r.status}${r.expiring ? ` · 剩 ${r.daysLeft} 天` : ''}`
                      : tab === 'gps' ? `SIM ${r.simNo || '—'} · ${r.status} · ${r.lastReport ? fmtDT(r.lastReport) : '未上报'}`
                        : tab === 'accident' ? `${fmtDT(r.accidentTime)} · 损失 ¥${Number(r.lossAmount).toFixed(2)} · 赔付 ¥${Number(r.claimAmount).toFixed(2)} · ${r.status}`
                          : `${r.occurDate?.slice(0, 10)} · ${r.note || '无备注'}`
                }</small>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 券活动：优惠券页签内的自动投放计划，不另开活动中心。 */
export function CouponCampaignPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<CouponCampaign[]>([])
  const [coupons, setCoupons] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ couponId: '', campaignName: '', startTime: dtLocal(new Date()), endTime: dtLocal(new Date(Date.now() + 30 * 86400000)), totalQuota: '100' })

  const load = () => {
    setLoading(true)
    Promise.all([extApi.campaigns(), couponApi.list()])
      .then(([c, cp]: any[]) => { setRows(c.data || []); setCoupons(cp.data || []) })
      .catch((e: any) => toast(e?.message || '券活动加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const create = async () => {
    if (!form.couponId || !form.campaignName.trim() || !form.startTime || !form.endTime) { toast('请填写券模板、活动名称和起止时间', 'warning'); return }
    try {
      await extApi.createCampaign({ couponId: Number(form.couponId), campaignName: form.campaignName.trim(), startTime: form.startTime, endTime: form.endTime, totalQuota: Number(form.totalQuota || 0) })
      toast('券活动已创建', 'success')
      setForm({ couponId: '', campaignName: '', startTime: dtLocal(new Date()), endTime: dtLocal(new Date(Date.now() + 30 * 86400000)), totalQuota: '100' })
      load()
    } catch (e: any) { toast(e?.message || '创建失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="coupon" size={15} chip />券活动<span className="ext-count">{rows.length}</span></T>
      <div className="inline-form">
        <select value={form.couponId} onChange={e => setForm({ ...form, couponId: e.target.value })}>
          <option value="">选择券模板</option>
          {coupons.map((c: any) => <option key={c.couponId} value={c.couponId}>{c.name}</option>)}
        </select>
        <input placeholder="活动名称，如 生日礼" value={form.campaignName} onChange={e => setForm({ ...form, campaignName: e.target.value })} />
        <DateRangeCalendar
          withTime
          start={form.startTime || ''}
          end={form.endTime || ''}
          placeholder="活动时间"
          onChange={(s, e) => setForm({ ...form, startTime: s, endTime: e })}
        />
        <input type="number" min="0" placeholder="名额" value={form.totalQuota} onChange={e => setForm({ ...form, totalQuota: e.target.value })} />
        <button className="btn-sm btn-sm-primary" onClick={create}>新增</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无券活动。适合做生日券、会员日、沉睡唤醒的自动投放。</div> : (
        <div className="ext-list">
          {rows.map(c => (
            <div key={c.campaignId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{c.campaignName} <span className="status-tag status-primary">{c.couponName || '券'}</span></strong>
                <small>{fmtDT(c.startTime)} ~ {fmtDT(c.endTime)} · 已发 {c.issuedQty}/{c.totalQuota} · {c.status}</small>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 团购券：优惠券页签内，线下核销码入账，不再分散到另一个系统。 */
export function GroupVoucherPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<GroupVoucher[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ code: '', title: '', amount: '', validFrom: today(), validTo: dtLocal(new Date(Date.now() + 30 * 86400000)).slice(0, 10) })
  const [redeemCode, setRedeemCode] = useState('')

  const load = () => {
    setLoading(true)
    extApi.vouchers().then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '团购券加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const create = async () => {
    if (!form.code.trim() || !form.title.trim() || !form.amount) { toast('请填写券码、名称和金额', 'warning'); return }
    try {
      await extApi.createVoucher({ code: form.code.trim(), title: form.title.trim(), amount: Number(form.amount), validFrom: form.validFrom, validTo: form.validTo })
      toast('团购券已录入', 'success')
      setForm({ code: '', title: '', amount: '', validFrom: today(), validTo: dtLocal(new Date(Date.now() + 30 * 86400000)).slice(0, 10) })
      load()
    } catch (e: any) { toast(e?.message || '录入失败', 'error') }
  }

  const redeem = async () => {
    if (!redeemCode.trim()) { toast('请输入券码', 'warning'); return }
    try { await extApi.redeemVoucher(redeemCode.trim()); toast('核销成功', 'success'); setRedeemCode(''); load() }
    catch (e: any) { toast(e?.message || '核销失败', 'error') }
  }

  return (
    <div>
      <T><Icon name="scan" size={15} chip />团购券核销<span className="ext-count">{rows.length}</span></T>
      <div className="inline-form">
        <input placeholder="团购券码" value={redeemCode} onChange={e => setRedeemCode(e.target.value)} />
        <button className="btn-sm btn-sm-primary" onClick={redeem}>核销</button>
      </div>
      <div className="inline-form">
        <input placeholder="券码，如 MT2026" value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} />
        <input placeholder="券名称" value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} />
        <input type="number" min="0" placeholder="面额" value={form.amount} onChange={e => setForm({ ...form, amount: e.target.value })} />
        <DateRangeCalendar
          start={form.validFrom || ''}
          end={form.validTo || ''}
          placeholder="有效期间"
          onChange={(s, e) => setForm({ ...form, validFrom: s, validTo: e })}
        />
        <button className="btn-sm btn-sm-default" onClick={create}>录入</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无团购券。美团/抖音券可先录入券码再核销。</div> : (
        <div className="ext-list">
          {rows.map(v => (
            <div key={v.voucherId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{v.title} <span className="status-tag status-primary">{v.code}</span></strong>
                <small>¥{Number(v.amount).toFixed(2)} · {fmtDT(v.validFrom)} ~ {fmtDT(v.validTo)} · {v.expired ? '已过期' : v.status}</small>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 站内信/外部通道出站队列：系统配置 API 页签内，可人工补发。 */
export function NotificationOutboxPanel() {
  const { toast } = useUI()
  const [rows, setRows] = useState<NotificationOutbox[]>([])
  const [loading, setLoading] = useState(true)

  const load = () => {
    setLoading(true)
    extApi.notifications().then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '通知队列加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const mark = async (id: number) => {
    try { await extApi.markNotificationSent(id); toast('已标记发送', 'success'); load() }
    catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  return (
    <div style={{ marginTop: 18 }}>
      <T><Icon name="bell" size={15} chip />通知出站队列<span className="ext-count">{rows.length}</span></T>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无待发通知。订单创建、到期提醒等事件会进入这里。</div> : (
        <div className="ext-list">
          {rows.map(n => (
            <div key={n.notifyId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{n.channel} · {n.template} <span className={`status-tag ${n.status === '已发送' ? 'status-success' : 'status-warning'}`}>{n.status}</span></strong>
                <small>{n.content} · {n.receiverName || `#${n.receiverId}`} · {fmtDT(n.createTime)}</small>
              </div>
              {n.status !== '已发送' && <button className="btn-sm btn-sm-default" onClick={() => mark(n.notifyId)}>标记已发</button>}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 智能定损：订单详情内，按严重度给出估价并留档；不替代人工复核。 */
export function DamagePanel({ orderId, onChanged }: { orderId: number; onChanged?: (amount: number) => void }) {
  const { toast } = useUI()
  const [rows, setRows] = useState<DamageAssessment[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ severity: '2', findings: '' })

  const load = () => {
    setLoading(true)
    extApi.damage(orderId).then((r: any) => setRows(r.data || [])).catch((e: any) => toast(e?.message || '定损记录加载失败', 'error')).finally(() => setLoading(false))
  }
  useEffect(load, [orderId])

  const create = async () => {
    try {
      const res: any = await extApi.createDamage({ orderId, severity: Number(form.severity), findings: form.findings || null })
      const amount = Number(res.data?.estimatedCost || 0)
      toast(`已生成定损建议 ¥${amount.toFixed(2)}，请人工复核`, 'success')
      setForm({ severity: '2', findings: '' })
      load()
      onChanged?.(amount)
    } catch (e: any) { toast(e?.message || '定损失败', 'error') }
  }

  return (
    <div style={{ marginTop: 12 }}>
      <T><Icon name="warning" size={15} chip />智能定损<span className="ext-count">{rows.length}</span></T>
      <div className="inline-form">
        <select value={form.severity} onChange={e => setForm({ ...form, severity: e.target.value })}>
          {[1, 2, 3, 4, 5].map(n => <option key={n} value={n}>{n} 级</option>)}
        </select>
        <input placeholder="发现的问题（选填）" value={form.findings} onChange={e => setForm({ ...form, findings: e.target.value })} />
        <button className="btn-sm btn-sm-primary" onClick={create}>生成估价</button>
      </div>
      {loading ? <div className="loading-line">加载中…</div> : rows.length === 0 ? <div className="field-hint">暂无定损记录。系统按严重度估价，最终金额以人工复核为准。</div> : (
        <div className="ext-list">
          {rows.map(d => (
            <div key={d.assessmentId} className="ext-list-row">
              <div className="ext-list-main">
                <strong>{d.severity} 级 · 建议 ¥{Number(d.estimatedCost).toFixed(2)} <span className="status-tag status-primary">{d.status}</span></strong>
                <small>{d.findings} · {fmtDT(d.createTime)}</small>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** 资金风险：押金应退、补缴待收、对账异常三行，放看板次区，不新开财务中心。 */
export function FinanceWatchCard() {
  const navigate = useNavigate()
  const [rows, setRows] = useState({ refund: 0, refundCount: 0, bills: 0, billAmount: 0, issues: 0 })
  useEffect(() => {
    const to = new Date()
    const from = new Date(Date.now() - 29 * 86400000)
    const d = (x: Date) => x.toISOString().slice(0, 10)
    Promise.all([
      settleApi.list({ from: d(from), to: d(to), page: 1, pageSize: 200 }).catch(() => null),
      supplementApi.bills().catch(() => null),
      reportApi.reconcile(d(from), d(to)).catch(() => null),
    ]).then(([s, b, rc]: any[]) => {
      const items = s?.data?.items || s?.items || []
      const refundRows = items.filter((x: any) => Number(x.refundAmount || 0) > 0)
      const bills = b?.data || b?.items || []
      setRows({
        refund: refundRows.reduce((n: number, x: any) => n + Number(x.refundAmount || 0), 0),
        refundCount: refundRows.length,
        bills: bills.length,
        billAmount: bills.reduce((n: number, x: any) => n + Math.max(0, Number(x.amount || 0) - Number(x.paidAmount || 0)), 0),
        issues: (rc?.data?.issues || rc?.issues || []).length,
      })
    })
  }, [])
  const line = 'finance-watch-row'
  return (
    <div className="card">
      <div className="card-title"><Icon name="settlements" size={16} chip />资金与对账<span className="card-title-count">近 30 天</span></div>
      <div className="finance-watch">
        <button className={line} onClick={() => navigate('/settlements')}>
          <span>近 30 天应退</span><strong>¥{rows.refund.toFixed(2)}</strong><small>{rows.refundCount} 张结算单累计</small>
        </button>
        <button className={line} onClick={() => navigate('/settlements')}>
          <span>待收补缴</span><strong>¥{rows.billAmount.toFixed(2)}</strong><small>{rows.bills} 笔未收清</small>
        </button>
        <button className={line} onClick={() => navigate('/reports')}>
          <span>对账异常</span><strong>{rows.issues}</strong><small>{rows.issues > 0 ? '需要核对流水' : '账目平衡'}</small>
        </button>
      </div>
    </div>
  )
}

/** 企业月结 / 信用免押档案：客户管理里的一个编辑弹窗，不新增顶级页面。 */
export function CustomerCreditModal({ customerId, customerName, onClose }: { customerId: number; customerName: string; onClose: () => void }) {
  const { toast } = useUI()
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ companyName: '', taxNo: '', creditLimit: '0', realNameStatus: '未认证', idCardNo: '', depositWaiverLimit: '0' })

  useEffect(() => {
    setLoading(true)
    extApi.credit(customerId).then((r: any) => {
      const d = r.data || {}
      setForm({
        companyName: d.companyName || '', taxNo: d.taxNo || '',
        creditLimit: String(d.creditLimit ?? 0), realNameStatus: d.realNameStatus || '未认证',
        idCardNo: d.idCardNo || '', depositWaiverLimit: String(d.depositWaiverLimit ?? 0),
      })
    }).catch((e: any) => toast(e?.message || '信用档案加载失败', 'error')).finally(() => setLoading(false))
  }, [customerId])

  const save = async () => {
    try {
      await extApi.saveCredit(customerId, {
        companyName: form.companyName || null, taxNo: form.taxNo || null,
        creditLimit: Number(form.creditLimit || 0), realNameStatus: form.realNameStatus,
        idCardNo: form.idCardNo || null, depositWaiverLimit: Number(form.depositWaiverLimit || 0),
      })
      toast('信用与企业档案已保存', 'success')
      onClose()
    } catch (e: any) { toast(e?.message || '保存失败', 'error') }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header"><span>企业月结 / 信用免押 · {customerName}</span><button className="close-btn" onClick={onClose}><Icon name="close" size={15} /></button></div>
        <div className="modal-body">
          <div className="form-grid">
            <label className="field"><span>企业名称</span><input placeholder="个人客户留空" value={form.companyName} onChange={e => setForm({ ...form, companyName: e.target.value })} /></label>
            <label className="field"><span>企业税号</span><input placeholder="开票用" value={form.taxNo} onChange={e => setForm({ ...form, taxNo: e.target.value })} /></label>
            <label className="field"><span>授信额度（元）</span><input type="number" min="0" value={form.creditLimit} onChange={e => setForm({ ...form, creditLimit: e.target.value })} /></label>
            <label className="field"><span>免押额度（元）</span><input type="number" min="0" value={form.depositWaiverLimit} onChange={e => setForm({ ...form, depositWaiverLimit: e.target.value })} /></label>
            <label className="field"><span>实名状态</span><select value={form.realNameStatus} onChange={e => setForm({ ...form, realNameStatus: e.target.value })}><option>未认证</option><option>认证中</option><option>已认证</option></select></label>
            <label className="field"><span>证件号码</span><input placeholder="身份证号（脱敏保存）" value={form.idCardNo} onChange={e => setForm({ ...form, idCardNo: e.target.value })} /></label>
          </div>
          <div className="field-hint" style={{ marginTop: 10 }}>免押额度只抵扣押金，不抵扣租金；企业授信用于月结客户，超额度后下单会按普通客户收押。</div>
        </div>
        <div className="modal-footer"><button className="btn-sm btn-sm-default" onClick={onClose}>取消</button><button className="btn-sm btn-sm-primary" onClick={save} disabled={loading}>保存</button></div>
      </div>
    </div>
  )
}

/** 员工绩效：报表页/看板底部，一排紧凑指标，不新开绩效系统。 */
export function PerformancePanel() {
  const [rows, setRows] = useState<StaffPerformance[]>([])
  useEffect(() => {
    extApi.performance().then((r: any) => setRows(r.data || [])).catch(() => {})
  }, [])
  if (rows.length === 0) return null
  return (
    <div className="dash-sub-block">
      <div className="dash-sub-title"><Icon name="users" size={14} chip />员工绩效</div>
      <div className="table-wrap">
        <table className="data-table compact-table performance-table">
          <thead><tr><th>员工</th><th className="num">开单</th><th className="num">结算</th><th className="num">营收</th><th className="num">退款</th></tr></thead>
          <tbody>
            {rows.map(r => <tr key={r.userId}><td data-label="员工">{r.userName}<span className="perf-role">· {r.roleName}</span></td><td data-label="开单" className="num">{r.orderCount}</td><td data-label="结算" className="num">{r.settleCount}</td><td data-label="营收" className="num">¥{Number(r.revenue).toFixed(2)}</td><td data-label="退款" className="num">¥{Number(r.refundAmount).toFixed(2)}</td></tr>)}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/** 需求预测：看板底部，显示未来 7 天单量趋势，指导调车和排班。 */
export function ForecastPanel() {
  const [data, setData] = useState<Forecast | null>(null)
  useEffect(() => {
    extApi.forecast(7).then((r: any) => setData(r.data)).catch(() => {})
  }, [])
  if (!data) return null
  return (
    <div className="dash-sub-block">
      <div className="dash-sub-title"><Icon name="reports" size={14} chip />未来 7 天需求预测<span className="text-muted">近 {data.days} 天均值 {Number(data.avgOrders).toFixed(1)} 单 · {data.trend}</span></div>
      <div className="forecast-strip">
        {data.forecast.map(p => (
          <div key={p.date} className="forecast-day">
            <strong>{p.orders}</strong>
            <small>{p.date}</small>
          </div>
        ))}
      </div>
    </div>
  )
}
