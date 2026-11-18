import { useEffect, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { opsApi, vehicleApi } from '@/api'
import Icon from '@/components/Icon'
import { fmtD } from '@/utils/datetime'

interface Plan {
  planId: number
  vehicleId: number
  vehicleNo?: string | null
  brand?: string | null
  planName: string
  triggerType: string
  triggerValue: number
  lastServiceTime?: string | null
  lastMileage: number
  currentMileage: number
  nextDueTime?: string | null
  status: string
  remark?: string | null
  due: boolean
  mileageSinceService: number
}

const emptyForm = () => ({
  vehicleId: '', planName: '', triggerType: '按天数', triggerValue: '30',
  lastServiceTime: '', lastMileage: '0', currentMileage: '0', remark: '',
})

/**
 * 预防保养计划：只做“什么时候该保养”的台账，真正工单仍走 MaintenancePanel。
 * 放在维保页签内部，不再单独占一个侧边栏菜单。
 */
export default function MaintenancePlanPanel() {
  const { toast, confirm } = useUI()
  const [plans, setPlans] = useState<Plan[]>([])
  const [vehicles, setVehicles] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [modal, setModal] = useState(false)
  const [editId, setEditId] = useState<number | null>(null)
  const [form, setForm] = useState(emptyForm())
  const [saving, setSaving] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const [p, v]: any = await Promise.all([opsApi.plans(), vehicleApi.list()])
      setPlans(p?.data || [])
      setVehicles(v?.data || [])
    } catch (e: any) {
      toast(e?.message || '保养计划加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const openAdd = () => {
    setEditId(null)
    setForm(emptyForm())
    setModal(true)
  }

  const openEdit = (p: Plan) => {
    setEditId(p.planId)
    setForm({
      vehicleId: String(p.vehicleId), planName: p.planName, triggerType: p.triggerType,
      triggerValue: String(p.triggerValue),
      lastServiceTime: p.lastServiceTime ? p.lastServiceTime.slice(0, 16) : '',
      lastMileage: String(p.lastMileage), currentMileage: String(p.currentMileage),
      remark: p.remark || '',
    })
    setModal(true)
  }

  const save = async () => {
    const vehicleId = Number(form.vehicleId)
    const triggerValue = Number(form.triggerValue)
    if (!vehicleId) { toast('请选择车辆', 'warning'); return }
    if (!form.planName.trim()) { toast('请填写计划名称', 'warning'); return }
    if (!Number.isInteger(triggerValue) || triggerValue <= 0) { toast('触发值必须是正整数', 'warning'); return }
    setSaving(true)
    try {
      const payload = {
        vehicleId,
        planName: form.planName.trim(),
        triggerType: form.triggerType,
        triggerValue,
        lastServiceTime: form.lastServiceTime ? new Date(form.lastServiceTime).toISOString() : null,
        lastMileage: Number(form.lastMileage) || 0,
        currentMileage: Number(form.currentMileage) || 0,
        remark: form.remark.trim(),
      }
      if (editId) await opsApi.updatePlan(editId, payload)
      else await opsApi.createPlan(payload)
      toast(editId ? '保养计划已更新' : '保养计划已创建', 'success')
      setModal(false)
      load()
    } catch (e: any) {
      toast(e?.message || '保存失败', 'error')
    } finally {
      setSaving(false)
    }
  }

  const toggle = async (p: Plan) => {
    try {
      await opsApi.setPlanStatus(p.planId, p.status === '启用' ? '停用' : '启用')
      load()
    } catch (e: any) {
      toast(e?.message || '操作失败', 'error')
    }
  }

  const generate = async (p: Plan) => {
    const ok = await confirm({
      title: '生成维保工单',
      message: `为 ${p.vehicleNo || '该车辆'} 生成「${p.planName}」维保工单，并记录本次保养？`,
      type: 'question', confirmText: '生成',
    })
    if (!ok) return
    try {
      const res: any = await opsApi.generatePlan(p.planId)
      toast(`工单 ${res?.data?.workOrderNo || ''} 已生成`, 'success')
      load()
    } catch (e: any) {
      toast(e?.message || '生成工单失败', 'error')
    }
  }

  return (
    <div className="plan-panel">
      <div className="plan-panel-head">
        <span className="field-hint">按天数或里程提醒保养；到期后一键生成维保工单。</span>
        <button className="btn-sm btn-sm-primary" onClick={openAdd}>
          <Icon name="create-order" size={13} color="#fff" />新增计划
        </button>
      </div>
      {loading ? (
        <div className="loading-line"><Icon name="refresh" size={16} />保养计划加载中…</div>
      ) : plans.length === 0 ? (
        <div className="empty"><Icon name="empty" size={40} chip /><div>暂无保养计划</div></div>
      ) : (
        <div className="table-wrap">
          <table className="data-table nowrap-table">
            <thead>
              <tr>
                <th>车辆</th><th>计划</th><th>规则</th><th>上次保养</th>
                <th>里程</th><th>下次到期</th><th>状态</th><th>操作</th>
              </tr>
            </thead>
            <tbody>
              {plans.map(p => (
                <tr key={p.planId} className={p.due ? 'plan-row-due' : ''}>
                  <td><strong>{p.vehicleNo || '—'}</strong><div className="muted-text">{p.brand || ''}</div></td>
                  <td>{p.planName}{p.remark && <div className="muted-text">{p.remark}</div>}</td>
                  <td>{p.triggerType} {p.triggerValue}</td>
                  <td>{p.lastServiceTime ? fmtD(p.lastServiceTime) : '—'}</td>
                  <td>{p.currentMileage} km<div className="muted-text">距上次 {p.mileageSinceService} km</div></td>
                  <td>{p.nextDueTime ? fmtD(p.nextDueTime) : (p.triggerType === '按里程' ? `再 ${p.triggerValue} km` : '—')}
                    {p.due && <span className="badge badge-warning" style={{ marginLeft: 6 }}>到期</span>}
                  </td>
                  <td><span className={`badge ${p.status === '启用' ? 'badge-success' : 'badge-muted'}`}>{p.status}</span></td>
                  <td>
                    <div className="btn-group">
                      <button className="btn-sm btn-sm-default" onClick={() => openEdit(p)}>编辑</button>
                      <button className="btn-sm btn-sm-primary" onClick={() => generate(p)} disabled={p.status !== '启用'}>生成工单</button>
                      <button className="btn-sm btn-sm-default" onClick={() => toggle(p)}>{p.status === '启用' ? '停用' : '启用'}</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <div className="modal-overlay" onClick={() => setModal(false)}>
          <div className="modal" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>{editId ? '编辑保养计划' : '新增保养计划'}</span>
              <button className="close-btn" onClick={() => setModal(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-row">
                <div className="form-group">
                  <label>车辆</label>
                  <select value={form.vehicleId} onChange={e => setForm({ ...form, vehicleId: e.target.value })}>
                    <option value="">请选择</option>
                    {vehicles.map(v => <option key={v.vehicleId} value={v.vehicleId}>{v.vehicleNo} · {v.brand || ''}</option>)}
                  </select>
                </div>
                <div className="form-group">
                  <label>计划名称</label>
                  <input value={form.planName} onChange={e => setForm({ ...form, planName: e.target.value })} placeholder="如：月度刹车检查" />
                </div>
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>触发方式</label>
                  <select value={form.triggerType} onChange={e => setForm({ ...form, triggerType: e.target.value })}>
                    <option value="按天数">按天数</option>
                    <option value="按里程">按里程</option>
                  </select>
                </div>
                <div className="form-group">
                  <label>{form.triggerType === '按天数' ? '间隔天数' : '间隔里程（km）'}</label>
                  <input type="number" min="1" step="1" value={form.triggerValue} onChange={e => setForm({ ...form, triggerValue: e.target.value })} />
                </div>
              </div>
              {form.triggerType === '按天数' && (
                <div className="form-group">
                  <label>上次保养时间</label>
                  <input type="datetime-local" value={form.lastServiceTime} onChange={e => setForm({ ...form, lastServiceTime: e.target.value })} />
                  <div className="field-hint">留空按创建时间起算。</div>
                </div>
              )}
              <div className="form-row">
                <div className="form-group">
                  <label>上次保养里程（km）</label>
                  <input type="number" min="0" step="1" value={form.lastMileage} onChange={e => setForm({ ...form, lastMileage: e.target.value })} />
                </div>
                <div className="form-group">
                  <label>当前里程（km）</label>
                  <input type="number" min="0" step="1" value={form.currentMileage} onChange={e => setForm({ ...form, currentMileage: e.target.value })} />
                </div>
              </div>
              <div className="form-group">
                <label>备注</label>
                <input value={form.remark} onChange={e => setForm({ ...form, remark: e.target.value })} placeholder="选填" />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setModal(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={save} disabled={saving}>{saving ? '保存中…' : '保存'}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
