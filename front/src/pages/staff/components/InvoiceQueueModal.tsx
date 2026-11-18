import { useEffect, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { opsApi } from '@/api'
import Icon from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'

interface Invoice {
  invoiceId: number
  invoiceNo: string
  orderNo?: string | null
  title: string
  taxNo: string
  invoiceType: string
  amount: number
  email: string
  status: string
  createTime: string
  issueTime?: string | null
  remark?: string | null
}

/** 待开票队列：从归还结算标题栏进入，只给管理员 / 财务处理，不单独开菜单 */
export default function InvoiceQueueModal({ onClose }: { onClose: () => void }) {
  const { toast, confirm, prompt } = useUI()
  const [list, setList] = useState<Invoice[]>([])
  const [loading, setLoading] = useState(true)

  const load = async () => {
    setLoading(true)
    try {
      const res: any = await opsApi.invoices('待开票')
      setList(res?.data || [])
    } catch (e: any) {
      toast(e?.message || '发票队列加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const issue = async (inv: Invoice) => {
    const ok = await confirm({
      title: '确认已开票',
      message: `${inv.invoiceNo} · ${inv.title} · ¥${Number(inv.amount).toFixed(2)}，确认后在税务平台完成开票并回传。`,
      type: 'question', confirmText: '已开票',
    })
    if (!ok) return
    try {
      await opsApi.issueInvoice(inv.invoiceId)
      toast('已标记开票', 'success')
      load()
    } catch (e: any) {
      toast(e?.message || '操作失败', 'error')
    }
  }

  const reject = async (inv: Invoice) => {
    const note = await prompt({
      title: '驳回开票申请',
      message: '填写驳回原因（会展示给申请人）',
      placeholder: '如：抬头 / 税号不完整',
    })
    if (note === null) return
    try {
      await opsApi.rejectInvoice(inv.invoiceId, note || '信息不完整')
      toast('已驳回', 'success')
      load()
    } catch (e: any) {
      toast(e?.message || '操作失败', 'error')
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-lg" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>待开票申请</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          {loading ? (
            <div className="loading-line"><Icon name="refresh" size={16} />发票队列加载中…</div>
          ) : list.length === 0 ? (
            <div className="empty"><Icon name="empty" size={40} chip /><div>没有待开票申请</div></div>
          ) : (
            <div className="table-wrap">
              <table className="data-table invoice-table">
                <thead>
                  <tr><th>申请号</th><th>订单号</th><th>抬头</th><th>金额</th><th>操作</th></tr>
                </thead>
                <tbody>
                  {list.map(inv => (
                    <tr key={inv.invoiceId}>
                      <td className="mono invoice-apply-no">{inv.invoiceNo}</td>
                      <td className="mono invoice-order-no">{inv.orderNo || '—'}</td>
                      <td>
                        <strong>{inv.title}</strong>
                        {inv.taxNo && <div className="field-hint">税号 {inv.taxNo}</div>}
                        <div className="field-hint">{inv.email}</div>
                        <div className="field-hint">{inv.invoiceType} · 申请 {fmtDT(inv.createTime)}</div>
                      </td>
                      <td className="num">¥{Number(inv.amount).toFixed(2)}</td>
                      <td>
                        <div className="btn-group">
                          <button className="btn-sm btn-sm-primary" onClick={() => issue(inv)}>已开票</button>
                          <button className="btn-sm btn-sm-default" onClick={() => reject(inv)}>驳回</button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
