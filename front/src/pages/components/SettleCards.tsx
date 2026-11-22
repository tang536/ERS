import { useNavigate } from 'react-router-dom'
import { settleApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import { money } from '@/utils/money'
import { fmtDT } from '@/utils/datetime'

/**
 * 结算单卡片：顾客「我的订单」与「归还结算单」页共用。
 * 补缴入口也收在这里，支付失败、生成补缴单的提示两页保持一致。
 */
export function SettleCard({ settle }: { settle: any }) {
  const { toast } = useUI()
  const navigate = useNavigate()

  const handlePay = async () => {
    try {
      const res: any = await settleApi.payOnline(settle.settleId, '')
      const payId = res?.data?.payId
      if (!payId) { toast('补缴单生成失败，请稍后重试', 'error'); return }
      navigate(`/supplement-pay/${payId}`)
    } catch (e: any) { toast(e?.message || '发起在线支付失败', 'error') }
  }

  const outstanding = Math.max(0, Number(settle.supplementAmount || 0) - Number(settle.supplementPaid || 0))

  return (
    <article className="settle-card">
      <div className="settle-card-head">
        <div>
          <strong className="mono">{settle.orderNo}</strong>
          <span className="settle-vehicle">{settle.vehicleNo || '—'}</span>
        </div>
        <span className="cell-time">{fmtDT(settle.settleTime)}</span>
      </div>

      <div className="settle-money-grid">
        <div><span>实际租金</span><strong>{money(settle.actualRent)}</strong></div>
        <div><span>逾期费</span><strong>{money(settle.overdueFee)}</strong></div>
        <div><span>损坏费</span><strong>{money(settle.damageFee)}</strong></div>
        <div className="highlight"><span>应退</span><strong>{money(settle.refundAmount)}</strong></div>
        <div className={Number(settle.supplementAmount || 0) > 0 ? 'warn' : ''}>
          <span>应补缴</span><strong>{money(settle.supplementAmount)}</strong>
        </div>
      </div>

      <div className="settle-card-foot">
        <div>
          {Number(settle.supplementAmount || 0) <= 0 ? (
            <span className="status-tag status-default">无需补缴</span>
          ) : settle.supplementCollected ? (
            <span className="status-tag status-success">已缴清{settle.paidChannel ? `（${settle.paidChannel}）` : ''}</span>
          ) : (
            <span className="status-tag status-warning">待缴 {money(outstanding)}</span>
          )}
          {settle.remark && <span className="settle-remark">备注：{settle.remark}</span>}
        </div>
        {Number(settle.supplementAmount || 0) > 0 && !settle.supplementCollected && (
          <button className="btn-sm btn-sm-primary" onClick={handlePay}>
            <Icon name="rent" size={13} color="#fff" />在线补缴
          </button>
        )}
      </div>
    </article>
  )
}

/** 未被结算单承载的待支付补缴单：点「去支付」直达补缴收银台 */
export function OrphanSupplementCard({ supplement }: { supplement: any }) {
  const navigate = useNavigate()
  return (
    <article className="settle-card">
      <div className="settle-card-head">
        <div><strong className="mono">{supplement.orderNo}</strong><span className="settle-vehicle">{supplement.payType || '—'}</span></div>
        <span className="status-tag status-warning">待支付</span>
      </div>
      <div className="settle-card-foot">
        <strong className="num">{money(supplement.payAmount)}</strong>
        <button className="btn-sm btn-sm-primary" onClick={() => navigate(`/supplement-pay/${supplement.payId}`)}>
          <Icon name="rent" size={13} color="#fff" />去支付
        </button>
      </div>
    </article>
  )
}
