import { useCallback, useEffect, useRef, useState } from 'react'
import { authApi } from '@/api'
import Icon from '@/components/Icon'

type Challenge = {
  id: string
  background: string
  piece: string
  width: number
  height: number
  pieceCanvas: number
  pieceY: number
  maxOffset: number
}

type Phase = 'loading' | 'idle' | 'checking' | 'success' | 'fail' | 'error'

/** 滑块按钮宽度，必须与 CSS 中 .slider-knob 的 width 保持一致 */
const KNOB = 46

/**
 * 登录滑动拼图验证弹窗。
 *
 * 弹窗只在用户点「登录」且账号密码通过前端基础校验后打开，不再把验证码塞进登录表单：
 * 表单更干净，也让“先填完再验人机”的顺序更清楚。
 *
 * 拼图由服务端生成，前端拿到的只是背景图、拼图块和尺寸；拖动结束后提交横向偏移，
 * 服务端比对缺口位置并签发一次性票据。票据由父组件带进登录请求，前端无法伪造“通过”。
 */
export default function SliderCaptchaModal({
  open,
  onClose,
  onVerified,
}: {
  open: boolean
  onClose: () => void
  onVerified: (ticket: string) => void
}) {
  const [challenge, setChallenge] = useState<Challenge | null>(null)
  const [phase, setPhase] = useState<Phase>('loading')
  const [offset, setOffset] = useState(0)
  const [dragging, setDragging] = useState(false)
  const [message, setMessage] = useState('')
  const trackRef = useRef<HTMLDivElement>(null)
  const offsetRef = useRef(0)
  const dragRef = useRef({ startX: 0, startOffset: 0, maxPx: 1 })
  const successTimer = useRef<number | null>(null)
  // 弹窗打开期间父组件会不断重渲染，回调用 ref 取最新值，避免 effect 因函数身份变化而清理成功定时器
  const phaseRef = useRef<Phase>('loading')
  const onCloseRef = useRef(onClose)
  phaseRef.current = phase
  onCloseRef.current = onClose

  const load = useCallback(async () => {
    setPhase('loading')
    setMessage('')
    setChallenge(null)
    setOffset(0)
    offsetRef.current = 0
    try {
      const res: any = await authApi.sliderCaptcha()
      // 统一信封 { data:{...} }；兼容调试服务把字段直接放在顶层
      const data = res?.data?.id && res?.data?.background ? res.data : res
      if (!data?.id || !data?.background || !data?.piece) throw new Error('验证图加载失败')
      setChallenge({
        id: data.id,
        background: data.background,
        piece: data.piece,
        width: Number(data.width) || 340,
        height: Number(data.height) || 190,
        pieceCanvas: Number(data.pieceCanvas) || 66,
        pieceY: Number(data.pieceY) || 0,
        maxOffset: Number(data.maxOffset) || 274,
      })
      setPhase('idle')
    } catch (e: any) {
      setPhase('error')
      setMessage(e?.message || '验证图加载失败，请重试')
    }
  }, [])

  useEffect(() => {
    if (open) void load()
    else {
      setChallenge(null)
      setPhase('loading')
      setOffset(0)
      offsetRef.current = 0
    }
  }, [open, load])

  useEffect(() => {
    if (!open) return
    const old = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && phaseRef.current !== 'checking') onCloseRef.current()
    }
    window.addEventListener('keydown', onKey)
    return () => {
      document.body.style.overflow = old
      window.removeEventListener('keydown', onKey)
      if (successTimer.current) window.clearTimeout(successTimer.current)
    }
  }, [open])

  const setOffsetSafe = (value: number) => {
    const max = challenge?.maxOffset ?? 0
    const next = Math.max(0, Math.min(max, value))
    offsetRef.current = next
    setOffset(next)
  }

  const verify = async () => {
    if (!challenge || phase === 'checking' || phase === 'success' || phase === 'fail') return
    setPhase('checking')
    setMessage('')
    try {
      const res: any = await authApi.verifySlider({
        id: challenge.id,
        offset: Math.round(offsetRef.current),
      })
      const ticket = res?.data?.ticket || res?.ticket
      if (!ticket) throw new Error(res?.message || '验证未通过')
      setPhase('success')
      setMessage('验证通过')
      successTimer.current = window.setTimeout(() => onVerified(String(ticket)), 520)
    } catch (e: any) {
      setPhase('fail')
      setMessage(e?.message || '拼图未对齐，请重新拖动滑块')
      setOffsetSafe(0)
    }
  }

  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!challenge || phase === 'checking' || phase === 'success' || phase === 'fail') return
    if (e.button !== undefined && e.button !== 0) return
    e.preventDefault()
    const trackW = trackRef.current?.clientWidth || 0
    dragRef.current = {
      startX: e.clientX,
      startOffset: offsetRef.current,
      maxPx: Math.max(1, trackW - KNOB),
    }
    setDragging(true)
    e.currentTarget.setPointerCapture(e.pointerId)
  }

  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragging || !challenge) return
    const maxPx = dragRef.current.maxPx
    const deltaPx = e.clientX - dragRef.current.startX
    const deltaLogical = (deltaPx / maxPx) * challenge.maxOffset
    setOffsetSafe(dragRef.current.startOffset + deltaLogical)
  }

  const onPointerUp = () => {
    if (!dragging) return
    setDragging(false)
    if (offsetRef.current > 0) void verify()
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (!challenge || phase === 'checking' || phase === 'success' || phase === 'fail') return
    const step = e.shiftKey ? 20 : 8
    if (e.key === 'ArrowRight' || e.key === 'ArrowUp') {
      e.preventDefault()
      setOffsetSafe(offsetRef.current + step)
    } else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') {
      e.preventDefault()
      setOffsetSafe(offsetRef.current - step)
    } else if (e.key === 'Home') {
      e.preventDefault()
      setOffsetSafe(0)
    } else if (e.key === 'End') {
      e.preventDefault()
      setOffsetSafe(challenge.maxOffset)
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      if (offsetRef.current > 0) void verify()
    }
  }

  if (!open) return null

  const ratio = challenge && challenge.maxOffset > 0 ? offset / challenge.maxOffset : 0
  const leftPct = challenge ? (offset / challenge.width) * 100 : 0
  const pieceWidthPct = challenge ? (challenge.pieceCanvas / challenge.width) * 100 : 0
  const pieceTopPct = challenge ? (challenge.pieceY / challenge.height) * 100 : 0

  return (
    <div
      className="modal-overlay slider-overlay"
      onMouseDown={e => { if (e.target === e.currentTarget && phase !== 'checking') onClose() }}
    >
      <div
        className="modal slider-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="slider-title"
        data-slider-id={challenge?.id || ''}
      >
        <header className="slider-head">
          <span className="slider-head-icon"><Icon name="shield" size={18} color="#fff" /></span>
          <div className="slider-head-text">
            <h3 id="slider-title">安全验证</h3>
            <p>拖动滑块，把拼图对准缺口</p>
          </div>
          <button
            type="button"
            className="close-btn"
            onClick={onClose}
            disabled={phase === 'checking'}
            aria-label="关闭验证"
          >
            <Icon name="close" size={18} />
          </button>
        </header>

        <div className="slider-body">
          <div className={`slider-canvas ${phase === 'success' ? 'is-success' : ''}`} aria-busy={phase === 'loading'}>
            {challenge && (
              <>
                <img className="slider-bg" src={challenge.background} alt="" draggable={false} />
                <img
                  className="slider-piece"
                  src={challenge.piece}
                  alt=""
                  draggable={false}
                  style={{
                    left: `${leftPct}%`,
                    top: `${pieceTopPct}%`,
                    width: `${pieceWidthPct}%`,
                  }}
                />
              </>
            )}
            {phase === 'loading' && <div className="slider-mask">正在生成验证图…</div>}
            {phase === 'error' && (
              <button type="button" className="slider-retry" onClick={() => void load()}>
                {message || '加载失败，点击重试'}
              </button>
            )}
            {phase === 'success' && (
              <div className="slider-success">
                <Icon name="check" size={20} color="#fff" />
                <span>验证通过</span>
              </div>
            )}
          </div>

          <div
            ref={trackRef}
            className={`slider-track ${dragging ? 'is-dragging' : ''} ${phase === 'success' ? 'is-success' : ''} ${phase === 'fail' ? 'is-fail' : ''}`}
          >
            <div className="slider-fill" style={{ width: `${Math.max(0, Math.min(100, ratio * 100))}%` }} />
            <span className="slider-track-text">
              {phase === 'checking' ? '正在校验…'
                : phase === 'success' ? '验证通过'
                  : phase === 'fail' ? '未对齐，请重试'
                    : dragging ? '松开完成验证' : '按住滑块，拖动完成拼图'}
            </span>
            <div
              className="slider-knob"
              role="slider"
              tabIndex={0}
              aria-label="拖动滑块完成拼图"
              aria-valuemin={0}
              aria-valuemax={challenge?.maxOffset ?? 0}
              aria-valuenow={Math.round(offset)}
              aria-valuetext={`${Math.round(offset)} / ${challenge?.maxOffset ?? 0}`}
              style={{ left: `calc(${ratio * 100}% - ${ratio * KNOB}px)` }}
              onPointerDown={onPointerDown}
              onPointerMove={onPointerMove}
              onPointerUp={onPointerUp}
              onPointerCancel={() => setDragging(false)}
              onKeyDown={onKeyDown}
            >
              <Icon name={phase === 'success' ? 'check' : 'chevron-right'} size={18} />
              {phase !== 'success' && <Icon name="chevron-right" size={18} />}
            </div>
          </div>

          <div className="slider-foot">
            {phase === 'fail'
              ? <button type="button" className="slider-again" onClick={() => void load()}>重新验证</button>
              : <span>{message || '拼图验证可防止脚本批量尝试登录'}</span>}
          </div>
        </div>
      </div>
    </div>
  )
}
