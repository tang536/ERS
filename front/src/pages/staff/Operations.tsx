import { useEffect, useMemo, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { addonApi, couponApi, customerApi } from '@/api'
import Icon from '@/components/Icon'
import { fmtDT, nowDatetimeInput, afterDaysDatetimeInput } from '@/utils/datetime'
import type { AddonItem, Coupon, Customer } from '@/types'

type Tab = 'coupon' | 'addon'

/** 优惠券表单：时间为 datetime-local 值，提交时原样传后端（后端按本地时间解析） */
interface CouponForm {
  name: string
  amount: string
  minAmount: string
  validFrom: string
  validTo: string
  totalQty: string
  perLimit: string
  /** 兑换码：顾客在顾客端输入即可领券，留空表示不开放口令领取 */
  redeemCode: string
  /** 是否开放兑换 */
  redeemOpen: boolean
}

const emptyCouponForm = (): CouponForm => ({
  name: '',
  amount: '',
  minAmount: '0',
  validFrom: nowDatetimeInput(),
  validTo: afterDaysDatetimeInput(30),
  totalQty: '0',
  perLimit: '1',
  redeemCode: '',
  redeemOpen: false,
})

const emptyAddonForm = () => ({ name: '', price: '', unit: '一次性', description: '', sortNo: '0' })

/**
 * 运营配置（券 + 增值服务）。
 *
 * 券只抵租金、不抵押金，一单最多用一张；增值服务按下单时的名称与单价快照计入订单总额。
 * 这两个模块都是管理员维护的经营参数，前台只在下单页选用，不在此处修改。
 */
export default function Operations() {
  const { toast, confirm } = useUI()
  const [tab, setTab] = useState<Tab>('coupon')
  const [coupons, setCoupons] = useState<Coupon[]>([])
  const [addons, setAddons] = useState<AddonItem[]>([])
  const [loading, setLoading] = useState(true)

  // ===== 券 =====
  const [couponModal, setCouponModal] = useState(false)
  const [editCouponId, setEditCouponId] = useState<number | null>(null)
  const [couponForm, setCouponForm] = useState<CouponForm>(emptyCouponForm())
  const [couponErrors, setCouponErrors] = useState<Record<string, string>>({})

  // 发券
  const [grantCoupon, setGrantCoupon] = useState<Coupon | null>(null)
  const [grantAll, setGrantAll] = useState(true)
  const [grantKeyword, setGrantKeyword] = useState('')
  const [grantCustomers, setGrantCustomers] = useState<Customer[]>([])
  const [grantSelected, setGrantSelected] = useState<Set<number>>(new Set())
  const [granting, setGranting] = useState(false)

  // ===== 增值服务 =====
  const [addonModal, setAddonModal] = useState(false)
  const [editAddonId, setEditAddonId] = useState<number | null>(null)
  const [addonForm, setAddonForm] = useState(emptyAddonForm())
  const [addonErrors, setAddonErrors] = useState<Record<string, string>>({})

  const loadCoupons = async () => {
    try {
      const res: any = await couponApi.list()
      setCoupons(res.data || [])
    } catch (e: any) {
      toast(e?.message || '优惠券加载失败', 'error')
    }
  }

  const loadAddons = async () => {
    try {
      const res: any = await addonApi.list(false)
      setAddons(res.data || [])
    } catch (e: any) {
      toast(e?.message || '增值服务加载失败', 'error')
    }
  }

  useEffect(() => {
    setLoading(true)
    Promise.all([loadCoupons(), loadAddons()]).finally(() => setLoading(false))
  }, [])

  // 发券弹窗打开时按关键词搜索顾客（只在需要选人时请求）
  useEffect(() => {
    if (!grantCoupon || grantAll) return
    let alive = true
    const timer = window.setTimeout(() => {
      customerApi.page({ keyword: grantKeyword || undefined, page: 1, pageSize: 50 })
        .then((res: any) => { if (alive) setGrantCustomers(res.data?.items || []) })
        .catch(() => { if (alive) setGrantCustomers([]) })
    }, 250)
    return () => { alive = false; window.clearTimeout(timer) }
  }, [grantCoupon, grantAll, grantKeyword])

  const activeCoupons = useMemo(() => coupons.filter(c => c.status === '启用').length, [coupons])

  // ===== 券：新增 / 编辑 =====
  const openCouponAdd = () => {
    setEditCouponId(null)
    setCouponForm(emptyCouponForm())
    setCouponErrors({})
    setCouponModal(true)
  }

  const openCouponEdit = (c: Coupon) => {
    setEditCouponId(c.couponId)
    setCouponForm({
      name: c.name,
      amount: String(c.amount),
      minAmount: String(c.minAmount),
      validFrom: c.validFrom.slice(0, 16),
      validTo: c.validTo.slice(0, 16),
      totalQty: String(c.totalQty),
      perLimit: String(c.perLimit),
      redeemCode: c.redeemCode || '',
      redeemOpen: !!c.redeemOpen,
    })
    setCouponErrors({})
    setCouponModal(true)
  }

  const saveCoupon = async () => {
    const e: Record<string, string> = {}
    if (!couponForm.name.trim()) e.name = '请填写券名称'
    const amount = Number(couponForm.amount)
    if (!Number.isFinite(amount) || amount <= 0) e.amount = '抵扣金额必须大于 0'
    if (!couponForm.validFrom) e.validFrom = '请选择生效时间'
    if (!couponForm.validTo) e.validTo = '请选择失效时间'
    else if (couponForm.validTo <= couponForm.validFrom) e.validTo = '失效时间必须晚于生效时间'
    const totalQty = Number(couponForm.totalQty) || 0
    if (totalQty < 0) e.totalQty = '发放总量不能为负数'
    const perLimit = Number(couponForm.perLimit) || 1
    if (perLimit < 1) e.perLimit = '每人限领至少 1 张'
    const redeemCode = couponForm.redeemCode.trim().toUpperCase()
    if (redeemCode && !/^[A-Z0-9]{4,20}$/.test(redeemCode)) e.redeemCode = '兑换码需为 4~20 位字母或数字'
    if (couponForm.redeemOpen && !redeemCode) e.redeemCode = '开放兑换前请先填写兑换码'
    setCouponErrors(e)
    if (Object.keys(e).length > 0) return

    const payload = {
      name: couponForm.name.trim(),
      amount,
      minAmount: Number(couponForm.minAmount) || 0,
      validFrom: couponForm.validFrom,
      validTo: couponForm.validTo,
      totalQty,
      perLimit,
      redeemCode: redeemCode || null,
      redeemOpen: couponForm.redeemOpen && !!redeemCode,
    }
    try {
      if (editCouponId) {
        await couponApi.update(editCouponId, payload)
        toast('优惠券已更新', 'success')
      } else {
        await couponApi.create(payload)
        toast('优惠券已创建', 'success')
      }
      setCouponModal(false)
      loadCoupons()
    } catch (err: any) {
      toast(err?.message || '保存失败', 'error')
    }
  }

  const toggleCoupon = async (c: Coupon) => {
    const next = c.status === '启用' ? '停用' : '启用'
    const ok = await confirm({
      title: `${next}优惠券`,
      message: next === '停用'
        ? `停用后不再发放「${c.name}」，已领出的券在有效期内仍可使用。`
        : `启用后将可以继续发放「${c.name}」。`,
      type: 'question',
      confirmText: next,
    })
    if (!ok) return
    try {
      await couponApi.setStatus(c.couponId, next)
      toast(`已${next}「${c.name}」`, 'success')
      loadCoupons()
    } catch (err: any) {
      toast(err?.message || '操作失败', 'error')
    }
  }

  // ===== 券：发券 =====
  const openGrant = (c: Coupon) => {
    setGrantCoupon(c)
    setGrantAll(true)
    setGrantKeyword('')
    setGrantCustomers([])
    setGrantSelected(new Set())
  }

  const submitGrant = async () => {
    if (!grantCoupon) return
    if (!grantAll && grantSelected.size === 0) {
      toast('请至少选择一位顾客', 'warning')
      return
    }
    setGranting(true)
    try {
      const res: any = await couponApi.grant({
        couponId: grantCoupon.couponId,
        customerIds: grantAll ? [] : [...grantSelected],
      })
      toast(res?.message || '发放完成', 'success')
      setGrantCoupon(null)
      loadCoupons()
    } catch (err: any) {
      toast(err?.message || '发放失败', 'error')
    } finally {
      setGranting(false)
    }
  }

  // ===== 增值服务 =====
  const openAddonAdd = () => {
    setEditAddonId(null)
    setAddonForm(emptyAddonForm())
    setAddonErrors({})
    setAddonModal(true)
  }

  const openAddonEdit = (a: AddonItem) => {
    setEditAddonId(a.addonId)
    setAddonForm({
      name: a.name,
      price: String(a.price),
      unit: a.unit,
      description: a.description || '',
      sortNo: String(a.sortNo),
    })
    setAddonErrors({})
    setAddonModal(true)
  }

  const saveAddon = async () => {
    const e: Record<string, string> = {}
    if (!addonForm.name.trim()) e.name = '请填写服务名称'
    const price = Number(addonForm.price)
    if (!Number.isFinite(price) || price < 0) e.price = '价格不能为负数'
    setAddonErrors(e)
    if (Object.keys(e).length > 0) return

    const payload = {
      name: addonForm.name.trim(),
      price,
      unit: addonForm.unit,
      description: addonForm.description.trim() || null,
      sortNo: Number(addonForm.sortNo) || 0,
    }
    try {
      if (editAddonId) {
        await addonApi.update(editAddonId, payload)
        toast('增值服务已更新', 'success')
      } else {
        await addonApi.create(payload)
        toast('增值服务已创建', 'success')
      }
      setAddonModal(false)
      loadAddons()
    } catch (err: any) {
      toast(err?.message || '保存失败', 'error')
    }
  }

  const toggleAddon = async (a: AddonItem) => {
    const next = a.status === '启用' ? '停用' : '启用'
    try {
      await addonApi.setStatus(a.addonId, next)
      toast(`已${next}「${a.name}」`, 'success')
      loadAddons()
    } catch (err: any) {
      toast(err?.message || '操作失败', 'error')
    }
  }

  if (loading) {
    return <div className="loading-line"><Icon name="refresh" size={18} />运营配置加载中…</div>
  }

  return (
    <div className="card">
      <div className="card-title">
        <Icon name="coupon" size={18} chip />运营配置
        <span className="card-title-count">优惠券 {activeCoupons} 个启用 · 增值服务 {addons.length} 项</span>
      </div>

      <div className="toolbar">
        <div className="tab-switch">
          <button className={`tab-btn ${tab === 'coupon' ? 'active' : ''}`} onClick={() => setTab('coupon')}>
            <Icon name="coupon" size={15} />优惠券<span className="tab-count">{coupons.length}</span>
          </button>
          <button className={`tab-btn ${tab === 'addon' ? 'active' : ''}`} onClick={() => setTab('addon')}>
            <Icon name="addon" size={15} />增值服务<span className="tab-count">{addons.length}</span>
          </button>
        </div>
        {tab === 'coupon' ? (
          <button className="btn-sm btn-sm-primary btn-icon-sm" onClick={openCouponAdd}>
            <Icon name="create-order" size={15} color="#fff" /><span className="btn-label">新增优惠券</span>
          </button>
        ) : (
          <button className="btn-sm btn-sm-primary btn-icon-sm" onClick={openAddonAdd}>
            <Icon name="create-order" size={15} color="#fff" /><span className="btn-label">新增服务项</span>
          </button>
        )}
      </div>

      {tab === 'coupon' ? (
        <>
          <div className="callout callout-info">
            <Icon name="info" size={18} chip />
            <span>券只抵扣租金，不抵押金；同一订单最多使用一张。券状态改为「停用」后不再发放，已领出的券也会同时失效，操作前请确认。设置了兑换码并开放领取后，顾客可在「我的优惠券」自助领券，也可到店出示二维码由前台核销。</span>
          </div>
          <div className="table-wrap">
            <table className="data-table nowrap-table">
              <thead>
                <tr>
                  <th>券名称</th><th>抵扣</th><th>门槛</th><th>兑换码</th><th className="col-hide-md">有效期</th>
                  <th>发放/总量</th><th>限领</th><th>状态</th><th>操作</th>
                </tr>
              </thead>
              <tbody>
                {coupons.map(c => (
                  <tr key={c.couponId}>
                    <td><strong>{c.name}</strong></td>
                    <td>¥{c.amount.toFixed(2)}</td>
                    <td>{c.minAmount > 0 ? `满 ¥${c.minAmount.toFixed(2)}` : '无门槛'}</td>
                    <td>
                      {c.redeemCode
                        ? <span className="redeem-code-cell"><Icon name="qrcode" size={13} />{c.redeemCode}
                          <span className={`badge ${c.redeemOpen ? 'badge-success' : 'badge-muted'}`}>{c.redeemOpen ? '开放' : '仅码'}</span>
                        </span>
                        : <span className="muted-text">未设置</span>}
                    </td>
                    <td className="col-hide-md">{fmtDT(c.validFrom)} ~ {fmtDT(c.validTo)}</td>
                    <td>{c.issuedQty} / {c.totalQty > 0 ? c.totalQty : '不限'}</td>
                    <td>{c.perLimit} 张</td>
                    <td>
                      <span className={`badge ${c.status === '启用' ? 'badge-success' : 'badge-muted'}`}>{c.status}</span>
                    </td>
                    <td>
                      <div className="btn-group">
                        <button className="btn-sm btn-sm-default" onClick={() => openCouponEdit(c)}>编辑</button>
                        <button className="btn-sm btn-sm-primary" onClick={() => openGrant(c)} disabled={c.status !== '启用'}>发券</button>
                        <button className="btn-sm btn-sm-default" onClick={() => toggleCoupon(c)}>
                          {c.status === '启用' ? '停用' : '启用'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {coupons.length === 0 && (
            <div className="empty"><Icon name="empty" size={40} chip /><div>暂无优惠券，点右上角「新增优惠券」创建</div></div>
          )}
        </>
      ) : (
        <>
          <div className="callout callout-info">
            <Icon name="info" size={18} chip />
            <span>「按天」服务按订单计费天数计价，「一次性」只收一次；下单时会把名称与单价快照进订单，之后改价不影响历史订单。</span>
          </div>
          <div className="table-wrap">
            <table className="data-table nowrap-table">
              <thead>
                <tr>
                  <th>服务名称</th><th>价格</th><th>计价方式</th><th className="col-hide-md">说明</th>
                  <th>排序</th><th>状态</th><th>操作</th>
                </tr>
              </thead>
              <tbody>
                {addons.map(a => (
                  <tr key={a.addonId}>
                    <td><strong>{a.name}</strong></td>
                    <td>¥{a.price.toFixed(2)}</td>
                    <td>{a.unit}</td>
                    <td className="col-hide-md">{a.description || '—'}</td>
                    <td>{a.sortNo}</td>
                    <td>
                      <span className={`badge ${a.status === '启用' ? 'badge-success' : 'badge-muted'}`}>{a.status}</span>
                    </td>
                    <td>
                      <div className="btn-group">
                        <button className="btn-sm btn-sm-default" onClick={() => openAddonEdit(a)}>编辑</button>
                        <button className="btn-sm btn-sm-default" onClick={() => toggleAddon(a)}>
                          {a.status === '启用' ? '停用' : '启用'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {addons.length === 0 && (
            <div className="empty"><Icon name="empty" size={40} chip /><div>暂无增值服务，点右上角「新增服务项」创建</div></div>
          )}
        </>
      )}

      {/* ===== 券：新增 / 编辑 ===== */}
      {couponModal && (
        <div className="modal-overlay" onClick={() => setCouponModal(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>{editCouponId ? '编辑优惠券' : '新增优惠券'}</span>
              <button className="close-btn" onClick={() => setCouponModal(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>券名称 <span className="req">*</span></label>
                <input
                  className={couponErrors.name ? 'has-error' : ''}
                  value={couponForm.name}
                  onChange={e => { setCouponForm({ ...couponForm, name: e.target.value }); setCouponErrors({ ...couponErrors, name: '' }) }}
                  placeholder="如：新客立减 10 元"
                  autoFocus
                />
                {couponErrors.name && <div className="field-error">{couponErrors.name}</div>}
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>抵扣金额（元） <span className="req">*</span></label>
                  <input
                    type="number" min="0.01" step="0.01"
                    className={couponErrors.amount ? 'has-error' : ''}
                    value={couponForm.amount}
                    onChange={e => { setCouponForm({ ...couponForm, amount: e.target.value }); setCouponErrors({ ...couponErrors, amount: '' }) }}
                  />
                  {couponErrors.amount && <div className="field-error">{couponErrors.amount}</div>}
                </div>
                <div className="form-group">
                  <label>使用门槛（元）</label>
                  <input
                    type="number" min="0" step="0.01"
                    value={couponForm.minAmount}
                    onChange={e => setCouponForm({ ...couponForm, minAmount: e.target.value })}
                  />
                  <div className="field-hint">订单租金达到该金额才可用，0 表示无门槛。</div>
                </div>
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>生效时间 <span className="req">*</span></label>
                  <input
                    type="datetime-local"
                    className={couponErrors.validFrom ? 'has-error' : ''}
                    value={couponForm.validFrom}
                    onChange={e => { setCouponForm({ ...couponForm, validFrom: e.target.value }); setCouponErrors({ ...couponErrors, validFrom: '' }) }}
                  />
                  {couponErrors.validFrom && <div className="field-error">{couponErrors.validFrom}</div>}
                </div>
                <div className="form-group">
                  <label>失效时间 <span className="req">*</span></label>
                  <input
                    type="datetime-local"
                    className={couponErrors.validTo ? 'has-error' : ''}
                    value={couponForm.validTo}
                    min={couponForm.validFrom}
                    onChange={e => { setCouponForm({ ...couponForm, validTo: e.target.value }); setCouponErrors({ ...couponErrors, validTo: '' }) }}
                  />
                  {couponErrors.validTo && <div className="field-error">{couponErrors.validTo}</div>}
                </div>
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>发放总量</label>
                  <input
                    type="number" min="0" step="1"
                    className={couponErrors.totalQty ? 'has-error' : ''}
                    value={couponForm.totalQty}
                    onChange={e => { setCouponForm({ ...couponForm, totalQty: e.target.value }); setCouponErrors({ ...couponErrors, totalQty: '' }) }}
                  />
                  <div className="field-hint">0 表示不限量。</div>
                  {couponErrors.totalQty && <div className="field-error">{couponErrors.totalQty}</div>}
                </div>
                <div className="form-group">
                  <label>每人限领</label>
                  <input
                    type="number" min="1" step="1"
                    className={couponErrors.perLimit ? 'has-error' : ''}
                    value={couponForm.perLimit}
                    onChange={e => { setCouponForm({ ...couponForm, perLimit: e.target.value }); setCouponErrors({ ...couponErrors, perLimit: '' }) }}
                  />
                  {couponErrors.perLimit && <div className="field-error">{couponErrors.perLimit}</div>}
                </div>
              </div>

              {/* 兑换码：顾客自己在顾客端领券的口令。码与开关分开，方便先发码给老顾客、后开放领取 */}
              <div className="form-row">
                <div className="form-group">
                  <label>兑换码</label>
                  <input
                    className={couponErrors.redeemCode ? 'has-error' : ''}
                    value={couponForm.redeemCode}
                    maxLength={20}
                    placeholder="如 SPRING20（4~20 位字母或数字）"
                    onChange={e => {
                      setCouponForm({ ...couponForm, redeemCode: e.target.value.toUpperCase() })
                      setCouponErrors({ ...couponErrors, redeemCode: '' })
                    }}
                  />
                  <div className="field-hint">顾客在「我的优惠券」输入该码即可自助领取；留空表示不开放口令领取。</div>
                  {couponErrors.redeemCode && <div className="field-error">{couponErrors.redeemCode}</div>}
                </div>
                <div className="form-group">
                  <label>开放兑换</label>
                  <label className="switch-line">
                    <input
                      type="checkbox"
                      checked={couponForm.redeemOpen}
                      onChange={e => setCouponForm({ ...couponForm, redeemOpen: e.target.checked })}
                    />
                    <span>{couponForm.redeemOpen ? '顾客可自助领券' : '关闭（仅后台发券）'}</span>
                  </label>
                  <div className="field-hint">关闭后兑换码立即失效，不影响已领出的券。</div>
                </div>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setCouponModal(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={saveCoupon}>保存</button>
            </div>
          </div>
        </div>
      )}

      {/* ===== 券：发券 ===== */}
      {grantCoupon && (
        <div className="modal-overlay" onClick={() => setGrantCoupon(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>发放「{grantCoupon.name}」</span>
              <button className="close-btn" onClick={() => setGrantCoupon(null)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>发放范围</label>
                <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontWeight: 400 }}>
                  <input type="radio" checked={grantAll} onChange={() => setGrantAll(true)} />
                  全部正常状态顾客
                </label>
                <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontWeight: 400 }}>
                  <input type="radio" checked={!grantAll} onChange={() => setGrantAll(false)} />
                  指定顾客
                </label>
              </div>

              {!grantAll && (
                <>
                  <div className="form-group">
                    <label>搜索顾客</label>
                    <input
                      value={grantKeyword}
                      onChange={e => setGrantKeyword(e.target.value)}
                      placeholder="按姓名 / 手机号搜索"
                    />
                  </div>
                  <div style={{ maxHeight: 260, overflow: 'auto', border: '1px solid var(--border-color, #e5e7eb)', borderRadius: 8 }}>
                    {grantCustomers.map(c => (
                      <label key={c.customerId} style={{ display: 'flex', gap: 8, alignItems: 'center', padding: '8px 12px', borderBottom: '1px solid var(--border-color, #e5e7eb)', fontWeight: 400 }}>
                        <input
                          type="checkbox"
                          checked={grantSelected.has(c.customerId)}
                          onChange={e => {
                            const next = new Set(grantSelected)
                            if (e.target.checked) next.add(c.customerId)
                            else next.delete(c.customerId)
                            setGrantSelected(next)
                          }}
                        />
                        <span>{c.fullName}</span>
                        <span className="mono" style={{ marginLeft: 'auto', opacity: 0.7 }}>{c.contactPhone || '—'}</span>
                      </label>
                    ))}
                    {grantCustomers.length === 0 && (
                      <div className="empty" style={{ padding: 24 }}>没有匹配的顾客</div>
                    )}
                  </div>
                  <div className="field-hint">已选 {grantSelected.size} 位顾客</div>
                </>
              )}
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setGrantCoupon(null)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={submitGrant} disabled={granting}>
                {granting ? '发放中…' : '确认发放'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ===== 增值服务：新增 / 编辑 ===== */}
      {addonModal && (
        <div className="modal-overlay" onClick={() => setAddonModal(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>{editAddonId ? '编辑增值服务' : '新增增值服务'}</span>
              <button className="close-btn" onClick={() => setAddonModal(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>服务名称 <span className="req">*</span></label>
                <input
                  className={addonErrors.name ? 'has-error' : ''}
                  value={addonForm.name}
                  onChange={e => { setAddonForm({ ...addonForm, name: e.target.value }); setAddonErrors({ ...addonErrors, name: '' }) }}
                  placeholder="如：骑行意外险"
                  autoFocus
                />
                {addonErrors.name && <div className="field-error">{addonErrors.name}</div>}
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>价格（元） <span className="req">*</span></label>
                  <input
                    type="number" min="0" step="0.01"
                    className={addonErrors.price ? 'has-error' : ''}
                    value={addonForm.price}
                    onChange={e => { setAddonForm({ ...addonForm, price: e.target.value }); setAddonErrors({ ...addonErrors, price: '' }) }}
                  />
                  {addonErrors.price && <div className="field-error">{addonErrors.price}</div>}
                </div>
                <div className="form-group">
                  <label>计价方式</label>
                  <select value={addonForm.unit} onChange={e => setAddonForm({ ...addonForm, unit: e.target.value })}>
                    <option value="一次性">一次性</option>
                    <option value="按天">按天</option>
                  </select>
                </div>
              </div>
              <div className="form-group">
                <label>说明</label>
                <input
                  value={addonForm.description}
                  onChange={e => setAddonForm({ ...addonForm, description: e.target.value })}
                  placeholder="选填，展示在下单页"
                />
              </div>
              <div className="form-group">
                <label>排序号</label>
                <input
                  type="number" step="1"
                  value={addonForm.sortNo}
                  onChange={e => setAddonForm({ ...addonForm, sortNo: e.target.value })}
                />
                <div className="field-hint">数字越小越靠前。</div>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setAddonModal(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={saveAddon}>保存</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
