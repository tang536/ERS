import type { ReactNode } from 'react'
import Icon from '@/components/Icon'
import type { IconName } from '@/components/Icon'

/**
 * 带浮动标签的输入框。
 *
 * 为什么不用「外置 label + placeholder」：
 * 占位文字在输入后立刻消失，用户改到一半就看不到这个框是账号还是密码；
 * 浮动标签把字段名留在框内，聚焦/有值时上浮到边框上方，视觉上也不额外占高度。
 *
 * 结构上 label 绝对定位在输入框内部，因此必须用 htmlFor 绑定 input id，
 * 点标签仍能聚焦到输入框，键盘与读屏也能正确关联。
 */
export default function FloatingInput({
  id,
  label,
  icon,
  value,
  onChange,
  type = 'text',
  autoComplete,
  autoFocus,
  error,
  rightSlot,
  maxLength,
  inputMode,
  disabled,
  onKeyDown,
}: {
  id: string
  label: string
  icon?: IconName
  value: string
  onChange: (v: string) => void
  type?: string
  autoComplete?: string
  autoFocus?: boolean
  error?: string
  rightSlot?: ReactNode
  maxLength?: number
  inputMode?: 'text' | 'numeric'
  disabled?: boolean
  onKeyDown?: (e: React.KeyboardEvent<HTMLInputElement>) => void
}) {
  return (
    <div className={`float-field ${value ? 'has-value' : ''} ${error ? 'is-error' : ''}`}>
      <div className="input-affix">
        {icon && <Icon name={icon} size={16} className="input-affix-icon" />}
        <input
          id={id}
          type={type}
          value={value}
          placeholder=" "
          autoComplete={autoComplete}
          autoFocus={autoFocus}
          maxLength={maxLength}
          inputMode={inputMode}
          disabled={disabled}
          aria-invalid={!!error}
          aria-describedby={error ? `${id}-error` : undefined}
          onChange={e => onChange(e.target.value)}
          onKeyDown={onKeyDown}
        />
        {rightSlot}
      </div>
      <label className="float-label" htmlFor={id}>{label}</label>
    </div>
  )
}
