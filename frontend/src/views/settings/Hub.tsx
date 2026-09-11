/*
 * Hub —— 设置中心导航壳（等价 Vue 版 settings/Hub.vue，还原 SettingsHubPage）：
 * 顶栏（标题 + 胶囊搜索条）+ 左侧 MD3 导航项 + 右侧内容区（子路由 Outlet）。
 * 与 Vue 版差异：Vue 版在壳内以 tab 状态切换子页，React 版沿用本仓库已有的
 * 路由结构（/settings/launcher 等为独立子路由，App.tsx 标题表按最长前缀匹配），
 * 左栏导航点击即路由跳转；搜索过滤协议不变——query 经 SettingsSearchContext
 * 下发，子页计算卡片可见性并回传命中数（等价 defineExpose(applySearchFilter)），
 * 命中数显示在导航项徽章上；当前页无命中时自动跳到第一个有结果的页，全无命中
 * 显示空态。
 */
import { useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { SettingsSearchContext } from './search'

const tabs = [
  { key: 'launcher', icon: '⚙', title: '启动器设置', desc: '实例、目录、Java 与下载' },
  { key: 'personalization', icon: '🎨', title: '个性化', desc: '主题、背景与工作区布局' },
  { key: 'about', icon: 'ℹ', title: '关于', desc: '团队、依赖与项目信息' },
]

const SEARCHABLE = ['launcher', 'personalization']

export default function SettingsHub() {
  const location = useLocation()
  const navigate = useNavigate()

  const [searchQuery, setSearchQuery] = useState('')
  const [counts, setCounts] = useState<Record<string, number>>({
    launcher: -1, personalization: -1, about: -1,
  })

  const activeTab = useMemo(() => {
    const hit = tabs.find((t) => location.pathname.startsWith(`/settings/${t.key}`))
    return hit?.key ?? 'launcher'
  }, [location.pathname])

  const searching = searchQuery.trim().length > 0
  const emptyState = searching && activeTab !== 'about' && (counts[activeTab] ?? -1) === 0

  function setCount(key: string, count: number) {
    setCounts((prev) => (prev[key] === count ? prev : { ...prev, [key]: count }))
  }

  // 当前页无命中而其他可搜索页有 → 自动跳到第一个有结果的页（等价 Vue 版 applySearch 尾部逻辑）
  if (searching && activeTab !== 'about' && (counts[activeTab] ?? -1) === 0
    && SEARCHABLE.some((k) => (counts[k] ?? -1) > 0)) {
    const target = SEARCHABLE.find((k) => (counts[k] ?? -1) > 0)
    if (target) {
      // 渲染期间不能直接导航，延迟到下一帧（等价 Vue 版 requestAnimationFrame 语义）
      setTimeout(() => navigate(`/settings/${target}`), 0)
    }
  }

  return (
    <SettingsSearchContext.Provider value={{ query: searchQuery, counts, setCount }}>
      <section className="flex h-full flex-col overflow-hidden">
        {/* 顶部标题栏 */}
        <header className="flex items-center justify-between gap-4 border-b border-subtle-border bg-background px-6 pb-3 pt-4">
          <div className="flex flex-col gap-1">
            <h1 className="m-0 text-[21px] font-bold text-primary-text">设置</h1>
            <p className="m-0 text-[11px] text-hint-text">启动器配置、运行环境与个性化</p>
          </div>
          <Input
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="搜索设置…"
            className="w-[260px] rounded-full bg-muted border-none focus-visible:ring-1 focus-visible:ring-ring/40"
            onKeyDown={(e) => { if (e.key === 'Escape') setSearchQuery('') }}
          />
        </header>

        <div className="grid min-h-0 flex-1 grid-cols-[220px_1fr]">
          {/* 左侧标签栏 */}
          <nav className="flex flex-col gap-1 overflow-y-auto border-r border-subtle-border bg-background px-3">
            <span className="mx-2 mt-4 mb-2 text-[10px] font-bold tracking-widest text-muted-text">分类</span>
            {tabs.map((tab) => (
              <button
                key={tab.key}
                className={`flex cursor-pointer flex-col gap-1 rounded-2xl border-none bg-transparent px-4 py-2 text-left transition-colors duration-150 ${
                  activeTab === tab.key ? 'bg-accent' : 'hover:bg-muted'
                }`}
                onClick={() => navigate(`/settings/${tab.key}`)}
              >
                <span className="flex items-center gap-2">
                  <span className={`text-[15px] ${activeTab === tab.key ? 'text-primary' : 'text-subtext-text'}`}>{tab.icon}</span>
                  <span className={`text-[13px] font-semibold ${activeTab === tab.key ? 'text-primary-text' : 'text-subtext-text'}`}>
                    {tab.title}
                  </span>
                  {searching && (counts[tab.key] ?? -1) >= 0 ? (
                    <Badge className="ml-auto rounded-sm bg-badge px-2 py-0.5 text-[9px] font-bold text-primary">
                      {counts[tab.key]}
                    </Badge>
                  ) : null}
                </span>
                <span className="text-[10px] text-hint-text">{tab.desc}</span>
              </button>
            ))}
          </nav>

          {/* 右侧内容区（子路由渲染区，页面过渡由 App 壳的 key 重挂载动画承担） */}
          <div className="relative min-w-0 overflow-y-auto">
            <Outlet />

            {/* 搜索无命中空态 */}
            {emptyState ? (
              <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-background text-[13px] text-hint-text">
                <span className="text-[30px] text-muted-text">🔍</span>
                <span>没有找到相关设置，换个关键词试试喵~</span>
              </div>
            ) : null}
          </div>
        </div>
      </section>
    </SettingsSearchContext.Provider>
  )
}
