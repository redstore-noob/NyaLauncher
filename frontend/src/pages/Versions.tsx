/*
 * 版本管理页（VersionManagerPage.axaml，参照 Vue 版 VersionsView.vue）：
 * 头部（标题/文件夹下拉/重新扫描/打开游戏文件夹 12 项菜单）+
 * 左 250px 实例列表（右键菜单：打开/重命名/删除/设为当前）+
 * 右详情卡 7 标签（版本/启动设置/编辑实例/已安装模组/资源包/光影/游戏存档）+
 * 底部状态栏 + 查看日志遮罩（GameLogOverlay）。
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronDown } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Slider } from '@/components/ui/slider'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { GetGameDirectory, GetProfileFolders, GetVersionProfile, SaveGameDirectory, SaveVersionProfile } from '../../wailsjs/go/bindings/ConfigAPI.js'
import {
  GetCurrentInstanceSnapshot,
  GetInstanceContentDirectory,
  GetVersionDetails,
  RefreshInstances,
  RenameInstance,
  SelectInstance,
  DeleteInstance,
  GetInstanceGameDirectory,
} from '../../wailsjs/go/bindings/InstanceAPI.js'
import { GetInstanceVisual, BackupSave, DeleteSave, ExportSave, ToggleContentEntry } from '../../wailsjs/go/bindings/ContentAPI.js'
import { GetMemoryDecision } from '../../wailsjs/go/bindings/LauncherAPI.js'
import { OpenInExplorer, SaveFile as pickSavePath } from '../../wailsjs/go/bindings/SystemAPI.js'
import { alert, confirm as nyaConfirm, promptDialog } from '@/components/overlay/dialog'
import GameLogOverlay from '@/components/overlay/GameLogOverlay'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime.js'
import type { config, content, instance } from '../../wailsjs/go/models.js'

const tabs = ['版本', '启动设置', '编辑实例', '已安装模组', '资源包', '光影', '游戏存档']

const CONTENT_TABS: Record<string, { list: (d: instance.GameVersionDetails | null) => content.GameContentEntry[]; unit: string }> = {
  已安装模组: { list: (d) => d?.Mods ?? [], unit: '个模组' },
  资源包: { list: (d) => d?.ResourcePacks ?? [], unit: '个资源包' },
  光影: { list: (d) => d?.Shaders ?? [], unit: '个光影' },
  游戏存档: { list: (d) => d?.Saves ?? [], unit: '个存档' },
}

// folderMenuItems 中 tag 语义（对照原版 VersionManagerPage Actions 菜单）：
//   '' → 游戏根目录；versions/libraries/assets → 游戏根目录下的公共目录
//   其余（saves/mods/…）→ 实例内容目录（版本隔离时为实例专属目录，共享时回落游戏目录）
const folderMenuItems = [
  { tag: '', glyph: '📂', label: '打开游戏根目录' },
  { tag: 'versions', glyph: '📦', label: '版本文件夹（versions）' },
  { tag: 'libraries', glyph: '📚', label: '依赖库（libraries）' },
  { tag: 'assets', glyph: '🧱', label: '资源文件（assets）' },
  { tag: 'saves', glyph: '💾', label: '存档（saves）' },
  { tag: 'mods', glyph: '🧩', label: '模组（mods）' },
  { tag: 'resourcepacks', glyph: '🖼️', label: '资源包（resourcepacks）' },
  { tag: 'shaderpacks', glyph: '✨', label: '光影包（shaderpacks）' },
  { tag: 'screenshots', glyph: '📷', label: '截图（screenshots）' },
  { tag: 'config', glyph: '⚙️', label: '配置（config）' },
  { tag: 'logs', glyph: '📄', label: '日志（logs）' },
  { tag: 'crash-reports', glyph: '🐞', label: '崩溃报告（crash-reports）' },
]
const INSTANCE_SCOPED_TAGS = new Set(['saves', 'mods', 'resourcepacks', 'shaderpacks', 'screenshots', 'config', 'logs', 'crash-reports'])

function joinPath(dir: string | undefined, name: string): string {
  if (!dir) return name
  return dir.replace(/[\\/]+$/, '') + '\\' + name
}

function glyphOf(versionId: string): string {
  return (versionId ?? '?')[0].toUpperCase()
}

function fmtMemory(mb: number): string {
  return mb >= 1024 ? `${(mb / 1024).toFixed(2).replace(/\.?0+$/, '')} GiB` : `${mb} MiB`
}

const DEFAULT_PROFILE: config.GameVersionProfile = {
  MinecraftDirectory: '',
  VersionId: '',
  MinimumMemoryMb: 512,
  MaximumMemoryMb: 4096,
  UseIndependentMemorySettings: false,
  FollowGlobalAdvancedSettings: true,
  WindowWidth: 854,
  WindowHeight: 480,
  IsVersionIsolationEnabled: false,
  JavaExecutable: '',
  AdditionalJvmArguments: [],
  AdditionalGameArguments: [],
} as config.GameVersionProfile

export default function VersionsView() {
  const navigate = useNavigate()

  // 文件夹
  const [folders, setFolders] = useState<string[]>([])
  const [selectedFolder, setSelectedFolder] = useState('')
  const selectedFolderRef = useRef(selectedFolder)
  selectedFolderRef.current = selectedFolder

  // 实例列表
  const [versions, setVersions] = useState<string[]>([])
  const [selectedVersion, setSelectedVersion] = useState('')
  const [statusText, setStatusText] = useState('请选择一个 Minecraft 文件夹')

  // 详情
  const [details, setDetails] = useState<instance.GameVersionDetails | null>(null)
  const [activeTab, setActiveTab] = useState('版本')
  const [profile, setProfile] = useState<config.GameVersionProfile>({ ...DEFAULT_PROFILE })
  const profileRef = useRef(profile)
  profileRef.current = profile
  const [newName, setNewName] = useState('')
  const [memoryPolicyText, setMemoryPolicyText] = useState('')
  const [contentSearch, setContentSearch] = useState('')
  const [contentToggling, setContentToggling] = useState('')

  // 高级选项多行参数（等价 Vue 版 computed get/set 的字符串视图）
  const [jvmArgsText, setJvmArgsText] = useState('')
  const [gameArgsText, setGameArgsText] = useState('')

  // 实例图标（ContentAPI.GetInstanceVisual；/localfile 流失败回退字形）
  const [visuals, setVisuals] = useState<Record<string, content.GameInstanceVisual>>({})
  const [brokenIcons, setBrokenIcons] = useState<Set<string>>(new Set())

  // 右键菜单 / 日志遮罩
  const [instanceMenu, setInstanceMenu] = useState<{ open: boolean; x: number; y: number; version: string }>({ open: false, x: 0, y: 0, version: '' })
  const [logOpen, setLogOpen] = useState(false)

  const detailsRef = useRef(details)
  detailsRef.current = details

  function closeInstanceMenu(open = false) {
    if (!open) setInstanceMenu({ open: false, x: 0, y: 0, version: '' })
  }

  function visualGlyph(v: string): string | null {
    return visuals[v]?.FallbackGlyph || null
  }
  function visualIconUrl(v: string): string {
    const path = visuals[v]?.IconPath
    if (!path || brokenIcons.has(v)) return ''
    return `/localfile?path=${encodeURIComponent(path)}`
  }
  function onVisualImgError(v: string) {
    setBrokenIcons((prev) => {
      const next = new Set(prev)
      next.add(v)
      return next
    })
  }

  // ---------- 数据加载 ----------
  async function loadInstanceVisuals(list: string[]) {
    const results = await Promise.allSettled(
      list.map(async (v) => [v, await GetInstanceVisual(v, detailsRef.current?.LoaderName ?? '')] as const),
    )
    const map: Record<string, content.GameInstanceVisual> = {}
    for (const r of results) {
      if (r.status === 'fulfilled' && r.value[1]) map[r.value[0]] = r.value[1]
    }
    setVisuals(map)
  }

  function applySnapshot(snap: instance.GameInstanceSnapshot | null) {
    if (!snap) return
    const list = snap.VersionIds ?? []
    setVersions(list)
    setSelectedVersion(snap.SelectedVersionId ?? '')
    setStatusText(snap.ErrorMessage || `游戏目录：${snap.GameDirectory ?? '—'}`)
    void loadInstanceVisuals(list)
  }

  async function loadFolders() {
    try {
      const list = (await GetProfileFolders()) ?? []
      setFolders(list)
      const configured = await GetGameDirectory()
      setSelectedFolder(list.find((f) => f === configured) ?? list[0] ?? '')
    } catch (e) {
      console.error('加载文件夹列表失败', e)
    }
  }

  async function refresh(path: string) {
    setStatusText('正在扫描实例…')
    try {
      const snap = await RefreshInstances(path)
      applySnapshot(snap)
    } catch (e) {
      setStatusText(`扫描失败：${e}`)
      console.error('扫描实例失败', e)
    }
  }

  async function onFolderChange(folder: string) {
    setSelectedFolder(folder)
    if (!folder) return
    try {
      await SaveGameDirectory(folder)
    } catch (e) {
      console.error('保存游戏目录失败', e)
    }
    await refresh(folder)
  }

  async function loadDetails(versionId: string) {
    try {
      const d = await GetVersionDetails(versionId)
      setDetails(d)
      return d
    } catch (e) {
      setDetails(null)
      console.error('读取实例详情失败', e)
      return null
    }
  }

  async function refreshMemoryPolicy() {
    try {
      const p = profileRef.current
      const decision = await GetMemoryDecision(null)
      if (!decision) return
      setMemoryPolicyText(
        p.UseIndependentMemorySettings
          ? `独立调整已开启；按当前可用内存估算，本实例最大使用 ${fmtMemory(decision.MaximumMemoryMb)}，启动时会重新计算。`
          : `独立调整已关闭；本实例使用全局策略估算最大 ${fmtMemory(decision.MaximumMemoryMb)}。`,
      )
    } catch (e) {
      console.error('读取内存策略失败', e)
    }
  }

  async function selectVersion(v: string) {
    setSelectedVersion(v)
    setContentSearch('')
    await SelectInstance(v).catch((e) => console.error('选中实例失败', e))
    await loadDetails(v)
    try {
      const loaded = await GetVersionProfile(selectedFolderRef.current, v)
      if (loaded) {
        setProfile({ ...DEFAULT_PROFILE, ...loaded })
        setJvmArgsText((loaded.AdditionalJvmArguments ?? []).join('\n'))
        setGameArgsText((loaded.AdditionalGameArguments ?? []).join('\n'))
        profileRef.current = { ...DEFAULT_PROFILE, ...loaded }
      }
      await refreshMemoryPolicy()
    } catch (e) {
      console.error('读取实例设置失败', e)
    }
  }

  // ---------- 头部：打开游戏文件夹菜单 ----------
  async function onOpenSubFolder(item: { tag: string }) {
    const base = selectedFolderRef.current
    if (!base) {
      alert('请先选择一个 Minecraft 文件夹。', { severity: 'warning' })
      return
    }
    try {
      let target: string
      if (item.tag === '') {
        target = base
      } else if (INSTANCE_SCOPED_TAGS.has(item.tag)) {
        // 实例内容目录（后端按版本隔离布局解析）；隔离关闭时即游戏目录本身
        const snap = await GetCurrentInstanceSnapshot()
        const contentDir = await GetInstanceContentDirectory(snap, base)
        target = joinPath(contentDir || base, item.tag)
      } else {
        target = joinPath(base, item.tag)
      }
      await OpenInExplorer(target)
    } catch (e) {
      alert(`打开文件夹失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  // ---------- 实例右键菜单 ----------
  function openInstanceMenu(e: React.MouseEvent, v: string) {
    e.preventDefault()
    setInstanceMenu({ open: true, x: e.clientX, y: e.clientY, version: v })
  }

  async function openInstanceFolder(v: string) {
    closeInstanceMenu()
    try {
      const snap = await GetCurrentInstanceSnapshot()
      await OpenInExplorer(joinPath(joinPath(snap.MinecraftDirectory || selectedFolderRef.current, 'versions'), v))
    } catch (e) {
      alert(`打开文件夹失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  async function renameInstanceMenu(v: string) {
    closeInstanceMenu()
    const name = await promptDialog('重命名实例', `输入「${v}」的新名称：`, { defaultValue: v })
    if (!name || name.trim() === v) return
    try {
      const finalId = await RenameInstance(v, name.trim())
      setStatusText(`已重命名为 ${finalId}`)
      await refresh(selectedFolderRef.current)
    } catch (e) {
      setStatusText(`重命名失败：${(e as Error)?.message ?? e}`)
      alert(`重命名失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  async function setCurrentInstance(v: string) {
    closeInstanceMenu()
    try {
      await SelectInstance(v)
      setStatusText(`已设为当前实例：${v}`)
      await refresh(selectedFolderRef.current)
    } catch (e) {
      alert(`设置当前实例失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  async function deleteInstanceMenu(v: string) {
    closeInstanceMenu()
    if (!(await nyaConfirm('删除实例', `确定删除实例「${v}」？将移除版本文件夹及其全部内容，该操作不可恢复。`, { confirmLabel: '删除' }))) return
    try {
      await DeleteInstance(v, selectedFolderRef.current)
      setStatusText(`已删除实例：${v}`)
      setSelectedVersion((cur) => {
        if (cur === v) {
          setDetails(null)
          setActiveTab('版本')
          return ''
        }
        return cur
      })
      await refresh(selectedFolderRef.current)
    } catch (e) {
      alert(`删除实例失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  // ---------- 存档操作 / 内容启停 ----------
  async function toggleContent(entry: content.GameContentEntry, enabled: boolean) {
    setContentToggling(entry.SourcePath)
    try {
      await ToggleContentEntry(entry.SourcePath, !enabled)
      setDetails((d) => {
        if (!d) return d
        const patch = (list: content.GameContentEntry[]) =>
          list.map((it) => (it.SourcePath === entry.SourcePath ? { ...it, IsDisabled: !enabled } : it))
        return { ...d, Mods: patch(d.Mods ?? []), ResourcePacks: patch(d.ResourcePacks ?? []), Shaders: patch(d.Shaders ?? []), Saves: patch(d.Saves ?? []), convertValues: d.convertValues }
      })
      setStatusText(`已${enabled ? '启用' : '禁用'}「${entry.Name}」`)
    } catch (e) {
      setStatusText(`切换失败：${(e as Error)?.message ?? e}`)
      alert(`切换「${entry.Name}」状态失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    } finally {
      setContentToggling('')
    }
  }

  async function backupSave(entry: content.GameContentEntry) {
    try {
      setStatusText(`正在备份存档「${entry.Name}」…`)
      const zipPath = await BackupSave(entry.SourcePath)
      setStatusText(`已备份：${zipPath}`)
      alert(`存档已备份到 ${zipPath}`, { severity: 'success' })
    } catch (e) {
      setStatusText(`备份失败：${(e as Error)?.message ?? e}`)
      alert(`备份存档失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  async function exportSave(entry: content.GameContentEntry) {
    try {
      // SystemAPI.SaveFile(title, defaultName, filterName, pattern) 选导出路径
      const dest = await pickSavePath('导出存档', `${entry.Name}.zip`, 'ZIP 压缩包', '*.zip')
      if (!dest) return
      setStatusText(`正在导出存档「${entry.Name}」…`)
      const zipPath = await ExportSave(entry.SourcePath, dest)
      setStatusText(`已导出：${zipPath || dest}`)
      alert(`存档已导出到 ${zipPath || dest}`, { severity: 'success' })
    } catch (e) {
      setStatusText(`导出失败：${(e as Error)?.message ?? e}`)
      alert(`导出存档失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  async function deleteSave(entry: content.GameContentEntry) {
    if (!(await nyaConfirm('删除存档', `确定删除存档「${entry.Name}」？该操作不可恢复。`, { confirmLabel: '删除' }))) return
    try {
      await DeleteSave(entry.SourcePath)
      setStatusText(`已删除存档：${entry.Name}`)
      await loadDetails(selectedVersion)
    } catch (e) {
      alert(`删除存档失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  // ---------- 启动设置保存 / 编辑实例 ----------
  function patchProfile(changes: Partial<config.GameVersionProfile>) {
    setProfile((p) => {
      const next = { ...p, ...changes }
      profileRef.current = next
      return next
    })
  }

  async function saveSettings() {
    const p = profileRef.current
    const payload: config.GameVersionProfile = {
      ...p,
      AdditionalJvmArguments: jvmArgsText.split('\n').filter(Boolean),
      AdditionalGameArguments: gameArgsText.split('\n').filter(Boolean),
    }
    if (payload.MinimumMemoryMb < 256 || payload.MaximumMemoryMb < payload.MinimumMemoryMb) {
      setStatusText('保存失败：最小内存至少为 256 MiB，最大内存不能小于最小内存。')
      alert(statusText, { severity: 'warning' })
      return
    }
    try {
      await SaveVersionProfile(payload)
      setStatusText('实例设置已保存。')
    } catch (e) {
      setStatusText(`保存失败：${e}`)
      console.error('保存实例设置失败', e)
    }
  }

  async function renameInstance() {
    const name = newName.trim()
    if (!name || !selectedVersion) return
    if (!(await nyaConfirm('重命名实例', `确定将「${selectedVersion}」重命名为「${name}」吗？`))) return
    try {
      const finalId = await RenameInstance(selectedVersion, name)
      setStatusText(`已重命名为 ${finalId}`)
      setNewName('')
      await refresh(selectedFolderRef.current)
    } catch (e) {
      setStatusText(`重命名失败：${e}`)
      console.error('重命名失败', e)
    }
  }

  async function deleteInstance() {
    if (!selectedVersion) return
    if (!(await nyaConfirm(
      '删除实例',
      `确定删除实例「${selectedVersion}」？将移除版本文件夹及其全部内容，该操作不可恢复。`,
      { confirmLabel: '删除' },
    ))) return
    try {
      await DeleteInstance(selectedVersion, selectedFolderRef.current)
      setStatusText(`已删除实例：${selectedVersion}`)
      setSelectedVersion('')
      setNewName('')
      setDetails(null)
      setActiveTab('版本')
      await refresh(selectedFolderRef.current)
    } catch (e) {
      setStatusText(`删除失败：${(e as Error)?.message ?? e}`)
      alert(`删除实例失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  async function openVersionFolder() {
    // 打开当前选中实例的游戏目录（版本隔离时为实例内容目录）
    if (!selectedVersion || !selectedFolderRef.current) return
    try {
      const snap = await GetCurrentInstanceSnapshot()
      const dir = await GetInstanceGameDirectory(snap, selectedFolderRef.current)
      await OpenInExplorer(dir || selectedFolderRef.current)
    } catch (e) {
      alert(`打开文件夹失败：${(e as Error)?.message ?? e}`, { severity: 'error' })
    }
  }

  // ---------- 派生 ----------
  const detailRows = useMemo(() => {
    const d = details
    return [
      { label: '实际版本 ID', value: d?.VersionId },
      { label: 'Minecraft 基础版本', value: d?.BaseGameVersion },
      { label: '版本类型', value: d?.VersionType },
      { label: '模组加载器', value: d?.LoaderName },
      { label: '模组加载器版本', value: d?.LoaderVersion },
      { label: '版本隔离', value: d == null ? '' : d.IsIsolated ? '已开启' : '已关闭' },
      { label: '布局识别来源', value: d?.LayoutProvider },
      { label: '实例内容目录', value: d?.ContentDirectory },
      { label: '发布时间', value: d?.ReleaseTime },
      { label: 'Java 要求', value: d?.JavaRequirement },
      { label: '主类', value: d?.MainClass },
    ]
  }, [details])

  const contentEntryList = CONTENT_TABS[activeTab]?.list(details) ?? []
  const contentSummary = CONTENT_TABS[activeTab] ? `${contentEntryList.length} ${CONTENT_TABS[activeTab].unit}` : ''
  const filteredContent = useMemo(() => {
    const q = contentSearch.trim().toLowerCase()
    if (!q) return contentEntryList
    return contentEntryList.filter(
      (e) => (e.Name ?? '').toLowerCase().includes(q) || (e.Description ?? '').toLowerCase().includes(q),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [contentEntryList, contentSearch])

  const selectedIconUrl = selectedVersion ? visualIconUrl(selectedVersion) : ''

  // ---------- 生命周期 ----------
  useEffect(() => {
    ;(async () => {
      await loadFolders()
      try {
        applySnapshot(await GetCurrentInstanceSnapshot())
      } catch (e) {
        console.error('读取实例快照失败', e)
      }
    })()
    EventsOn('instance:changed', applySnapshot)
    EventsOn('config:profilesChanged', () => { void loadFolders() })
    return () => {
      EventsOff('instance:changed')
      EventsOff('config:profilesChanged')
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 独立内存设置开关切换后刷新策略说明（等价 Vue watch UseIndependentMemorySettings）
  useEffect(() => {
    void refreshMemoryPolicy()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profile.UseIndependentMemorySettings])

  return (
    <section className="flex h-full flex-col px-[26px] py-[22px]">
      {/* 头部：标题 + 文件夹选择 + 重新扫描 + 打开游戏文件夹 */}
      <div className="mb-[18px] flex items-center gap-2">
        <h1 className="mr-[18px] text-[23px] font-semibold text-foreground">版本管理</h1>
        <Select value={selectedFolder} onValueChange={(v) => void onFolderChange(v)}>
          <SelectTrigger className="h-[42px] min-w-[280px] flex-1">
            <SelectValue placeholder="选择已添加的 Minecraft 文件夹" />
          </SelectTrigger>
          <SelectContent>
            {folders.map((f) => (
              <SelectItem key={f} value={f}>{f}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button variant="secondary" size="lg" onClick={() => void refresh(selectedFolder)}>重新扫描</Button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="secondary" size="lg">
              打开游戏文件夹 <ChevronDown size={14} className="text-muted-foreground" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="max-h-[340px] min-w-60 overflow-y-auto">
            {folderMenuItems.map((item) => (
              <DropdownMenuItem key={item.tag} onSelect={() => void onOpenSubFolder(item)}>
                <span>{item.glyph}</span>{item.label}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {/* 主体：左 250px 实例列表 + 右详情卡 */}
      <div className="grid min-h-0 flex-1 grid-cols-[250px_16px_1fr]">
        {/* 左：实例版本列表 */}
        <div className="flex min-h-0 flex-col rounded-[13px] border border-border bg-[var(--panel-bg)] p-3">
          <div className="mx-1 mb-2.5 mt-0.5 text-[13px] font-semibold text-secondary-text">实例版本</div>
          <div className="flex flex-1 flex-col gap-0.5 overflow-y-auto">
            {versions.map((v) => (
              <button
                key={v}
                className={`flex w-full items-center gap-2 rounded-sm px-3 py-2 text-left text-[13px] text-body-text transition-colors ${
                  v === selectedVersion ? 'bg-accent font-semibold text-primary' : 'hover:bg-accent'
                }`}
                onClick={() => void selectVersion(v)}
                onContextMenu={(e) => openInstanceMenu(e, v)}
              >
                {visualIconUrl(v) ? (
                  <img
                    src={visualIconUrl(v)}
                    className="size-7 shrink-0 rounded-sm object-cover"
                    alt=""
                    onError={() => onVisualImgError(v)}
                  />
                ) : (
                  <span className="flex size-7 shrink-0 items-center justify-center rounded-sm bg-badge font-bold text-primary">
                    {visualGlyph(v) ?? glyphOf(v)}
                  </span>
                )}
                <span className="truncate">{v}</span>
              </button>
            ))}
          </div>
          <div className="mx-1 mb-0.5 mt-2.5 text-[10px] text-muted-text">{versions.length} 个实例</div>
        </div>

        {/* 右：详情卡 */}
        <Card className="col-start-3 flex min-h-0 flex-col p-5">
          {!selectedVersion ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-[7px]">
              <span className="text-[40px] text-secondary-text">▦</span>
              <span className="text-[21px] font-semibold text-secondary-text">请选择一个实例版本</span>
              <span className="text-[11px] text-subtext-text">选择后可以查看详情、内容与启动设置</span>
            </div>
          ) : (
            <>
              {/* 详情头部 */}
              <div className="mb-3.5 flex items-center gap-3">
                {selectedIconUrl ? (
                  <img
                    src={selectedIconUrl}
                    className="size-12 shrink-0 rounded-md object-cover"
                    alt=""
                    onError={() => onVisualImgError(selectedVersion)}
                  />
                ) : (
                  <div className="flex size-12 shrink-0 items-center justify-center rounded-md bg-badge text-[20px] font-bold text-primary">
                    {visualGlyph(selectedVersion) ?? glyphOf(selectedVersion)}
                  </div>
                )}
                <div className="flex min-w-0 flex-col gap-[3px]">
                  <span className="truncate text-[21px] font-semibold text-foreground">{selectedVersion}</span>
                  <span className="text-[11px] text-subtext-text">{details?.BaseGameVersion || 'Minecraft'}</span>
                </div>
                <Button variant="secondary" size="sm" onClick={() => void openVersionFolder()}>打开文件夹</Button>
                <Button variant="secondary" size="sm" onClick={() => { if (selectedVersion) setLogOpen(true) }}>查看日志</Button>
              </div>

              {/* MD3 标签页 */}
              <Tabs value={activeTab} onValueChange={setActiveTab}>
                <TabsList className="h-9 w-full justify-start rounded-lg">
                  {tabs.map((t) => (
                    <TabsTrigger key={t} value={t}>{t}</TabsTrigger>
                  ))}
                </TabsList>
              </Tabs>

              <div className="flex min-h-0 flex-1 flex-col">
                {/* 版本 */}
                {activeTab === '版本' && (
                  <div className="nya-tab-scroll">
                    {detailRows.map((row) => (
                      <div key={row.label} className="grid grid-cols-[150px_1fr] gap-2">
                        <span className="text-[13px] text-muted-foreground">{row.label}</span>
                        <span className="break-all text-[13px] text-body-text">{row.value || '—'}</span>
                      </div>
                    ))}
                  </div>
                )}

                {/* 启动设置 */}
                {activeTab === '启动设置' && (
                  <div className="nya-tab-scroll">
                    <label className="flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                      <Switch
                        checked={!!profile.IsVersionIsolationEnabled}
                        onCheckedChange={(v) => patchProfile({ IsVersionIsolationEnabled: v })}
                      />
                      开启版本隔离（模组、资源包、光影、配置和存档使用识别到的实例内容目录）
                    </label>
                    <div className="flex flex-col gap-2">
                      <label className="flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                        <Switch
                          checked={profile.UseIndependentMemorySettings}
                          onCheckedChange={(v) => patchProfile({ UseIndependentMemorySettings: v })}
                        />
                        独立调整（关闭时锁定下方滑块并使用全局内存设置）
                      </label>
                      <div className="flex items-center justify-between">
                        <span className="text-[13px] text-muted-foreground">实例最小内存</span>
                        <span className="text-[13px] font-semibold text-secondary-text">{profile.MinimumMemoryMb} MiB</span>
                      </div>
                      <Slider
                        value={[profile.MinimumMemoryMb]}
                        min={256}
                        max={4096}
                        step={256}
                        disabled={!profile.UseIndependentMemorySettings}
                        onValueChange={(vals) => patchProfile({ MinimumMemoryMb: vals[0] })}
                      />
                      <div className="mt-1 flex items-center justify-between">
                        <span className="text-[13px] text-muted-foreground">实例最大内存</span>
                        <span className="text-[13px] font-semibold text-secondary-text">{profile.MaximumMemoryMb} MiB</span>
                      </div>
                      <Slider
                        value={[profile.MaximumMemoryMb]}
                        min={512}
                        max={4096}
                        step={256}
                        disabled={!profile.UseIndependentMemorySettings}
                        onValueChange={(vals) => patchProfile({ MaximumMemoryMb: vals[0] })}
                      />
                      <span className="text-[11px] text-hint-text">
                        实际最大内存不会超过设置页中的全局上限。{memoryPolicyText}
                      </span>
                    </div>
                    <details className="nya-advanced">
                      <summary>高级选项</summary>
                      <label className="mt-2.5 flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                        <Switch
                          checked={profile.FollowGlobalAdvancedSettings}
                          onCheckedChange={(v) => patchProfile({ FollowGlobalAdvancedSettings: v })}
                        />
                        跟随全局高级启动设置（默认开启；关闭后才能自定义）
                      </label>
                      <div className="mt-2.5 grid grid-cols-[1fr_12px_1fr]">
                        <div>
                          <div className="nya-field-label">窗口宽度</div>
                          <Input
                            type="number"
                            value={profile.WindowWidth}
                            onChange={(e) => patchProfile({ WindowWidth: Number(e.target.value) || 0 })}
                          />
                        </div>
                        <div>
                          <div className="nya-field-label">窗口高度</div>
                          <Input
                            type="number"
                            value={profile.WindowHeight}
                            onChange={(e) => patchProfile({ WindowHeight: Number(e.target.value) || 0 })}
                          />
                        </div>
                      </div>
                      <div className="nya-field-label mt-2.5">Java 可执行文件（留空时自动检测）</div>
                      <Input
                        type="text"
                        value={profile.JavaExecutable ?? ''}
                        onChange={(e) => patchProfile({ JavaExecutable: e.target.value })}
                      />
                      <div className="nya-field-label mt-2.5">额外 JVM 参数（每行一个参数）</div>
                      <textarea
                        rows={3}
                        className="nya-area"
                        value={jvmArgsText}
                        onChange={(e) => setJvmArgsText(e.target.value)}
                      />
                      <div className="nya-field-label mt-2.5">额外游戏参数（每行一个参数）</div>
                      <textarea
                        rows={3}
                        className="nya-area"
                        value={gameArgsText}
                        onChange={(e) => setGameArgsText(e.target.value)}
                      />
                    </details>
                    <Button className="h-auto self-start px-[18px] py-2" onClick={() => void saveSettings()}>保存实例设置</Button>
                  </div>
                )}

                {/* 编辑实例 */}
                {activeTab === '编辑实例' && (
                  <div className="nya-tab-scroll">
                    <div className="nya-field-label">实例名称（将直接重命名版本文件夹与文件）</div>
                    <div className="grid grid-cols-[1fr_auto] gap-2">
                      <Input
                        type="text"
                        value={newName}
                        onChange={(e) => setNewName(e.target.value)}
                        placeholder="输入新的实例名称"
                      />
                      <Button variant="secondary" onClick={() => void renameInstance()}>重命名</Button>
                    </div>
                    <div className="nya-field-label">导出为整合包</div>
                    <Button variant="secondary" className="self-start" onClick={() => navigate('/modpack')}>制作整合包</Button>
                    <div className="text-[11px] text-hint-text">
                      跳转到整合包制作页：选择 Modrinth / MultiMC 格式、勾选内容并填写作者与描述。
                    </div>
                    <div className="nya-field-label">危险操作</div>
                    <Button variant="destructive" className="h-auto self-start px-[18px] py-2" onClick={() => void deleteInstance()}>删除实例</Button>
                    <div className="text-[11px] text-hint-text">删除操作不可恢复，将移除版本文件夹及其全部内容。</div>
                  </div>
                )}

                {/* 内容类标签页（已安装模组 / 资源包 / 光影 / 存档） */}
                {CONTENT_TABS[activeTab] != null && (
                  <>
                    <div className="flex items-center gap-3 px-3.5 pt-3.5">
                      <span className="shrink-0 text-[13px] font-semibold text-secondary-text">{contentSummary}</span>
                      <Input
                        type="text"
                        value={contentSearch}
                        onChange={(e) => setContentSearch(e.target.value)}
                        className="flex-1"
                        placeholder={`搜索${activeTab}名称…`}
                      />
                    </div>
                    {filteredContent.length === 0 ? (
                      <div className="flex flex-1 items-center justify-center text-[13px] text-hint-text">没有匹配的{activeTab}</div>
                    ) : (
                      <div className="flex flex-1 flex-col gap-1 overflow-y-auto px-3.5 pb-3.5 pt-2">
                        {filteredContent.map((entry) => (
                          <div
                            key={entry.SourcePath}
                            className="flex cursor-default items-center gap-3 rounded-sm bg-transparent px-3 py-3 transition-colors hover:bg-accent"
                          >
                            <span className="flex size-8 shrink-0 items-center justify-center rounded-sm bg-badge">{entry.FallbackGlyph || '📦'}</span>
                            <div className="flex min-w-0 flex-col gap-0.5">
                              <span className={`truncate text-[13px] font-semibold text-foreground ${entry.IsDisabled ? 'line-through opacity-60' : ''}`}>{entry.Name}</span>
                              <span className="truncate text-[10px] text-hint-text">{entry.MetadataLine}</span>
                            </div>
                            <div className="ml-auto flex shrink-0 items-center gap-2">
                              {activeTab === '游戏存档' ? (
                                <>
                                  <Button variant="outline" size="sm" className="text-[11px]" onClick={() => void backupSave(entry)}>备份</Button>
                                  <Button variant="outline" size="sm" className="text-[11px]" onClick={() => void exportSave(entry)}>导出</Button>
                                  <Button variant="outline" size="sm" className="text-[11px] text-destructive" onClick={() => void deleteSave(entry)}>删除</Button>
                                </>
                              ) : (
                                <>
                                  <span className="text-[10px] text-hint-text">{entry.IsDisabled ? '已禁用' : '已启用'}</span>
                                  <Switch
                                    checked={!entry.IsDisabled}
                                    disabled={contentToggling === entry.SourcePath}
                                    onCheckedChange={(v) => void toggleContent(entry, v)}
                                    aria-label="启用/禁用"
                                  />
                                </>
                              )}
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </>
                )}
              </div>
            </>
          )}
        </Card>
      </div>

      {/* 底部状态栏 */}
      <div className="mt-[13px] break-all text-[11px] text-subtext-text">{statusText}</div>

      {/* 实例右键菜单（打开文件夹/重命名/删除/设为当前） */}
      {instanceMenu.open && (
        <div className="fixed z-[800]" style={{ left: instanceMenu.x, top: instanceMenu.y }}>
          <DropdownMenu open onOpenChange={(o) => closeInstanceMenu(o)}>
            <DropdownMenuTrigger asChild>
              <span className="block size-px" aria-hidden="true" />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" side="right" sideOffset={4} className="min-w-44" onContextMenu={(e) => e.preventDefault()}>
              <DropdownMenuItem onSelect={() => void openInstanceFolder(instanceMenu.version)}>
                <span>📂</span>打开文件夹
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => void renameInstanceMenu(instanceMenu.version)}>
                <span>✏️</span>重命名
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => void setCurrentInstance(instanceMenu.version)}>
                <span>▶</span>设为当前
              </DropdownMenuItem>
              <DropdownMenuItem className="text-destructive" onSelect={() => void deleteInstanceMenu(instanceMenu.version)}>
                <span>🗑</span>删除
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      )}

      {/* 查看日志（GameLogOverlay；当前选中实例的启动日志视图） */}
      <GameLogOverlay open={logOpen} onClose={() => setLogOpen(false)} />
    </section>
  )
}
