import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import Icon from '@/components/Icon'

type Props = {
  start: string
  end: string
  onChange: (start: string, end: string) => void
  /** true 时值格式为 YYYY-MM-DDTHH:mm；false 为 YYYY-MM-DD */
  withTime?: boolean
  minDate?: string
  className?: string
  placeholder?: string
}

const pad = (n: number) => String(n).padStart(2, '0')
const localToday = () => {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
const datePart = (v: string) => (v || '').slice(0, 10)
const timePart = (v: string, fallback = '09:00') =>
  v && v.includes('T') ? v.slice(11, 16) : fallback
const parseDay = (key: string) => {
  const [y, m, d] = key.split('-').map(Number)
  return new Date(y, (m || 1) - 1, d || 1)
}
const dayKey = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
const monthStart = (key: string) => {
  const d = parseDay(key || localToday())
  return new Date(d.getFullYear(), d.getMonth(), 1)
}
const addMonth = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth() + n, 1)
const weekLabels = ['一', '二', '三', '四', '五', '六', '日']
const rangeText = (v: string, withTime?: boolean) => {
  if (!v) return ''
  return withTime ? v.replace('T', ' ') : v
}

/**
 * 日期区间选择器：一个日历完成起止选择。
 *
 * 交互与用户要求一致：第一次点击是开始日；第二次点击若晚于开始日则为结束日，
 * 否则视为重新开始（清空结束日，以本次点击作为新的开始日）。
 */
export default function DateRangeCalendar({
  start, end, onChange, withTime, minDate, className = '', placeholder = '选择日期区间',
}: Props) {
  const [open, setOpen] = useState(false)
  const [view, setView] = useState(() => monthStart(datePart(start) || datePart(end)))
  const [pos, setPos] = useState({ top: 0, left: 0, width: 328 })
  const triggerRef = useRef<HTMLButtonElement>(null)
  const popRef = useRef<HTMLDivElement>(null)

  const startDay = datePart(start)
  const endDay = datePart(end)
  const startTime = timePart(start)
  const endTime = timePart(end, startTime)

  const emit = (s: string, e: string) => {
    if (!withTime) { onChange(s, e); return }
    const st = timePart(start, '09:00')
    const et = timePart(end, st)
    onChange(s ? `${s}T${st}` : '', e ? `${e}T${et}` : '')
  }

  const choose = (day: string) => {
    if (minDate && day < minDate) return
    if (!startDay || (startDay && endDay)) {
      emit(day, '')
      return
    }
    if (day > startDay) {
      emit(startDay, day)
    } else {
      emit(day, '')
    }
  }

  const openPicker = () => {
    const r = triggerRef.current?.getBoundingClientRect()
    if (r) {
      const width = Math.min(336, window.innerWidth - 24)
      const left = Math.min(Math.max(8, r.left), window.innerWidth - width - 8)
      // 日历（含起止时间）约 430px 高；下方放不下时整体上移，
      // 避免手机端底部被裁掉看不到「完成」。
      const estimated = withTime ? 430 : 370
      let top = r.bottom + 6
      if (top + estimated > window.innerHeight - 8) {
        top = Math.max(8, window.innerHeight - 8 - estimated)
      }
      setPos({ top, left, width })
    }
    setView(monthStart(startDay || endDay))
    setOpen(true)
  }

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node
      if (triggerRef.current?.contains(t) || popRef.current?.contains(t)) return
      setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => {
      document.removeEventListener('mousedown', onDown)
    }
  }, [open])

  const cells = useMemo(() => {
    const first = new Date(view.getFullYear(), view.getMonth(), 1)
    const offset = (first.getDay() + 6) % 7
    const list: Date[] = []
    for (let i = 0; i < 42; i += 1) {
      list.push(new Date(view.getFullYear(), view.getMonth(), 1 - offset + i))
    }
    return list
  }, [view])

  const rangeLabel = startDay || endDay
    ? `${rangeText(start, withTime) || '选择开始'} → ${rangeText(end, withTime) || '选择结束'}`
    : placeholder

  return (
    <div className={`date-range ${className}`}>
      <button
        type="button"
        ref={triggerRef}
        className={`date-range-trigger ${startDay ? '' : 'is-empty'}`}
        onClick={() => (open ? setOpen(false) : openPicker())}
        aria-haspopup="dialog"
        aria-expanded={open}
      >
        <Icon name="clock" size={14} />
        <span>{rangeLabel}</span>
        <Icon name="chevron-right" size={13} className="date-range-caret" />
      </button>

      {open && createPortal(
        <div
          className="date-range-popover"
          ref={popRef}
          role="dialog"
          aria-label="选择日期区间"
          style={{ top: pos.top, left: pos.left, width: pos.width }}
        >
          <div className="date-range-head">
            <button type="button" className="dr-nav" onClick={() => setView(v => addMonth(v, -1))} aria-label="上个月">
              <Icon name="chevron-left" size={15} />
            </button>
            <strong>{view.getFullYear()} 年 {view.getMonth() + 1} 月</strong>
            <button type="button" className="dr-nav" onClick={() => setView(v => addMonth(v, 1))} aria-label="下个月">
              <Icon name="chevron-right" size={15} />
            </button>
          </div>

          <div className="date-range-week">
            {weekLabels.map(w => <span key={w}>{w}</span>)}
          </div>

          <div className="date-range-grid">
            {cells.map(d => {
              const key = dayKey(d)
              const outside = d.getMonth() !== view.getMonth()
              const disabled = !!minDate && key < minDate
              const isStart = key === startDay
              const isEnd = key === endDay
              const inRange = !!startDay && !!endDay && key > startDay && key < endDay
              return (
                <button
                  type="button"
                  key={key}
                  className={[
                    'dr-day',
                    outside ? 'is-outside' : '',
                    disabled ? 'is-disabled' : '',
                    isStart ? 'is-start' : '',
                    isEnd ? 'is-end' : '',
                    inRange ? 'in-range' : '',
                  ].filter(Boolean).join(' ')}
                  disabled={disabled}
                  onClick={() => choose(key)}
                >{d.getDate()}</button>
              )
            })}
          </div>

          {withTime && (
            <div className="date-range-times">
              <label>
                <span>起租时间</span>
                <input
                  type="time"
                  value={startDay ? startTime : ''}
                  disabled={!startDay}
                  onChange={e => {
                    const next = e.target.value
                    const endNext = endTime || next
                    onChange(startDay ? `${startDay}T${next}` : '', endDay ? `${endDay}T${endNext}` : '')
                  }}
                />
              </label>
              <label>
                <span>到期时间</span>
                <input
                  type="time"
                  value={endDay ? endTime : ''}
                  disabled={!endDay}
                  onChange={e => {
                    const next = e.target.value
                    onChange(start ? start : '', endDay ? `${endDay}T${next}` : '')
                  }}
                />
              </label>
            </div>
          )}

          <div className="date-range-foot">
            <button type="button" className="btn-sm btn-sm-default" onClick={() => { onChange('', ''); setOpen(false) }}>清除</button>
            <button
              type="button"
              className="btn-sm btn-sm-default"
              onClick={() => { emit(localToday(), ''); setView(monthStart(localToday())) }}
            >今天</button>
            <button type="button" className="btn-sm btn-sm-primary" onClick={() => setOpen(false)}>完成</button>
          </div>
        </div>,
        document.body,
      )}
    </div>
  )
}
