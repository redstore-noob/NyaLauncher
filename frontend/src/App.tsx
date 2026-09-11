/*
 * 窗口壳 App（React 版，等价 Vue 版 App.vue）：
 * 三行 Grid（标题栏 58px / 内容 1fr / 状态栏 30px）+ 左导航栏（52↔176px）。
 * wails 拖拽区（--wails-draggable）、窗口控制按钮、Tooltip、任务活动指示、
 * FxLayer / GameLogOverlay / OverlayHost（全局弹窗）挂载与页面切换过渡。
 */
import { useEffect, useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { WindowMinimise, WindowToggleMaximise, Quit, EventsOn } from '../wailsjs/runtime/runtime.js'
import { GetAppVersion } from '../wailsjs/go/bindings/SystemAPI.js'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from '@/components/ui/tooltip'
import Icon from '@/components/overlay/Icon'
import FxLayer from '@/components/overlay/FxLayer'
import GameLogOverlay from '@/components/overlay/GameLogOverlay'
import OverlayHost from '@/components/overlay/OverlayHost'
import { HomeEditModeContext } from '@/components/home/homeEditMode'

// 与 Vue 版 router meta.title 一致的标题表（取最长前缀匹配，等价 matched 最深记录）
const ROUTE_TITLES: Array<[string, string]> = [
  ['/', '工作区'],
  ['/versions', '版本管理'],
  ['/download', '资源下载'],
  ['/music', '音乐播放器'],
  ['/modpack', '整合包制作'],
  ['/settings/launcher', '启动器设置'],
  ['/settings/personalization', '个性化'],
  ['/settings/ai', 'AI 设置'],
  ['/settings/about', '关于'],
  ['/settings/account', '账号管理'],
  ['/settings', '设置'],
]

const navItems = [
  { to: '/download', label: '资源下载', icon: 'nav-download' },
  { to: '/versions', label: '实例管理', icon: 'nav-instances' },
  { to: '/settings/account', label: '账号管理', icon: 'nav-account' },
  { to: '/music', label: '音乐播放器', icon: 'nav-music' },
  { to: '/settings', label: '设置', icon: 'nav-settings' },
]

function isActivePath(current: string, item: { to: string }, all: typeof navItems): boolean {
  if (item.to === '/settings') {
    // 有更具体的子路由导航项（如 /settings/account）时不再点亮「设置」
    if (current.startsWith('/settings') && navItems.some((n) => n.to !== '/settings' && isActivePath(current, n, all))) return false
    return current.startsWith('/settings')
  }
  return current === item.to
}

export default function App() {
  const location = useLocation()
  const navigate = useNavigate()

  const [navExpanded, setNavExpanded] = useState(false)
  const [editMode, setEditMode] = useState(false)
  const [maximised, setMaximised] = useState(false)
  const [logOpen, setLogOpen] = useState(false)
  const [taskActive, setTaskActive] = useState(false)
  const [appVersion, setAppVersion] = useState('v0.0.0-dev')

  // 任务活动指示：下载 / 集成包导出有活动任务时亮起（对应原版 TaskActivityButton）
  useEffect(() => {
    EventsOn('download:progress', (snap: { Phase?: number }) => {
      const phase = Number(snap?.Phase ?? 0)
      if (phase === 1 || phase === 2) { setTaskActive(true); return }
      // 非活跃阶段（完成/失败/取消/空闲）时熄灭
      setTaskActive(false)
    })
    EventsOn('modpack:exportProgress', (p: { Total?: number }) => { setTaskActive((p?.Total ?? 0) > 0) })
    // 窗口最大化状态事件（Go 端须 EventsEmit("window:maximised", bool)；未接时按钮图标保持默认）
    EventsOn('window:maximised', (v: unknown) => { setMaximised(!!v) })
  }, [])

  // 版本号经 SystemAPI.GetAppVersion 动态获取，失败时回落占位
  useEffect(() => {
    GetAppVersion()
      .then((v) => { if (v) setAppVersion(v.startsWith('v') ? v : `v${v}`) })
      .catch(() => { /* 保留占位 */ })
  }, [])

  const isPageRoute = location.pathname !== '/'
  const pageTitle = useMemo(() => {
    const hit = ROUTE_TITLES.filter(([prefix]) => location.pathname === prefix || location.pathname.startsWith(prefix === '/' ? '/' : prefix + '/') || location.pathname === prefix)
      .sort((a, b) => b[0].length - a[0].length)[0]
    return hit?.[1] ?? ''
  }, [location.pathname])

  function minimise() { WindowMinimise() }
  function toggleMaximise() {
    WindowToggleMaximise()
    // ToggleMaximise 无返回值，乐观翻转图标（Go 端如 EventsEmit window:maximised 会覆盖）
    setMaximised((v) => !v)
  }
  function quit() { Quit() }
  function goHome() { navigate('/') }

  return (
    <div
      className="relative isolate h-screen grid overflow-hidden bg-background text-foreground"
      style={{ gridTemplateRows: 'var(--titlebar-height) 1fr var(--statusbar-height)' } as CSSProperties}
    >
      {/* 个性化特效层：自定义背景壁纸（最底层）+ 彩虹背景/星尘/点击圆环 */}
      <FxLayer />

      {/* 全局浮层：NyaAlert / NyaPrompt / DownloadStatusPanel（对应 Vue 版 dialog.js mountOverlays） */}
      <OverlayHost />

      {/* TooltipProvider 必须包裹全应用：导航/标题栏/状态栏的任何 Tooltip 都依赖它 */}
      <TooltipProvider>

        {/* Row 0：自定义标题栏（58px）。身份区/空白区可拖拽移动窗口 */}
        <header
          className="grid grid-cols-[auto_1fr_auto] items-center border-b border-subtle-border bg-background pr-2.5 shadow-[0_1px_6px_rgba(0,0,0,0.08)]"
          style={{ '--wails-draggable': 'drag' } as CSSProperties}
        >
          {/* 左：应用身份 */}
          <div className="flex items-center gap-2.5 pl-[19px] pr-[22px]" style={{ '--wails-draggable': 'noDrag' } as CSSProperties}>
            <div className="grid size-9 place-items-center rounded-[11px] bg-accent-dark">
              <span className="grid size-7 place-items-center rounded-[7px] border border-medium-border bg-primary font-bold text-[14px] text-white">N</span>
            </div>
            <div className="flex flex-col">
              <div className="flex items-center gap-2">
                <span className="text-[15px] font-semibold text-primary-text">NyaLauncher</span>
                <Badge className="rounded-[7px] px-[7px] py-0.5 text-[8px]">PREVIEW 预览版本</Badge>
              </div>
              <span className="text-[10px] text-hint-text">{appVersion}</span>
            </div>
          </div>

          {/* 中：返回按钮（进入页面导航时显示）+ 页面标题 + 拖拽空白 */}
          <div className="flex h-full items-center gap-3">
            {isPageRoute ? (
              <Button
                variant="secondary"
                className="nav-back-button btn-anim h-8 bg-surface px-3.5 border-strong-border text-body-text"
                style={{ '--wails-draggable': 'noDrag' } as CSSProperties}
                onClick={goHome}
              >
                <span className="icon">←</span>
                <span>返回工作区</span>
              </Button>
            ) : null}
            {pageTitle ? <span className="text-[13px] font-semibold text-secondary-text">{pageTitle}</span> : null}
          </div>

          {/* 右：窗口控制（42×34，关闭钮 hover 红底白字） */}
          <div className="flex gap-1" style={{ '--wails-draggable': 'noDrag' } as CSSProperties}>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="window-button" onClick={minimise}>
                  <svg width="12" height="12" viewBox="0 0 12 12"><rect x="0.5" y="8.8" width="11" height="1.8" fill="currentColor" /></svg>
                </button>
              </TooltipTrigger>
              <TooltipContent>最小化</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="window-button" onClick={toggleMaximise}>
                  {!maximised ? (
                    <svg width="12" height="12" viewBox="0 0 12 12">
                      <rect x="1.5" y="1.5" width="9" height="9" fill="none" stroke="currentColor" strokeWidth="1.4" />
                    </svg>
                  ) : (
                    <svg width="12" height="12" viewBox="0 0 12 12">
                      <path d="M 3.5,1.5 L 10.5,1.5 L 10.5,8.5 M 1.5,3.5 L 8.5,3.5 L 8.5,10.5 L 1.5,10.5 Z" fill="none" stroke="currentColor" strokeWidth="1.4" />
                    </svg>
                  )}
                </button>
              </TooltipTrigger>
              <TooltipContent>最大化或还原</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="window-button close" onClick={quit}>
                  <svg width="12" height="12" viewBox="0 0 12 12">
                    <path d="M 2,2 L 10,10 M 10,2 L 2,10" stroke="currentColor" strokeWidth="1.4" />
                  </svg>
                </button>
              </TooltipTrigger>
              <TooltipContent>关闭</TooltipContent>
            </Tooltip>
          </div>
        </header>

        {/* Row 1：导航侧栏（52/176 可展开）+ 内容区 */}
        <div className="flex min-h-0">
          <nav
            className={`nav-rail flex shrink-0 flex-col justify-between overflow-hidden bg-background border-r border-subtle-border py-3 ${navExpanded ? 'expanded' : ''}`}
            style={{ transition: 'width var(--dur-medium) var(--ease-emphasized)' }}
          >
            <div className="flex flex-col gap-1">
              {navItems.map((item) => (
                <Tooltip key={item.to} delayDuration={navExpanded ? 800 : 300}>
                  <TooltipTrigger asChild>
                    <button
                      className={`nav-rail-item btn-anim ${isActivePath(location.pathname, item, navItems) ? 'active' : ''}`}
                      onClick={() => navigate(item.to)}
                    >
                      <Icon className="nav-icon" name={item.icon} size={18} />
                      <span className="nav-label">{item.label}</span>
                    </button>
                  </TooltipTrigger>
                  <TooltipContent side="right">{item.label}</TooltipContent>
                </Tooltip>
              ))}
            </div>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="nav-rail-item btn-anim" onClick={() => setNavExpanded((v) => !v)}>
                  <Icon className="nav-icon" name={navExpanded ? 'nav-chevron-left' : 'nav-chevron-right'} size={18} />
                  <span className="nav-label">{navExpanded ? '收起' : '展开'}</span>
                </button>
              </TooltipTrigger>
              <TooltipContent side="right">{navExpanded ? '收起侧栏' : '展开侧栏'}</TooltipContent>
            </Tooltip>
          </nav>

          <main className="min-w-0 min-h-0 flex-1 overflow-y-auto">
            {/* 页面切换过渡：key 重挂载触发 .page-route-enter 入场动画 */}
            <div key={location.pathname} className="page-route-enter h-full">
              {/* 主页编辑模式经 Context 下发给 ComponentCanvas（等价 Vue 版 provide('homeEditMode')） */}
              <HomeEditModeContext.Provider value={{ editing: editMode }}>
                <Outlet />
              </HomeEditModeContext.Provider>
            </div>
          </main>
        </div>

        {/* Row 2：状态栏（30px） */}
        <footer className="flex items-center justify-between px-4 border-t border-subtle-border bg-background">
          <div>
            <span className="text-[9px] text-warning">测试版 · 功能不稳定，不建议日常使用</span>
          </div>
          <div className="flex items-center gap-1">
            <Button
              size="sm"
              variant="outline"
              className={`add-component-button btn-anim h-6 px-2.5 ${editMode ? 'edit-active' : ''}`}
              onClick={() => setEditMode((v) => !v)}
            >{editMode ? '退出编辑' : '编辑模式'}</Button>
            {/* 任务活动指示（原版 MainWindow TaskActivityButton）：有活动下载/导出任务时亮起，点击打开下载页 */}
            <Tooltip>
              <TooltipTrigger asChild>
                <button className={`task-activity ${taskActive ? 'active' : ''}`} onClick={() => navigate('/download')}>
                  <span className={`block size-2 rounded-full ${taskActive ? 'bg-primary' : 'bg-hint-text/40'}`} />
                </button>
              </TooltipTrigger>
              <TooltipContent>{taskActive ? '有进行中的任务，点击查看' : '暂无进行中的任务'}</TooltipContent>
            </Tooltip>
            <Button size="sm" variant="outline" className="add-component-button btn-anim h-6 px-2.5" onClick={() => setLogOpen(true)}>日志</Button>
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="settings-quick" onClick={() => navigate('/settings')}>
                  <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 15.5A3.5 3.5 0 1 1 12 8.5a3.5 3.5 0 0 1 0 7zm7.4-2.6c.04-.3.07-.6.07-.9s-.02-.6-.07-.9l2-1.6a.5.5 0 0 0 .12-.64l-1.9-3.3a.5.5 0 0 0-.6-.22l-2.4 1a7 7 0 0 0-1.6-.94l-.36-2.5A.5.5 0 0 0 14.2 2h-3.8a.5.5 0 0 0-.5.42l-.35 2.5c-.58.24-1.12.56-1.6.94l-2.4-1a.5.5 0 0 0-.61.22l-1.9 3.3a.5.5 0 0 0 .12.64l2 1.6c-.04.3-.07.6-.07.9s.02.6.07.9l-2 1.6a.5.5 0 0 0-.12.64l1.9 3.3c.13.22.39.31.6.22l2.4-1c.5.38 1.03.7 1.6.94l.36 2.5c.04.24.25.42.5.42h3.8c.25 0 .46-.18.5-.42l.35-2.5a7 7 0 0 0 1.6-.94l2.4 1c.23.09.49 0 .61-.22l1.9-3.3a.5.5 0 0 0-.12-.64l-2-1.6z" />
                  </svg>
                </button>
              </TooltipTrigger>
              <TooltipContent>打开设置</TooltipContent>
            </Tooltip>
          </div>
        </footer>

        {/* 启动日志遮罩（Controls/GameLogOverlay 移植） */}
        <GameLogOverlay open={logOpen} onClose={() => setLogOpen(false)} />
      </TooltipProvider>
    </div>
  )
}

