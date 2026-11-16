import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { customerApi, orderApi, couponApi, addonApi, pointApi } from '@/api'
import { useAuth } from '@/hooks/useAuth'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import { nowDatetimeInput, afterDaysDatetimeInput, isBeforeNow, clampLeaseStart } from '@/utils/datetime'
import type { RentableVehicle, RentEstimate, MyCoupon, AddonItem, PointSummary } from '@/types'

/**
 * 车辆详情与下单页（/rent/:vehicleId）。
 *
 * 与列表页的分工：列表只负责「有哪些车」，价格、租期、优惠与最终应付都在这里算。
 * 顾客在详情页选好起止时间后，服务端按该车实际定价与本人会员折扣试算，
 * 页面只做券/积分/增值服务的叠加预览，下单时后端再按同口径复核。
 */

/** 快捷租期：把「我要租多久」变成一次点击，而不是让顾客自己去拨两个时间控件 */
const QUICK_RANGES = [
  { label: '4 小时', hours: 4 },
  { label: '12 小时', hours: 12 },
  { label: '1 天', hours: 24 },
  { label: '3 天', hours: 72 },
  { label: '7 天', hours: 168 },
]

const money = (v: number) => `¥${(Number.isFinite(v) ? v : 0).toFixed(2)}`

/** datetime-local 值加小时，输出同格式的本地时间字符串（不能走 UTC，否则整体差 8 小时） */
const addHours = (value: string, hours: number) => {
  const d = new Date(value || nowDatetimeInput())
  d.setMinutes(d.getMinutes() + hours * 60)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

const ageText = (buyTime?: string | null) => {
  if (!buyTime) return '车龄未知'
  const years = (Date.now() - new Date(buyTime).getTime()) / (365.25 * 24 * 3600 * 1000)
  if (!Number.isFinite(years) || years < 0) return '车龄未知'
  return years < 1 ? '不足 1 年' : `约 ${Math.floor(years)} 年车龄`
}

export default function RentVehicle() {
  const { vehicleId } = useParams()
  const id = Number(vehicleId)
  const navigate = useNavigate()
  const { user } = useAuth()
  const { toast, confirm } = useUI()

  const [profile, setProfile] = useState<any>(null)
  const [loading, setLoading] = useState(true)
  const [vehicles, setVehicles] = useState<RentableVehicle[]>([])
  const [form, setForm] = useState({ leaseStart: nowDatetimeInput(), leaseEnd: afterDaysDatetimeInput(1) })
  const [estimate, setEstimate] = useState<RentEstimate | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)

  const [coupons, setCoupons] = useState<MyCoupon[]>([])
  const [addons, setAddons] = useState<AddonItem[]>([])
  const [points, setPoints] = useState<PointSummary | null>(null)
  const [couponId, setCouponId] = useState(0)
  const [usePoints, setUsePoints] = useState(false)
  const [addonIds, setAddonIds] = useState<number[]>([])

  const verified = !!profile && !!profile.idCard && !!profile.contactPhone
  const vehicle = useMemo(() => vehicles.find(v => v.vehicleId === id), [vehicles, id])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [v, p] = await Promise.all([orderApi.availableVehicles(), customerApi.get(user!.userId)])
      setVehicles((v as any).data || [])
      setProfile((p as any).data)
    } catch (e: any) {
      toast(e?.message || '车辆信息加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }, [toast, user])

  useEffect(() => { if (user?.userId) load() }, [load, user?.userId])

  // 权益数据：只有实名通过后才需要（未认证本来也下不了单）
  useEffect(() => {
    if (!verified) return
    let alive = true
    couponApi.mine().then((r: any) => { if (alive) setCoupons(r.data || []) }).catch(() => {})
    addonApi.list(true).then((r: any) => { if (alive) setAddons(r.data || []) }).catch(() => {})
    pointApi.summary().then((r: any) => { if (alive) setPoints(r.data || null) }).catch(() => {})
    return () => { alive = false }
  }, [verified])

  // 试算：按车辆实际定价与本人会员折扣，服务端算；换车/改时间都会重算
  useEffect(() => {
    if (!vehicle) { setEstimate(null); return }
    if (!form.leaseStart || !form.leaseEnd) { setEstimate(null); return }
    if (new Date(form.leaseEnd) <= new Date(form.leaseStart)) { setEstimate(null); return }
    let alive = true
    orderApi.estimate(form.leaseStart, form.leaseEnd, vehicle.vehicleId)
      .then((r: any) => { if (alive) setEstimate(r.data) })
      .catch(() => { if (alive) setEstimate(null) })
    return () => { alive = false }
  }, [vehicle, form.leaseStart, form.leaseEnd])

  const applyQuick = (hours: number) => {
    const start = isBeforeNow(form.leaseStart) ? nowDatetimeInput() : form.leaseStart
    setForm({ leaseStart: start, leaseEnd: addHours(start, hours) })
    setErrors({})
  }

  const applyStart = (raw: string) => {
    const value = clampLeaseStart(raw)
    setForm(f => ({ ...f, leaseStart: value }))
    setErrors(e => ({ ...e, leaseStart: '' }))
    if (value !== raw) toast('起租时间不能早于当前时间，已自动调整为当前时间', 'warning')
  }

  /** 券、积分只抵租金；增值服务按天/一次性计费后并入应付 */
  const usableCoupons = useMemo(
    () => coupons.filter(c => c.status === '未使用' && new Date(c.expireTime).getTime() > Date.now()),
    [coupons]
  )
  const rentBase = estimate?.totalRent || 0
  const selectedCoupon = usableCoupons.find(c => c.id === couponId)
  const couponAmount = selectedCoupon && rentBase >= selectedCoupon.minAmount
    ? Math.min(selectedCoupon.amount, rentBase)
    : 0
  const rentAfterCoupon = Math.max(0, rentBase - couponAmount)
  const maxPoints = points ? Math.min(points.balance, Math.floor(rentAfterCoupon * 0.1 * 100)) : 0
  const effectivePoints = usePoints ? maxPoints : 0
  const pointsAmount = effectivePoints / 100
  const billableDays = estimate ? Math.max(1, Math.ceil(estimate.hours / 24)) : 1
  const addonFee = addons
    .filter(a => addonIds.includes(a.addonId))
    .reduce((sum, a) => sum + a.price * (a.unit === '按天' ? billableDays : 1), 0)
  const finalPayable = Math.round((rentAfterCoupon - pointsAmount + addonFee + (estimate?.deposit || 0)) * 100) / 100

  const handleSubmit = async () => {
    if (!verified) {
      toast('请先完成实名认证后再下单', 'warning')
      navigate('/profile')
      return
    }
    if (!vehicle) return
    if (isBeforeNow(form.leaseStart)) {
      setForm(f => ({ ...f, leaseStart: nowDatetimeInput() }))
      toast('起租时间已早于当前时间，已自动调整为当前时间，请确认金额后重新提交', 'warning')
      return
    }
    const e: Record<string, string> = {}
    if (!form.leaseStart) e.leaseStart = '请选择起租时间'
    if (!form.leaseEnd) e.leaseEnd = '请选择到期时间'
    if (form.leaseStart && form.leaseEnd && new Date(form.leaseEnd) <= new Date(form.leaseStart))
      e.leaseEnd = '到期时间必须晚于起租时间'
    setErrors(e)
    if (Object.keys(e).length) return

    const ok = await confirm({
      title: '确认线上下单',
      message: `租赁「${vehicle.vehicleNo}」，租期 ${estimate?.hours ?? '--'} 小时，应付合计 ${money(finalPayable)}。下单后车辆将立即为您锁定。`,
      type: 'question',
      confirmText: '确认下单',
    })
    if (!ok) return

    setSubmitting(true)
    try {
      const res: any = await orderApi.createOnline({
        vehicleId: vehicle.vehicleId,
        leaseStart: form.leaseStart,
        leaseEnd: form.leaseEnd,
        customerCouponId: couponAmount > 0 ? couponId : null,
        pointsUsed: effectivePoints,
        addonIds,
      })
      const order = res.data
      navigate(`/pay/${encodeURIComponent(order.orderNo)}`)
    } catch (e2: any) {
      toast(e2?.message || '下单失败', 'error')
      load()
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) return <div className="loading-line"><Icon name="refresh" size={18} />正在加载车辆详情…</div>

  if (!vehicle) {
    return (
      <div className="empty">
        <Icon name="vehicles" size={44} chip />
        <div>这台车暂时不可租（可能已被他人租走或已下线）</div>
        <button className="btn-sm btn-sm-primary" style={{ marginTop: 12 }} onClick={() => navigate('/rent')}>
          看看其他车辆
        </button>
      </div>
    )
  }

  return (
    <div>
      <div className="vehicle-detail-head">
        <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => navigate('/rent')}>
          <Icon name="chevron-left" size={14} /><span className="btn-label">全部车辆</span>
        </button>
        <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => navigate('/coupons')}>
          <Icon name="coupon" size={14} /><span className="btn-label">我的优惠券</span>
        </button>
      </div>

      {/* ===== 车辆主信息 ===== */}
      <div className="vehicle-hero">
        <div className="vehicle-hero-photo">
          {vehicle.imageUrl
            ? <img src={vehicle.imageUrl} alt={vehicle.vehicleNo} />
            : <Icon name="vehicles" size={48} />}
        </div>
        <div className="vehicle-hero-body">
          <div className="vehicle-hero-title">
            <strong>{vehicle.vehicleNo}</strong>
            <span className="status-tag status-success">可租</span>
          </div>
          <div className="vehicle-hero-brand">{vehicle.brand || '未知品牌'} · {ageText(vehicle.buyTime)}</div>
          <div className="vehicle-hero-stats">
            <div><span>小时租金</span><strong>{money(vehicle.hourlyRent)}</strong></div>
            <div><span>日租金</span><strong>{money(vehicle.dailyRent)}</strong></div>
            <div><span>押金</span><strong>{money(vehicle.deposit)}</strong></div>
            <div><span>近 90 天</span><strong>{vehicle.orderCount || 0} 单</strong></div>
          </div>
        </div>
      </div>

      {!verified && (
        <div className="callout callout-warning">
          <Icon name="warning" size={18} chip />
          <div>
            <strong>下单前需完成实名认证</strong>
            <p>请先在「个人中心」补全真实姓名、身份证号与手机号。</p>
          </div>
          <button className="btn-sm btn-sm-primary" onClick={() => navigate('/profile')}>去实名认证</button>
        </div>
      )}

      {/* ===== 租期 ===== */}
      <div className="card">
        <div className="card-title"><Icon name="clock" size={18} chip />选择租期</div>

        <div className="lease-quick">
          {QUICK_RANGES.map(q => (
            <button
              key={q.hours}
              className={`lease-quick-btn ${estimate && estimate.hours === q.hours ? 'active' : ''}`}
              onClick={() => applyQuick(q.hours)}
            >
              {q.label}
            </button>
          ))}
        </div>

        <div className="form-row">
          <div className="form-group">
            <label>起租时间 <span className="req">*</span></label>
            <input
              type="datetime-local"
              className={errors.leaseStart ? 'has-error' : ''}
              value={form.leaseStart}
              min={nowDatetimeInput()}
              onChange={e => applyStart(e.target.value)}
            />
            {errors.leaseStart && <div className="field-error">{errors.leaseStart}</div>}
          </div>
          <div className="form-group">
            <label>到期时间 <span className="req">*</span></label>
            <input
              type="datetime-local"
              className={errors.leaseEnd ? 'has-error' : ''}
              value={form.leaseEnd}
              min={form.leaseStart || nowDatetimeInput()}
              onChange={e => { setForm({ ...form, leaseEnd: e.target.value }); setErrors({ ...errors, leaseEnd: '' }) }}
            />
            {errors.leaseEnd && <div className="field-error">{errors.leaseEnd}</div>}
          </div>
        </div>

        {estimate ? (
          <div className="estimate">
            <div className="estimate-head"><Icon name="settlements" size={16} chip />费用试算（实时）</div>
            <div className="estimate-grid">
              <div><span>计费时长</span><strong>{estimate.hours} 小时</strong></div>
              <div><span>小时租金</span><strong>{money(estimate.hourlyRent ?? estimate.dailyRent / 24)}</strong></div>
              <div><span>会员折扣</span><strong>{(estimate.discount * 10).toFixed(1)} 折</strong></div>
              <div><span>预收租金</span><strong>{money(estimate.totalRent)}</strong></div>
              <div><span>押金</span><strong>{money(estimate.deposit)}</strong></div>
              <div><span>优惠前应付</span><strong>{money(estimate.payable)}</strong></div>
            </div>
            {estimate.creditNote && (
              <p className="estimate-tip" style={{ color: 'var(--warning-text, #b45309)' }}>
                <Icon name="info" size={13} chip />信用提示：{estimate.creditNote}
              </p>
            )}
            {!!estimate.maxLeaseDays && estimate.hours > estimate.maxLeaseDays * 24 && (
              <p className="estimate-tip" style={{ color: 'var(--danger-text, #C0392B)' }}>
                <Icon name="error" size={13} chip />租期超出信用上限（最长 {estimate.maxLeaseDays} 天），请缩短到期时间。
              </p>
            )}
          </div>
        ) : null}
      </div>

      {/* ===== 优惠与增值服务 ===== */}
      {estimate && (
        <div className="card">
          <div className="card-title"><Icon name="coupon" size={18} chip />优惠与增值服务</div>

          <div className="form-group">
            <label><Icon name="coupon" size={14} chip />我的优惠券</label>
            <select value={couponId} onChange={e => setCouponId(parseInt(e.target.value))}>
              <option value={0}>不使用优惠券</option>
              {usableCoupons.map(c => (
                <option key={c.id} value={c.id} disabled={rentBase < c.minAmount}>
                  {c.name}（抵 {money(c.amount)}{c.minAmount > 0 ? ` · 满 ${money(c.minAmount)}` : ''}）
                </option>
              ))}
            </select>
            {usableCoupons.length === 0 && <div className="field-hint">暂无可用的券，可在「我的优惠券」用兑换码领取。</div>}
            {selectedCoupon && rentBase < selectedCoupon.minAmount && (
              <div className="field-hint" style={{ color: 'var(--warning-text, #b45309)' }}>
                本单租金未达到门槛 {money(selectedCoupon.minAmount)}，暂不可用。
              </div>
            )}
          </div>

          {/* 积分：改成「开关」，顾客自己决定用不用，不再填一个容易填错的数字 */}
          <div className="form-group">
            <label><Icon name="points" size={14} chip />积分抵扣</label>
            <label className={`points-toggle ${maxPoints <= 0 ? 'disabled' : ''}`}>
              <input
                type="checkbox"
                checked={usePoints && maxPoints > 0}
                disabled={maxPoints <= 0}
                onChange={e => setUsePoints(e.target.checked)}
              />
              <span>
                {usePoints && maxPoints > 0
                  ? `已开启：使用 ${maxPoints} 分，抵 ${money(maxPoints / 100)}`
                  : '不使用积分抵扣'}
              </span>
            </label>
            <div className="field-hint">
              {points ? `当前 ${points.balance} 分，本单最多抵 ${money(maxPoints / 100)}` : '积分加载中…'}
            </div>
          </div>

          {addons.length > 0 && (
            <div className="form-group">
              <label><Icon name="addon" size={14} chip />增值服务</label>
              <div className="addon-list">
                {addons.map(a => {
                  const checked = addonIds.includes(a.addonId)
                  return (
                    <label key={a.addonId} className={`addon-item ${checked ? 'active' : ''}`}>
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={e => setAddonIds(prev => e.target.checked ? [...prev, a.addonId] : prev.filter(x => x !== a.addonId))}
                      />
                      <span className="addon-item-main">
                        <strong><Icon name="addon" size={15} />{a.name}</strong>
                        {a.description && <small>{a.description}</small>}
                      </span>
                      <span className="addon-item-price">
                        {money(a.price)}{a.unit === '按天' ? `/天 × ${billableDays} = ${money(a.price * billableDays)}` : ''}
                      </span>
                    </label>
                  )
                })}
              </div>
            </div>
          )}

          <div className="rent-bill">
            <div className="rent-bill-row"><span>租金（折扣后）</span><strong>{money(rentBase)}</strong></div>
            <div className="rent-bill-row"><span>券抵扣</span><strong className="minus">-{money(couponAmount)}</strong></div>
            <div className="rent-bill-row"><span>积分抵扣{effectivePoints > 0 ? `（${effectivePoints} 分）` : ''}</span><strong className="minus">-{money(pointsAmount)}</strong></div>
            <div className="rent-bill-row"><span>增值服务</span><strong>+{money(addonFee)}</strong></div>
            <div className="rent-bill-row"><span>押金（可退）</span><strong>+{money(estimate.deposit)}</strong></div>
            <div className="rent-bill-total">应付合计 <strong>{money(finalPayable)}</strong></div>
            <p className="estimate-tip">券与积分只抵租金；取消订单自动退回。</p>
          </div>

          <div className="form-actions">
            <button className="btn-sm btn-sm-primary" onClick={handleSubmit} disabled={submitting}>
              {submitting ? '下单中…' : '确认下单并支付'}
            </button>
            <button className="btn-sm btn-sm-default" onClick={() => navigate('/rent')}>再挑一台</button>
          </div>
        </div>
      )}
    </div>
  )
}
