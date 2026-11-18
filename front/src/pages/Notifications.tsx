import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { messageApi } from '@/api'
import { useAuth } from '@/hooks/useAuth'
import { useUI } from '@/components/UIProvider'
import Icon, { type IconName } from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'
import type { NotificationItem } from '@/types'

const LEVEL_ICON: Record<string, IconName> = {
  danger: 'warning',
  warning: 'warning',
  success: 'success',
  info: 'info',
}

/**
 * 消息中心。
 *
 * 不是顶栏「消息通知」下拉面板的放大版：这里按未读/分类筛选、按日期分组，
 * 保留完整正文和可点击的去处理入口，客服回复、退款、订单提醒都能回看。
 */
export default function Notifications() {
  const { user } = useAuth()
  const { toast } = useUI()
  const navigate = useNavigate()
  const isCustomer = user?.userType === 'customer'
  const [items, setItems] = useState<NotificationItem[]>([])
  const [unread, setUnread] = useState(0)
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState('all')

  const load = () => {
    setLoading(true)
    messageApi.list()
      .then((res: any) => {
        const data = res.data || {}
        setItems(data.items || [])
        setUnread(data.unread || 0)
      })
      .catch((e: any) => toast(e?.message || '消息加载失败', 'error'))
      .finally(() => setLoading(false))
  }

  useEffect(() => { load() }, [])

  const markAllRead = async () => {
    try {
      await messageApi.markAllRead()
      setItems(prev => prev.map(n => ({ ...n, isRead: true, readTime: new Date().toISOString() })))
      setUnread(0)
      toast('已全部标记为已读', 'success')
    } catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  const openItem = (n: NotificationItem) => {
    if (!n.isRead && n.messageId != null) {
      setItems(prev => prev.map(x => (x.messageId === n.messageId ? { ...x, isRead: true } : x)))
      setUnread(c => Math.max(0, c - 1))
      messageApi.markRead([n.messageId]).catch(() => load())
    }
    if (n.link) navigate(n.link)
  }

  const removeItem = async (n: NotificationItem) => {
    if (n.messageId == null) return
    try {
      await messageApi.remove([n.messageId])
      setItems(prev => prev.filter(x => x.messageId !== n.messageId))
      if (!n.isRead) setUnread(c => Math.max(0, c - 1))
      toast('已删除 1 条', 'success')
    } catch (e: any) { toast(e?.message || '删除失败', 'error') }
  }

  const categories = Array.from(new Set(items.map(n => n.category).filter(Boolean)))
  const filters = [
    { key: 'all', label: '全部' },
    { key: 'unread', label: `未读${unread > 0 ? ` ${unread}` : ''}` },
    ...categories.map(c => ({ key: c, label: c })),
  ]
  const filtered = items.filter(n => {
    if (filter === 'all') return true
    if (filter === 'unread') return !n.isRead
    return n.category === filter
  })
  const groups = filtered.reduce<Record<string, NotificationItem[]>>((acc, n) => {
    const day = (n.time || '').slice(0, 10) || '更早'
    ;(acc[day] ||= []).push(n)
    return acc
  }, {})
  const dayLabel = (day: string) => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return day
    const d = new Date(`${day}T00:00:00`)
    return d.toLocaleDateString('zh-CN', { month: 'long', day: 'numeric', weekday: 'long' })
  }

  return (
    <div className="notification-page">
      <div className="card">
        <div className="card-title">
          {isCustomer && (
            <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => navigate('/profile')} title="返回个人中心">
              <Icon name="chevron-left" size={15} /><span className="btn-label">返回</span>
            </button>
          )}
          <Icon name="bell" size={18} chip />消息中心
          <div className="card-title-actions">
            <button className="btn-sm btn-sm-default" onClick={load} disabled={loading}>
              <Icon name="refresh" size={14} />刷新
            </button>
          </div>
        </div>

        <div className="notify-summary">
          <div className="notify-summary-count">
            <strong>{unread}</strong>
            <span>条未读</span>
          </div>
          <div className="notify-summary-text">
            <strong>{unread > 0 ? '有新的客服回复或系统提醒' : '所有消息都已读'}</strong>
            <small>共 {items.length} 条消息，按日期归档，可随时回看</small>
          </div>
          {unread > 0 && (
            <button className="btn-sm btn-sm-primary" onClick={markAllRead}>
              <Icon name="check" size={14} color="#fff" />全部已读
            </button>
          )}
        </div>

        <div className="notify-filters">
          {filters.map(f => (
            <button
              key={f.key}
              className={`choice-chip ${filter === f.key ? 'is-active' : ''}`}
              onClick={() => setFilter(f.key)}
            >{f.label}</button>
          ))}
        </div>

        {Object.entries(groups).map(([day, list]) => (
          <section className="notify-day" key={day}>
            <div className="notify-day-title">{dayLabel(day)}</div>
            {list.map(n => (
              <article
                key={n.id || n.messageId}
                className={`notify-list-item ${n.isRead ? '' : 'is-unread'} level-${n.level || 'info'}`}
              >
                <button className="notify-item-main" onClick={() => openItem(n)}>
                  <span className="notify-item-icon"><Icon name={LEVEL_ICON[n.level] || 'info'} size={18} /></span>
                  <span className="notify-item-text">
                    <strong>{n.title}</strong>
                    <span>{n.description}</span>
                    <small>{fmtDT(n.time)}{n.category ? ` · ${n.category}` : ''}</small>
                  </span>
                  {n.link && <span className="notify-item-go"><Icon name="chevron-right" size={16} /></span>}
                </button>
                {n.messageId != null && (
                  <button className="btn-sm btn-sm-default btn-icon-sm notify-item-del" title="删除这条消息" onClick={() => removeItem(n)}>
                    <Icon name="trash" size={14} />
                  </button>
                )}
              </article>
            ))}
          </section>
        ))}

        {loading && <div className="loading-line"><Icon name="refresh" size={18} />加载中…</div>}
        {!loading && filtered.length === 0 && (
          <div className="empty"><Icon name="empty" size={40} chip /><div>{filter === 'unread' ? '没有未读消息' : '暂无消息'}</div></div>
        )}
      </div>
    </div>
  )
}
