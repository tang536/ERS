import { useEffect, useState } from 'react'
import { settleApi, supplementApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import { useNavigate } from 'react-router-dom'
import Icon from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'

/**
 * 归还结算单（顾客侧独立页面）。
 *
 * 与「我的租赁订单」拆开：订单页只看租期、状态和操作；本页只回答一件事——
 * 这单车还回来以后，租金怎么算、押金退多少、还欠不欠钱。
 */
export default function MySettlements() {
  const { toast } = useUI()
  const navigate = useNavigate()
  const [settles, setSettles] = useState<any[]>([])
  const [supplements, setSupplements] = useState<any[]>([])
  const [loading, setLoading] = useState(true)

  const load = () => {
    setLoading(true)
    settleApi.mine()
      .then((res: any) => setSettles(res.data || []))
      .catch((e: any) => toast(e?.message || '结算单加载失败', 'error'))
      .finally(() => setLoading(false))
    supplementApi.mine()
      .then((res: any) => setSupplements(res.data || []))
      .catch(() => { /* 补缴列表失败不影响结算单主流程 */ })
  }

  useEffect(() => { load() }, [])

  const handleSettlePay = async (s: any) => {
    try {
      const res: any = await settleApi.payOnline(s.settleId, '')
      const payId = res?.data?.payId
      if (!payId) { toast('补缴单生成失败，请稍后重试', 'error'); return }
      navigate(`/supplement-pay/${payId}`)
    } catch (e: any) { toast(e?.message || '发起在线支付失败', 'error') }
  }

  const pendingSupplements = supplements.filter(s => s.payStatus === '待支付')
  /** 已被结算单承载的补缴不再重复列（同一笔钱出现两次会让人误以为欠了两笔） */
  const orphanSupplements = pendingSupplements.filter(
    s => !settles.some((x: any) => x.orderId === s.orderId))

  const money = (n: any) => Number(n || 0).toFixed(2)

  return (
    <div>
      <div className="card">
        <div className="card-title page-title-back">
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => navigate('/my-orders')} title="返回我的租赁订单">
            <Icon name="chevron-left" size={15} /><span className="btn-label">返回</span>
          </button>
          <Icon name="settlements" size={18} chip />归还结算单
          <span className="card-title-count">共 {settles.length} 条</span>
          <button className="btn-sm btn-sm-default btn-icon-sm" style={{ marginLeft: 'auto' }} onClick={load}>
            <Icon name="refresh" size={15} /><span className="btn-label">刷新</span>
          </button>
        </div>

        {settles.map((s: any) => {
          const outstanding = Math.max(0, Number(s.supplementAmount || 0) - Number(s.supplementPaid || 0))
          return (
            <article className="settle-card" key={s.settleId}>
              <div className="settle-card-head">
                <div>
                  <strong className="mono">{s.orderNo}</strong>
                  <span className="settle-vehicle">{s.vehicleNo || '—'}</span>
                </div>
                <span className="cell-time">{fmtDT(s.settleTime)}</span>
              </div>

              <div className="settle-money-grid">
                <div><span>实际租金</span><strong>¥{money(s.actualRent)}</strong></div>
                <div><span>逾期费</span><strong>¥{money(s.overdueFee)}</strong></div>
                <div><span>损坏费</span><strong>¥{money(s.damageFee)}</strong></div>
                <div className="highlight"><span>应退</span><strong>¥{money(s.refundAmount)}</strong></div>
                <div className={Number(s.supplementAmount || 0) > 0 ? 'warn' : ''}>
                  <span>应补缴</span><strong>¥{money(s.supplementAmount)}</strong>
                </div>
              </div>

              <div className="settle-card-foot">
                <div>
                  {Number(s.supplementAmount || 0) <= 0 ? (
                    <span className="status-tag status-default">无需补缴</span>
                  ) : s.supplementCollected ? (
                    <span className="status-tag status-success">已缴清{s.paidChannel ? `（${s.paidChannel}）` : ''}</span>
                  ) : (
                    <span className="status-tag status-warning">待缴 ¥{outstanding.toFixed(2)}</span>
                  )}
                  {s.remark && <span className="settle-remark">备注：{s.remark}</span>}
                </div>
                {Number(s.supplementAmount || 0) > 0 && !s.supplementCollected && (
                  <button className="btn-sm btn-sm-primary" onClick={() => handleSettlePay(s)}>
                    <Icon name="rent" size={13} color="#fff" />在线补缴
                  </button>
                )}
              </div>
            </article>
          )
        })}

        {loading && <div className="loading-line"><Icon name="refresh" size={18} />数据加载中…</div>}
        {!loading && settles.length === 0 && (
          <div className="empty">
            <Icon name="empty" size={40} chip />
            <div>还没有结算单。门店验收车辆并结算后，会在这里生成明细。</div>
          </div>
        )}
      </div>

      {orphanSupplements.length > 0 && (
        <div className="card">
          <div className="card-title">
            <Icon name="warning" size={18} chip />其他待补缴费用
            <span className="card-title-count">{orphanSupplements.length} 笔待支付</span>
          </div>
          {orphanSupplements.map(s => (
            <article className="settle-card" key={s.payId}>
              <div className="settle-card-head">
                <div><strong className="mono">{s.orderNo}</strong><span className="settle-vehicle">{s.payType || '—'}</span></div>
                <span className="status-tag status-warning">待支付</span>
              </div>
              <div className="settle-card-foot">
                <strong className="num">¥{money(s.payAmount)}</strong>
                <button className="btn-sm btn-sm-primary" onClick={() => navigate(`/supplement-pay/${s.payId}`)}>
                  <Icon name="rent" size={13} color="#fff" />去支付
                </button>
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  )
}
