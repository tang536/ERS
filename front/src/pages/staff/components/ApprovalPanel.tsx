import { useEffect, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { opsApi } from '@/api'
import Icon from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'

interface Approval {
  approvalId: number
  requestNo: string
  bizType: string
  orderId?: number | null
  amount: number
  reason: string
  status: string
  createTime: string
  approveTime?: string | null
  approveNote?: string | null
}

/**
 * 金额审批：只处理“减免 / 退款 / 赔偿”这类需要管理员点头的单据。
 * 挂在运营配置的第三个页签里，不新开侧边栏，避免和日常业务菜单抢位置。
 */
export default function ApprovalPanel() {
  const { toast, confirm, prompt } = useUI()
  const [list, setList] = useState<Approval[]>([])
  const [status, setStatus] = useState('待审批')
  const [loading, setLoading] = useState(true)

  const load = async () => {
    setLoading(true)
    try {
      const res: any = await opsApi.approvals(status || undefined)
      setList(res?.data || [])
    } catch (e: any) {
      toast(e?.message || '审批列表加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [status])

  const decide = async (a: Approval, approve: boolean) => {
    const note = approve ? '' : await prompt({
      title: '驳回审批',
      message: '填写驳回原因（会展示给申请人）',
      placeholder: '如：金额超出权限 / 缺少凭证',
    })
    if (!approve && note === null) return
    const ok = await confirm({
      title: approve ? '通过审批' : '驳回审批',
      message: `${a.requestNo} · ${a.bizType} · ¥${Number(a.amount).toFixed(2)}`,
      type: approve ? 'question' : 'warning',
      confirmText: approve ? '通过' : '驳回',
    })
    if (!ok) return
    try {
      await opsApi.decideApproval(a.approvalId, approve, note || undefined)
      toast(approve ? '已通过' : '已驳回', 'success')
      load()
    } catch (e: any) {
      toast(e?.message || '审批操作失败', 'error')
    }
  }

  return (
    <div className="approval-panel">
      <div className="approval-toolbar">
        <span className="field-hint">只列金额类审批；通过后仍需在对应业务页面执行退款或减免。</span>
        <select value={status} onChange={e => setStatus(e.target.value)} aria-label="审批状态">
          <option value="待审批">待审批</option>
          <option value="已通过">已通过</option>
          <option value="已驳回">已驳回</option>
          <option value="">全部</option>
        </select>
      </div>
      {loading ? (
        <div className="loading-line"><Icon name="refresh" size={16} />审批列表加载中…</div>
      ) : list.length === 0 ? (
        <div className="empty"><Icon name="empty" size={40} chip /><div>当前没有符合条件的审批</div></div>
      ) : (
        <div className="table-wrap">
          <table className="data-table nowrap-table">
            <thead>
              <tr><th>申请单</th><th>类型</th><th>金额</th><th>事由</th><th>申请时间</th><th>状态</th><th>操作</th></tr>
            </thead>
            <tbody>
              {list.map(a => (
                <tr key={a.approvalId}>
                  <td className="mono">{a.requestNo}</td>
                  <td>{a.bizType}</td>
                  <td className="num">¥{Number(a.amount).toFixed(2)}</td>
                  <td className="approval-reason" title={a.reason}>{a.reason}</td>
                  <td className="cell-time">{fmtDT(a.createTime)}</td>
                  <td>
                    <span className={`badge ${a.status === '已通过' ? 'badge-success' : a.status === '已驳回' ? 'badge-muted' : 'badge-warning'}`}>{a.status}</span>
                    {a.approveNote && <div className="field-hint">{a.approveNote}</div>}
                  </td>
                  <td>
                    {a.status === '待审批' ? (
                      <div className="btn-group">
                        <button className="btn-sm btn-sm-primary" onClick={() => decide(a, true)}>通过</button>
                        <button className="btn-sm btn-sm-default" onClick={() => decide(a, false)}>驳回</button>
                      </div>
                    ) : <span className="muted-text">已处理</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
