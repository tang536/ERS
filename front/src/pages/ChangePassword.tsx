import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { authApi } from '@/api'
import { clearAuth, useAuth } from '@/hooks/useAuth'
import { useUI } from '@/components/UIProvider'
import Icon from '@/components/Icon'

/**
 * 员工临时密码强制改密页。
 *
 * 管理员在账号管理里生成一次性临时密码后，登录响应会带 mustChangePassword=true；
 * 路由守卫把员工锁在本页，后端 JWT 也带 must_change 声明，改密前只放行本接口与退出登录。
 * 改密成功后后端吊销全部会话，前端清登录态回到登录页，避免旧令牌继续可用。
 */
export default function ChangePassword() {
  const { user } = useAuth()
  const { toast } = useUI()
  const navigate = useNavigate()
  const [oldPassword, setOldPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [show, setShow] = useState(false)
  const [saving, setSaving] = useState(false)

  const handleSubmit = async () => {
    if (!oldPassword) { toast('请输入临时密码', 'warning'); return }
    if (newPassword.length < 8 || !/[A-Za-z]/.test(newPassword) || !/\d/.test(newPassword)) {
      toast('新密码至少 8 位，并同时包含字母和数字', 'warning'); return
    }
    if (newPassword !== confirmPassword) { toast('两次输入的新密码不一致', 'warning'); return }
    setSaving(true)
    try {
      await authApi.staffChangePassword(oldPassword, newPassword)
      toast('密码已修改，请用新密码重新登录', 'success')
      clearAuth()
      navigate('/login', { replace: true })
    } catch (e: any) {
      toast(e?.message || '改密失败，请稍后重试', 'error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="card" style={{ maxWidth: 560, margin: '24px auto' }}>
      <div className="card-title">
        <Icon name="lock" size={18} chip />修改临时密码
        <span className="card-title-count">首次登录必填</span>
      </div>
      <div className="callout callout-warning" style={{ marginBottom: 16 }}>
        <Icon name="warning" size={18} chip />
        <div>
          <strong>你正在使用管理员发放的临时密码</strong>
          <p>临时密码 24 小时内有效；修改完成前，系统只允许访问本页和退出登录。</p>
        </div>
      </div>

      <div className="form-group">
        <label>当前临时密码</label>
        <div className="input-affix">
          <Icon name="lock" size={16} className="input-affix-icon" />
          <input type={show ? 'text' : 'password'} value={oldPassword} onChange={e => setOldPassword(e.target.value)}
                 placeholder="请输入临时密码" autoComplete="current-password" />
          <button type="button" className="input-affix-btn" onClick={() => setShow(v => !v)} tabIndex={-1}>
            <Icon name={show ? 'eye-off' : 'eye'} size={16} />
          </button>
        </div>
      </div>
      <div className="form-group">
        <label>新密码</label>
        <input type={show ? 'text' : 'password'} value={newPassword} onChange={e => setNewPassword(e.target.value)}
               placeholder="至少 8 位，含字母和数字" autoComplete="new-password" />
      </div>
      <div className="form-group">
        <label>确认新密码</label>
        <input type={show ? 'text' : 'password'} value={confirmPassword} onChange={e => setConfirmPassword(e.target.value)}
               placeholder="再次输入新密码" autoComplete="new-password" />
      </div>
      <p className="field-hint">
        当前账号：{user?.userName || user?.userId} · 改密成功后所有设备都会下线，需要重新登录。
      </p>
      <div className="form-actions">
        <button className="btn-sm btn-sm-primary" onClick={handleSubmit} disabled={saving}>
          {saving ? '保存中…' : '保存新密码'}
        </button>
        <button className="btn-sm btn-sm-default" onClick={() => { clearAuth(); navigate('/login', { replace: true }) }}>
          退出登录
        </button>
      </div>
    </div>
  )
}
