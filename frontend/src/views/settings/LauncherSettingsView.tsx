/*
 * LauncherSettingsView —— 等价 Vue 版 settings/Launcher.vue，还原 SettingsPage
 * （游戏/Java/下载/账户）+ LauncherSettingsPage（快捷键）。
 * 真实绑定：ConfigAPI（隔离/校验/目录/Java/JVM 参数）、LauncherAPI（内存 6 命令 +
 * DetectJavaMajorVersion）、DownloadAPI（下载源 7 命令）、MonitorAPI.GetMemorySnapshot。
 * 添加 Java / 游戏目录支持 SystemAPI.SelectFile/SelectDirectory 对话框；
 * 输入框留空时点「添加」弹对话框，填写路径则手动添加。
 * 快捷键为前端捕获 + localStorage 持久化（key: nyalauncher.hotkeys）。
 * 待接入（与 Vue 版一致）：JavaRuntimeLocator 自动检索、下载源测速。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SettingsCard, SettingRow, NyaToggle, NyaSelect, NyaSlider, PathListPanel } from '@/components/settings'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import * as Config from '../../../wailsjs/go/bindings/ConfigAPI.js'
import * as Launcher from '../../../wailsjs/go/bindings/LauncherAPI.js'
import * as Download from '../../../wailsjs/go/bindings/DownloadAPI.js'
import { GetMemorySnapshot } from '../../../wailsjs/go/bindings/MonitorAPI.js'
import { SelectDirectory, SelectFile } from '../../../wailsjs/go/bindings/SystemAPI.js'
import { matchAliases, useSettingsSearch } from './search'
import './settings-shared.css'

interface JavaPathItem { JavaPath: string; JavaVersion?: string }
interface MemorySnapshot { LauncherMemoryMb: number; JvmMemoryMb: number; JavaProcessCount: number }

/* ---------- 搜索（Hub 调用，等价 defineExpose(applySearchFilter)） ---------- */
const CARD_ALIASES: Record<string, string[]> = {
  game: ['游戏设置', '实例', '版本隔离', '游戏目录', '内存', '自动内存', '校验文件'],
  java: ['Java 环境', 'java', 'jvm', '虚拟机', '路径', '参数', '运行时'],
  download: ['下载设置', '下载', '下载源', '镜像', '并发', '线程', '回退'],
  account: ['账户管理', '账号', '登录', '微软', '离线'],
  hotkeys: ['快捷键', '热键', '打开设置', '快速启动', '按键', '录制'],
}

function pathsEqual(a: string | null, b: string | null): boolean {
  if (!a || !b) return false
  const norm = (p: string) => p.replace(/[\\/]+$/, '').toLowerCase()
  return norm(a) === norm(b)
}

function formatMemory(mb: number): string {
  return mb >= 1024 ? `${(mb / 1024).toFixed(2).replace(/\.?0+$/, '')} GiB (${mb} MiB)` : `${mb} MiB`
}

export default function LauncherSettingsView() {
  const navigate = useNavigate()
  const search = useSettingsSearch()
  const query = search?.query ?? ''

  /* ---------- 卡片可见性（跟随 Hub 搜索） ---------- */
  const cardVisible = useMemo(() => {
    const q = query.trim()
    if (!q) return null // null = 全部可见
    const result: Record<string, boolean> = {}
    for (const key of Object.keys(CARD_ALIASES)) {
      result[key] = matchAliases(CARD_ALIASES[key], q)
    }
    return result
  }, [query])

  // 命中数回传 Hub（等价 Vue 版 applySearchFilter 返回值）
  useEffect(() => {
    if (!search) return
    if (!query.trim()) { search.setCount('launcher', -1); return }
    const hits = Object.keys(CARD_ALIASES).filter((k) => matchAliases(CARD_ALIASES[k], query)).length
    search.setCount('launcher', hits)
  }, [query, search])

  /* ---------- 版本隔离 / 校验文件 ---------- */
  const [isolation, setIsolation] = useState(false)
  const [verifyFiles, setVerifyFiles] = useState(false)
  const isolationHint = isolation
    ? '已开启：未单独配置的实例默认使用版本隔离；检测到其他启动器（PCL/HMCL 等）的隔离布局时跟随该布局。'
    : '已关闭：未单独配置的实例使用共享目录；检测到其他启动器（PCL/HMCL 等）的隔离布局时跟随该布局。'

  async function onIsolationChange(v: boolean) {
    setIsolation(v)
    await Config.SaveDefaultVersionIsolation(v)
  }
  async function onVerifyFilesChange(v: boolean) {
    setVerifyFiles(v)
    await Config.SaveVerifyFilesBeforeLaunch(v)
  }

  /* ---------- 游戏目录 ---------- */
  const [gameFolders, setGameFolders] = useState<string[]>([])
  const [gameDirectory, setGameDirectory] = useState('')
  const [selectedGameDir, setSelectedGameDir] = useState(-1)
  const [newGameDirPath, setNewGameDirPath] = useState('')
  const [gameDirHint, setGameDirHint] = useState('')

  const gameDirItems = gameFolders.map((p) => ({
    text: p,
    badgeText: pathsEqual(p, gameDirectory) ? '当前' : '目录',
    badgeHighlight: pathsEqual(p, gameDirectory),
  }))
  const canSetCurrentGameDir = !!gameFolders[selectedGameDir] && !pathsEqual(gameFolders[selectedGameDir], gameDirectory)
  const canRemoveGameDir = selectedGameDir >= 0

  const reloadGameDirectories = useCallback(async () => {
    const folders = (await Config.GetProfileFolders()) || []
    const dir = (await Config.GetGameDirectory()) || ''
    setGameFolders(folders)
    setGameDirectory(dir)
    setSelectedGameDir(folders.findIndex((p) => pathsEqual(p, dir))) // 默认选中当前目录
    setGameDirHint(`当前游戏目录：${dir}（共 ${folders.length} 个已添加目录）。`)
  }, [])

  async function addGameDirectory() {
    // 输入框有内容时手动添加；否则弹出系统目录选择对话框
    let path = newGameDirPath.trim()
    if (!path) {
      try {
        path = await SelectDirectory('选择 Minecraft 根目录')
      } catch { /* 用户取消 */ }
      if (!path) return
    }
    if (!(await Config.AddProfileFolder(path))) {
      setGameDirHint('添加失败：该文件夹可能不包含有效的 Minecraft 版本或可识别的实例。')
      return
    }
    await Config.SaveGameDirectory(path)
    setNewGameDirPath('')
    await reloadGameDirectories()
  }
  async function setCurrentGameDir() {
    const sel = gameFolders[selectedGameDir]
    if (!sel) return
    await Config.SaveGameDirectory(sel)
    await reloadGameDirectories()
  }
  async function removeGameDirectory() {
    const sel = gameFolders[selectedGameDir]
    if (!sel) return
    if (pathsEqual(sel, gameDirectory)) {
      setGameDirHint('无法移除当前正在使用的目录，请先切换到其他目录。')
      return
    }
    if (!(await Config.RemoveProfileFolder(sel))) {
      setGameDirHint('移除失败。')
      return
    }
    setSelectedGameDir(-1)
    await reloadGameDirectories()
  }

  /* ---------- 内存 ---------- */
  const [memoryMb, setMemoryMb] = useState(4096)
  const [memorySliderMax, setMemorySliderMax] = useState(4096)
  const [memoryAuto, setMemoryAuto] = useState(false)
  const [systemMemory, setSystemMemory] = useState<{ TotalMemoryMb: number } | null>(null)
  const [memoryMonitor, setMemoryMonitor] = useState<MemorySnapshot | null>(null)
  const [memoryHint, setMemoryHint] = useState('')

  const memoryRangeText = systemMemory
    ? `系统总内存 ${formatMemory(systemMemory.TotalMemoryMb)} · 可选上限 ${formatMemory(memorySliderMax)}`
    : '系统总内存'

  const memoryMbRef = useRef(memoryMb); memoryMbRef.current = memoryMb
  const memoryAutoRef = useRef(memoryAuto); memoryAutoRef.current = memoryAuto
  const systemMemoryRef = useRef(systemMemory); systemMemoryRef.current = systemMemory
  const memoryMonitorRef = useRef(memoryMonitor); memoryMonitorRef.current = memoryMonitor

  const updateMemoryHint = useCallback(async () => {
    const sys = systemMemoryRef.current
    if (!sys) return
    let hint = ''
    if (memoryAutoRef.current) {
      const d = await Launcher.GetMemoryDecision(null)
      const pct = d.TotalMemoryMb > 0 ? Math.round((d.MaximumMemoryMb * 100) / d.TotalMemoryMb) : 0
      hint = `可用 ${formatMemory(d.AvailableMemoryMb)} / 总计 ${formatMemory(d.TotalMemoryMb)}` +
        ` → 预计分配 ${formatMemory(d.MaximumMemoryMb)}（${pct}%）` +
        `，为系统保留 ${formatMemory(d.ReservedMemoryMb)}。每次启动前自动重新计算。`
    } else {
      const pct = sys.TotalMemoryMb > 0 ? Math.round((memoryMbRef.current * 100) / sys.TotalMemoryMb) : 0
      hint = `手动上限 ${formatMemory(memoryMbRef.current)}（占总内存 ${pct}%）。实例可单独设置更低值。`
    }
    const mon = memoryMonitorRef.current
    if (mon) {
      hint += ` 当前：启动器 ${mon.LauncherMemoryMb} MiB · JVM ${mon.JvmMemoryMb} MiB（${mon.JavaProcessCount} 个 Java 进程）。`
    }
    setMemoryHint(hint)
  }, [])

  async function onMemoryChange(v: number) {
    setMemoryMb(v)
    await Launcher.SaveManualMaximumMemoryMb(Math.round(v))
    await updateMemoryHint()
  }
  async function onMemoryAutoChange(v: boolean) {
    setMemoryAuto(v)
    await Launcher.SetAutomaticMemoryAdjustmentEnabled(v)
    await updateMemoryHint()
  }

  /* ---------- Java ---------- */
  const [javaPaths, setJavaPaths] = useState<JavaPathItem[]>([])
  const [selectedJava, setSelectedJava] = useState(-1)
  const [newJavaPath, setNewJavaPath] = useState('')
  const [javaHint, setJavaHint] = useState('')

  const javaItems = javaPaths.map((item, i) => ({
    text: item.JavaPath,
    badgeText: item.JavaVersion ? `Java ${item.JavaVersion}` : '版本未知',
    badgeHighlight: i === 0,
  }))
  const canSetDefaultJava = !!javaPaths[selectedJava] && selectedJava !== 0

  const reloadJavaList = useCallback(async () => {
    const paths = (await Config.GetJavaPaths()) || []
    setJavaPaths(paths)
    setSelectedJava((prev) => (prev >= paths.length ? -1 : prev))
    setJavaHint(paths.length === 0
      ? '尚未保存 Java 路径，启动时将自动检测。'
      : paths.length === 1
        ? `已保存 1 条：${paths[0].JavaPath}`
        : `已保存 ${paths.length} 条，默认：${paths[0].JavaPath}`)
  }, [])

  async function addJava() {
    // 输入框有内容时手动添加；否则弹出系统文件选择对话框（javaw.exe / java.exe）
    let path = newJavaPath.trim()
    if (!path) {
      try {
        path = await SelectFile('选择 Java 可执行文件', 'Java 可执行文件', '*.exe')
      } catch { /* 用户取消 */ }
      if (!path) return
    }
    let version = 'unknown'
    try {
      const v = await Launcher.DetectJavaMajorVersion(path)
      if (v !== null && v !== undefined) version = String(v)
    } catch { /* 探测失败按 unknown 入库 */ }
    if (await Config.AddJava(path, version)) {
      setJavaHint(version !== 'unknown' ? `已添加 Java ${version}：${path}` : `已添加（未能识别版本）：${path}`)
      setNewJavaPath('')
      await reloadJavaList()
    } else {
      setJavaHint('添加 Java 路径失败。')
    }
  }
  async function setDefaultJava() {
    const sel = javaPaths[selectedJava]
    if (!sel) return
    if (!(await Config.SetPrimaryJava(sel.JavaPath))) {
      setJavaHint('设置默认 Java 失败。')
      return
    }
    const current = await Config.LoadGlobalLaunchSettings()
    await Config.SaveGlobalLaunchSettings({ ...current, JavaExecutable: '' })
    setJavaHint(`已将 ${sel.JavaPath} 设为默认。`)
    await reloadJavaList()
  }
  async function removeJava() {
    const sel = javaPaths[selectedJava]
    if (!sel) return
    if (await Config.RemoveJava(sel.JavaPath)) {
      setJavaHint(`已移除：${sel.JavaPath}`)
      setSelectedJava(-1)
      await reloadJavaList()
    } else {
      setJavaHint('移除 Java 路径失败。')
    }
  }

  /* ---------- JVM 参数 ---------- */
  const [jvmArgsText, setJvmArgsText] = useState('')
  async function saveJvmArgs() {
    const args = jvmArgsText.split(/\r?\n/).map((s) => s.trim()).filter(Boolean)
    const current = await Config.LoadGlobalLaunchSettings()
    const ok = await Config.SaveGlobalLaunchSettings({ ...current, JavaExecutable: '', AdditionalJvmArguments: args })
    setJavaHint(ok ? 'JVM 参数已保存。' : '保存失败。')
  }

  /* ---------- Java 运行时 ---------- */
  const [javaRuntimeText, setJavaRuntimeText] = useState('')
  const reloadJavaRuntimes = useCallback(async () => {
    try {
      const runtimes = (await Download.GetInstalledJavaRuntimes()) || []
      setJavaRuntimeText(runtimes.length === 0
        ? '尚未安装自动下载的 Java 运行时。'
        : `已安装：${runtimes.map((r) => `Java ${r.MajorVersion ?? '?'}`).join('、')}`)
    } catch { setJavaRuntimeText('') }
  }, [])

  /* ---------- 下载设置 ---------- */
  const [sources, setSources] = useState<Array<{ Name: string }>>([])
  const [activeSource, setActiveSource] = useState('')
  const [fallbackSource, setFallbackSource] = useState('')
  const [parallelDownloads, setParallelDownloads] = useState(8)
  const [downloadSourceHint, setDownloadSourceHint] = useState('')

  const sourceOptions = sources.map((s) => ({ value: s.Name, label: s.Name }))
  const fallbackOptions = [
    { value: '__disabled__', label: '禁用回退' },
    ...sources.map((s) => ({ value: s.Name, label: s.Name })),
  ]

  function updateDownloadHint(active: string, fallback: string) {
    setDownloadSourceHint(fallback && fallback !== '__disabled__'
      ? `当前：${active}，失败时自动回退到 ${fallback}。`
      : `当前：${active}，未设置回退源。`)
  }
  async function onActiveSourceChange(name: string) {
    setActiveSource(name)
    const src = sources.find((s) => s.Name === name)
    if (src) await Download.SaveActiveDownloadSource(src as never)
    updateDownloadHint(name, fallbackSource)
  }
  async function onFallbackChange(name: string) {
    setFallbackSource(name)
    if (name === '__disabled__') {
      await Download.SaveFallbackDownloadSource(null as never)
    } else {
      const src = sources.find((s) => s.Name === name)
      if (src) await Download.SaveFallbackDownloadSource(src as never)
    }
    updateDownloadHint(activeSource, name)
  }
  async function onParallelChange(v: number) {
    setParallelDownloads(v)
    await Download.SaveParallelDownloads(Math.round(v))
  }

  /* ---------- 快捷键（前端捕获 + localStorage 持久化） ---------- */
  const HOTKEY_KEY = 'nyalauncher.hotkeys'
  const [hotkeys, setHotkeys] = useState<Record<string, string>>(() => {
    try { return JSON.parse(localStorage.getItem(HOTKEY_KEY) || '') || {} } catch { return {} }
  })
  const [hotkeyHint, setHotkeyHint] = useState<Record<string, string>>({})
  const capturing = useRef<string | null>(null)
  const hotkeysRef = useRef(hotkeys); hotkeysRef.current = hotkeys

  function persistHotkeys(next: Record<string, string>) {
    setHotkeys(next)
    localStorage.setItem(HOTKEY_KEY, JSON.stringify(next))
  }
  function formatGesture(e: KeyboardEvent): string {
    const parts: string[] = []
    if (e.ctrlKey) parts.push('Ctrl')
    if (e.altKey) parts.push('Alt')
    if (e.shiftKey) parts.push('Shift')
    const key = e.key.length === 1 ? e.key.toUpperCase() : e.key
    if (!['Control', 'Alt', 'Shift'].includes(e.key)) parts.push(key)
    return parts.join('+')
  }
  function onKeydown(e: KeyboardEvent) {
    const action = capturing.current
    if (!action) return
    e.preventDefault()
    e.stopPropagation()
    if (e.key === 'Escape') {
      setHotkeyHint((h) => ({ ...h, [action]: '' }))
      capturing.current = null
      return
    }
    if (!(e.ctrlKey || e.altKey)) {
      setHotkeyHint((h) => ({ ...h, [action]: '无效组合：需要包含 Ctrl 或 Alt，再试一次（Esc 取消）' }))
      return
    }
    persistHotkeys({ ...hotkeysRef.current, [action]: formatGesture(e) })
    setHotkeyHint((h) => ({ ...h, [action]: '' }))
    capturing.current = null
  }
  function startHotkeyCapture(action: string) {
    if (capturing.current === action) { capturing.current = null; return }
    capturing.current = action
    setHotkeyHint((h) => ({ ...h, [action]: '请按下新组合键（需包含 Ctrl 或 Alt）· Esc 取消 · 再点一次按钮取消' }))
  }
  function clearHotkey(action: string) {
    const next = { ...hotkeysRef.current }
    delete next[action]
    persistHotkeys(next)
  }

  /* ---------- 初始化 ---------- */
  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const iso = await Config.GetDefaultVersionIsolation()
        const verify = await Config.GetVerifyFilesBeforeLaunch()
        if (cancelled) return
        setIsolation(iso)
        setVerifyFiles(verify)
        await reloadGameDirectories()

        setMemorySliderMax(await Launcher.GetMemorySliderMaximum())
        setMemoryMb(await Launcher.GetManualMaximumMemoryMb())
        setMemoryAuto(await Launcher.IsAutomaticMemoryAdjustmentEnabled())
        const sys = await Launcher.GetSystemMemory()
        if (cancelled) return
        setSystemMemory(sys)
        GetMemorySnapshot()
          .then((s) => { if (!cancelled) setMemoryMonitor(s as MemorySnapshot) })
          .finally(updateMemoryHint)
        // 等 React 提交一帧，让 updateMemoryHint 读到的 ref 与最新 state 一致
        await new Promise((r) => setTimeout(r, 0))
        await updateMemoryHint()

        const settings = await Config.LoadGlobalLaunchSettings()
        if (cancelled) return
        setJvmArgsText((settings.AdditionalJvmArguments || []).join('\n'))
        await reloadJavaList()
        await reloadJavaRuntimes()

        const srcs = (await Download.GetAllDownloadSources()) || []
        const active = await Download.GetActiveDownloadSourceName()
        const fallbackName = await Download.GetFallbackDownloadSourceName()
        const parallel = await Download.GetParallelDownloads()
        if (cancelled) return
        setSources(srcs)
        setActiveSource(active)
        setFallbackSource(fallbackName || '__disabled__')
        setParallelDownloads(parallel)
        updateDownloadHint(active, fallbackName || '__disabled__')
      } catch (err) {
        if (!cancelled) setGameDirHint(`加载设置失败：${(err as Error)?.message || err}`)
      }
    })()
    window.addEventListener('keydown', onKeydown, true)
    return () => {
      cancelled = true
      window.removeEventListener('keydown', onKeydown, true)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const vis = (key: string) => (cardVisible === null ? true : cardVisible[key])

  return (
    <section className="page px-10 pt-10 pb-8">
      <header className="page-head">
        <h1 className="m-0 text-[28px] font-bold text-primary-text">启动器设置</h1>
        <p className="m-0 text-[13px] text-hint-text">实例、目录、Java 与下载配置</p>
      </header>

      <div className="cards">
        {/* ==================== 游戏设置 ==================== */}
        {vis('game') ? (
          <SettingsCard icon="🎮" title="游戏设置" subtitle="实例行为、游戏目录与内存分配"
            actions={<Button variant="link" size="sm" onClick={() => navigate('/versions')}>实例管理 ▸</Button>}>
            <SettingRow
              title="版本隔离"
              hint="开启后新实例默认使用独立内容目录（存档、Mod、资源包、光影等各自隔离）"
              extraHint={<span className="text-[11px] text-primary">{isolationHint}</span>}>
              <NyaToggle checked={isolation} onChange={onIsolationChange} />
            </SettingRow>

            <SettingRow title="启动前校验文件完整性"
              hint="每次启动游戏前检查关键文件是否齐全，缺失时自动补全下载">
              <NyaToggle checked={verifyFiles} onChange={onVerifyFilesChange} />
            </SettingRow>

            {/* 游戏目录 */}
            <div className="field-block" data-title="游戏目录">
              <PathListPanel
                label="游戏目录"
                items={gameDirItems}
                selectedIndex={selectedGameDir}
                onSelect={setSelectedGameDir}
                headHint="「添加目录」填写 Minecraft 根目录路径；「设为当前」切换使用的目录"
                hint={<span>{gameDirHint}</span>}>
                <Button size="sm" onClick={addGameDirectory}>添加目录</Button>
                <Button variant="secondary" size="sm" disabled={!canSetCurrentGameDir} onClick={setCurrentGameDir}>设为当前</Button>
                <Button variant="secondary" size="sm" disabled={!canRemoveGameDir} onClick={removeGameDirectory}>删除选中</Button>
              </PathListPanel>
              <Input
                value={newGameDirPath}
                onChange={(e) => setNewGameDirPath(e.target.value)}
                placeholder="输入 Minecraft 根目录绝对路径后点击「添加目录」" />
            </div>

            {/* 内存 */}
            <div className="field-block" data-title="全局最大内存 内存 自动内存">
              <NyaSlider
                value={memoryMb}
                min={512}
                max={memorySliderMax}
                step={256}
                label="全局最大内存"
                showBadge
                badgeText={formatMemory(memoryMb)}
                disabled={memoryAuto}
                onChange={onMemoryChange}>
                <span>{memoryRangeText}</span>
              </NyaSlider>
              <SettingRow title="启动时根据可用内存自动调整"
                extraHint={<span className="text-[11px] text-hint-text">{memoryHint}</span>}>
                <NyaToggle checked={memoryAuto} onChange={onMemoryAutoChange} />
              </SettingRow>
            </div>
          </SettingsCard>
        ) : null}

        {/* ==================== Java ==================== */}
        {vis('java') ? (
          <SettingsCard icon="☕" title="Java 运行环境" subtitle="Java 路径管理与启动参数"
            actions={(
              <Button variant="link" size="sm" disabled title="待接入：JavaRuntimeLocator 系统扫描（未移植）">
                自动检索全部
              </Button>
            )}>
            <div className="field-block" data-title="Java java jvm 虚拟机 路径 参数 运行时">
              <PathListPanel
                label="已保存的 Java"
                items={javaItems}
                selectedIndex={selectedJava}
                onSelect={setSelectedJava}
                headHint="「添加 Java…」填写 javaw.exe 路径（自动探测版本）；列表第一条为默认"
                hint={<span>{javaHint}</span>}>
                <Button size="sm" onClick={addJava}>添加 Java…</Button>
                <Button variant="secondary" size="sm" disabled={!canSetDefaultJava} onClick={setDefaultJava}>设为默认</Button>
                <Button variant="secondary" size="sm" disabled={selectedJava < 0} onClick={removeJava}>删除选中</Button>
              </PathListPanel>
              <Input
                value={newJavaPath}
                onChange={(e) => setNewJavaPath(e.target.value)}
                placeholder="输入 Java 可执行文件绝对路径（如 C:\Program Files\Java\...\javaw.exe）后点击「添加 Java…」" />
            </div>

            <div className="field-block" data-title="jvm 参数 虚拟机">
              <span className="text-[14px] font-semibold text-secondary-text">额外 JVM 参数</span>
              <span className="text-[11px] text-hint-text">每行一个参数，如 -Dfml.ignorePatchDiscrepancies=true</span>
              <textarea
                value={jvmArgsText}
                onChange={(e) => setJvmArgsText(e.target.value)}
                rows={3}
                className="jvm-area w-full rounded-md border border-input bg-background px-3 py-2 text-[12px] text-foreground transition-[border-color,box-shadow] duration-150 placeholder:text-placeholder-text focus-visible:border-ring focus-visible:outline-none focus-visible:ring-[2px] focus-visible:ring-ring/40"
                placeholder={'-XX:+UseG1GC\n-XX:MaxGCPauseMillis=50'} />
              <Button size="sm" onClick={saveJvmArgs}>保存 JVM 参数</Button>
            </div>

            <SettingRow title="Java 运行时下载"
              hint="自动下载 Temurin JDK（Java 8 / 17 / 21）到 .minecraft/runtime，启动时自动检测"
              extraHint={<span className="text-[11px] text-hint-text">{javaRuntimeText}</span>}>
              <Button size="sm" onClick={() => navigate('/download')}>前往下载中心 ›</Button>
            </SettingRow>
          </SettingsCard>
        ) : null}

        {/* ==================== 下载设置 ==================== */}
        {vis('download') ? (
          <SettingsCard icon="⬇" title="下载设置" subtitle="下载源选择与并发下载配置">
            <div className="field-block" data-title="下载 下载源 镜像">
              <div className="field-row">
                <span className="text-[14px] font-semibold text-secondary-text">下载源</span>
                <div className="field-row-controls">
                  <NyaSelect value={activeSource} options={sourceOptions} onChange={onActiveSourceChange} />
                  <Button variant="secondary" size="sm" disabled title="待接入：DownloadSourceProvider.MeasureLatencyAsync（未移植）">
                    测速
                  </Button>
                </div>
              </div>
            </div>

            <div className="field-block" data-title="回退 下载源">
              <div className="field-row">
                <span className="text-[14px] font-semibold text-secondary-text">自动回退源</span>
                <NyaSelect value={fallbackSource} options={fallbackOptions} onChange={onFallbackChange} />
              </div>
              <span className="text-[11px] text-hint-text">{downloadSourceHint}</span>
            </div>

            <div className="field-block" data-title="并发 线程 下载">
              <NyaSlider
                value={parallelDownloads}
                min={1}
                max={32}
                step={1}
                label="并行下载线程数"
                showBadge
                badgeText={String(parallelDownloads)}
                onChange={onParallelChange}>
                <span>同时下载的文件数量，网络较好时可适当增大</span>
              </NyaSlider>
            </div>
          </SettingsCard>
        ) : null}

        {/* ==================== 账户管理 ==================== */}
        {vis('account') ? (
          <SettingsCard icon="👤" title="账户管理" subtitle="管理正版与离线账号、切换默认账号并编辑玩家外观"
            actions={<Button size="sm" onClick={() => navigate('/settings/account')}>打开账户管理</Button>} />
        ) : null}

        {/* ==================== 快捷键（原 LauncherSettingsPage） ==================== */}
        {vis('hotkeys') ? (
          <SettingsCard icon="⌨" title="快捷键" subtitle="应用内操作快捷键，录制时需包含 Ctrl 或 Alt">
            <SettingRow title="打开设置"
              hint="在启动器任意界面按下即可打开设置页。点击右侧按钮录制新组合键。"
              extraHint={hotkeyHint.openSettings ? <span className="text-[11px] text-hint-text">{hotkeyHint.openSettings}</span> : null}>
              <Button variant="link" size="sm" className="min-w-[110px]" onClick={() => startHotkeyCapture('openSettings')}>
                {hotkeys.openSettings || '未设置'}
              </Button>
            </SettingRow>

            <SettingRow title="快捷启动"
              hint="以当前选中的实例与账户直接启动游戏（需先在启动页选好版本）。默认未设置。"
              extraHint={hotkeyHint.quickLaunch ? <span className="text-[11px] text-hint-text">{hotkeyHint.quickLaunch}</span> : null}>
              {hotkeys.quickLaunch ? (
                <Button variant="secondary" size="sm" onClick={() => clearHotkey('quickLaunch')}>清除</Button>
              ) : null}
              <Button variant="link" size="sm" className="min-w-[110px]" onClick={() => startHotkeyCapture('quickLaunch')}>
                {hotkeys.quickLaunch || '未设置'}
              </Button>
            </SettingRow>
          </SettingsCard>
        ) : null}
      </div>
    </section>
  )
}
