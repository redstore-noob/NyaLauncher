/*
 * 下载大厅（DownloadPage.axaml，参照 Vue 版 DownloadView.vue）：
 * 标题区（进度/取消/打开文件夹/刷新）+ 左竖排 6 标签
 * （Minecraft 本体 / Mod / 整合包 / 光影包 / 材质包 / Java）+
 * MinecraftDownloadOverlay（版本确认）与 ContentDownloadOverlay（内容下载/整合包安装）遮罩。
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { PackageOpen, RotateCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  ApplyVersionFilter,
  CancelDownload,
  GetCurrentDownloadSnapshot,
  GetVersions,
  InstallJavaRuntime,
  QueryAvailableJavaVersions,
  StartDownload,
  StartModLoaderDownload,
  GetInstalledJavaRuntimes,
  DeleteJavaRuntime,
} from '../../wailsjs/go/bindings/DownloadAPI.js'
import { SaveJava, GetGameDirectory } from '../../wailsjs/go/bindings/ConfigAPI.js'
import { OpenInExplorer, SelectFile } from '../../wailsjs/go/bindings/SystemAPI.js'
import { alert, confirm as nyaConfirm } from '@/components/overlay/dialog'
import SearchBar from '@/components/pages/download/SearchBar'
import Pager from '@/components/pages/download/Pager'
import MinecraftDownloadOverlay from '@/components/overlay/MinecraftDownloadOverlay'
import ContentDownloadOverlay, { type ContentKind, type ProjectLike } from '@/components/overlay/ContentDownloadOverlay'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime.js'
import type { download, models } from '../../wailsjs/go/models.js'

const PAGE_SIZE = 50
const tabNames = ['Minecraft 本体', 'Mod', '整合包', '光影包', '材质包', 'Java']
const modrinthTabs = ['Mod', '整合包', '光影包', '材质包']

// Modrinth project_type 与加载器 facets（对应 ModrinthSearch.SearchAsync）
const modrinthConfig: Record<string, { type: string; loaders: string[] | null }> = {
  Mod: { type: 'mod', loaders: ['fabric', 'forge', 'quilt', 'neoforge'] },
  整合包: { type: 'modpack', loaders: null },
  光影包: { type: 'shader', loaders: null },
  材质包: { type: 'resourcepack', loaders: null },
}

const TAB_KINDS: Record<string, ContentKind> = { Mod: 'mod', 整合包: 'modpack', 光影包: 'shaderpack', 材质包: 'resourcepack' }
const TAB_GLYPHS: Record<string, string> = { Mod: '🧩', 整合包: '📦', 光影包: '✨', 材质包: '🖼️' }
const TAB_ICON_CLASS: Record<string, string> = { Mod: 'bg-accent-deep', 整合包: 'bg-destructive', 光影包: 'bg-success', 材质包: 'bg-badge' }

const javaAdvice = [
  { ver: 'Java 8', text: '适用于 Minecraft 1.8 ~ 1.16.x（老版本必须使用 Java 8）' },
  { ver: 'Java 11', text: '适用于 1.12 ~ 1.16.x 的部分模组环境' },
  { ver: 'Java 17', text: '适用于 Minecraft 1.17 ~ 1.20.x 及常见模组' },
  { ver: 'Java 21', text: '适用于 Minecraft 1.20.5+、最新快照与 NeoForge 21.x' },
  { ver: 'Java 25', text: '适用于最新快照与未来版本（Zulu/Temurin 提供）' },
]
// 顺序必须与 Go JavaVendor 枚举一致：Zulu=0 / Oracle=1 / Temurin=2
const javaVendors = ['Azul Zulu', 'Oracle OpenJDK', 'Eclipse Temurin']

function localVersionFilter(list: models.MinecraftVersion[], key: string): models.MinecraftVersion[] {
  // 后端筛选不可用时的本地近似（C# VersionFilter 语义）
  const map: Record<string, (v: models.MinecraftVersion) => boolean> = {
    release: (v) => v.type === 'release',
    snapshot: (v) => v.type === 'snapshot',
    old: (v) => ['old_alpha', 'old_beta'].includes(v.type),
  }
  const pred = map[key]
  return pred ? list.filter(pred) : list
}

function joinPath(dir: string | undefined | null, name: string): string {
  if (!dir) return name
  return dir.replace(/[\\/]+$/, '') + '\\' + name
}
function formatCount(n?: number | null): string {
  const v = n ?? 0
  if (v >= 1e6) return (v / 1e6).toFixed(1) + 'M'
  if (v >= 1e3) return (v / 1e3).toFixed(1) + 'k'
  return String(v)
}
function formatBytes(n?: number | null): string {
  if (!n) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let i = 0
  let v = n
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toFixed(1)} ${units[i]}`
}
function formatDate(t: unknown): string {
  const d = t ? new Date(t as string) : null
  return d && !isNaN(d.getTime()) ? d.toLocaleDateString() : '—'
}

export default function DownloadView() {
  // ---------- 标签切换 ----------
  const [activeTab, setActiveTab] = useState('Minecraft 本体')
  const [loadingOverlay, setLoadingOverlay] = useState(true)

  // ---------- Minecraft 版本 ----------
  const [allVersions, setAllVersions] = useState<models.MinecraftVersion[]>([])
  const [versionQuery, setVersionQuery] = useState('')
  const [versionTypeFilter, setVersionTypeFilter] = useState('release')
  const [versionPage, setVersionPage] = useState(1)
  const [versionFiltered, setVersionFiltered] = useState<models.MinecraftVersion[]>([])
  const versionQueryRef = useRef(versionQuery)
  versionQueryRef.current = versionQuery
  const versionTypeFilterRef = useRef(versionTypeFilter)
  versionTypeFilterRef.current = versionTypeFilter

  // ---------- Modrinth ----------
  const [contentQuery, setContentQuery] = useState('')
  const [contentPage, setContentPage] = useState(1)
  const [contentCache, setContentCache] = useState<Record<string, { all: ProjectLike[]; loading: boolean }>>({})

  // ---------- 内容下载遮罩 { project, kind, localPath } ----------
  const [contentOverlay, setContentOverlay] = useState<{ project: ProjectLike | null; kind: ContentKind; localPath: string } | null>(null)
  const [mcOverlayVersion, setMcOverlayVersion] = useState<models.MinecraftVersion | null>(null)
  // ---------- 下载进度 ----------
  const [downloadActive, setDownloadActive] = useState(false)
  const [progressPercent, setProgressPercent] = useState(0)
  const [taskStatusText, setTaskStatusText] = useState('当前任务:无')
  const [finishedVersion, setFinishedVersion] = useState('')

  // ---------- Java ----------
  const [javaVendor, setJavaVendor] = useState(javaVendors[0])
  // 后端生成的模型类缺 DisplayName/DetailText 字段（实际返回值带），按扩展形状读取
  type JavaCandidate = download.JavaDownloadCandidate & { DisplayName?: string; DetailText?: string }
  const [javaCandidates, setJavaCandidates] = useState<JavaCandidate[]>([])
  const [javaSelection, setJavaSelection] = useState<JavaCandidate | null>(null)
  const [javaRuntimes, setJavaRuntimes] = useState<download.InstalledJavaRuntime[]>([])
  const [javaProgressVisible, setJavaProgressVisible] = useState(false)
  const [javaProgressPercent, setJavaProgressPercent] = useState(0)
  const [javaStatusText, setJavaStatusText] = useState('')

  const searchTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  // ---------- Minecraft 版本筛选（走后端 ApplyVersionFilter，失败回退本地） ----------
  async function recomputeVersionFilter() {
    const q = versionQueryRef.current.trim().toLowerCase()
    let list = allVersions
    if (q) list = list.filter((v) => v.id.toLowerCase().includes(q))
    try {
      // 与原版一致：类型筛选走后端 DownloadAPI.ApplyVersionFilter（all/release/snapshot/old）
      if (versionTypeFilterRef.current !== 'all') {
        list = (await ApplyVersionFilter(list, versionTypeFilterRef.current)) ?? list
      }
    } catch (e) {
      console.error('版本筛选失败，回退本地筛选', e)
      list = localVersionFilter(list, versionTypeFilterRef.current)
    }
    setVersionFiltered(list)
  }

  useEffect(() => {
    setVersionPage(1)
    void recomputeVersionFilter()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [versionQuery, versionTypeFilter, allVersions])

  async function loadVersions() {
    try {
      // 优先走后端清单（跟随下载源镜像），失败回退空列表
      setAllVersions((await GetVersions()) ?? [])
    } catch (e) {
      console.error('获取版本清单失败', e)
      setAllVersions([])
    }
  }

  // MinecraftDownloadOverlay 确认回调：原版 StartDownload；带加载器 StartModLoaderDownload
  async function onMcOverlayConfirm(options: { loaderType: number; loaderVersion: download.ModLoaderVersion | null; instanceName: string; skipFabricApi: boolean }) {
    const v = mcOverlayVersion
    setMcOverlayVersion(null)
    if (!v) return
    try {
      setTaskStatusText(`开始下载 ${options.instanceName || v.id}`)
      if (options.loaderType === 0) {
        await StartDownload(v)
      } else {
        await StartModLoaderDownload(v, options.loaderVersion!, options.instanceName, options.skipFabricApi)
      }
    } catch (e) {
      console.error('启动版本下载失败', e)
      alert('下载失败：' + e, { severity: 'error' })
    }
  }

  // ---------- Modrinth 搜索（300ms 防抖联网搜索；一次拉前 100 条，客户端过滤分页） ----------
  async function searchModrinth(tab: string, query: string) {
    const cfg = modrinthConfig[tab]
    if (!cfg) return
    setContentCache((c) => ({ ...c, [tab]: { all: [], loading: true } }))
    try {
      const facets: string[][] = [[`project_type:${cfg.type}`]]
      if (cfg.loaders) facets.push(cfg.loaders.map((l) => `categories:${l}`))
      const url =
        'https://api.modrinth.com/v2/search?limit=100&index=relevance' +
        `&query=${encodeURIComponent(query)}` +
        `&facets=${encodeURIComponent(JSON.stringify(facets))}`
      const resp = await fetch(url)
      const data = await resp.json()
      setContentCache((c) => ({ ...c, [tab]: { all: data.hits ?? [], loading: false } }))
    } catch (e) {
      console.error(`搜索 ${tab} 失败`, e)
      setContentCache((c) => ({ ...c, [tab]: { all: [], loading: false } }))
    }
  }

  function switchTab(t: string) {
    setActiveTab(t)
    setContentPage(1)
    if (modrinthTabs.includes(t) && !contentCache[t]) {
      void searchModrinth(t, contentQuery.trim())
    }
  }

  // 搜索词变化：重置页码 + 对 Modrinth 标签页做 300ms 防抖重查
  const firstQueryRender = useRef(true)
  useEffect(() => {
    if (firstQueryRender.current) {
      firstQueryRender.current = false
      return
    }
    setContentPage(1)
    if (!modrinthTabs.includes(activeTab)) return
    if (searchTimer.current) clearTimeout(searchTimer.current)
    searchTimer.current = setTimeout(() => {
      void searchModrinth(activeTab, contentQuery.trim())
    }, 300)
    return () => {
      if (searchTimer.current) clearTimeout(searchTimer.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [contentQuery])

  const contentState = contentCache[activeTab] ?? { all: [], loading: false }
  const contentFiltered = useMemo(() => {
    const q = contentQuery.trim().toLowerCase()
    const all = contentState.all
    if (!q) return all
    return all.filter(
      (p) => String(p.title ?? '').toLowerCase().includes(q) || String(p.description ?? '').toLowerCase().includes(q),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [contentState, contentQuery])

  const versionTotalPages = Math.max(1, Math.ceil(versionFiltered.length / PAGE_SIZE))
  const versionPageItems = versionFiltered.slice((versionPage - 1) * PAGE_SIZE, versionPage * PAGE_SIZE)
  const contentTotalPages = Math.max(1, Math.ceil(contentFiltered.length / PAGE_SIZE))
  const contentPageItems = contentFiltered.slice((contentPage - 1) * PAGE_SIZE, contentPage * PAGE_SIZE)

  function downloadContent(p: ProjectLike) {
    setContentOverlay({ project: p, kind: TAB_KINDS[activeTab] ?? 'mod', localPath: '' })
  }

  // 整合包标签页：导入本地整合包（.mrpack / CurseForge .zip）→ ContentDownloadOverlay 安装流程
  async function importLocalModpack() {
    try {
      const path = await SelectFile('选择整合包文件', '整合包', '*.mrpack;*.zip')
      if (!path) return
      setContentOverlay({ project: null, kind: 'modpack', localPath: path })
    } catch (e) {
      console.error('选择整合包文件失败', e)
      alert('选择文件失败：' + e, { severity: 'error' })
    }
  }

  // ---------- 下载进度（download:progress） ----------
  function applyDownloadSnapshot(snap: download.GameDownloadSnapshot | null) {
    if (!snap) return
    const pct = Math.min(100, snap.Percentage ?? 0)
    const running = !!snap.VersionID && pct > 0 && pct < 100
    setDownloadActive(running)
    if (running) {
      setProgressPercent(pct)
      setTaskStatusText(
        `${snap.VersionID} · ${snap.StageName ?? ''} ${snap.Detail ?? ''} ` +
        `${formatBytes(snap.CompletedBytes)}/${formatBytes(snap.TotalBytes)} · ${formatBytes(snap.BytesPerSecond)}/s`,
      )
    } else if (snap.VersionID && pct >= 100) {
      // 下载完成：记录版本号，展示「打开文件夹」入口
      setFinishedVersion(snap.VersionID)
      setTaskStatusText(`${snap.VersionID} 下载完成`)
    }
  }

  async function openDownloadFolder() {
    const version = finishedVersion
    if (!version) return
    try {
      const gameDir = (await GetGameDirectory()) || ''
      // 优先定位到版本目录；不存在时退回打开游戏根目录
      const candidates = [joinPath(joinPath(gameDir, 'versions'), version), gameDir].filter(Boolean)
      for (const dir of candidates) {
        try { await OpenInExplorer(dir); return } catch { /* 尝试下一个 */ }
      }
    } catch (e) {
      console.error('打开下载目录失败', e)
    }
  }

  async function onCancelDownload() {
    try {
      await CancelDownload()
      setTaskStatusText('已取消')
      setDownloadActive(false)
    } catch (e) {
      console.error('取消下载失败', e)
    }
  }

  // ---------- Java ----------
  async function loadJavaCandidates() {
    setJavaStatusText('正在获取可用版本…')
    try {
      // JavaVendor 枚举按索引传给后端（QueryAvailableJavaVersions(ctx, vendor)）
      const vendorIndex = Math.max(0, javaVendors.indexOf(javaVendor))
      const list = ((await QueryAvailableJavaVersions(vendorIndex as never)) ?? []) as JavaCandidate[]
      setJavaCandidates(list)
      setJavaSelection(null)
      setJavaStatusText('')
    } catch (e) {
      setJavaStatusText(`获取失败：${e}`)
      console.error('获取 Java 版本失败', e)
    }
  }

  async function loadJavaRuntimes() {
    try {
      setJavaRuntimes((await GetInstalledJavaRuntimes()) ?? [])
    } catch (e) {
      console.error('读取已安装 Java 失败', e)
    }
  }

  async function installJava() {
    if (!javaSelection) return
    setJavaProgressVisible(true)
    setJavaProgressPercent(0)
    setJavaStatusText(`开始安装 ${javaSelection.DisplayName ?? ''}…`)
    try {
      await InstallJavaRuntime(javaSelection)
      setJavaStatusText('安装完成。')
      await loadJavaRuntimes()
    } catch (e) {
      setJavaStatusText(`安装失败：${e}`)
      console.error('安装 Java 失败', e)
    } finally {
      setJavaProgressVisible(false)
    }
  }

  async function useJavaRuntime(rt: download.InstalledJavaRuntime) {
    try {
      await SaveJava(rt.JavaExecutablePath, rt.MajorVersion ? `Java ${rt.MajorVersion}` : 'Java')
      setTaskStatusText('已设为全局 Java')
    } catch (e) {
      console.error('设置全局 Java 失败', e)
    }
  }

  async function removeJavaRuntime(rt: download.InstalledJavaRuntime) {
    if (!(await nyaConfirm('删除 Java 运行时', `确定删除此 Java 运行时（${rt.MajorVersion ? `Java ${rt.MajorVersion}` : rt.DirectoryPath}）？`))) return
    try {
      await DeleteJavaRuntime(rt.DirectoryPath)
      await loadJavaRuntimes()
    } catch (e) {
      console.error('删除 Java 失败', e)
    }
  }

  // ---------- 通用 ----------
  function onRefresh() {
    if (activeTab === 'Minecraft 本体') void loadVersions()
    else if (modrinthTabs.includes(activeTab)) void searchModrinth(activeTab, contentQuery.trim())
    else if (activeTab === 'Java') { void loadJavaCandidates(); void loadJavaRuntimes() }
  }

  // ---------- 生命周期 ----------
  useEffect(() => {
    ;(async () => {
      try {
        await Promise.all([loadVersions(), loadJavaRuntimes(), loadJavaCandidates()])
        applyDownloadSnapshot(await GetCurrentDownloadSnapshot())
      } catch (e) {
        console.error('下载页初始化失败', e)
      } finally {
        setLoadingOverlay(false)
        void recomputeVersionFilter()
      }
    })()
    EventsOn('download:progress', applyDownloadSnapshot)
    EventsOn('download:javaProgress', (p: { Percentage?: number; Detail?: string } | null) => {
      if (p) {
        setJavaProgressVisible(true)
        setJavaProgressPercent(Math.min(100, p.Percentage ?? 0))
        setJavaStatusText(p.Detail ?? '')
      }
    })
    return () => {
      EventsOff('download:progress')
      EventsOff('download:javaProgress')
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const tabGlyph = TAB_GLYPHS[activeTab] ?? '📦'
  const tabIconClass = TAB_ICON_CLASS[activeTab] ?? ''

  return (
    <section className="relative flex h-full flex-col gap-0 p-[40px] pl-8">
      {/* 标题区 */}
      <div className="mb-6 flex items-end justify-between gap-4">
        <div className="flex min-w-0 flex-1 flex-col gap-2">
          <h1 className="m-0 text-[28px] font-bold text-foreground">下载大厅</h1>
          {downloadActive && <Progress value={progressPercent} className="h-1" />}
          <div className="flex items-center justify-between gap-2">
            <span className="truncate text-[11px] text-primary">{taskStatusText}</span>
            {downloadActive && <Button variant="secondary" size="sm" onClick={() => void onCancelDownload()}>取消下载</Button>}
            {finishedVersion && <Button variant="secondary" size="sm" onClick={() => void openDownloadFolder()}>打开文件夹</Button>}
          </div>
        </div>
        <Button variant="secondary" className="mb-0.5" onClick={onRefresh}>
          <RotateCw size={14} /><span>刷新</span>
        </Button>
      </div>

      {/* TabControl：左侧竖排标签 */}
      <div className="grid min-h-0 flex-1 grid-cols-[auto_1fr] overflow-hidden rounded-2xl bg-[var(--panel-bg)]">
        <Tabs orientation="vertical" value={activeTab} onValueChange={switchTab}>
          <TabsList className="h-auto flex-col items-stretch gap-1 rounded-none border-r border-subtle-border bg-transparent p-3">
            {tabNames.map((t) => (
              <TabsTrigger
                key={t}
                value={t}
                className="whitespace-nowrap px-4 py-2 text-[13px] font-normal data-[state=active]:bg-accent data-[state=active]:font-semibold data-[state=active]:text-primary data-[state=active]:shadow-none"
              >
                {t}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>

        <div className="flex flex-col gap-4 overflow-y-auto px-6 pb-6 pt-5">
          {/* ===== Minecraft 本体 ===== */}
          {activeTab === 'Minecraft 本体' && (
            <>
              <SearchBar value={versionQuery} onChange={setVersionQuery} placeholder="搜索版本号...">
                <Select value={versionTypeFilter} onValueChange={setVersionTypeFilter}>
                  <SelectTrigger className="h-8 w-auto rounded-sm border-none bg-[var(--panel-bg)] px-2 text-[11px]">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">全部版本</SelectItem>
                    <SelectItem value="release">正式版</SelectItem>
                    <SelectItem value="snapshot">快照版</SelectItem>
                    <SelectItem value="old">远古版本</SelectItem>
                  </SelectContent>
                </Select>
              </SearchBar>
              {versionPageItems.length === 0 ? (
                <div className="my-10 flex flex-col items-center gap-2">
                  <span className="text-[38px] text-muted-text">👻</span>
                  <span className="text-sm text-hint-text">没有找到匹配的版本</span>
                  <span className="text-[13px] text-muted-text">换个关键词或筛选条件试试吧~</span>
                </div>
              ) : (
                <div className="flex flex-col gap-1">
                  {versionPageItems.map((v) => (
                    <button
                      key={v.id}
                      className="flex cursor-pointer items-center gap-2 rounded-md bg-muted px-3 py-3 text-left transition-colors hover:bg-accent"
                      onClick={() => setMcOverlayVersion(v)}
                    >
                      <span className="flex size-10 shrink-0 items-center justify-center rounded-sm bg-accent-deep text-lg">⛏</span>
                      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                        <span className="truncate text-[15px] font-semibold text-foreground">{v.id}</span>
                        <span className="truncate text-[13px] text-hint-text">{v.type} · {formatDate(v.releaseTime)}</span>
                      </div>
                      <span className="mx-2 ml-1 text-muted-text">⬇</span>
                    </button>
                  ))}
                </div>
              )}
              <div className="text-center text-[13px] text-muted-text">共 {versionFiltered.length} 个版本</div>
              <Pager page={versionPage} totalPages={versionTotalPages} onPrev={() => setVersionPage((p) => p - 1)} onNext={() => setVersionPage((p) => p + 1)} />
            </>
          )}

          {/* ===== Modrinth 内容类（Mod / 整合包 / 光影包 / 材质包） ===== */}
          {modrinthTabs.includes(activeTab) && (
            <>
              <div className="flex items-center gap-3">
                <SearchBar value={contentQuery} onChange={setContentQuery} className="flex-1" placeholder={`搜索${activeTab}...`} />
                {activeTab === '整合包' && (
                  <Button variant="secondary" className="shrink-0" onClick={() => void importLocalModpack()}>
                    <PackageOpen size={14} /><span>导入本地整合包</span>
                  </Button>
                )}
              </div>
              {contentState.loading ? (
                <div className="my-10 flex flex-col items-center gap-2">
                  <span className="animate-spin text-[38px] text-muted-text">⏳</span>
                  <span className="text-sm text-hint-text">正在搜索 {activeTab}…</span>
                </div>
              ) : contentPageItems.length === 0 ? (
                <div className="my-10 flex flex-col items-center gap-2">
                  <span className="text-[38px] text-muted-text">👻</span>
                  <span className="text-sm text-hint-text">没有找到匹配的{activeTab}</span>
                  <span className="text-[13px] text-muted-text">换个关键词或筛选条件试试吧~</span>
                </div>
              ) : (
                <div className="flex flex-col gap-1">
                  {contentPageItems.map((p) => (
                    <button
                      key={String(p.project_id)}
                      className="flex cursor-pointer items-center gap-2 rounded-md bg-muted px-3 py-3 text-left transition-colors hover:bg-accent"
                      onClick={() => downloadContent(p)}
                    >
                      {p.icon_url ? (
                        <img className="size-10 shrink-0 rounded-sm object-cover" src={String(p.icon_url)} alt="" />
                      ) : (
                        <span className={`flex size-10 shrink-0 items-center justify-center rounded-sm text-lg ${tabIconClass}`}>{tabGlyph}</span>
                      )}
                      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                        <span className="truncate text-[15px] font-semibold text-foreground">{String(p.title ?? '')}</span>
                        <span className="truncate text-[13px] text-hint-text">{String(p.description ?? '')}</span>
                      </div>
                      <div className="mr-2 flex flex-col items-end gap-1">
                        <span className="text-[11px] text-body-text">⬇ {formatCount(p.downloads)}</span>
                        <span className="text-[11px] text-muted-text">♥ {formatCount(p.follows)}</span>
                      </div>
                    </button>
                  ))}
                </div>
              )}
              <div className="text-center text-[13px] text-muted-text">来自 Modrinth · 共 {contentFiltered.length} 个{activeTab}</div>
              <Pager page={contentPage} totalPages={contentTotalPages} onPrev={() => setContentPage((p) => p - 1)} onNext={() => setContentPage((p) => p + 1)} />
            </>
          )}

          {/* ===== Java ===== */}
          {activeTab === 'Java' && (
            <div className="flex flex-col gap-4">
              <Card className="flex flex-col gap-2 rounded-2xl p-4 px-5">
                <div className="text-sm font-semibold text-foreground">版本选择建议</div>
                {javaAdvice.map((row) => (
                  <div key={row.ver} className="grid grid-cols-[80px_1fr] gap-3">
                    <span className="text-[13px] font-semibold text-primary">{row.ver}</span>
                    <span className="text-[13px] text-body-text">{row.text}</span>
                  </div>
                ))}
                <div className="text-[11px] text-hint-text">下载多套 Java 不会冲突，启动器会按版本自动选择最合适的一套。</div>
              </Card>

              <Card className="flex flex-col gap-2 rounded-2xl p-4 px-5">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-foreground">下载 JDK</div>
                    <div className="text-[11px] text-hint-text">按当前平台自动选择安装包（zip / tar.gz），下载完成后校验并安装</div>
                  </div>
                  <Button variant="outline" size="sm" className="text-[11px] text-[var(--link-text-color)]" onClick={() => void loadJavaCandidates()}>
                    <RotateCw size={12} /> 刷新列表
                  </Button>
                </div>
                <div className="grid grid-cols-[200px_1fr] gap-4">
                  <div className="flex flex-col gap-2">
                    <div className="text-[13px] font-semibold text-secondary-text">JDK 提供商</div>
                    <Select value={javaVendor} onValueChange={(v) => { setJavaVendor(v); setTimeout(() => void loadJavaCandidates(), 0) }}>
                      <SelectTrigger className="w-full rounded-sm"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        {javaVendors.map((v) => (
                          <SelectItem key={v} value={v}>{v}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="flex flex-col gap-2">
                    <div className="text-[13px] font-semibold text-secondary-text">可用版本（实时获取）</div>
                    <div className="flex max-h-[220px] min-h-[120px] flex-col gap-2 overflow-y-auto rounded-sm bg-muted p-2">
                      {javaCandidates.map((c) => (
                        <button
                          key={c.DetailText ?? `${c.MajorVersion}-${c.BuildVersion}`}
                          className={`flex cursor-pointer flex-col gap-0.5 rounded-sm bg-card p-2 text-left transition-opacity hover:opacity-[var(--hover-opacity)] ${
                            javaSelection === c ? 'ring-2 ring-ring' : ''
                          }`}
                          onClick={() => setJavaSelection(c)}
                        >
                          <span className="text-[13px] font-semibold text-foreground">{c.DisplayName}</span>
                          <span className="text-[11px] text-hint-text">{c.DetailText}</span>
                        </button>
                      ))}
                    </div>
                    <div className="flex items-center justify-between gap-3">
                      <span className="truncate text-[13px] text-primary">{javaSelection ? javaSelection.DisplayName : ''}</span>
                      <Button size="sm" disabled={!javaSelection} onClick={() => void installJava()}>
                        ⬇ 下载所选
                      </Button>
                    </div>
                  </div>
                </div>
                {javaProgressVisible && <Progress value={javaProgressPercent} className="h-1" />}
                {javaStatusText && <div className="truncate text-[13px] text-primary">{javaStatusText}</div>}
              </Card>

              <Card className="flex flex-col gap-2 rounded-2xl p-4 px-5">
                <div className="text-sm font-semibold text-foreground">已安装的 Java 运行时</div>
                <div className="text-[11px] text-hint-text">「使用此 Java」设为全局路径；「删除」移除此运行时</div>
                {javaRuntimes.length === 0 && (
                  <div className="text-[11px] text-hint-text">
                    尚未安装自动下载的 Java 运行时，选择上方供应商与版本后开始下载。
                  </div>
                )}
                {javaRuntimes.map((rt) => (
                  <div key={rt.DirectoryPath} className="flex items-center gap-2 rounded-md bg-card px-3 py-2">
                    <span className="shrink-0 text-[13px] font-semibold text-foreground">{rt.MajorVersion ? `Java ${rt.MajorVersion}` : 'Java'}</span>
                    <span className="min-w-0 flex-1 truncate text-[11px] text-hint-text">{rt.JavaExecutablePath}</span>
                    <Button variant="outline" size="sm" className="text-[11px] text-[var(--link-text-color)]" onClick={() => void useJavaRuntime(rt)}>使用此 Java</Button>
                    <Button variant="outline" size="sm" className="text-[11px] text-destructive" onClick={() => void removeJavaRuntime(rt)}>删除</Button>
                  </div>
                ))}
              </Card>
            </div>
          )}
        </div>
      </div>

      {/* 全屏加载遮罩（首次进入） */}
      {loadingOverlay && (
        <div className="absolute inset-0 z-10 flex flex-col items-center justify-center gap-4 bg-overlay">
          <span className="animate-spin text-[38px] text-muted-text">⏳</span>
          <span className="text-lg font-semibold text-body-text">正在加载资源…</span>
          <span className="text-[11px] text-hint-text">正在连接 Modrinth API…</span>
        </div>
      )}

      {/* 版本下载确认遮罩（MinecraftDownloadOverlay） */}
      <MinecraftDownloadOverlay
        version={mcOverlayVersion}
        onClose={() => setMcOverlayVersion(null)}
        onConfirm={(options) => void onMcOverlayConfirm(options)}
      />

      {/* 内容下载遮罩（ContentDownloadOverlay：版本选择 + 目标实例 + 进度） */}
      <ContentDownloadOverlay
        open={contentOverlay !== null}
        project={contentOverlay?.project ?? null}
        kind={contentOverlay?.kind ?? 'modpack'}
        localModpackPath={contentOverlay?.localPath ?? ''}
        onClose={() => setContentOverlay(null)}
      />
    </section>
  )
}
