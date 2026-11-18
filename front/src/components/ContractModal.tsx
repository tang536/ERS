import { useEffect, useRef, useState } from 'react'
import { contractApi } from '@/api'
import { useUI } from './UIProvider'
import Icon from './Icon'
import { fmtDT } from '@/utils/datetime'
import type { Contract } from '@/types'

/**
 * 电子合同弹窗：查看正文 + 手写签名留档。
 *
 * 未签署时展示按当前订单生成的预览，签名后调后端把正文快照、SHA-256 与签名图一起落库；
 * 已签署时只读展示留档正文与签名，不再允许改动。
 */
export default function ContractModal({ orderId, orderNo, onClose }: {
  orderId: number
  orderNo: string
  onClose: () => void
}) {
  const { toast } = useUI()
  const [contract, setContract] = useState<Contract | null>(null)
  const [loading, setLoading] = useState(true)
  const [signing, setSigning] = useState(false)
  const [hasInk, setHasInk] = useState(false)

  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const drawing = useRef(false)
  const last = useRef<{ x: number; y: number } | null>(null)
  const dirty = useRef(false)

  const signed = contract?.status === '已签署'

  useEffect(() => {
    let alive = true
    contractApi.get(orderId)
      .then((res: any) => { if (alive) setContract(res.data) })
      .catch((e: any) => { if (alive) { toast(e?.message || '合同加载失败', 'error'); onClose() } })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
    // onClose 是父组件每次渲染新建的内联函数，放进依赖会导致重复拉取；它在错误分支里只用于收尾
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId, toast])

  // 签名板初始化：只做一次，按 DPR 放大画布避免笔迹发虚
  useEffect(() => {
    if (loading || signed) return
    const canvas = canvasRef.current
    if (!canvas) return
    const rect = canvas.getBoundingClientRect()
    const dpr = window.devicePixelRatio || 1
    canvas.width = Math.max(1, Math.floor(rect.width * dpr))
    canvas.height = Math.max(1, Math.floor(180 * dpr))
    const ctx = canvas.getContext('2d')
    if (ctx) {
      ctx.scale(dpr, dpr)
      ctx.lineWidth = 2.5
      ctx.lineCap = 'round'
      ctx.lineJoin = 'round'
      ctx.strokeStyle = '#111827'
    }
    dirty.current = false
    setHasInk(false)
  }, [loading, signed])

  const pointOf = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    return { x: e.clientX - rect.left, y: e.clientY - rect.top }
  }

  const startDraw = (e: React.PointerEvent<HTMLCanvasElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId)
    drawing.current = true
    last.current = pointOf(e)
  }

  const moveDraw = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return
    const canvas = canvasRef.current
    const prev = last.current
    if (!canvas || !prev) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    const p = pointOf(e)
    ctx.beginPath()
    ctx.moveTo(prev.x, prev.y)
    ctx.lineTo(p.x, p.y)
    ctx.stroke()
    last.current = p
    if (!dirty.current) {
      dirty.current = true
      setHasInk(true)
    }
  }

  const endDraw = () => {
    drawing.current = false
    last.current = null
  }

  const clearInk = () => {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')
    if (!canvas || !ctx) return
    ctx.clearRect(0, 0, canvas.width, canvas.height)
    dirty.current = false
    setHasInk(false)
  }

  const submitSign = async () => {
    const canvas = canvasRef.current
    if (!canvas || !hasInk) {
      toast('请先在签名区手写签名', 'warning')
      return
    }
    setSigning(true)
    try {
      const res: any = await contractApi.sign({ orderId, signatureData: canvas.toDataURL('image/png') })
      setContract(res.data)
      toast('合同已签署并留档', 'success')
    } catch (e: any) {
      toast(e?.message || '签署失败，请重试', 'error')
    } finally {
      setSigning(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-lg" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span>电子租赁合同 · {orderNo}</span>
          <button className="close-btn" onClick={onClose}><Icon name="close" size={15} /></button>
        </div>
        <div className="modal-body">
          {loading ? (
            <div className="loading-line"><Icon name="refresh" size={18} />合同加载中…</div>
          ) : contract ? (
            <>
              {signed && (
                <div className="callout callout-info">
                  <Icon name="success" size={18} chip />
                  <span>
                    已于 {fmtDT(contract.signTime)} 签署，合同编号 {contract.contractNo}。
                    正文哈希：<span className="mono">{contract.contentHash.slice(0, 16)}…</span>
                  </span>
                </div>
              )}

              <div
                className="contract-content"
                style={{
                  whiteSpace: 'pre-wrap',
                  maxHeight: 320,
                  overflow: 'auto',
                  background: 'var(--surface-2)',
                  border: '1px solid var(--border)',
                  borderRadius: 8,
                  padding: '12px 16px',
                  fontSize: 13,
                  lineHeight: 1.7,
                }}
              >
                {contract.content}
              </div>

              {signed ? (
                <div style={{ marginTop: 16 }}>
                  <div className="field-hint">承租方签名：</div>
                  {contract.signatureUrl ? (
                    <img
                      src={contract.signatureUrl}
                      alt="承租方签名"
                      style={{ maxWidth: 280, maxHeight: 120, border: '1px solid var(--border)', borderRadius: 8, background: '#fff' }}
                    />
                  ) : (
                    <div className="field-hint">签名图缺失</div>
                  )}
                </div>
              ) : (
                <div style={{ marginTop: 16 }}>
                  <div className="field-hint" style={{ marginBottom: 6 }}>
                    请在下方签名区手写签名；签署后合同正文与签名将一并留档，不可修改。
                  </div>
                  <canvas
                    ref={canvasRef}
                    onPointerDown={startDraw}
                    onPointerMove={moveDraw}
                    onPointerUp={endDraw}
                    onPointerLeave={endDraw}
                    onPointerCancel={endDraw}
                    style={{
                      display: 'block',
                      width: '100%',
                      height: 180,
                      background: '#fff',
                      border: '1px dashed var(--border-strong)',
                      borderRadius: 8,
                      touchAction: 'none',
                      cursor: 'crosshair',
                    }}
                  />
                  <div className="form-actions" style={{ marginTop: 10 }}>
                    <button className="btn-sm btn-sm-default" onClick={clearInk} disabled={!hasInk || signing}>重写</button>
                    <button className="btn-sm btn-sm-primary" onClick={submitSign} disabled={!hasInk || signing}>
                      {signing ? '签署中…' : '确认签署'}
                    </button>
                  </div>
                </div>
              )}
            </>
          ) : null}
        </div>
        <div className="modal-footer">
          <button className="btn-sm btn-sm-default" onClick={onClose}>关闭</button>
        </div>
      </div>
    </div>
  )
}
