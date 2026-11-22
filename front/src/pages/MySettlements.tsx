import { useNavigate } from 'react-router-dom'
import Icon from '@/components/Icon'
import { useCustomerSettlements } from '@/hooks/useCustomerSettlements'
import { SettleCard, OrphanSupplementCard } from './components/SettleCards'

/**
 * 归还结算单（顾客侧独立页面）。
 *
 * 与「我的租赁订单」拆开：订单页只看租期、状态和操作；本页只回答一件事——
 * 这单车还回来以后，租金怎么算、押金退多少、还欠不欠钱。
 * 数据加载、补缴去重与卡片渲染与订单页共享，避免两页出现两种口径。
 */
export default function MySettlements() {
  const navigate = useNavigate()
  const { settles, orphanSupplements, loading, reload } = useCustomerSettlements()

  return (
    <div>
      <div className="card">
        <div className="card-title page-title-back">
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => navigate('/my-orders')} title="返回我的租赁订单">
            <Icon name="chevron-left" size={15} /><span className="btn-label">返回</span>
          </button>
          <Icon name="settlements" size={18} chip />归还结算单
          <span className="card-title-count">共 {settles.length} 条</span>
          <button className="btn-sm btn-sm-default btn-icon-sm" style={{ marginLeft: 'auto' }} onClick={reload}>
            <Icon name="refresh" size={15} /><span className="btn-label">刷新</span>
          </button>
        </div>

        {settles.map((s: any) => <SettleCard key={s.settleId} settle={s} />)}

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
          {orphanSupplements.map((s: any) => <OrphanSupplementCard key={s.payId} supplement={s} />)}
        </div>
      )}
    </div>
  )
}
