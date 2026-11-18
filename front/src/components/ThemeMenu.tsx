import { useEffect, useRef, useState } from 'react'
import Icon from '@/components/Icon'
import { useTheme } from '@/hooks/useTheme'
import type { Theme } from '@/hooks/useTheme'

const META: Record<Theme, { label: string; icon: 'sun' | 'moon' | 'palette'; desc: string }> = {
  light: { label: '浅色', icon: 'sun', desc: '暖白纸面，默认' },
  dark: { label: '深色', icon: 'moon', desc: '夜间与低光环境' },
  pink: { label: '粉色', icon: 'palette', desc: '玫瑰粉纸面' },
}

/**
 * 主题选择菜单。
 *
 * 顶栏与落地页共用同一个组件：只有一个可用主题时整颗按钮隐藏（配置即界面）；
 * 有两种以上时点开列出全部选项，直接选中而不是「点一下换一个」的盲轮换。
 * 选择只写 localStorage 与 html[data-theme]，不涉及后端偏好，换设备不会互相影响。
 */
export default function ThemeMenu({ variant = 'header' }: { variant?: 'header' | 'landing' }) {
  const { theme, available, canToggle, setTheme } = useTheme()
  const [open, setOpen] = useState(false)
  const boxRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!open) return
    const onDoc = (e: MouseEvent) => {
      if (!boxRef.current?.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDoc)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!canToggle) return null
  const current = META[theme]

  return (
    <div className={`theme-menu theme-menu--${variant}`} ref={boxRef}>
      <button
        type="button"
        className={variant === 'landing' ? 'landing-theme-btn' : 'header-icon-btn'}
        title={`界面主题：${current.label}（点击切换）`}
        aria-label={`界面主题：${current.label}`}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(v => !v)}
      >
        <Icon name={current.icon} size={variant === 'landing' ? 17 : 19} />
      </button>

      {open && (
        <div className="theme-menu-pop" role="menu">
          <div className="theme-menu-title">界面主题</div>
          {available.map(t => {
            const meta = META[t]
            return (
              <button
                key={t}
                type="button"
                role="menuitemradio"
                aria-checked={theme === t}
                className={`theme-menu-item ${theme === t ? 'is-active' : ''}`}
                onClick={() => { setTheme(t); setOpen(false) }}
              >
                <span className={`theme-swatch theme-swatch--${t}`} aria-hidden="true" />
                <span className="theme-menu-text">
                  <strong>{meta.label}</strong>
                  <small>{meta.desc}</small>
                </span>
                {theme === t && <Icon name="check" size={15} />}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
