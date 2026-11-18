import { useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { opsApi } from '@/api'
import Icon from '@/components/Icon'
import type { Order } from '@/types'

interface Props {
  order: Order
  onClose: () => void
  onCreated?: () => void
}

/**
 * 开票申请：只从订单详情进入，避免在结算列表里再加一排按钮。
 * 金额默认带出订单实收/预估租金，可改；税号、邮箱由顾客提供。
 */
export default function InvoiceModal({ order, onClose, onCreated }: Props) {
  const { toast } = useUI()
  const [form, setForm] = useState({
    title: order.customerName || '',
    taxNo: '',
    invoiceType: '电子普通发票',
    amount: String(order.actualRent ?? order.totalRent ?? 0),
    email: '',
    remark: '',
  })
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    const amount = Number(form.amount)
    if (!form.title.trim()) { toast('请填写发票抬头', 'warning'); return }
    if (form.invoiceType === '增值税专用发票' && !form.taxNo.trim()) { toast('专用发票必须填写税号', 'warning'); return }
    if (!Number.isFinite(amount) || amount <= 0) { toast('开票金额必须大于 0', 'warning'); return }
    if (!form.email.trim()) { toast('请填写接收邮箱', 'warning'); return }
    setSaving(true)
    try {
      await opsApi.createInvoice({
        orderId: order.orderId,
        title: form.title.trim(),
        taxNo: form.taxNo.trim(),
        invoiceType: form.invoiceType,
        amount,
        email: form.email.trim(),
        remark: form.remark.trim(),
      })
      toast('开票申请已提交', 'success')
      onCreated?.()
      onClose()
    } catch (e: any) {
      toast(e?.message || '开票申请提交失败', 'error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>申请开票 · {order.orderNo}</span>
          <button className="close-btn" onClick={onClose} aria-label="关闭"><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          <div className="form-row">
            <div className="form-group">
              <label>发票抬头</label>
              <input value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} placeholder="个人姓名或企业名称" />
            </div>
            <div className="form-group">
              <label>发票类型</label>
              <select value={form.invoiceType} onChange={e => setForm({ ...form, invoiceType: e.target.value })}>
                <option value="电子普通发票">电子普通发票</option>
                <option value="增值税专用发票">增值税专用发票</option>
              </select>
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>税号</label>
              <input value={form.taxNo} onChange={e => setForm({ ...form, taxNo: e.target.value })} placeholder="个人普票可留空；专用发票必填" />
            </div>
            <div className="form-group">
              <label>开票金额（元）</label>
              <input type="number" min="0.01" step="0.01" value={form.amount} onChange={e => setForm({ ...form, amount: e.target.value })} />
            </div>
          </div>
          <div className="form-group">
            <label>接收邮箱</label>
            <input type="email" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} placeholder="电子发票发送地址" />
          </div>
          <div className="form-group">
            <label>备注</label>
            <input value={form.remark} onChange={e => setForm({ ...form, remark: e.target.value })} placeholder="选填" />
          </div>
          <div className="field-hint">提交后由管理员或财务处理；实际开票仍以税务平台为准。</div>
        </div>
        <div className="modal-footer">
          <button className="btn-sm btn-sm-default" onClick={onClose}>取消</button>
          <button className="btn-sm btn-sm-primary" onClick={submit} disabled={saving}>{saving ? '提交中…' : '提交申请'}</button>
        </div>
      </div>
    </div>
  )
}
