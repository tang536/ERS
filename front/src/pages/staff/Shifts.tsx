import { useCallback, useEffect, useState } from 'react'
import { useUI } from '@/components/UIProvider'
import { shiftApi } from '@/api'
import Icon from '@/components/Icon'
import { fmtDT } from '@/utils/datetime'
import type { Shift } from '@/types'

const money = (v: number | null | undefined) => `¥${Number(v || 0).toFixed(2)}`

/**
 * 交接班（班次现金对账）。
 *
 * 交班金额不是手填的：系统按「开班时间 ~ 当前时刻」汇总 payment_record 各渠道净额
 * （收款减退款），前台只需录入实点现金，差额必须写原因。
 */
export default function Shifts() {
  const { toast, confirm } = useUI()
  const [current, setCurrent] = useState<Shift | null>(null)
  const [history, setHistory] = useState<Shift[]>([])
  const [loading, setLoading] = useState(true)
  const [opening, setOpening] = useState(false)
  const [closing, setClosing] = useState(false)

  const [openCash, setOpenCash] = useState('0')
  const [closeModal, setCloseModal] = useState(false)
  const [actualCash, setActualCash] = useState('')
  const [closeNote, setCloseNote] = useState('')
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [curRes, histRes]: any[] = await Promise.all([shiftApi.current(), shiftApi.history(1, 30)])
      setCurrent(curRes?.data || null)
      setHistory(histRes?.data || [])
    } catch (e: any) {
      toast(e?.message || '班次信息加载失败', 'error')
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => { load() }, [load])

  const handleOpen = async () => {
    const cash = Number(openCash)
    if (!Number.isFinite(cash) || cash < 0) {
      toast('备用金不能为负数', 'warning')
      return
    }
    setSaving(true)
    setOpening(true)
    try {
      await shiftApi.open({ openingCash: cash })
      toast('已开班', 'success')
      setOpenCash('0')
      load()
    } catch (e: any) {
      toast(e?.message || '开班失败', 'error')
    } finally {
      setSaving(false)
      setOpening(false)
    }
  }

  const openClose = () => {
    if (!current) return
    // 默认带出「账面应存现金」，前台只需按抽屉实际金额微调
    setActualCash(String((current.openingCash + current.cashCollected).toFixed(2)))
    setCloseNote('')
    setCloseModal(true)
  }

  const handleClose = async () => {
    if (!current) return
    const cash = Number(actualCash)
    if (!Number.isFinite(cash) || cash < 0) {
      toast('实点现金不能为负数', 'warning')
      return
    }
    const expected = current.openingCash + current.cashCollected
    const diff = Math.round((cash - expected) * 100) / 100
    if (diff !== 0 && !closeNote.trim()) {
      toast('存在长款/短款，必须填写差额说明', 'warning')
      return
    }
    const ok = await confirm({
      title: '确认交班',
      message: `实点现金 ${money(cash)}，账面应存 ${money(expected)}，差额 ${diff >= 0 ? '+' : ''}${money(diff)}。交班后本班次不可再修改。`,
      type: diff === 0 ? 'question' : 'warning',
      confirmText: '确认交班',
      danger: diff !== 0,
    })
    if (!ok) return
    setSaving(true)
    try {
      await shiftApi.close({ shiftId: current.shiftId, cashActual: cash, note: closeNote.trim() || null })
      toast('已完成交班', 'success')
      setCloseModal(false)
      load()
    } catch (e: any) {
      toast(e?.message || '交班失败', 'error')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return <div className="loading-line"><Icon name="refresh" size={18} />班次信息加载中…</div>
  }

  const expectedCash = current ? current.openingCash + current.cashCollected : 0

  return (
    <div>
      <div className="card">
        <div className="card-title">
          <Icon name="settlements" size={18} chip />{current ? '当前班次' : '开班'}
          {current && <span className="card-title-count">{current.shiftNo} · {current.operatorName}</span>}
        </div>

        {current ? (
          <>
            <div className="callout callout-info">
              <Icon name="info" size={18} chip />
              <span>开班时间 {fmtDT(current.openTime)}。下列金额为本班实时汇总（收款 − 退款），交班时系统按此与实点现金对账。</span>
            </div>

            <div className="estimate">
              <div className="estimate-grid">
                <div><span>开班备用金</span><strong>{money(current.openingCash)}</strong></div>
                <div><span>现金净收入</span><strong>{money(current.cashCollected)}</strong></div>
                <div><span>支付宝净收款</span><strong>{money(current.alipayAmount)}</strong></div>
                <div><span>微信净收款</span><strong>{money(current.wechatAmount)}</strong></div>
                <div><span>银行卡净收款</span><strong>{money(current.bankAmount)}</strong></div>
                <div><span>本班新单 / 结算</span><strong>{current.orderCount} / {current.settleCount}</strong></div>
              </div>
              <div className="estimate-total">账面应存现金（备用金 + 现金净收入） <strong>{money(expectedCash)}</strong></div>
            </div>

            <div className="form-actions">
              <button className="btn-sm btn-sm-primary" onClick={openClose}>交班</button>
              <button className="btn-sm btn-sm-default" onClick={load}>刷新汇总</button>
            </div>
          </>
        ) : (
          <>
            <div className="callout callout-info">
              <Icon name="info" size={18} chip />
              <span>开班时登记抽屉里的备用金。班次存续期间本机收款都会计入本班，交班时按实点现金核对。</span>
            </div>
            <div className="form-row">
              <div className="form-group">
                <label>开班备用金（元）</label>
                <input
                  type="number" min="0" step="0.01"
                  value={openCash}
                  onChange={e => setOpenCash(e.target.value)}
                />
                <div className="field-hint">抽屉里用于找零的起始现金，0 也可以。</div>
              </div>
            </div>
            <div className="form-actions">
              <button className="btn-sm btn-sm-primary" onClick={handleOpen} disabled={saving || opening}>
                {opening ? '开班中…' : '确认开班'}
              </button>
            </div>
          </>
        )}
      </div>

      <div className="card">
        <div className="card-title">
          <Icon name="orders" size={16} chip />交班历史
          <span className="card-title-count">最近 {history.length} 个班次</span>
        </div>

        <div className="table-wrap">
          <table className="data-table nowrap-table">
            <thead>
              <tr>
                <th>班次号</th><th>操作人</th><th>开班</th><th>交班</th>
                <th>备用金</th><th>现金净收</th><th>实点现金</th><th>差额</th>
                <th className="col-hide-md">支付宝</th><th className="col-hide-md">微信</th><th className="col-hide-md">银行卡</th>
                <th>状态</th>
              </tr>
            </thead>
            <tbody>
              {history.map(s => (
                <tr key={s.shiftId}>
                  <td className="mono">{s.shiftNo}</td>
                  <td>{s.operatorName}</td>
                  <td>{fmtDT(s.openTime)}</td>
                  <td>{fmtDT(s.closeTime)}</td>
                  <td>{money(s.openingCash)}</td>
                  <td>{money(s.cashCollected)}</td>
                  <td>{money(s.cashActual)}</td>
                  <td style={{ color: s.difference < 0 ? 'var(--danger-text, #C0392B)' : undefined }}>
                    {s.difference >= 0 ? '+' : ''}{money(s.difference)}
                  </td>
                  <td className="col-hide-md">{money(s.alipayAmount)}</td>
                  <td className="col-hide-md">{money(s.wechatAmount)}</td>
                  <td className="col-hide-md">{money(s.bankAmount)}</td>
                  <td>
                    <span className={`badge ${s.status === '进行中' ? 'badge-warning' : 'badge-success'}`}>{s.status}</span>
                    {s.note && <div className="field-hint">{s.note}</div>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {history.length === 0 && (
          <div className="empty"><Icon name="empty" size={40} chip /><div>暂无交班记录</div></div>
        )}
      </div>

      {closeModal && current && (
        <div className="modal-overlay" onClick={() => setCloseModal(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span>交班 · {current.shiftNo}</span>
              <button className="close-btn" onClick={() => setCloseModal(false)}><Icon name="close" size={15} /></button>
            </div>
            <div className="modal-body">
              <div className="form-row">
                <div className="form-group">
                  <label>账面应存现金</label>
                  <input value={expectedCash.toFixed(2)} readOnly />
                  <div className="field-hint">备用金 + 现金净收入</div>
                </div>
                <div className="form-group">
                  <label>实点现金（元） <span className="req">*</span></label>
                  <input
                    type="number" min="0" step="0.01"
                    value={actualCash}
                    onChange={e => setActualCash(e.target.value)}
                    autoFocus
                  />
                </div>
              </div>
              {(() => {
                const cash = Number(actualCash)
                if (!Number.isFinite(cash)) return null
                const diff = Math.round((cash - expectedCash) * 100) / 100
                return (
                  <div className={`callout ${diff === 0 ? 'callout-info' : 'callout-warning'}`}>
                    <Icon name={diff === 0 ? 'success' : 'warning'} size={18} chip />
                    <span>
                      {diff === 0 ? '账实相符。' : diff > 0 ? `长款 ${money(diff)}，请核对是否多收。` : `短款 ${money(-diff)}，请核对是否漏记或找零有误。`}
                    </span>
                  </div>
                )
              })()}
              <div className="form-group">
                <label>差额说明{diffNeedsNote(actualCash, expectedCash) ? <span className="req"> *</span> : null}</label>
                <input
                  value={closeNote}
                  onChange={e => setCloseNote(e.target.value)}
                  placeholder="账实不符时必须填写原因，如：顾客找零未计入"
                />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm btn-sm-default" onClick={() => setCloseModal(false)}>取消</button>
              <button className="btn-sm btn-sm-primary" onClick={handleClose} disabled={saving}>
                {saving ? '交班中…' : '确认交班'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/** 实点金额与账面不一致时需要说明原因 */
function diffNeedsNote(actual: string, expected: number): boolean {
  const cash = Number(actual)
  if (!Number.isFinite(cash)) return false
  return Math.round((cash - expected) * 100) / 100 !== 0
}
