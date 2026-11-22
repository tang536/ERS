import { useEffect, useState } from 'react'
import { orderApi, printApi, customerApi } from '@/api'
import { useUI } from '@/components/UIProvider'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '@/hooks/useAuth'
import Icon from '@/components/Icon'
import Pagination from '@/components/Pagination'
import ReceiptModal from '@/components/ReceiptModal'
import ContractModal from '@/components/ContractModal'
import CopyText from '@/components/CopyText'
import { ReturnRequestModal, ReviewModal } from './components/CustomerExtras'
import { money } from '@/utils/money'
import { ORDER_BADGE } from '@/utils/statusBadges'
import { fmtDT } from '@/utils/datetime'
import { useCustomerSettlements } from '@/hooks/useCustomerSettlements'
import { SettleCard, OrphanSupplementCard } from './components/SettleCards'

const PAGE_SIZE = 6

/**
 * 我的租赁订单 + 归还结算单。
 *
 * 两个列表在同一页并列：桌面左右两栏同时可见，窄屏用两个并列页签切换。
 * 订单卡片以车辆图片为主视觉；结算单只回答租金、押金、补缴。
 */
export default function MyOrders() {
  const { toast, confirm } = useUI()
  const { user } = useAuth()
  const navigate = useNavigate()
  const { settles, orphanSupplements, loading: settleLoading, reload: loadSettles } = useCustomerSettlements()
  const [searchParams] = useSearchParams()
  const [list, setList] = useState<any[]>([])
  const [keyword, setKeyword] = useState(searchParams.get('q') || '')
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  // 实名状态以服务端档案为准（本地缓存可能过期）
  const [profileVerified, setProfileVerified] = useState<boolean | null>(null)
  // 小票打印：顾客可对本人任意订单自助打印（后端按 Token 校验归属）
  const [receipt, setReceipt] = useState<string | null>(null)
  // 待支付倒计时：每秒减 1，用于给「待支付」订单展示剩余支付时间（超时会被系统自动取消）
  const [now, setNow] = useState(Date.now())
  /** 电子合同弹窗：未签署可手写签名，已签署只读 */
  const [contractOrder, setContractOrder] = useState<{ orderId: number; orderNo: string } | null>(null)
  const [returnOrder, setReturnOrder] = useState<any | null>(null)
  const [reviewOrder, setReviewOrder] = useState<any | null>(null)
  /** 窄屏下列宽不够并排两张表：用两个并列页签切换，桌面上仍然两表同屏 */
  const [activeBoard, setActiveBoard] = useState<'orders' | 'settles'>('orders')

  const load = () => {
    setLoading(true)
    orderApi.list(undefined, keyword || undefined)
      .then((res: any) => setList(res.data || []))
      .catch((e: any) => toast(e?.message || '订单加载失败', 'error'))
      .finally(() => setLoading(false))
  }

  useEffect(() => { load() }, [keyword])


  // 订单历史按 6 条/页浏览：十几条订单一屏铺开会把页面拉到几千像素
  useEffect(() => { setPage(1) }, [keyword])
  useEffect(() => {
    const maxPage = Math.max(1, Math.ceil(list.length / PAGE_SIZE))
    if (page > maxPage) setPage(maxPage)
  }, [list.length, page])

  // 从服务端拉取最新实名状态（避免本地缓存的 profileCompleted 过期）
  useEffect(() => {
    if (!user?.userId) return
    customerApi.get(user.userId)
      .then((res: any) => {
        const p = res.data
        setProfileVerified(!!p && !!p.idCard && !!p.contactPhone)
      })
      .catch(() => setProfileVerified(null))
  }, [user?.userId])

  // 倒计时驱动：只在页面上存在「待支付」订单时才跑，避免无意义的每秒重渲染
  useEffect(() => {
    const hasPending = list.some(o => o.orderStatus === '待支付')
    if (!hasPending) return
    const t = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(t)
  }, [list])

  /** 把后端给的 payRemainSeconds 换算成当前剩余秒数（本地每秒递减，避免频繁请求） */
  const remainOf = (o: any) => {
    if (o.orderStatus !== '待支付' || !o.payDeadline) return null
    const remain = Math.floor((new Date(o.payDeadline).getTime() - now) / 1000)
    return Math.max(0, remain)
  }

  const fmtRemain = (sec: number) => {
    const m = Math.floor(sec / 60)
    const s = sec % 60
    return `${m}:${String(s).padStart(2, '0')}`
  }

  const handlePrint = async (orderId: number) => {
    try {
      const res: any = await printApi.orderReceipt(orderId)
      setReceipt(res.content)
    } catch (e: any) { toast(e?.message || '小票获取失败', 'error') }
  }

  /** 取消待支付订单：立即释放车辆，不用干等超时 */
  const handleCancelPending = async (o: any) => {
    const ok = await confirm({
      title: '取消订单',
      message: `确定取消订单 ${o.orderNo}（${o.vehicleNo}）吗？取消后该车辆将立即释放给其他顾客。`,
      type: 'question',
      confirmText: '取消订单',
      cancelText: '再想想',
    })
    if (!ok) return
    try {
      const res: any = await orderApi.cancelPending(o.orderId)
      toast(res?.message || '订单已取消，车辆已释放', 'success')
      load()
    } catch (e: any) { toast(e?.message || '取消失败', 'error') }
  }

  /** 继续支付：跳回支付页 */
  const handlePay = (o: any) => navigate(`/pay/${o.orderNo}`)


  /**
   * 自助申请还车（无论是否到期都可申请）。这里只提交申请，不直接改订单状态——
   * 车况、费用要由门店确认，顾客自己点了就把钱退了会让门店失去控制。
   */
  const handleEarlyReturn = async (o: any) => {
    const ok = await confirm({
      title: '申请还车',
      message: `将为订单 ${o.orderNo}（${o.vehicleNo}）提交还车申请。门店审核通过后按实际用车时长结算，请尽快到店交车。`,
      type: 'question',
      confirmText: '提交申请',
    })
    if (!ok) return
    try {
      await orderApi.requestEarlyReturn(o.orderId)
      toast('申请已提交，请等待门店审核', 'success')
      // 重新拉列表，让「待审核」标记立刻出现
      const res: any = await orderApi.list(undefined, keyword || undefined)
      setList(res.data || [])
    } catch (e: any) { toast(e?.message || '申请失败', 'error') }
  }


  const paged = list.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  return (
    <div>
      {profileVerified === false && (
        <div className="callout callout-warning">
          <Icon name="warning" size={18} chip />
          <div>
            <strong>尚未完成实名认证</strong>
            <p>完成实名认证（真实姓名 + 身份证号 + 手机号）后，才能在线上自助下单租车。</p>
          </div>
          <button className="btn-sm btn-sm-primary" onClick={() => navigate('/profile')}>去实名认证</button>
        </div>
      )}

      <div className="my-board-tabs">
        <button className={`my-board-tab ${activeBoard === 'orders' ? 'is-active' : ''}`} onClick={() => setActiveBoard('orders')}>
          我的订单 <span>{list.length}</span>
        </button>
        <button className={`my-board-tab ${activeBoard === 'settles' ? 'is-active' : ''}`} onClick={() => setActiveBoard('settles')}>
          归还结算单 <span>{settles.length}</span>
        </button>
      </div>
      <div className="my-board">
      <div className={`card my-board-orders ${activeBoard === 'settles' ? 'is-hidden-mobile' : ''}`}>
        <div className="card-title">
          <Icon name="my-orders" size={18} chip />我的租赁订单
          <span className="card-title-count">共 {list.length} 条</span>
          <div className="card-title-actions">
            <button className="btn-sm btn-sm-primary btn-icon-sm" onClick={() => navigate('/rent')}>
              <Icon name="rent" size={15} color="#fff" /><span className="btn-label">线上租车</span>
            </button>
          </div>
        </div>

        <div className="toolbar">
          <div className="search-box">
            <input
              placeholder="搜索订单号 / 车辆编号"
              value={keyword}
              onChange={e => setKeyword(e.target.value)}
            />
            {keyword && (
              <button className="btn-sm btn-sm-default" onClick={() => setKeyword('')}>
                <Icon name="refresh" size={14} />重置
              </button>
            )}
          </div>
        </div>

        <div className="my-order-list">
          {paged.map(o => {
            const canEarlyReturn = (o.orderStatus === '已支付' || o.orderStatus === '租赁中')
              && o.earlyReturnStatus !== '待审核'
            const isPending = o.orderStatus === '待支付'
            const remain = remainOf(o)
            return (
              <article className="my-order-card" key={o.orderId}>
                <div className="my-order-media">
                  {o.vehicleImageUrl ? (
                    <img src={o.vehicleImageUrl} alt={`${o.vehicleNo} 车辆图片`} loading="lazy" />
                  ) : (
                    <span className="my-order-media-fallback">
                      <Icon name="vehicles" size={40} />
                      <em>{o.vehicleNo || '车辆待分配'}</em>
                    </span>
                  )}
                  <span className={`status-tag order-status-chip ${ORDER_BADGE[o.orderStatus] || 'status-default'}`}>
                    {o.orderStatus}
                  </span>
                </div>

                <div className="my-order-main">
                  <div className="my-order-head">
                    <CopyText text={o.orderNo} label="复制订单号" className="my-order-no" />
                    <span className="my-order-vehicle">{o.vehicleNo || '车辆待分配'}</span>
                    {o.vehicleBrand && <span className="my-order-brand">{o.vehicleBrand}</span>}
                  </div>

                  <div className="my-order-meta">
                    <div><span>起租</span><strong>{fmtDT(o.leaseStart)}</strong></div>
                    <div><span>到期</span><strong>{fmtDT(o.leaseEnd)}</strong></div>
                    <div><span>租金</span><strong className="num">{money(o.actualRent ?? o.totalRent)}</strong></div>
                    <div><span>押金</span><strong className="num">{money(o.deposit)}</strong></div>
                  </div>

                  {o.earlyReturnStatus === '待审核'
                    && (o.orderStatus === '已支付' || o.orderStatus === '租赁中') && (
                    <span className="status-tag status-warning">还车申请待审核</span>
                  )}
                  {isPending && remain !== null && (
                    <div className="pay-countdown">
                      <Icon name="clock" size={12} />
                      {remain > 0
                        ? <>剩余 {fmtRemain(remain)} 未支付将自动取消</>
                        : <>支付时间已过，系统即将释放车辆</>}
                    </div>
                  )}
                </div>

                <div className="my-order-actions">
                  {isPending && (
                    <>
                      <button className="btn-sm btn-sm-primary" onClick={() => handlePay(o)}>
                        <Icon name="rent" size={13} color="#fff" />去支付
                      </button>
                      <button className="btn-sm btn-sm-default" onClick={() => handleCancelPending(o)}>
                        <Icon name="close" size={13} />取消订单
                      </button>
                    </>
                  )}
                  <button className="btn-sm btn-sm-default" onClick={() => handlePrint(o.orderId)}>
                    <Icon name="print" size={13} />打印小票
                  </button>
                  {o.orderStatus !== '已取消' && (
                    <button className="btn-sm btn-sm-default" onClick={() => setContractOrder({ orderId: o.orderId, orderNo: o.orderNo })}>
                      <Icon name="orders" size={13} />电子合同
                    </button>
                  )}
                  {canEarlyReturn && (
                    <button className="btn-sm btn-sm-primary" onClick={() => handleEarlyReturn(o)}>
                      <Icon name="clock" size={13} color="#fff" />申请还车
                    </button>
                  )}
                  {(o.orderStatus === '已支付' || o.orderStatus === '租赁中') && (
                    <button className="btn-sm btn-sm-default" onClick={() => setReturnOrder(o)}>
                      <Icon name="clock" size={13} />还车登记
                    </button>
                  )}
                  {o.orderStatus === '已完结' && (
                    <button className="btn-sm btn-sm-default" onClick={() => setReviewOrder(o)}>
                      <Icon name="success" size={13} />评价
                    </button>
                  )}
                </div>
              </article>
            )
          })}
        </div>

        {loading && <div className="loading-line"><Icon name="refresh" size={18} />数据加载中…</div>}
        {!loading && list.length === 0 && (
          <div className="empty">
            <Icon name="empty" size={40} chip />
            <div>{keyword ? '没有匹配的订单' : '您还没有租赁订单，到店后由前台为您开单'}</div>
          </div>
        )}
        {!loading && list.length > 0 && (
          <Pagination total={list.length} page={page} pageSize={PAGE_SIZE} onChange={setPage} />
        )}
      </div>

      <div className={`card my-settle-board ${activeBoard === 'orders' ? 'is-hidden-mobile' : ''}`}>
        <div className="card-title">
          <Icon name="settlements" size={18} chip />归还结算单
          <span className="card-title-count">共 {settles.length} 条</span>
          <button className="btn-sm btn-sm-default btn-icon-sm" style={{ marginLeft: 'auto' }} onClick={() => navigate('/my-settlements')}>
            <Icon name="settlements" size={15} /><span className="btn-label">独立结算页</span>
          </button>
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={loadSettles}>
            <Icon name="refresh" size={15} /><span className="btn-label">刷新</span>
          </button>
        </div>

        {settles.map((s: any) => <SettleCard key={s.settleId} settle={s} />)}

        {orphanSupplements.length > 0 && (
          <div className="settle-extra">
            <div className="settle-extra-title">其他待补缴费用 · {orphanSupplements.length} 笔</div>
            {orphanSupplements.map((s: any) => <OrphanSupplementCard key={s.payId} supplement={s} />)}
          </div>
        )}

        {settleLoading && <div className="loading-line"><Icon name="refresh" size={18} />数据加载中…</div>}
        {!settleLoading && settles.length === 0 && orphanSupplements.length === 0 && (
          <div className="empty">
            <Icon name="empty" size={40} chip />
            <div>还没有结算单。门店验收车辆并结算后，会在这里生成明细。</div>
          </div>
        )}
      </div>
      </div>

      {receipt !== null && (
        <ReceiptModal
          title="租赁订单小票"
          content={receipt}
          onClose={() => setReceipt(null)}
          hint="请妥善保管小票，还车时凭此办理押金退还。"
        />
      )}

      {contractOrder && (
        <ContractModal
          orderId={contractOrder.orderId}
          orderNo={contractOrder.orderNo}
          onClose={() => setContractOrder(null)}
        />
      )}

      {returnOrder && (
        <ReturnRequestModal order={returnOrder} onClose={() => setReturnOrder(null)} onDone={() => { load(); loadSettles() }} />
      )}
      {reviewOrder && (
        <ReviewModal order={reviewOrder} onClose={() => setReviewOrder(null)} onDone={load} />
      )}
    </div>
  )
}
