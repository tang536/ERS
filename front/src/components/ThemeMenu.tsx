import Icon from '@/components/Icon'
import { useTheme } from '@/hooks/useTheme'
import type { Theme } from '@/hooks/useTheme'

const META: Record<Theme, { label: string; icon: 'sun' | 'moon' | 'palette' }> = {
  light: { label: '浅色', icon: 'sun' },
  dark: { label: '深色', icon: 'moon' },
  pink: { label: '粉色', icon: 'palette' },
}

/**
 * 主题切换按钮。
 *
 * 顶栏与落地页共用同一个组件：只有一个可用主题时整颗按钮隐藏（配置即界面）；
 * 有两种以上时点击一次就按可用顺序轮换到下一个，不弹菜单、不额外打断操作。
 * 选择只写 localStorage 与 html[data-theme]，不涉及后端偏好，换设备不会互相影响。
 */
export default function ThemeMenu({ variant = 'header' }: { variant?: 'header' | 'landing' }) {
  const { theme, available, canToggle, toggleTheme } = useTheme()

  if (!canToggle) return null
  const current = META[theme]
  const nextTheme = available[(available.indexOf(theme) + 1) % available.length] ?? theme
  const next = META[nextTheme]

  return (
    <div className={`theme-menu theme-menu--${variant}`}>
      <button
        type="button"
        className={variant === 'landing' ? 'landing-theme-btn' : 'header-icon-btn'}
        title={`界面主题：${current.label}，点击切换到${next.label}`}
        aria-label={`界面主题：${current.label}，点击切换到${next.label}`}
        onClick={toggleTheme}
      >
        <Icon name={current.icon} size={variant === 'landing' ? 17 : 19} />
      </button>
    </div>
  )
}
