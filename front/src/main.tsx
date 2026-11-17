import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { UIProvider } from '@/components/UIProvider'
import { initSpringCards } from '@/utils/springCards'
import { installResponsiveTableLabels } from '@/utils/responsiveTables'
import '@/styles/index.css'
import '@/styles/theme.css'

// 卡片弹射归位：document 级事件委托，一次注册全站生效
initSpringCards()

// 手机端表格卡片化：把表头列名同步到单元格，避免窄屏只剩数值没有字段名
installResponsiveTableLabels()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <UIProvider>
        <App />
      </UIProvider>
    </BrowserRouter>
  </React.StrictMode>
)
