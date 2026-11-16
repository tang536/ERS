import { useCallback, useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { couponApi, customerApi } from '@/api'
import { useAuth } from '@/hooks/useAuth'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import { qrSvg } from '@/utils/qrcode'
import type { MyCoupon } from '@/types'

/**
 * 顾客端「我的优惠券」。
 *
 * 两条入口都在这一页：
 *   1. 兑换码——管理员在运营配置里给券设码并勾选「开放兑换」后，顾客输码自助领券；
 *   2. 核销二维码——每张券都有唯一券码，二维码里带的就是这个码，
 *      前台扫码即可解析出**持券顾客**与券面信息并核销。
 * 二维码内容刻意带前缀，避免扫码枪把普通条码误当成券码；
 * 后端解析时会剥掉前缀，顾客手输裸码也能用。
 */

const QR_PREFIX = 'ERENTAL:COUPON:'
const qrText = (code: string) => `${QR_PREFIX}${code}`

const money = (v: number) => `¥${(Number.isFinite(v) ? v : 0).toFixed(2)}`
const fmtDate = (v: string) => new Date(v).toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' })

type TabKey = '未使用' | '已使用' | '已过期'

const TABS: TabKey[] = ['未使用', '已使用', '已过期']

export default function MyCoupons() {
  const { user } = useAuth()
  const { toast } = useUI()

  const [list, setList] = useState<MyCoupon[]>([])
  const [loading, setLoading] = useState(true)
  const [tab, setTab] = useState<TabKey>('未使用')
  const [code, setCode] = useState('')
  const [redeeming, setRedeeming] = useState(false)
  const [profile, setProfile] = useState<any>(null)

  const [qrOpen, setQrOpen] = useState<MyCoupon | null>(null)
  const [qrMarkup, setQrMarkup] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const res: any = await couponApi.mine()
      setList(res.data || [])
    } catch (e: any) {
      toast(e?.message || '优惠券加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    if (!user?.userId) return
    customerApi.get(user.userId).then((r: any) => setProfile(r.data)).catch(() => {})
  }, [user?.userId])

  const counts = useMemo(() => {
    const c: Record<TabKey, number> = { 未使用: 0, 已使用: 0, 已过期: 0 }
    list.forEach(x => {
      const k = (x.status as TabKey)
      if (k in c) c[k] += 1
    })
    return c
  }, [list])

  const shown = useMemo(() => list.filter(c => c.status === tab), [list, tab])

  const redeem = async () => {
    const c = code.trim().toUpperCase()
    if (!c) { toast('请输入兑换码', 'warning'); return }
    setRedeeming(true)
    try {
      const res: any = await couponApi.redeem(c)
      toast(res?.message || '领取成功', 'success')
      setCode('')
      setTab('未使用')
      load()
    } catch (e: any) {
      toast(e?.message || '兑换失败', 'error')
    } finally {
      setRedeeming(false)
    }
  }

  const openQr = async (c: MyCoupon) => {
    if (!c.code) { toast('该券缺少券码，请下拉刷新后重试', 'warning'); return }
    setQrOpen(c)
    setQrMarkup('')
    try {
      setQrMarkup(await qrSvg(qrText(c.code), { size: 260, margin: 2, level: 'M' }))
    } catch {
      toast('二维码生成失败，可改用券码手输', 'error')
    }
  }

  const copyCode = async (c: MyCoupon) => {
    if (!c.code) return
    try {
      await navigator.clipboard.writeText(c.code)
      toast('券码已复制', 'success')
    } catch {
      toast('复制失败，请手动长按选择', 'warning')
    }
  }

  return (
    <div>
      <div className="vehicle-detail-head">
        <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => history.back()}>
          <Icon name="chevron-left" size={14} /><span className="btn-label">返回</span>
        </button>
      </div>

      {/* ===== 兑换码 ===== */}
      <div className="card">
        <div className="card-title"><Icon name="coupon" size={18} chip />兑换码领券</div>
        <div className="redeem-row">
          <input
            value={code}
            onChange={e => setCode(e.target.value.toUpperCase())}
            placeholder="输入兑换码"
            maxLength={20}
            aria-label="兑换码"
            onKeyDown={e => { if (e.key === 'Enter') redeem() }}
          />
          <button className="btn-sm btn-sm-primary" onClick={redeem} disabled={redeeming}>
            {redeeming ? '领取中…' : '立即领取'}
          </button>
        </div>
      </div>

      {/* ===== 我的券 ===== */}
      <div className="card">
        <div className="card-title">
          <Icon name="coupon" size={18} chip />我的优惠券
          <span className="card-title-count">共 {list.length} 张</span>
        </div>

        <div className="coupon-tabs">
          {TABS.map(t => (
            <button key={t} className={`coupon-tab ${tab === t ? 'active' : ''}`} onClick={() => setTab(t)}>
              {t}<span>{counts[t]}</span>
            </button>
          ))}
        </div>

        {loading ? (
          <div className="loading-line"><Icon name="refresh" size={18} />正在加载…</div>
        ) : shown.length === 0 ? (
          <div className="empty"><Icon name="coupon" size={38} chip /><div>暂无{tab}的优惠券</div></div>
        ) : (
          <div className="coupon-list">
            {shown.map(c => (
              <div key={c.id} className={`coupon-card ${c.status !== '未使用' ? 'disabled' : ''}`}>
                <div className="coupon-stub">
                  <Icon name="coupon" size={22} />
                  <strong>{money(c.amount)}</strong>
                  <small>{c.minAmount > 0 ? `满 ${money(c.minAmount)} 可用` : '无门槛'}</small>
                </div>
                <div className="coupon-main">
                  <div className="coupon-name">{c.name}</div>
                  <div className="coupon-meta">有效期至 {fmtDate(c.expireTime)}</div>
                  {c.code && <div className="coupon-code">券码 {c.code}</div>}
                </div>
                <div className="coupon-actions">
                  {c.status === '未使用' ? (
                    <>
                      <button className="btn-sm btn-sm-primary btn-icon-sm" onClick={() => openQr(c)}>
                        <Icon name="qrcode" size={14} color="#fff" /><span className="btn-label">出示二维码</span>
                      </button>
                      <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => copyCode(c)}>
                        <Icon name="copy" size={13} /><span className="btn-label">复制券码</span>
                      </button>
                    </>
                  ) : (
                    <span className={`status-tag ${c.status === '已使用' ? 'status-info' : 'status-warning'}`}>{c.status}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {qrOpen && createPortal(
        <div className="modal-overlay" onClick={() => setQrOpen(null)}>
          <div className="modal coupon-qr-modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <strong>券码二维码</strong>
              <button className="close-btn" onClick={() => setQrOpen(null)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="coupon-qr-box">
                {qrMarkup
                  ? <div className="coupon-qr-img" dangerouslySetInnerHTML={{ __html: qrMarkup }} />
                  : <div className="loading-line"><Icon name="refresh" size={16} />生成中…</div>}
                <div className="coupon-qr-name">{qrOpen.name}</div>
                <div className="coupon-qr-amount">{money(qrOpen.amount)}</div>
                <div className="coupon-qr-code">{qrOpen.code}</div>
                <div className="coupon-qr-holder">
                  持券人：{profile?.fullName || '本人'}
                  {profile?.contactPhone ? ` · ${profile.contactPhone}` : ''}
                </div>
                <div className="coupon-qr-tip">仅限本人使用：请出示给门店前台扫码，前台会核对持券人姓名与手机号后核销。</div>
              </div>
              <div className="form-actions">
                <button className="btn-sm btn-sm-default" onClick={() => copyCode(qrOpen)}>复制券码</button>
                <button className="btn-sm btn-sm-primary" onClick={() => setQrOpen(null)}>关闭</button>
              </div>
            </div>
          </div>
        </div>,
        document.body
      )}
    </div>
  )
}
