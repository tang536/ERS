import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { authApi } from '@/api'
import { useAuth, homePathOf } from '@/hooks/useAuth'
import { canAccess, isRegisteredPath } from '@/permissions'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'
import FloatingInput from './FloatingInput'
import SliderCaptchaModal from './SliderCaptchaModal'

/** 页签 key 与后端 MapRoleType 一一对应；选哪个页签就必须是哪个角色的账号 */
const STAFF_ROLES = [
  { key: 'admin', label: '管理员' },
  { key: 'reception', label: '前台' },
  { key: 'finance', label: '财务' },
]

/**
 * 工作人员登录面板。
 *
 * 与 CustomerLoginPanel 对称：只负责「门店员工」这一条登录链路。
 * 岗位页签会被后端校验（所选页签必须与账号真实角色一致），
 * 因此这里保留页签而不是让后端自己猜，避免用前台账号在管理员页签登录。
 *
 * @param onSwitchRole 切回顾客面板
 * @param autoFocus    是否自动聚焦账号框
 */
export default function StaffLoginPanel({
  onSwitchRole,
  autoFocus = false,
}: {
  onSwitchRole?: () => void
  autoFocus?: boolean
}) {
  const navigate = useNavigate()
  // 守卫把未登录的员工路由访问转到 /login?as=staff 时，会把原路径放进 state.from；
  // 登录成功后若该路径对本次登录的角色可访问，直接回去，省一次手工导航。
  const location = useLocation()
  const { toast } = useUI()
  const { login } = useAuth()

  const [roleType, setRoleType] = useState('admin')
  const [account, setAccount] = useState('')
  const [password, setPassword] = useState('')
  const [showPwd, setShowPwd] = useState(false)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(false)
  /** 登录前先弹滑动验证；验证通过拿到票据后才真正提交账号密码 */
  const [sliderOpen, setSliderOpen] = useState(false)

  const validate = () => {
    const e: Record<string, string> = {}
    if (!account.trim()) e.account = '请输入登录账号'
    if (!password) e.password = '请输入密码'
    setErrors(e)
    return Object.keys(e).length === 0
  }

  /** 前端基础校验通过后打开滑动验证弹窗 */
  const beginLogin = () => {
    if (loading) return
    if (!validate()) return
    setSliderOpen(true)
  }

  /** 滑动验证通过：带一次性票据提交登录 */
  const finishLogin = async (ticket: string) => {
    setSliderOpen(false)
    setLoading(true)
    try {
      const res: any = await authApi.staffLogin({
        loginAccount: account.trim(),
        password,
        roleType,
        sliderTicket: ticket,
      })
      const info = res.data
      login(info)
      toast(`欢迎回来，${info.userName}`, 'success')
      // 管理员发放的临时密码：先强制改密，改完再回原目标页
      if (info.mustChangePassword) {
        navigate('/change-password', { replace: true })
        return
      }
      // 回跳来源页：守卫转来登录时记录的原路径，仅当该路径对本次登录的角色可访问时才回跳
      const from = (location.state as { from?: string } | null)?.from
      const target = from && isRegisteredPath(from) && canAccess(from, info.roleName) ? from : homePathOf(info)
      navigate(target, { replace: true })
    } catch (e: any) {
      toast(e?.message || '登录失败，请稍后重试', 'error')
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <div className="lp">
      <header className="lp-head">
        <span className="lp-badge lp-badge--staff">
          <Icon name="shield" size={15} color="#fff" />
        </span>
        <div>
          <h2 className="lp-title">工作人员登录</h2>
          <p className="lp-sub">门店工作台 · 订单车辆结算</p>
        </div>
      </header>

      <div className="role-tabs">
        {STAFF_ROLES.map(r => (
          <button
            key={r.key}
            type="button"
            className={`role-tab ${roleType === r.key ? 'active' : ''}`}
            onClick={() => setRoleType(r.key)}
          >
            {r.label}
          </button>
        ))}
      </div>
      <p className="role-tabs-hint">请先选择你的岗位，再输入对应岗位的账号登录</p>

      <div className="form-group">
        <FloatingInput
          id="staff-account"
          label="登录账号"
          icon="user"
          value={account}
          autoFocus={autoFocus}
          autoComplete="username"
          error={errors.account}
          onChange={v => {
            setAccount(v)
            if (errors.account) setErrors({ ...errors, account: '' })
          }}
          onKeyDown={e => e.key === 'Enter' && beginLogin()}
        />
        {errors.account && <div className="field-error">{errors.account}</div>}
      </div>

      <div className="form-group">
        <FloatingInput
          id="staff-password"
          label="登录密码"
          icon="lock"
          type={showPwd ? 'text' : 'password'}
          value={password}
          autoComplete="current-password"
          error={errors.password}
          onChange={v => {
            setPassword(v)
            if (errors.password) setErrors({ ...errors, password: '' })
          }}
          onKeyDown={e => e.key === 'Enter' && beginLogin()}
          rightSlot={(
            <button type="button" className="input-affix-btn" onClick={() => setShowPwd(v => !v)} tabIndex={-1} aria-label="显示或隐藏密码">
              <Icon name={showPwd ? 'eye-off' : 'eye'} size={16} />
            </button>
          )}
        />
        {errors.password && <div className="field-error">{errors.password}</div>}
      </div>

      <button className="btn-primary" onClick={beginLogin} disabled={loading}>
        {loading ? '登录中…' : '登 录'}
      </button>

      <div className="lp-foot">
        <span className="lp-muted">工作人员账号由管理员在后台开通</span>
        {onSwitchRole && (
          <button type="button" className="lp-switch" onClick={onSwitchRole}>
            我是顾客
            <Icon name="chevron-right" size={14} />
          </button>
        )}
      </div>
      </div>

      <SliderCaptchaModal
        open={sliderOpen}
        onClose={() => setSliderOpen(false)}
        onVerified={ticket => { void finishLogin(ticket) }}
      />
    </>
  )
}
