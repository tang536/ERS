import { useEffect, useState, useRef, type CSSProperties } from 'react'
import { authApi, customerApi, pointApi, myExtApi, messageApi } from '@/api'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '@/hooks/useAuth'
import { useUI } from '@/components/UIProvider'
import { logoutEverywhere } from '@/utils/session'
import { fmtD } from '@/utils/datetime'
import Icon from '@/components/Icon'
import Avatar from '@/components/Avatar'
import ImageLightbox from '@/components/ImageLightbox'
import { qrDataUrl } from '@/utils/qrcode'
import type { PointSummary, ReferralStats, CustomerCredit } from '@/types'
import { TicketModal } from './components/CustomerExtras'

export default function Profile() {
  const { user, login } = useAuth()
  const { toast, confirm, prompt } = useUI()
  const navigate = useNavigate()
  const [info, setInfo] = useState<any>(null)
  const [loading, setLoading] = useState(true)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState({ fullName: '', idCard: '', contactPhone: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  /** 积分概要：个人资料卡展示余额，明细跳会员中心 */
  const [points, setPoints] = useState<PointSummary | null>(null)
  const [referral, setReferral] = useState<ReferralStats | null>(null)
  const [credit, setCredit] = useState<CustomerCredit | null>(null)
  const [ticketOpen, setTicketOpen] = useState(false)
  /** 顾客消息中心未读数：个人中心的「消息通知」按钮上直接显示 */
  const [unread, setUnread] = useState(0)
  /** 推荐码二维码（新人扫码后直达注册页并自动填码） */
  const [refQr, setRefQr] = useState<string | null>(null)
  /** 邀请与奖励明细弹窗 */
  const [refDetailOpen, setRefDetailOpen] = useState(false)

  const load = () => {
    if (!user?.userId) return
    setLoading(true)
    customerApi.get(user.userId)
      .then((res: any) => {
        setInfo(res.data)
        setForm({
          fullName: res.data?.fullName || '',
          idCard: res.data?.idCard || '',
          contactPhone: res.data?.contactPhone || '',
        })
      })
      .catch((e: any) => toast(e?.message || '个人信息加载失败', 'error'))
      .finally(() => setLoading(false))
  }

  useEffect(load, [user?.userId])
  useEffect(() => {
    pointApi.summary()
      .then((res: any) => setPoints(res.data || null))
      .catch(() => setPoints(null))
    myExtApi.referral()
      .then((res: any) => setReferral(res.data || null))
      .catch(() => setReferral(null))
    myExtApi.credit()
      .then((res: any) => setCredit(res.data || null))
      .catch(() => setCredit(null))
    messageApi.list()
      .then((res: any) => setUnread(Number(res.data?.unread || 0)))
      .catch(() => setUnread(0))
  }, [])

  // 客服回复消息里的链接带 ticket=1：直接打开工单弹窗，顾客不用再找「联系客服」
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get('ticket') === '1') setTicketOpen(true)
  }, [])

  const validate = () => {
    const e: Record<string, string> = {}
    if (!form.fullName.trim()) e.fullName = '请填写姓名'
    if (!form.idCard.trim()) e.idCard = '租车必备，请填写身份证号'
    else if (!/^\d{17}[\dXx]$/.test(form.idCard.trim())) e.idCard = '身份证号需为 18 位'
    if (!form.contactPhone.trim()) e.contactPhone = '租车必备，请填写手机号'
    else if (!/^1[3-9]\d{9}$/.test(form.contactPhone.trim())) e.contactPhone = '手机号需为 11 位有效号码'
    setErrors(e)
    return Object.keys(e).length === 0
  }

  const handleSave = async () => {
    if (!validate()) return
    setSaving(true)
    try {
      await authApi.updateCustomerProfile({
        fullName: form.fullName.trim(),
        idCard: form.idCard.trim(),
        contactPhone: form.contactPhone.trim(),
      })
      toast('资料已保存，到店租车时可直接开单', 'success')
      setEditing(false)
      // 同步更新本地登录态，避免每次刷新重新提示完善资料
      if (user) login({ ...user, profileCompleted: true })
      load()
    } catch (e: any) {
      toast(e?.message || '保存失败', 'error')
    } finally {
      setSaving(false)
    }
  }

  /** 用户自主注销：二次确认 → 输入登录密码 → 后端销户（未完结订单会被拦截） */
  const handleDeactivate = async () => {
    const ok = await confirm({
      title: '注销账号',
      message: '注销后该账号将无法再登录，用户名会被释放。历史订单与操作日志仍会保留以便追溯。确定要继续吗？',
      type: 'error',
      danger: true,
      confirmText: '继续注销',
    })
    if (!ok) return

    const pwd = await prompt({
      title: '身份验证',
      message: '请输入当前账号的登录密码以完成注销',
      inputType: 'password',
      placeholder: '当前登录密码',
      confirmText: '确认注销',
      validate: v => (v.trim() ? null : '请输入登录密码'),
    })
    if (!pwd) return

    try {
      await authApi.deactivate(pwd.trim())
      toast('账号已注销，正在退出…', 'success')
      // 注销成功后后端已强制清掉会话，本地登出也必须走统一入口
      window.setTimeout(() => { logoutEverywhere(); navigate('/login', { replace: true }) }, 700)
    } catch (e: any) {
      toast(e?.message || '注销失败', 'error')
    }
  }

  // ----- 修改密码（账号安全卡内联表单） -----
  // 复杂度规则与注册页一致（≥8 位且同时含字母和数字），前端先拦一道，最终以后端为准
  const [pwdOpen, setPwdOpen] = useState(false)
  const [pwdForm, setPwdForm] = useState({ oldPassword: '', newPassword: '', confirmPassword: '' })
  const [pwdErrors, setPwdErrors] = useState<Record<string, string>>({})
  const [pwdSaving, setPwdSaving] = useState(false)
  /** 三个密码框各自的「小眼睛」：分别记，避免点一个把另外两个也点亮 */
  const [pwdShow, setPwdShow] = useState({ oldPassword: false, newPassword: false, confirmPassword: false })
  /** 身份证号的明文/遮字开关：顾客改自己的证件号时默认可见（自己核对方便），点眼睛转成圆点 */
  const [idShow, setIdShow] = useState(true)
  const [uploadingAvatar, setUploadingAvatar] = useState(false)
  /** 上传接口返回的最新地址；用于立即刷新，之后以数据库 avatar_url 为准 */
  const [avatarSrc, setAvatarSrc] = useState<string | null>(null)
  /** 头像大图预览的地址；null 表示没有打开 */
  const [avatarZoom, setAvatarZoom] = useState<string | null>(null)
  /** 真实头像是否已加载：决定是否显示「点击查看原图」提示 */
  const [avatarReady, setAvatarReady] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const validatePwd = () => {
    const e: Record<string, string> = {}
    if (!pwdForm.oldPassword) e.oldPassword = '请输入原密码'
    if (!pwdForm.newPassword) e.newPassword = '请输入新密码'
    else if (pwdForm.newPassword.length < 8 || !/[A-Za-z]/.test(pwdForm.newPassword) || !/\d/.test(pwdForm.newPassword))
      e.newPassword = '密码至少 8 位，且需同时包含字母和数字'
    else if (pwdForm.newPassword === pwdForm.oldPassword) e.newPassword = '新密码不能与原密码相同'
    if (pwdForm.confirmPassword !== pwdForm.newPassword) e.confirmPassword = '两次输入的新密码不一致'
    setPwdErrors(e)
    return Object.keys(e).length === 0
  }

  const handleChangePassword = async () => {
    if (!validatePwd()) return
    setPwdSaving(true)
    try {
      await authApi.changePassword(pwdForm.oldPassword, pwdForm.newPassword)
      // 改密后后端会强制下线全部会话（凭据已变，旧 token 不应继续可用）。
      // 这里主动退出并说明原因，好过让用户撞上「下一次点页面突然被踢回登录页」。
      toast('密码已修改，请用新密码重新登录', 'success')
      setPwdOpen(false)
      setPwdForm({ oldPassword: '', newPassword: '', confirmPassword: '' })
      setPwdErrors({})
      window.setTimeout(() => { logoutEverywhere(); navigate('/login', { replace: true }) }, 700)
    } catch (e: any) {
      toast(e?.message || '修改失败', 'error')
    } finally {
      setPwdSaving(false)
    }
  }

  if (loading) return <div className="loading-line"><Icon name="refresh" size={18} />加载中…</div>
  if (!info) return <div className="empty"><Icon name="empty" size={40} chip /><div>未获取到个人信息</div></div>

  const complete = info.profileCompleted !== false && !!info.idCard && !!info.contactPhone

  return (
    <div>
      <div className="profile-hero">
        <div className="profile-avatar-wrap">
          <Avatar
            className="profile-avatar"
            name={info?.fullName || user?.userName}
            src={avatarSrc || info?.avatarUrl || user?.avatarUrl || undefined}
            onOpen={setAvatarZoom}
            onLoaded={() => setAvatarReady(true)}
          />
          <button className="avatar-edit-btn" onClick={() => fileInputRef.current?.click()} disabled={uploadingAvatar} title="更换头像（原图不压缩）">
            <Icon name="camera" size={14} color="#fff" />
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            style={{ display: 'none' }}
            onChange={async (e) => {
              const f = e.target.files?.[0]
              e.target.value = ''
              if (!f) return
              setUploadingAvatar(true)
              try {
                const res: any = await authApi.uploadAvatar(f)
                const url = res?.data?.url as string | undefined
                if (url) {
                  setAvatarSrc(url)
                  setInfo({ ...info, avatarUrl: url })
                  if (user) login({ ...user, avatarUrl: url })
                }
                toast('头像已更新（原图未压缩）', 'success')
              } catch (err: any) {
                toast(err?.message || '头像上传失败', 'error')
              } finally { setUploadingAvatar(false) }
            }}
          />
        </div>
        <div className="profile-hero-info">
          <h2>{info.fullName}</h2>
          <div className="profile-hero-tags">
            <span className="status-tag status-primary">{info.memberLevel}</span>
            <span className="status-tag status-success">{(info.memberDiscount * 10).toFixed(1)} 折优惠</span>
            {complete
              ? <span className="status-tag status-success">租车资料已完善</span>
              : <span className="status-tag status-warning">租车资料待完善</span>}
          </div>
          <p>注册时间：{fmtD(info.createTime)}</p>
          {avatarReady && <p className="profile-avatar-hint">点击头像可查看原图</p>}
        </div>
      </div>

      {/* ===== 我的服务：订单 / 会员 / 优惠券 =====
          这三项原来是顶部导航与底部标签栏的一级入口，占掉顾客端一半的导航格，
          但日常使用频率全部低于「租车」本身。收成个人中心里的一排小按钮后，
          导航只留租车与个人中心，功能一个不少，入口更集中。 */}
      <div className="profile-services">
        <button className="profile-service-btn" onClick={() => navigate('/my-orders')} title="我的订单">
          <span className="profile-service-icon"><Icon name="my-orders" size={22} /></span>
          <span className="profile-service-text">我的订单</span>
        </button>
        <button className="profile-service-btn" onClick={() => navigate('/membership')} title="会员中心">
          <span className="profile-service-icon"><Icon name="points" size={22} /></span>
          <span className="profile-service-text">会员中心</span>
        </button>
        <button className="profile-service-btn" onClick={() => navigate('/coupons')} title="我的优惠券">
          <span className="profile-service-icon"><Icon name="coupon" size={22} /></span>
          <span className="profile-service-text">我的优惠券</span>
        </button>
        <button className="profile-service-btn" onClick={() => navigate('/notifications')} title="消息通知">
          <span className="profile-service-icon">
            <Icon name="info" size={22} />
            {unread > 0 && <span className="profile-unread-dot">{unread > 99 ? '99+' : unread}</span>}
          </span>
          <span className="profile-service-text">消息通知</span>
        </button>
        <button className="profile-service-btn" onClick={() => setTicketOpen(true)} title="联系客服">
          <span className="profile-service-icon"><Icon name="user" size={22} /></span>
          <span className="profile-service-text">联系客服</span>
        </button>
      </div>

      {referral && (
        <div className="card">
          <div className="card-title"><Icon name="points" size={18} chip />老带新推荐</div>
          <div className="referral-row">
            <div>
              <span className="detail-label">我的推荐码</span>
              <div className="referral-code mono">{referral.referralCode || '—'}</div>
            </div>
            <div className="referral-stats">
              <div><strong>{referral.invitedCount}</strong><span>已邀请</span></div>
              <div><strong>¥{Number(referral.rewardTotal || 0).toFixed(0)}</strong><span>累计奖励</span></div>
            </div>
            <div className="referral-actions">
              <button
                className="btn-sm btn-sm-default"
                onClick={() => {
                  if (!referral.referralCode) return
                  navigator.clipboard?.writeText(referral.referralCode)
                    .then(() => toast('推荐码已复制', 'success'))
                    .catch(() => toast('复制失败，请手动选择', 'warning'))
                }}
              >复制推荐码</button>
              <button
                className="btn-sm btn-sm-primary"
                onClick={async () => {
                  if (!referral.referralCode) return
                  const url = `${window.location.origin}/login?as=customer&ref=${encodeURIComponent(referral.referralCode)}`
                  try { setRefQr(await qrDataUrl(url, { size: 320 })) }
                  catch { toast('二维码生成失败，请稍后重试', 'error') }
                }}
              ><Icon name="qrcode" size={14} color="#fff" />邀请二维码</button>
              <button className="btn-sm btn-sm-default" onClick={() => setRefDetailOpen(true)}>
                邀请明细
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-title">
          <Icon name="profile" size={18} chip />个人资料
          <button className="card-title-action" onClick={() => { setEditing(true); setErrors({}) }}>编辑资料</button>
        </div>

        {!complete && !editing && (
          <div className="callout callout-warning">
            <Icon name="warning" size={18} chip />
            <div>
              <strong>尚未完成实名认证</strong>
              <p>姓名与手机号在租车时采集，补全后即可自助下单，到店也能直接开单。</p>
            </div>
          </div>
        )}

        <div className="detail-grid">
          <div className="detail-item"><span className="detail-label">用户名</span><span className="detail-value mono">{info.loginAccount || '—'}</span></div>
          <div className="detail-item"><span className="detail-label">姓名</span><span className="detail-value">{info.fullName}</span></div>
          <div className="detail-item"><span className="detail-label">身份证号</span><span className="detail-value mono">{info.idCard || '待完善'}</span></div>
          <div className="detail-item"><span className="detail-label">手机号</span><span className="detail-value">{info.contactPhone || '待完善'}</span></div>
          <div className="detail-item">
            <span className="detail-label">积分余额</span>
            <span className="detail-value">
              {points ? `${points.balance} 分` : '—'}
              <button className="btn-sm btn-sm-default" style={{ marginLeft: 8 }} onClick={() => navigate('/membership')}>查看明细</button>
            </span>
          </div>
        </div>
      </div>

      {credit && (
        <div className="card">
          <div className="card-title"><Icon name="shield" size={18} chip />信用与免押</div>
          <div className="detail-grid">
            <div className="detail-item"><span className="detail-label">实名状态</span><span className="detail-value"><span className={`status-tag ${credit.realNameStatus === '已认证' ? 'status-success' : 'status-warning'}`}>{credit.realNameStatus}</span></span></div>
            <div className="detail-item"><span className="detail-label">信用额度</span><span className="detail-value">¥{Number(credit.creditLimit).toFixed(2)}</span></div>
            <div className="detail-item"><span className="detail-label">免押额度</span><span className="detail-value">¥{Number(credit.depositWaiverLimit).toFixed(2)}</span></div>
            <div className="detail-item"><span className="detail-label">客户类型</span><span className="detail-value">{credit.isCorporate ? `企业客户 · ${credit.companyName || '—'}` : '个人客户'}</span></div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-title">
          <Icon name="shield" size={18} chip />账号安全
          <button className="card-title-action" onClick={() => { setPwdOpen(true); setPwdErrors({}) }}>修改密码</button>
        </div>

        <p className="muted" style={{ margin: '8px 0 0' }}>修改密码需先验证原密码；新密码与注册同样要求「至少 8 位且同时包含字母和数字」。</p>

        <div className="danger-zone">
          <div className="danger-zone-text">
            <strong>注销账号</strong>
            <p>
              注销后账号无法再登录，用户名可重新注册；历史订单、结算记录与操作日志仍保留。
              存在未完结订单时，需先完成归还结算。
            </p>
          </div>
          <button className="btn-sm btn-sm-danger" onClick={handleDeactivate}>
            <Icon name="warning" size={15} color="#fff" />注销账号
          </button>
        </div>
      </div>

      {/* 编辑资料弹窗 */}
      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>编辑个人资料</span>
              <button className="close-btn" onClick={() => setEditing(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>姓名 <span className="req">*</span></label>
                <input
                  className={errors.fullName ? 'has-error' : ''}
                  value={form.fullName}
                  onChange={e => { setForm({ ...form, fullName: e.target.value }); setErrors({ ...errors, fullName: '' }) }}
                  placeholder="请输入真实姓名"
                />
                {errors.fullName && <div className="field-error">{errors.fullName}</div>}
              </div>
              <div className="form-group">
                <label>身份证号 <span className="req">*</span></label>
                {/* 身份证号带「小眼睛」：默认可见便于核对，点一下转成圆点遮字（店员在旁时不泄露） */}
                <div className={`input-affix ${errors.idCard ? 'has-error' : ''}`}>
                  <input
                    value={form.idCard}
                    onChange={e => { setForm({ ...form, idCard: e.target.value }); setErrors({ ...errors, idCard: '' }) }}
                    placeholder="18 位身份证号"
                    spellCheck={false}
                    style={!idShow ? { WebkitTextSecurity: 'disc' } as CSSProperties : undefined}
                  />
                  <button
                    type="button"
                    className="input-affix-btn"
                    onClick={() => setIdShow(v => !v)}
                    tabIndex={-1}
                    title={idShow ? '隐藏身份证号' : '显示身份证号'}
                    aria-label={idShow ? '隐藏身份证号' : '显示身份证号'}
                  >
                    <Icon name={idShow ? 'eye-off' : 'eye'} size={16} />
                  </button>
                </div>
                {errors.idCard && <div className="field-error">{errors.idCard}</div>}
              </div>
              <div className="form-group">
                <label>手机号 <span className="req">*</span></label>
                <input
                  className={errors.contactPhone ? 'has-error' : ''}
                  value={form.contactPhone}
                  onChange={e => { setForm({ ...form, contactPhone: e.target.value }); setErrors({ ...errors, contactPhone: '' }) }}
                  placeholder="11 位手机号"
                />
                {errors.contactPhone && <div className="field-error">{errors.contactPhone}</div>}
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setEditing(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={handleSave} disabled={saving}>{saving ? '保存中…' : '保存资料'}</button>
            </div>
          </div>
        </div>
      )}

      {/* 修改密码弹窗 */}
      {pwdOpen && (
        <div className="modal-overlay" onClick={() => setPwdOpen(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>修改登录密码</span>
              <button className="close-btn" onClick={() => setPwdOpen(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>原密码 <span className="req">*</span></label>
                <div className={`input-affix ${pwdErrors.oldPassword ? 'has-error' : ''}`}>
                  <input
                    type={pwdShow.oldPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    value={pwdForm.oldPassword}
                    onChange={e => { setPwdForm({ ...pwdForm, oldPassword: e.target.value }); setPwdErrors({ ...pwdErrors, oldPassword: '' }) }}
                    placeholder="请输入当前登录密码"
                  />
                  <button type="button" className="input-affix-btn" tabIndex={-1}
                    onClick={() => setPwdShow(s => ({ ...s, oldPassword: !s.oldPassword }))}
                    title={pwdShow.oldPassword ? '隐藏密码' : '显示密码'}
                    aria-label={pwdShow.oldPassword ? '隐藏密码' : '显示密码'}>
                    <Icon name={pwdShow.oldPassword ? 'eye-off' : 'eye'} size={16} />
                  </button>
                </div>
                {pwdErrors.oldPassword && <div className="field-error">{pwdErrors.oldPassword}</div>}
              </div>
              <div className="form-group">
                <label>新密码 <span className="req">*</span></label>
                <div className={`input-affix ${pwdErrors.newPassword ? 'has-error' : ''}`}>
                  <input
                    type={pwdShow.newPassword ? 'text' : 'password'}
                    autoComplete="new-password"
                    value={pwdForm.newPassword}
                    onChange={e => { setPwdForm({ ...pwdForm, newPassword: e.target.value }); setPwdErrors({ ...pwdErrors, newPassword: '' }) }}
                    placeholder="至少 8 位，同时包含字母和数字"
                  />
                  <button type="button" className="input-affix-btn" tabIndex={-1}
                    onClick={() => setPwdShow(s => ({ ...s, newPassword: !s.newPassword }))}
                    title={pwdShow.newPassword ? '隐藏密码' : '显示密码'}
                    aria-label={pwdShow.newPassword ? '隐藏密码' : '显示密码'}>
                    <Icon name={pwdShow.newPassword ? 'eye-off' : 'eye'} size={16} />
                  </button>
                </div>
                {pwdErrors.newPassword && <div className="field-error">{pwdErrors.newPassword}</div>}
              </div>
              <div className="form-group">
                <label>确认新密码 <span className="req">*</span></label>
                <div className={`input-affix ${pwdErrors.confirmPassword ? 'has-error' : ''}`}>
                  <input
                    type={pwdShow.confirmPassword ? 'text' : 'password'}
                    autoComplete="new-password"
                    value={pwdForm.confirmPassword}
                    onChange={e => { setPwdForm({ ...pwdForm, confirmPassword: e.target.value }); setPwdErrors({ ...pwdErrors, confirmPassword: '' }) }}
                    placeholder="再次输入新密码"
                  />
                  <button type="button" className="input-affix-btn" tabIndex={-1}
                    onClick={() => setPwdShow(s => ({ ...s, confirmPassword: !s.confirmPassword }))}
                    title={pwdShow.confirmPassword ? '隐藏密码' : '显示密码'}
                    aria-label={pwdShow.confirmPassword ? '隐藏密码' : '显示密码'}>
                    <Icon name={pwdShow.confirmPassword ? 'eye-off' : 'eye'} size={16} />
                  </button>
                </div>
                {pwdErrors.confirmPassword && <div className="field-error">{pwdErrors.confirmPassword}</div>}
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setPwdOpen(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={handleChangePassword} disabled={pwdSaving}>{pwdSaving ? '提交中…' : '确认修改'}</button>
            </div>
          </div>
        </div>
      )}

      {refQr && (
        <div className="modal-overlay" onClick={() => setRefQr(null)}>
          <div className="modal referral-qr-modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>邀请二维码</span>
              <button className="close-btn" onClick={() => setRefQr(null)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body referral-qr-body">
              <img src={refQr} alt="邀请二维码" />
              <p>好友扫码后直达注册页，推荐码 <strong className="mono">{referral?.referralCode}</strong> 已自动填好。</p>
            </div>
          </div>
        </div>
      )}

      {refDetailOpen && referral && (
        <div className="modal-overlay" onClick={() => setRefDetailOpen(false)}>
          <div className="modal modal-lg" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>邀请与奖励明细</span>
              <button className="close-btn" onClick={() => setRefDetailOpen(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              {(referral.items || []).length === 0 ? (
                <div className="empty"><Icon name="empty" size={36} chip /><div>还没有邀请记录</div></div>
              ) : (
                <div className="table-wrap">
                  <table className="data-table">
                    <thead><tr><th>受邀用户</th><th>状态</th><th>奖励券</th><th>邀请时间</th></tr></thead>
                    <tbody>
                      {referral.items.map(r => (
                        <tr key={r.referralId}>
                          <td>{r.inviteeName || `用户 ${r.inviteeId}`}</td>
                          <td><span className={`status-tag ${r.status === '已完成' ? 'status-success' : 'status-default'}`}>{r.status}</span></td>
                          <td>{r.rewardCouponId ? `已发放（#${r.rewardCouponId}）` : '待首单完成'}</td>
                          <td className="cell-time">{fmtD(r.createTime)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {avatarZoom && (
        <ImageLightbox
          src={avatarZoom}
          alt={`${info?.fullName || '用户'}的头像大图`}
          onClose={() => setAvatarZoom(null)}
        />
      )}
      {ticketOpen && <TicketModal onClose={() => setTicketOpen(false)} />}
    </div>
  )
}
