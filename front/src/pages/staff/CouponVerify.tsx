import { useState } from 'react'
import { couponApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import QrScanner from '@/components/QrScanner'
import type { CouponVerifyInfo } from '@/types'

/**
 * 前台「优惠券核销」。
 *
 * 顾客在「我的优惠券」里出示二维码，码值就是 customer_coupon.code；
 * 扫码后先查（GET /coupon/verify）把券面与持券顾客一起带出来，人工核对无误再核销。
 * 刻意做成「先查后核销」两步：券一旦核销不可撤销，扫到就自动扣会误伤顾客。
 */

/** 二维码前缀由顾客端拼接；后端也会剥一次，这里先剥保证手输/扫码两条路径一致 */
const QR_PREFIX = 'ERENTAL:COUPON:'
const normalize = (raw: string) => {
  const t = (raw || '').trim()
  const upper = t.toUpperCase()
  return upper.startsWith(QR_PREFIX) ? t.slice(QR_PREFIX.length).trim().toUpperCase() : upper
}

const money = (v: number) => `¥${(Number.isFinite(v) ? v : 0).toFixed(2)}`
const fmtDT = (v?: string | null) => v ? new Date(v).toLocaleString('zh-CN', { hour12: false }) : '—'

export default function CouponVerify() {
  const { toast, confirm } = useUI()
  const [code, setCode] = useState('')
  const [info, setInfo] = useState<CouponVerifyInfo | null>(null)
  const [loading, setLoading] = useState(false)
  const [scanning, setScanning] = useState(false)
  const [consuming, setConsuming] = useState(false)

  const lookup = async (raw?: string) => {
    const c = normalize(raw ?? code)
    if (!c) { toast('请扫码或输入券码', 'warning'); return }
    setCode(c)
    setLoading(true)
    setInfo(null)
    try {
      const res: any = await couponApi.verify(c)
      setInfo(res.data)
    } catch (e: any) {
      toast(e?.message || '未找到该券', 'error')
    } finally {
      setLoading(false)
    }
  }

  const consume = async () => {
    if (!info || !info.usable) return
    const ok = await confirm({
      title: '确认核销该券',
      message: `券「${info.name}」抵扣 ${money(info.amount)}，持券人 ${info.customerName}。核销后不可撤销，请确认已核对顾客身份。`,
      type: 'warning',
      confirmText: '确认核销',
    })
    if (!ok) return
    setConsuming(true)
    try {
      const res: any = await couponApi.consume(info.code)
      toast(res?.message || '核销成功', 'success')
      setInfo(res.data)
    } catch (e: any) {
      toast(e?.message || '核销失败', 'error')
    } finally {
      setConsuming(false)
    }
  }

  return (
    <div>
      <div className="page-head">
        <h2><Icon name="scan" size={20} chip />优惠券核销</h2>
        <p className="page-sub">扫描顾客出示的券码二维码，核对持券人后核销；也可手动输入券码。</p>
      </div>

      <div className="card">
        <div className="scan-row">
          <div className="scan-input">
            <Icon name="qrcode" size={18} />
            <input
              value={code}
              onChange={e => setCode(e.target.value.toUpperCase())}
              onKeyDown={e => { if (e.key === 'Enter') lookup() }}
              placeholder="扫码枪扫描或输入券码，如 CP3F9A…"
              aria-label="券码"
            />
          </div>
          <button className="btn-sm btn-sm-primary btn-icon-sm" onClick={() => setScanning(true)}>
            <Icon name="camera" size={15} color="#fff" /><span className="btn-label">摄像头扫码</span>
          </button>
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => lookup()} disabled={loading}>
            <Icon name="search" size={15} /><span className="btn-label">{loading ? '查询中…' : '查询'}</span>
          </button>
        </div>
        <p className="field-hint">券码与顾客一一对应：扫码结果会显示持券人，请与到场顾客核对姓名/手机号后再核销。</p>
      </div>

      {info && (
        <div className="card coupon-verify-card">
          <div className="card-title">
            <Icon name="coupon" size={18} chip />券面信息
            <span className={`status-tag ${info.usable ? 'status-success' : 'status-warning'}`}>{info.status}</span>
          </div>

          <div className="coupon-verify-head">
            <div className="coupon-verify-amount">{money(info.amount)}</div>
            <div>
              <div className="coupon-verify-name">{info.name}</div>
              <div className="coupon-verify-sub">
                {info.minAmount > 0 ? `满 ${money(info.minAmount)} 可用` : '无门槛'} · 有效期至 {fmtDT(info.expireTime)}
              </div>
            </div>
          </div>

          <div className="kv-grid">
            <div><span>券码</span><strong className="mono">{info.code}</strong></div>
            <div><span>持券顾客</span><strong>{info.customerName}</strong></div>
            <div><span>手机号</span><strong className="mono">{info.contactPhone || '—'}</strong></div>
            <div><span>证件号</span><strong className="mono">{info.idCardMasked || '—'}</strong></div>
            <div><span>会员等级</span><strong>{info.memberLevel || '普通会员'}</strong></div>
            <div><span>有效期至</span><strong>{fmtDT(info.expireTime)}</strong></div>
            {info.status === '已使用' && <div><span>使用时间</span><strong>{fmtDT(info.useTime)}</strong></div>}
          </div>

          {info.status === '已使用' && (
            <div className="callout callout-warning">
              <Icon name="warning" size={18} chip />
              <div>该券已于 {fmtDT(info.useTime)} 核销{info.orderId ? `（订单 #${info.orderId}）` : ''}，不能重复使用。</div>
            </div>
          )}
          {info.status === '已过期' && (
            <div className="callout callout-warning">
              <Icon name="warning" size={18} chip />
              <div>该券已过有效期，不能核销。</div>
            </div>
          )}

          <div className="form-actions">
            <button className="btn-sm btn-sm-primary" onClick={consume} disabled={!info.usable || consuming}>
              {consuming ? '核销中…' : '确认核销'}
            </button>
            <button className="btn-sm btn-sm-default" onClick={() => { setInfo(null); setCode('') }}>继续扫下一张</button>
          </div>
        </div>
      )}

      {scanning && (
        <QrScanner
          onDetected={text => {
            setScanning(false)
            lookup(text)
          }}
          onClose={() => setScanning(false)}
        />
      )}
    </div>
  )
}
