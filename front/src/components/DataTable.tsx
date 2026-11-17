import type { ReactNode } from 'react'

export interface DataTableColumn<T> {
  /** 列标识；同时作为无 render 时的取值字段 */
  key: string
  label: string
  /** 数值列右对齐（金额、次数等） */
  numeric?: boolean
  /** 额外 class，例如 col-hide-md / cell-time */
  className?: string
  render?: (row: T) => ReactNode
}

interface DataTableProps<T> {
  columns: DataTableColumn<T>[]
  rows: T[]
  rowKey: (row: T) => string | number
  /** 无障碍名称：没有可见标题的表格也应说明自己是什么 */
  ariaLabel: string
}

/**
 * 统一的数据表格：列定义同时产出桌面表头与手机卡片字段名。
 *
 * 现在全站已有 `responsiveTables` 作为 DOM 兜底，但表头一旦再嵌筛选/排序控件，
 * 从 DOM 反推列名就容易把 option 文案带进卡片。新表格用本组件时，
 * `label` 是唯一来源，不再依赖扫描；旧表格仍由兜底脚本覆盖，可逐页迁移。
 */
export default function DataTable<T>({ columns, rows, rowKey, ariaLabel }: DataTableProps<T>) {
  return (
    <div className="table-wrap">
      <table className="data-table" aria-label={ariaLabel}>
        <thead>
          <tr>
            {columns.map(col => (
              <th key={col.key} className={`${col.numeric ? 'num' : ''} ${col.className || ''}`.trim() || undefined}>
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(row => (
            <tr key={rowKey(row)}>
              {columns.map(col => (
                <td
                  key={col.key}
                  data-label={col.label}
                  className={`${col.numeric ? 'num' : ''} ${col.className || ''}`.trim() || undefined}
                >
                  {col.render ? col.render(row) : ((row as Record<string, unknown>)[col.key] ?? '—') as ReactNode}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
