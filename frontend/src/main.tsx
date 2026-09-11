import type * as React from 'react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createHashRouter, RouterProvider, Navigate } from 'react-router-dom'
import App from './App'
import { ThemeProvider } from './store/theme'
import { Toaster } from '@/components/ui/sonner'

import './styles/themes.css'   // 家族主题令牌（生成物）
import './styles/shadcn.css'   // Tailwind v4 + shadcn 语义变量映射（依赖 themes.css 级联）
import './styles/base.css'     // 原 Avalonia 动效令牌与通用类
import './styles/overlay.css'  // React 版弹层/页面过渡动画（自 Vue scoped style 平移）

// 路由表与 Vue 版 router/index.js 一致（hash 模式）；页面为占位组件，由页面移植 agent 填充。
// lazy 助手：react-router 的 lazy 约定返回 { Component }
const lazyPage = (load: () => Promise<{ default: React.ComponentType }>) => async () => {
  const mod = await load()
  return { Component: mod.default }
}

const router = createHashRouter([
  {
    path: '/',
    element: <App />,
    children: [
      { index: true, lazy: lazyPage(() => import('./views/HomeView')) },
      { path: 'versions', lazy: lazyPage(() => import('./pages/Versions')) },
      { path: 'download', lazy: lazyPage(() => import('./pages/Download')) },
      { path: 'music', lazy: lazyPage(() => import('./views/MusicView')) },
      { path: 'modpack', lazy: lazyPage(() => import('./views/ModpackView')) },
      {
        path: 'settings',
        lazy: lazyPage(() => import('./views/settings/Hub')),
        children: [
          { index: true, element: <Navigate to="launcher" replace /> },
          { path: 'launcher', lazy: lazyPage(() => import('./views/settings/LauncherSettingsView')) },
          { path: 'personalization', lazy: lazyPage(() => import('./views/settings/PersonalizationSettingsView')) },
          { path: 'ai', lazy: lazyPage(() => import('./views/settings/AiSettingsView')) },
          { path: 'about', lazy: lazyPage(() => import('./views/settings/AboutView')) },
          { path: 'account', lazy: lazyPage(() => import('./views/settings/AccountView')) },
        ],
      },
    ],
  },
])

// <App /> 即窗口壳（三行 Grid + 导航栏）；页面经 <Outlet /> 渲染在内容区。
// Toaster（sonner）挂在根部，随主题变色（shadcn.css 已配 --normal-* 映射）。
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <RouterProvider router={router} />
      <Toaster position="bottom-right" />
    </ThemeProvider>
  </StrictMode>,
)
