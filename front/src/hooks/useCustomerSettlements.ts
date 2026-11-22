import { useCallback, useEffect, useMemo, useState } from 'react'
import { settleApi, supplementApi } from '@/api'
import { useUI } from '@/components/UIProvider'

/**
 * 顾客侧结算单 + 补缴单的统一数据源。
 *
 * 「我的订单」与「归还结算单」两页都展示同一份数据，原来各自写了一份加载与
 * orphanSupplements 去重逻辑；这里集中一次，避免同一笔补缴在两页出现不同结果。
 */
export function useCustomerSettlements() {
  const { toast } = useUI()
  const [settles, setSettles] = useState<any[]>([])
  const [supplements, setSupplements] = useState<any[]>([])
  const [loading, setLoading] = useState(true)

  const reload = useCallback(() => {
    setLoading(true)
    settleApi.mine()
      .then((res: any) => setSettles(res.data || []))
      .catch((e: any) => toast(e?.message || '结算单加载失败', 'error'))
      .finally(() => setLoading(false))
    supplementApi.mine()
      .then((res: any) => setSupplements(res.data || []))
      .catch(() => { /* 补缴列表失败不影响结算单主流程 */ })
  }, [toast])

  useEffect(() => { reload() }, [reload])

  /** 已被结算单承载的补缴不再重复列（同一笔钱出现两次会让人误以为欠了两笔） */
  const orphanSupplements = useMemo(() => supplements
    .filter((s: any) => s.payStatus === '待支付')
    .filter((s: any) => !settles.some((x: any) => x.orderId === s.orderId)),
  [supplements, settles])

  return { settles, supplements, orphanSupplements, loading, reload }
}
