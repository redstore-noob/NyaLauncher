import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router/index.js'
import './styles/themes.css'   // 家族主题令牌（生成物）
import './styles/shadcn.css'   // Tailwind v4 + shadcn 语义变量映射（依赖 themes.css 级联）
import './styles/base.css'     // 原 Avalonia 动效令牌与通用类
import './store/theme.js' // 挂载即应用 data-theme/data-mode（含持久化恢复）

createApp(App).use(router).mount('#app')
