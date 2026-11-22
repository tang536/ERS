import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { customerApi, orderApi } from '@/api'
import { useAuth } from '@/hooks/useAuth'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import type { RentableVehicle } from '@/types'
import { money } from '@/utils/money'

/**
 * 顾客端「线上租车」首页。
 *
 * 交互按需求改成「先看车、再看详情」：本页只做全量车辆陈列与排序，
 * 点击某一台车才进入 `/rent/:vehicleId` 详情页选租期、试算、用券/积分/增值服务。
 * 原来的「下拉框选车 + 同页填表单」在手机上要把整张表单滚完才看得见价格，
 * 顾客既比不了车，也不知道自己点的是哪一台。
 */

/** 排序维度：都对应顾客真实会问的问题（哪台便宜 / 哪台新 / 哪台多人租） */
type SortKey = 'recommend' | 'priceAsc' | 'priceDesc' | 'depositAsc' | 'popular' | 'newest'

const SORTS: Array<{ key: SortKey; label: string; hint: string }> = [
  { key: 'recommend', label: '综合推荐', hint: '价格与热度均衡' },
  { key: 'priceAsc', label: '租金从低到高', hint: '按小时租金排序' },
  { key: 'priceDesc', label: '租金从高到低', hint: '先看高配车' },
  { key: 'depositAsc', label: '押金从低到高', hint: '少压钱' },
  { key: 'popular', label: '人气优先', hint: '近 90 天成单量' },
  { key: 'newest', label: '新车优先', hint: '按购入时间' },
]


/** 车龄（年）：只用于「新车优先」的展示，不暴露购入价 */
const ageText = (buyTime?: string | null) => {
  if (!buyTime) return '车龄未知'
  const years = (Date.now() - new Date(buyTime).getTime()) / (365.25 * 24 * 3600 * 1000)
  if (!Number.isFinite(years) || years < 0) return '车龄未知'
  if (years < 1) return '不足 1 年'
  return `约 ${Math.floor(years)} 年车龄`
}

export default function RentBike() {
  const { user } = useAuth()
  const { toast } = useUI()
  const navigate = useNavigate()

  const [profile, setProfile] = useState<any>(null)
  const [loadingProfile, setLoadingProfile] = useState(true)
  const [vehicles, setVehicles] = useState<RentableVehicle[]>([])
  const [loading, setLoading] = useState(false)
  const [sort, setSort] = useState<SortKey>('recommend')
  const [keyword, setKeyword] = useState('')

  /** 实名认证以服务端档案为准：本地缓存的 profileCompleted 可能已经过期 */
  const verified = !!profile && !!profile.idCard && !!profile.contactPhone

  const loadProfile = useCallback(() => {
    if (!user?.userId) return
    setLoadingProfile(true)
    customerApi.get(user.userId)
      .then((res: any) => setProfile(res.data))
      .catch((e: any) => toast(e?.message || '实名状态加载失败', 'error'))
      .finally(() => setLoadingProfile(false))
  }, [user?.userId, toast])

  const loadVehicles = useCallback(async (silent = false) => {
    setLoading(true)
    try {
      const res: any = await orderApi.availableVehicles()
      setVehicles(res.data || [])
    } catch (e: any) {
      if (!silent) toast(e?.message || '可租车辆加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(loadProfile, [loadProfile])
  useEffect(() => { loadVehicles() }, [loadVehicles])

  /** 关键字只看车牌与品牌：顾客在门店现场最常说的是「那台 A002」 */
  const filtered = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    if (!kw) return vehicles
    return vehicles.filter(v =>
      v.vehicleNo.toLowerCase().includes(kw) || (v.brand || '').toLowerCase().includes(kw))
  }, [vehicles, keyword])

  const sorted = useMemo(() => {
    const list = [...filtered]
    switch (sort) {
      case 'priceAsc': return list.sort((a, b) => a.hourlyRent - b.hourlyRent)
      case 'priceDesc': return list.sort((a, b) => b.hourlyRent - a.hourlyRent)
      case 'depositAsc': return list.sort((a, b) => a.deposit - b.deposit)
      case 'popular': return list.sort((a, b) => (b.orderCount || 0) - (a.orderCount || 0))
      case 'newest':
        return list.sort((a, b) => {
          const at = a.buyTime ? new Date(a.buyTime).getTime() : 0
          const bt = b.buyTime ? new Date(b.buyTime).getTime() : 0
          return bt - at
        })
      default:
        // 综合：先把押金低、租金低的排前面，同价位再看人气，避免「默认序」看起来像没排
        return list.sort((a, b) => {
          const scoreA = a.hourlyRent / 2 + a.deposit / 500 - (a.orderCount || 0) / 20
          const scoreB = b.hourlyRent / 2 + b.deposit / 500 - (b.orderCount || 0) / 20
          return scoreA - scoreB
        })
    }
  }, [filtered, sort])

  const openVehicle = (v: RentableVehicle) => {
    if (!verified) {
      toast('请先完成实名认证后再租车', 'warning')
      navigate('/profile')
      return
    }
    navigate(`/rent/${v.vehicleId}`)
  }

  if (loadingProfile) {
    return <div className="loading-line"><Icon name="refresh" size={18} />正在核验实名认证状态…</div>
  }

  return (
    <div>
      {!verified && (
        <div className="callout callout-warning">
          <Icon name="warning" size={18} chip />
          <div>
            <strong>线上租车需先完成实名认证</strong>
            <p>补全姓名、身份证号与手机号后即可下单。</p>
          </div>
          <button className="btn-sm btn-sm-primary" onClick={() => navigate('/profile')}>去实名认证</button>
        </div>
      )}

      {/* ===== 一行放完搜索、排序与刷新：搜索在最左，排序收进下拉 ===== */}
      <div className="rent-toolbar">
        <div className="rent-search">
          <Icon name="search" size={15} />
          <input
            value={keyword}
            onChange={e => setKeyword(e.target.value)}
            placeholder="搜车牌或品牌"
            aria-label="搜索车辆"
          />
          {keyword && (
            <button className="rent-search-clear" onClick={() => setKeyword('')} aria-label="清空搜索">
              <Icon name="close" size={12} />
            </button>
          )}
        </div>
        <label className="rent-sort-select">
          <Icon name="sort" size={15} />
          <select
            value={sort}
            onChange={e => setSort(e.target.value as SortKey)}
            aria-label="车辆排序方式"
            title={SORTS.find(s => s.key === sort)?.hint}
          >
            {SORTS.map(s => <option key={s.key} value={s.key}>{s.label}</option>)}
          </select>
          <Icon name="chevron-right" size={13} />
        </label>
        <button
          className="btn-sm btn-sm-default btn-icon-sm rent-refresh"
          onClick={() => loadVehicles()}
          disabled={loading}
          aria-label="刷新车辆列表"
          title="刷新"
        >
          <Icon name="refresh" size={15} />
        </button>
      </div>

      {/* ===== 车辆列表 ===== */}
      {loading && vehicles.length === 0 ? (
        <div className="loading-line"><Icon name="refresh" size={18} />正在加载可租车辆…</div>
      ) : sorted.length === 0 ? (
        <div className="empty">
          <Icon name="empty" size={40} chip />
          <div>{vehicles.length === 0 ? '当前没有可租车辆，请稍后再试或到店咨询' : '没有匹配的车辆，换个关键词试试'}</div>
        </div>
      ) : (
        <>
          <div className="rent-count">共 {sorted.length} 台可租</div>
          <div className="rent-grid">
            {sorted.map(v => (
              <button key={v.vehicleId} className="rent-card" onClick={() => openVehicle(v)}>
                <div className="rent-card-photo">
                  {v.imageUrl
                    ? <img src={v.imageUrl} alt={v.vehicleNo} loading="lazy" />
                    : <Icon name="vehicles" size={30} />}
                </div>
                <div className="rent-card-body">
                  <div className="rent-card-head">
                    <strong>{v.vehicleNo}</strong>
                    <span className="rent-card-brand">{v.brand || '未知品牌'}</span>
                  </div>
                  <div className="rent-card-price">
                    <strong>{money(v.hourlyRent)}</strong><small>/小时</small>
                    <span>{money(v.dailyRent)}/天</span>
                  </div>
                  <div className="rent-card-meta">
                    <span>押金 {money(v.deposit)}</span>
                    <span>{ageText(v.buyTime)}</span>
                    {(v.orderCount || 0) > 0 && <span>近 90 天 {v.orderCount} 单</span>}
                  </div>
                </div>
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
