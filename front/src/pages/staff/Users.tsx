import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useUI } from '@/components/UIProvider'
import { useAuth } from '@/hooks/useAuth'
import { authApi, userApi } from '@/api'
import { logoutEverywhere } from '@/utils/session'
import type { StaffUser } from '@/types'
import { fmtDT } from '@/utils/datetime'
import DataTable, { type DataTableColumn } from '@/components/DataTable'
import Icon from '@/components/Icon'
import Avatar from '@/components/Avatar'

const EMPTY = { loginAccount: '', password: '', userName: '', roleType: 'reception' }

/** 复制文本：优先 Clipboard API，非安全上下文（如用 IP 访问时）退回 execCommand */

export default function Users() {
  const { toast, confirm, prompt } = useUI()
  const { user } = useAuth()
  const navigate = useNavigate()
  const [list, setList] = useState<StaffUser[]>([])
  const [loading, setLoading] = useState(true)
  const [keyword, setKeyword] = useState('')
  const [roleFilter, setRoleFilter] = useState('')
  const [showModal, setShowModal] = useState(false)
  const [editId, setEditId] = useState<number | null>(null)
  const [form, setForm] = useState({ ...EMPTY })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [tempPwd, setTempPwd] = useState<{ account: string; password: string; expiresAt: string } | null>(null)


  const load = async () => {
    setLoading(true)
    try {
      const res: any = await userApi.list()
      setList(res.data || [])
    } catch (e: any) {
      toast(e?.message || '账号列表加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }
  useEffect(() => { load() }, [])

  const filtered = list.filter(u => {
    const kw = keyword.trim()
    const okKw = !kw || u.loginAccount.includes(kw) || u.userName.includes(kw)
    const okRole = !roleFilter || u.roleName === roleFilter
    return okKw && okRole
  })

  /**
   * 敏感账号操作统一入口：每次都要求输入「当前登录管理员」的密码。
   * 返回 null 表示用户取消；密码同时提交给后端做二次校验，前端弹窗不是唯一防线。
   */
  const askAdminPassword = async (action: string): Promise<string | null> => {
    const pwd = await prompt({
      title: '管理员身份验证',
      message: `即将「${action}」，请输入当前管理员账号「${user?.loginAccount || ''}」的登录密码。`,
      inputType: 'password',
      placeholder: '当前管理员密码',
      confirmText: '验证并继续',
      validate: v => (v.trim() ? null : '请输入当前管理员密码'),
    })
    return pwd ? pwd.trim() : null
  }

  const validate = () => {
    const e: Record<string, string> = {}
    if (!editId) {
      if (!form.loginAccount.trim()) e.loginAccount = '请输入登录账号'
      else if (!/^[A-Za-z0-9_]{3,20}$/.test(form.loginAccount.trim())) e.loginAccount = '账号为 3-20 位字母、数字或下划线'
    }
    if (!form.userName.trim()) e.userName = '请输入姓名'
    // 密码只在新增时设置初始值；编辑资料不再直接改密码，必须走行内「重置密码」
    if (!editId && !form.password) e.password = '请输入初始密码'
    else if (!editId && (form.password.length < 8 || !/[A-Za-z]/.test(form.password) || !/\d/.test(form.password)))
      e.password = '初始密码至少 8 位，且需同时包含字母和数字'
    else if (form.password.length > 50) e.password = '密码长度不能超过 50 位'
    setErrors(e)
    return Object.keys(e).length === 0
  }

  const handleSave = async () => {
    if (!validate()) return
    const pwd = await askAdminPassword(editId ? '编辑该工作人员账号' : '新增工作人员账号')
    if (!pwd) return
    try {
      if (editId) {
        await userApi.update(editId, { userName: form.userName.trim() }, pwd)
        toast('账号信息已更新', 'success')
      } else {
        await userApi.create({ ...form, loginAccount: form.loginAccount.trim(), userName: form.userName.trim() }, pwd)
        toast('账号创建成功', 'success')
      }
      setShowModal(false)
      setEditId(null)
      setForm({ ...EMPTY })
      load()
    } catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  const handleToggle = async (u: StaffUser) => {
    const action = u.userStatus === 1 ? '禁用该账号' : '启用该账号'
    const ok = await confirm({
      title: action,
      message: `${action}「${u.loginAccount}」？${u.userStatus === 1 ? '停用后该账号的在线会话会立即失效。' : '启用后该账号可立即恢复登录。'}`,
      type: 'warning',
      confirmText: '继续',
    })
    if (!ok) return
    const pwd = await askAdminPassword(action)
    if (!pwd) return
    try {
      // 停用走专用端点（后端顺手吊销在线会话）；启用复用编辑端点，只改状态位
      if (u.userStatus === 1) await userApi.disable(u.userId, pwd)
      else await userApi.update(u.userId, { userStatus: 1 }, pwd)
      toast(`账号 ${u.loginAccount} 已${u.userStatus === 1 ? '禁用' : '启用'}`, 'success')
      load()
    } catch (e: any) { toast(e?.message || '操作失败', 'error') }
  }

  /** 打开编辑弹窗（列表行按钮与「去重置密码」共用） */
  const openEditor = (u: StaffUser) => {
    setEditId(u.userId)
    setForm({ loginAccount: u.loginAccount, password: '', userName: u.userName, roleType: u.roleName === '前台' ? 'reception' : 'finance' })
    setErrors({})
    setShowModal(true)
  }


  /** 生成一次性临时密码：明文由管理员当面/安全渠道转交，系统不留明文 */
  const handleResetPassword = async (u: StaffUser) => {
    const ok = await confirm({
      title: '重置密码',
      message: `将为「${u.loginAccount}」生成一次性临时密码，原密码立即失效、该账号现有登录全部下线。临时密码 24 小时内有效，本人首次登录必须修改密码。`,
      type: 'warning',
      confirmText: '生成临时密码',
    })
    if (!ok) return
    const pwd = await askAdminPassword('重置该账号密码')
    if (!pwd) return
    try {
      const res: any = await userApi.resetPassword(u.userId, pwd)
      setTempPwd({ account: u.loginAccount, password: res.data?.temporaryPassword || '', expiresAt: res.data?.expiresAt || '' })
      load()
    } catch (e: any) { toast(e?.message || '重置密码失败', 'error') }
  }

  const copyTempPassword = async () => {
    if (!tempPwd?.password) return
    try { await navigator.clipboard.writeText(tempPwd.password); toast('临时密码已复制', 'success') }
    catch { toast('复制失败，请手动选择文本', 'warning') }
  }

  const handleDelete = async (u: StaffUser) => {
    const ok = await confirm({
      title: '删除账号',
      message: `确定删除账号「${u.loginAccount}」（${u.userName}）吗？删除后该账号将无法登录，操作不可恢复。`,
      type: 'warning',
      danger: true,
      confirmText: '确认删除',
    })
    if (!ok) return
    const pwd = await askAdminPassword('删除该工作人员账号')
    if (!pwd) return
    try {
      await userApi.remove(u.userId, pwd)
      toast('账号已删除', 'success')
      load()
    } catch (e: any) { toast(e?.message || '删除失败', 'error') }
  }

  /**
   * 管理员注销自己的账号（需求：注销功能仅对「顾客」与「管理员」开放，
   * 管理员的入口只在账号管理页出现，顾客的入口只在个人中心出现）。
   * 后端会拦截「系统中最后一名管理员」，密码错误也会直接拒绝。
   */
  const handleSelfDeactivate = async () => {
    const ok = await confirm({
      title: '注销管理员账号',
      message: `确定注销当前登录的管理员账号「${user?.userName || ''}」吗？注销后该账号将被停用、无法再登录，操作日志与业务数据会保留以便追溯。`,
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
      // 注销后后端已强制清会话，本地登出同样走统一入口
      window.setTimeout(() => { logoutEverywhere(); navigate('/login', { replace: true }) }, 700)
    } catch (e: any) {
      toast(e?.message || '注销失败', 'error')
    }
  }

  const columns: DataTableColumn<StaffUser>[] = [
    { key: 'loginAccount', label: '登录账号', className: 'mono strong' },
    {
      key: 'userName', label: '姓名',
      render: u => (
        <span className="staff-user-cell">
          <Avatar className="staff-avatar" name={u.userName || u.loginAccount} src={u.avatarUrl} />
          <span>{u.userName || '—'}</span>
        </span>
      ),
    },
    {
      key: 'roleName', label: '角色',
      render: u => (
        <span className={`status-tag ${u.roleName === '管理员' ? 'status-danger' : u.roleName === '财务' ? 'status-warning' : 'status-primary'}`}>
          {u.roleName}
        </span>
      ),
    },
    {
      key: 'userStatus', label: '状态',
      render: u => <span className={`status-tag ${u.userStatus === 1 ? 'status-success' : 'status-default'}`}>{u.userStatus === 1 ? '启用' : '禁用'}</span>,
    },
    { key: 'loginFailCount', label: '失败次数', numeric: true, className: 'col-hide-md' },
    {
      key: 'actions', label: '操作',
      render: u => (
        <div className="btn-group">
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => openEditor(u)}><Icon name="config" size={14} /><span className="btn-label">编辑</span></button>
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => handleResetPassword(u)}><Icon name="lock" size={14} /><span className="btn-label">重置密码</span></button>
          <button className="btn-sm btn-sm-default btn-icon-sm" onClick={() => handleToggle(u)}><Icon name={u.userStatus === 1 ? 'shield' : 'success'} size={14} /><span className="btn-label">{u.userStatus === 1 ? '禁用' : '启用'}</span></button>
          <button className="btn-sm btn-sm-danger btn-icon-sm" onClick={() => handleDelete(u)} disabled={u.roleName === '管理员'} title={u.roleName === '管理员' ? '唯一管理员账号不可删除' : ''}><Icon name="trash" size={14} /><span className="btn-label">删除</span></button>
        </div>
      ),
    },
  ]

  return (
    <div className="card">
      <div className="card-title">
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          <Icon name="users" size={18} chip />员工账号
          <span className="card-title-count">共 {list.length} 个工作人员账号</span>
        </span>
        <button className="btn-sm btn-sm-primary btn-icon-sm" style={{ marginLeft: 'auto' }}
                onClick={() => { setEditId(null); setForm({ ...EMPTY }); setErrors({}); setShowModal(true) }}>
          <Icon name="create-order" size={15} color="#fff" /><span className="btn-label">新增账号</span>
        </button>
      </div>

      <div className="toolbar">
        <div className="search-box">
          <input placeholder="按账号 / 姓名筛选" value={keyword} onChange={e => setKeyword(e.target.value)} />
          <select value={roleFilter} onChange={e => setRoleFilter(e.target.value)}>
            <option value="">全部角色</option>
            <option value="前台">前台</option>
            <option value="财务">财务</option>
            <option value="管理员">管理员</option>
          </select>
          {(keyword || roleFilter) && (
            <button className="btn-sm btn-sm-default" onClick={() => { setKeyword(''); setRoleFilter('') }}>
              <Icon name="refresh" size={14} />重置
            </button>
          )}
        </div>
      </div>

      <DataTable columns={columns} rows={filtered} rowKey={u => u.userId} ariaLabel="员工账号列表" />

      {loading && <div className="loading-line"><Icon name="refresh" size={18} />数据加载中…</div>}
      {!loading && filtered.length === 0 && (
        <div className="empty"><Icon name="empty" size={40} chip /><div>{list.length === 0 ? '暂无账号' : '没有符合条件的账号'}</div></div>
      )}

      {/* 管理员自助注销：注销入口按角色收敛，管理员的只出现在本页 */}
      {user?.roleName === '管理员' && (
        <div className="danger-zone">
          <div className="danger-zone-text">
            <strong>注销我的管理员账号</strong>
            <p>注销后该账号停用且无法登录，历史记录保留；最后一名管理员不可注销。</p>
          </div>
          <button className="btn-sm btn-sm-danger" onClick={handleSelfDeactivate}>
            <Icon name="warning" size={15} color="#fff" />注销我的账号
          </button>
        </div>
      )}

      {showModal && (
        <div className="modal-overlay" onClick={() => setShowModal(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>{editId ? '编辑账号' : '新增工作人员账号'}</span>
              <button className="close-btn" onClick={() => setShowModal(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>登录账号 <span className="req">*</span></label>
                <input
                  className={errors.loginAccount ? 'has-error' : ''}
                  value={form.loginAccount}
                  onChange={e => { setForm({ ...form, loginAccount: e.target.value }); setErrors({ ...errors, loginAccount: '' }) }}
                  disabled={!!editId}
                  placeholder="3-20 位字母、数字或下划线"
                />
                {errors.loginAccount && <div className="field-error">{errors.loginAccount}</div>}
              </div>
              <div className="form-group">
                <label>姓名 <span className="req">*</span></label>
                <input
                  className={errors.userName ? 'has-error' : ''}
                  value={form.userName}
                  onChange={e => { setForm({ ...form, userName: e.target.value }); setErrors({ ...errors, userName: '' }) }}
                  placeholder="请输入真实姓名"
                />
                {errors.userName && <div className="field-error">{errors.userName}</div>}
              </div>
              {!editId && (
                <div className="form-group">
                  <label>初始密码 <span className="req">*</span></label>
                  <input
                    type="password"
                    className={errors.password ? 'has-error' : ''}
                    value={form.password}
                    onChange={e => { setForm({ ...form, password: e.target.value }); setErrors({ ...errors, password: '' }) }}
                    placeholder="至少 8 位，含字母和数字"
                  />
                  {errors.password && <div className="field-error">{errors.password}</div>}
                  <div className="field-hint">初始密码用于首次登录；如员工遗失，请用列表中的「重置密码」生成一次性临时密码。</div>
                </div>
              )}
              {!editId && (
                <div className="form-group">
                  <label>角色</label>
                  <select value={form.roleType} onChange={e => setForm({ ...form, roleType: e.target.value })}>
                    <option value="reception">前台（订单、结算、客户、车辆）</option>
                    <option value="finance">财务（结算、报表）</option>
                  </select>
                  <div className="field-hint">管理员为唯一内置角色，如需新增请联系系统维护人员。</div>
                </div>
              )}
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setShowModal(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={handleSave}>保存</button>
            </div>
          </div>
        </div>
      )}

      {tempPwd && (
        <div className="modal-overlay" onClick={() => setTempPwd(null)}>
          <div className="modal" onClick={e => e.stopPropagation()} style={{ maxWidth: 480 }}>
            <div className="modal-header">
              <span>一次性临时密码</span>
              <button className="close-btn" onClick={() => setTempPwd(null)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="callout callout-warning">
                <Icon name="warning" size={18} chip />
                <div>
                  <strong>仅显示这一次，关闭后无法再次查看</strong>
                  <p>请通过安全渠道当面交给「{tempPwd.account}」，不要发到聊天、工单或邮件。临时密码 24 小时内有效，首次登录必须改密。</p>
                </div>
              </div>
              <div className="form-group" style={{ marginTop: 16 }}>
                <label>临时密码</label>
                <div className="input-affix">
                  <input readOnly value={tempPwd.password} className="mono" onFocus={e => e.currentTarget.select()} />
                  <button type="button" className="input-affix-btn" onClick={copyTempPassword} title="复制临时密码">
                    <Icon name="copy" size={16} />
                  </button>
                </div>
                {tempPwd.expiresAt && <div className="field-hint">失效时间：{fmtDT(tempPwd.expiresAt)}</div>}
              </div>
            </div>
          </div>
        </div>
      )}

    </div>
  )
}
