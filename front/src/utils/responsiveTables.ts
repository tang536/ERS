/**
 * 手机端表格卡片化的列名补全。
 *
 * 列表页在 ≤620px 时会把 `.data-table` 的每一行渲染成一张卡片，
 * 由 CSS 用 `td::before { content: attr(data-label) }` 显示字段名。
 * 表格的列名本来只存在于 <thead>，而手机端 <thead> 会被隐藏；
 * 因此这里在 DOM 层把当前表头的文字同步到每个单元格的 data-label，
 * 避免每个列表页手写几十个重复的列名，也避免新表格漏写后出现
 * 「只剩右侧数值、左侧字段名空白」的移动端布局事故。
 *
 * 表头文字是列名的唯一来源：单元格已有 data-label 也以表头校正，
 * 避免上一轮把筛选下拉的全部 option 文案拼进列名后一直留在 DOM 里。
 * MutationObserver 负责覆盖翻页、筛选、弹窗内表格等异步渲染。
 */

const TABLE_SELECTOR = 'table.data-table'
const HEADER_SELECTOR = 'thead tr:last-child > th'

let scheduled = false

/**
 * 取表头真正的人读列名。
 * 表头里可能嵌了筛选下拉（如「品牌」+ 全部品牌选项）或排序按钮（「采购时间」+ ⇅）；
 * 直接用 textContent 会把所有 option 文案拼进 data-label，手机卡片就会显示
 * 「品牌全部阿斯顿马丁劳斯莱斯…」。克隆一份、只去掉控件和排序图标再取字。
 */
function headerLabel(th: HTMLTableCellElement): string {
  const clone = th.cloneNode(true) as HTMLTableCellElement
  clone.querySelectorAll('select, option, input, .th-sort-ico').forEach(el => el.remove())
  return (clone.textContent || '').replace(/\s+/g, ' ').trim()
}

function applyTableLabels(table: HTMLTableElement) {
  const headers = Array.from(table.querySelectorAll<HTMLTableCellElement>(HEADER_SELECTOR))
  if (!headers.length) return

  const labels = headers.map(headerLabel)

  table.querySelectorAll<HTMLTableRowElement>('tbody tr').forEach(row => {
    Array.from(row.cells).forEach((cell, index) => {
      // 合并单元格不属于某一列，不能套列名，否则「暂无数据」也会被写成字段行。
      if (cell.colSpan > 1 || cell.rowSpan > 1) return
      const current = cell.getAttribute('data-label')?.trim() || ''
      const headerText = labels[index]?.trim() || ''
      // 表头只有控件（勾选列）时读不到列名；不要用空字符串覆盖单元格已经
      // 写好的语义标签，否则 CSS 的「选择」文案还在，但 data-label 已被清空，
      // 任何按 data-label 做的可访问性检查都会误报。
      const fallback = headerText || (cell.classList.contains('col-check') ? '选择' : '')
      if (fallback && current !== fallback) cell.setAttribute('data-label', fallback)
    })
  })
}

function scan() {
  document.querySelectorAll<HTMLTableElement>(TABLE_SELECTOR).forEach(applyTableLabels)
}

function scheduleScan() {
  if (scheduled) return
  scheduled = true
  // 不用 requestAnimationFrame：浏览器标签页切到后台时 rAF 会被暂停/节流，
  // 表格已经渲染但列名要等回到前台才出现（自动截图与后台轮询都会踩到）。
  // 队列微任务只依赖 DOM 变化，渲染完成后立刻补列名，不等下一帧。
  queueMicrotask(() => {
    scheduled = false
    scan()
  })
}

export function installResponsiveTableLabels() {
  if (typeof document === 'undefined' || !document.body) return

  scan()
  const observer = new MutationObserver(scheduleScan)
  observer.observe(document.body, { childList: true, subtree: true })
  window.addEventListener('resize', scheduleScan, { passive: true })
}
