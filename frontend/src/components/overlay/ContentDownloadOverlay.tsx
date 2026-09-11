/*
 * ContentDownloadOverlay.tsx（移植自 Controls/ContentDownloadOverlay.axaml + DownloadTargetPicker
 * + DownloadStatusPanel，参照 Vue 版 ContentDownloadOverlay.vue）：Modrinth 内容（整合包/资源包/
 * 光影包）版本选择 + 目标实例选择 + 下载进度。radix Dialog 承载。
 *
 * 逻辑与 Vue 版一一对应：
 * - 版本列表拉取 api.modrinth.com/v2/project/{id}/version，MC 版本 / 加载器双过滤
 *   （加载器为 "minecraft" 占位值时不过滤）；
 * - 目标实例经 InstanceAPI.GetCurrentInstanceSnapshot + DownloadAPI.ResolveContentDirectoryForInstance；
 * - Mod/资源包/光影 → DownloadFileToInstance（进度 download:contentProgress）；
 *   自定义路径 → SystemAPI.SaveFile + DownloadFileToPath；
 * - 整合包 → 下载到临时目录 + InstallModpackToInstance，完成后清理临时目录；
 * - 本地整合包导入（localModpackPath 非空）→ 跳过在线版本选择，ReadModpackRequirements 读取要求。
 * PORTING（与 Vue 版相同的简化）：整合包不自动安装缺失 MC 版本/加载器，要求以提示文本展示；
 * 内容下载无取消入参，不提供取消按钮。
 */
import { useEffect, useRef, useState } from 'react'
import { ArrowDown, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  DownloadFileToInstance,
  DownloadFileToPath,
  InstallModpackToInstance,
  ReadModpackRequirements,
  ResolveContentDirectoryForInstance,
} from '../../../wailsjs/go/bindings/DownloadAPI.js'
import { GetCurrentInstanceSnapshot, RefreshInstances } from '../../../wailsjs/go/bindings/InstanceAPI.js'
import { DeleteDirectory } from '../../../wailsjs/go/bindings/LauncherAPI.js'
import { SaveFile as pickSaveFile } from '../../../wailsjs/go/bindings/SystemAPI.js'
import { alert } from './dialog'
import { EventsOn, EventsOff } from '../../../wailsjs/runtime/runtime.js'
import type { instance, download } from '../../../wailsjs/go/models.js'

export type ContentKind = 'mod' | 'modpack' | 'resourcepack' | 'shaderpack'

export interface ProjectLike {
  project_id?: string
  title?: string
  description?: string
  icon_url?: string
  downloads?: number
  follows?: number
}

interface Props {
  open: boolean
  project: ProjectLike | null // Modrinth 搜索结果 { project_id, title, description, icon_url }
  kind: ContentKind // 'modpack' | 'resourcepack' | 'shaderpack'（本地导入模式也是 modpack）
  localModpackPath: string // 非空 = 本地整合包导入模式
  onClose: () => void
}

interface ModrinthVersion {
  id: string
  name?: string
  version_number: string
  date_published?: string
  game_versions?: string[]
  loaders?: string[]
  files?: Array<{ url?: string; filename?: string; size?: number; primary?: boolean }>
}

const SUB_DIRS: Partial<Record<ContentKind, string>> = { resourcepack: 'resourcepacks', shaderpack: 'shaderpacks' }
const GLYPHS: Partial<Record<ContentKind, string>> = { modpack: '📦', resourcepack: '🎨', shaderpack: '✨' }
const LOADER_NAMES: Record<number, string> = { 0: '', 1: 'Fabric', 2: 'Quilt', 3: 'NeoForge', 4: 'Forge' }

function versionLabel(v: ModrinthVersion): string {
  return v.name && v.name !== v.version_number ? `${v.name}（${v.version_number}）` : v.version_number
}
function primaryFile(v: ModrinthVersion) {
  return (v.files ?? []).find((f) => f.primary) ?? (v.files ?? [])[0] ?? null
}
function fileNameOf(p?: string | null): string {
  return String(p ?? '').split(/[\\/]/).pop() || ''
}
function formatBytes(n?: number | null): string {
  if (!n) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let i = 0
  let v = n
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toFixed(1)} ${units[i]}`
}
function mcVersionSortDesc(a: string, b: string): number {
  const pa = a.split('.').map((n) => parseInt(n, 10) || 0)
  const pb = b.split('.').map((n) => parseInt(n, 10) || 0)
  for (let i = 0; i < 3; i++) {
    if ((pa[i] ?? 0) !== (pb[i] ?? 0)) return (pb[i] ?? 0) - (pa[i] ?? 0)
  }
  return b.localeCompare(a)
}

// OverlayHelpers.IsValidInstanceName
function validateInstanceName(name: string): string {
  if (!name) return '请输入新实例的名字。'
  if (name === '.' || name === '..') return '实例名包含不安全字符，请换一个名字。'
  if (/[\\/:*?"<>|]/.test(name)) return '实例名包含不安全字符，请换一个名字。'
  return ''
}

export default function ContentDownloadOverlay({ open, project, kind, localModpackPath, onClose }: Props) {
  const isLocalModpack = !!localModpackPath

  // 版本列表与过滤
  const [allVersions, setAllVersions] = useState<ModrinthVersion[]>([])
  const [filteredVersions, setFilteredVersions] = useState<ModrinthVersion[]>([])
  const [versionLoading, setVersionLoading] = useState(false)
  const [gameVersionFilter, setGameVersionFilter] = useState('所有版本')
  const [loaderFilter, setLoaderFilter] = useState('__all__')
  const [versionIndex, setVersionIndex] = useState('')
  const [gameVersionOptions, setGameVersionOptions] = useState<string[]>(['所有版本'])
  const [loaderOptions, setLoaderOptions] = useState<Array<{ value: string; label: string }>>([])

  // 下载目标
  const [snapshot, setSnapshot] = useState<instance.GameInstanceSnapshot | null>(null)
  const [targetId, setTargetId] = useState('')
  const [newInstanceName, setNewInstanceName] = useState('')

  // 状态面板
  const [idleText, setIdleText] = useState('')
  const [statusFileText, setStatusFileText] = useState('')
  const [statusDetail, setStatusDetail] = useState('')
  const [statusText, setStatusText] = useState('')
  const [downloading, setDownloading] = useState(false)
  const [progressPercent, setProgressPercent] = useState(0)

  // 过滤器等共享量（下载流程回调中读最新值；等价 Vue 的响应式读取）
  const stateRef = useRef({ snapshot, targetId, allVersions, gameVersionFilter, loaderFilter, versionIndex, filteredVersions })
  stateRef.current = { snapshot, targetId, allVersions, gameVersionFilter, loaderFilter, versionIndex, filteredVersions }
  const downloadingRef = useRef(false)
  downloadingRef.current = downloading
  const loadSeq = useRef(0)

  const headerTitle = isLocalModpack ? '导入整合包' : (project?.title ?? '')
  const headerSubtitle = isLocalModpack ? fileNameOf(localModpackPath) : (project?.description ?? '')
  const headerIcon = isLocalModpack ? '' : (project?.icon_url ?? '')
  const headerGlyph = GLYPHS[kind] ?? '📦'

  // PORTING_NOTE: 与 Vue 版一致直接返回 Promise（调用方以 truthy 判断）；此处标注 any 保持编译与运行时行为对齐，
  // 后续应改为 await ResolveContentDirectoryForInstance 后再使用字符串结果。
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  function contentDirOf(snap: instance.GameInstanceSnapshot | null, instanceId: string): any {
    if (!snap || !instanceId) return ''
    return ResolveContentDirectoryForInstance(snap.MinecraftDirectory, snap.SourcePath, instanceId)
  }

  // SetupVersionFilters：MC 版本降序 + 真实加载器列表（"minecraft" 占位值不过滤）
  function setupVersionFilters(list: ModrinthVersion[]) {
    const gameVersions = [...new Set(list.flatMap((v) => v.game_versions ?? []))].sort(mcVersionSortDesc)
    setGameVersionOptions(['所有版本', ...gameVersions])
    setGameVersionFilter('所有版本')
    const loaders = [...new Set(list.flatMap((v) => v.loaders ?? []).filter((l) => l && l.toLowerCase() !== 'minecraft'))]
    setLoaderOptions([
      { value: '__all__', label: '全部加载器' },
      ...loaders.map((l) => ({ value: l, label: l.charAt(0).toUpperCase() + l.slice(1) })),
    ])
    setLoaderFilter('__all__')
  }

  function applyVersionFilters(list: ModrinthVersion[], gv: string, ld: string) {
    const gv2 = gv === '所有版本' ? null : gv
    const ld2 = ld === '__all__' ? null : ld
    const sorted = [...list].sort((a, b) => String(b.date_published ?? '').localeCompare(String(a.date_published ?? '')))
    const filtered = sorted.filter(
      (v) =>
        (!gv2 || (v.game_versions ?? []).some((g) => g.toLowerCase() === gv2.toLowerCase())) &&
        (!ld2 || (v.loaders ?? []).some((l) => l.toLowerCase() === ld2.toLowerCase())),
    )
    setFilteredVersions(filtered)
    setVersionIndex(filtered.length > 0 ? '0' : '')
  }

  async function loadVersions() {
    const pid = project?.project_id
    if (!pid) return
    const seq = ++loadSeq.current
    setVersionLoading(true)
    setAllVersions([])
    setFilteredVersions([])
    try {
      const resp = await fetch(`https://api.modrinth.com/v2/project/${encodeURIComponent(pid)}/version`)
      const data = await resp.json()
      if (seq !== loadSeq.current) return
      const list: ModrinthVersion[] = Array.isArray(data) ? data : []
      setAllVersions(list)
      setupVersionFilters(list)
      applyVersionFilters(list, '所有版本', '__all__')
    } catch (e) {
      if (seq !== loadSeq.current) return
      setStatusText(`加载版本失败：${(e as Error)?.message ?? e}`)
    } finally {
      if (seq === loadSeq.current) setVersionLoading(false)
    }
  }

  // 本地整合包导入（SetupForLocalModpack）
  async function prepareLocalModpack(path: string) {
    setStatusDetail('正在解析整合包所需的游戏版本…')
    try {
      const req = await ReadModpackRequirements(path)
      if (req?.MinecraftVersion) {
        setIdleText(
          `要求 MC ${req.MinecraftVersion}` +
          (req.RawLoaderKey ? ` + ${LOADER_NAMES[req.LoaderType] ?? req.RawLoaderKey} ${req.LoaderVersion ?? ''}` : '（原版）'),
        )
      }
    } catch (e) {
      console.error('读取整合包要求失败', e)
    }
    setStatusDetail('')
  }

  // 打开时 ResetState + 拉数据 + 订阅进度（等价 watch(show) + EventsOn）
  useEffect(() => {
    if (!open) return
    setStatusText('')
    setStatusFileText('')
    setStatusDetail('')
    setIdleText('')
    setProgressPercent(0)
    setDownloading(false)
    setNewInstanceName('')
    setTargetId('')
    loadSeq.current++

    let cancelled = false
    ;(async () => {
      let snap: instance.GameInstanceSnapshot | null = null
      try {
        snap = await GetCurrentInstanceSnapshot()
      } catch (e) {
        console.error('读取实例快照失败', e)
        snap = null
      }
      if (cancelled) return
      setSnapshot(snap)
      if (isLocalModpack) await prepareLocalModpack(localModpackPath)
      else await loadVersions()
    })()

    function updateProgress(e: { downloaded?: number; total?: number } | null) {
      if (!e || !downloadingRef.current) return
      setProgressPercent(e.total && e.total > 0 ? Math.min(100, ((e.downloaded ?? 0) * 100) / e.total) : 0)
    }
    EventsOn('download:contentProgress', updateProgress)
    return () => {
      cancelled = true
      EventsOff('download:contentProgress')
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, localModpackPath])

  function onVersionSelected(idx: string) {
    setVersionIndex(idx)
    const v = filteredVersions[Number(idx)]
    if (!v) return
    const f = primaryFile(v)
    setIdleText(f ? `${versionLabel(v)} · ${formatBytes(f.size)}` : versionLabel(v))
  }

  function beginProgress(fileName: string) {
    setDownloading(true)
    setProgressPercent(0)
    setStatusFileText(fileName)
    setStatusDetail('')
  }

  function finish(message: string) {
    setDownloading(false)
    setProgressPercent(100)
    setStatusDetail(message)
    setStatusFileText('完成')
  }

  // RunModpackInstallAsync：读取要求 → 解析内容目录 → 解压安装 → 汇总
  async function runModpackInstall(packPath: string, instanceName: string, cleanupDir = '') {
    const snap = stateRef.current.snapshot
    if (!snap?.MinecraftDirectory) {
      setStatusText('无法定位游戏目录。')
      return
    }
    try {
      setStatusDetail('正在解析整合包所需的游戏版本…')
      const requirements = await ReadModpackRequirements(packPath).catch(() => null)
      const contentDir = contentDirOf(snap, instanceName) || snap.MinecraftDirectory
      setStatusDetail('正在解压整合包并安装依赖…')
      const result = await InstallModpackToInstance(packPath, contentDir)
      await RefreshInstances(snap.MinecraftDirectory).catch(() => {})
      const freshSnap = await GetCurrentInstanceSnapshot().catch(() => snap)
      stateRef.current.snapshot = freshSnap
      setSnapshot(freshSnap)

      const reqText = !requirements?.MinecraftVersion
        ? '未识别到版本要求'
        : `目标版本：${instanceName || '(选中实例)'}（MC ${requirements.MinecraftVersion}` +
          (requirements.RawLoaderKey
            ? `，加载器 ${LOADER_NAMES[requirements.LoaderType] ?? requirements.RawLoaderKey} ${requirements.LoaderVersion ?? ''}`
            : '，原版') + '）'
      let summary = `已解压 ${result?.InstalledFiles ?? 0} 个文件`
      if ((result?.DownloadedMods ?? 0) > 0) summary += `、下载依赖 ${result.DownloadedMods} 个`
      if ((result?.Errors ?? []).length > 0) summary += `，${result.Errors.length} 项失败`
      finish(`${summary}\n${reqText}`)
      if ((result?.Errors ?? []).length > 0) {
        setStatusText(result.Errors.slice(0, 3).join('；'))
      }
      alert(`整合包「${instanceName || fileNameOf(packPath)}」安装完成：${summary}`, { severity: 'success' })
    } finally {
      // 临时缓存目录清理（本地导入模式不传 cleanupDir，不删除用户源文件）
      if (cleanupDir) await DeleteDirectory(cleanupDir).catch(() => {})
    }
  }

  // 整合包：下载到游戏目录下的临时缓存目录 → InstallModpackToInstance → 清理临时目录
  async function downloadToTempAndInstall(file: NonNullable<ReturnType<typeof primaryFile>>, instanceName: string) {
    const snap = stateRef.current.snapshot
    if (!snap?.MinecraftDirectory) {
      setStatusText('无法定位游戏目录。')
      return
    }
    const tempDir = `${snap.MinecraftDirectory.replace(/[\\/]+$/, '')}\\nya-modpack-tmp`
    const tempPath = `${tempDir}\\${file.filename || 'modpack.mrpack'}`
    beginProgress(file.filename || 'modpack.mrpack')
    await DownloadFileToPath(file.url!, tempPath)
    await runModpackInstall(tempPath, instanceName, tempDir)
  }

  // 下载入口（OnDownloadClick）
  async function onDownload() {
    if (downloading) return
    setStatusText('')
    try {
      if (isLocalModpack) {
        await runModpackInstall(localModpackPath, '')
        return
      }
      const version = filteredVersions[Number(versionIndex)]
      if (!version) {
        setStatusText('请先选择版本。')
        return
      }
      const file = primaryFile(version)
      if (!file?.url) {
        setStatusText('所选版本无可下载文件。')
        return
      }
      if (kind === 'modpack') {
        // 整合包只能安装为独立实例：用户自定义名字
        const name = newInstanceName.trim()
        const err = validateInstanceName(name)
        if (err) {
          setStatusText(err)
          return
        }
        await downloadToTempAndInstall(file, name)
      } else if (targetId === '__custom__') {
        const savePath = await pickSaveFile('保存文件', file.filename ?? '', '内容文件', '*.*')
        if (!savePath) return
        beginProgress(file.filename ?? '')
        await DownloadFileToPath(file.url, savePath)
        finish(`已保存到 ${savePath}`)
      } else {
        if (!targetId) {
          setStatusText('请选择下载目标。')
          return
        }
        const contentDir = contentDirOf(stateRef.current.snapshot, targetId)
        if (!contentDir) {
          setStatusText('无法定位实例内容目录。')
          return
        }
        beginProgress(file.filename ?? '')
        await DownloadFileToInstance(file.url, file.filename ?? '', contentDir, SUB_DIRS[kind] ?? 'mods')
        finish(`已安装到 ${contentDir}`)
      }
    } catch (e) {
      setDownloading(false)
      setStatusText(`操作失败：${(e as Error)?.message ?? e}`)
    }
  }

  const newInstanceHint = (() => {
    const name = newInstanceName.trim()
    return name
      ? `将新建独立实例「${name}」并解压安装整合包内容`
      : '将新建一个独立实例，并在上方输入它的名字'
  })()

  const targetHint = (() => {
    if (targetId === '__custom__') return '下载时将弹出文件保存对话框'
    if (!targetId) return '选择要下载到的目标'
    const dir = contentDirOf(snapshot, targetId)
    const sub = SUB_DIRS[kind] ?? 'mods'
    return dir ? `将放入 ${dir}（${sub}）` : '实例内容目录不可用'
  })()

  const targetOptions = [
    ...(snapshot?.VersionIds ?? []).map((id) => ({ value: id, label: id })),
    { value: '__custom__', label: '自定义保存路径…' },
  ]

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="max-h-[600px] w-[520px] max-w-[520px] gap-[14px] overflow-y-auto rounded-2xl px-6 pb-[22px] pt-5" showCloseButton={false}>
        <DialogTitle className="sr-only">{headerTitle || '下载内容'}</DialogTitle>

        {/* 标题栏（OverlayHeader） */}
        <div className="flex items-center gap-3">
          <div className="flex size-11 shrink-0 items-center justify-center overflow-hidden rounded-[10px] bg-badge text-[20px]">
            {headerIcon ? <img src={headerIcon} className="size-full object-cover" alt="" /> : headerGlyph}
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className="truncate text-base font-bold text-foreground">{headerTitle}</span>
            <span className="truncate text-[11px] text-hint-text">{headerSubtitle}</span>
          </div>
          <button
            className="flex size-[30px] shrink-0 cursor-pointer items-center justify-center rounded-full bg-secondary text-subtext-text transition-opacity hover:opacity-[var(--hover-opacity)]"
            onClick={onClose}
          >
            <X size={14} />
          </button>
        </div>

        {/* 版本选择（本地导入时隐藏） */}
        {!isLocalModpack && (
          <div className="flex flex-col gap-1.5">
            <span className="text-[13px] font-semibold text-secondary-text">选择版本</span>
            <div className="grid grid-cols-[auto_1fr] items-center gap-x-2 gap-y-2">
              <span className="text-[11px] text-muted-text">MC 版本</span>
              <Select
                value={gameVersionFilter}
                onValueChange={(v) => { setGameVersionFilter(v); applyVersionFilters(allVersions, v, loaderFilter) }}
              >
                <SelectTrigger className="h-auto w-full rounded-lg border-none bg-muted px-2.5 py-[5px] text-xs">
                  <SelectValue placeholder="所有版本" />
                </SelectTrigger>
                <SelectContent>
                  {gameVersionOptions.map((g) => (
                    <SelectItem key={g} value={g}>{g}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {loaderOptions.length > 1 && (
                <>
                  <span className="text-[11px] text-muted-text">加载器</span>
                  <Select
                    value={loaderFilter}
                    onValueChange={(v) => { setLoaderFilter(v); applyVersionFilters(allVersions, gameVersionFilter, v) }}
                  >
                    <SelectTrigger className="h-auto w-full rounded-lg border-none bg-muted px-2.5 py-[5px] text-xs">
                      <SelectValue placeholder="全部加载器" />
                    </SelectTrigger>
                    <SelectContent>
                      {loaderOptions.map((l) => (
                        <SelectItem key={l.value} value={l.value}>{l.label}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </>
              )}
            </div>
            <Select
              value={versionIndex}
              onValueChange={onVersionSelected}
              disabled={versionLoading || filteredVersions.length === 0}
            >
              <SelectTrigger className="h-auto w-full rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]">
                <SelectValue placeholder={versionLoading ? '正在加载版本…' : (filteredVersions.length ? '选择版本' : '该过滤条件下没有可用版本')} />
              </SelectTrigger>
              <SelectContent>
                {filteredVersions.map((v, i) => (
                  <SelectItem key={v.id} value={String(i)}>
                    {versionLabel(v)}{primaryFile(v)?.size ? ` · ${formatBytes(primaryFile(v)!.size)}` : ''}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        {/* 下载到（DownloadTargetPicker） */}
        <div className="flex flex-col gap-1.5">
          <span className="text-[13px] font-semibold text-secondary-text">下载到</span>
          {kind === 'modpack' ? (
            <>
              <Input
                value={newInstanceName}
                onChange={(e) => setNewInstanceName(e.target.value)}
                maxLength={48}
                placeholder="给新实例取个名字，例如 MyModpack"
                className="h-auto rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]"
              />
              <span className="text-[11px] leading-relaxed text-hint-text">{newInstanceHint}</span>
            </>
          ) : (
            <>
              <Select value={targetId} onValueChange={setTargetId}>
                <SelectTrigger className="h-auto w-full rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]">
                  <SelectValue placeholder="选择目标实例…" />
                </SelectTrigger>
                <SelectContent>
                  {targetOptions.map((o) => (
                    <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <span className="break-all text-[11px] leading-relaxed text-hint-text">{targetHint}</span>
            </>
          )}
        </div>

        {/* 状态提示（错误/警告） */}
        {statusText && <span className="text-xs leading-relaxed text-destructive">{statusText}</span>}

        {/* 下载状态区（DownloadStatusPanel：ControlBg r12 + 环形进度） */}
        <div className="flex flex-col gap-2 rounded-xl bg-muted px-3.5 py-3">
          <span className="truncate text-xs text-secondary-text">{statusFileText || idleText || '选择版本后点击下载'}</span>
          {downloading && (
            <div className="relative mx-auto grid size-[46px] place-items-center">
              <div
                className="absolute size-[46px] rounded-full"
                style={{ background: `conic-gradient(var(--accent) ${progressPercent * 3.6}deg, var(--control-bg) 0deg)` }}
              />
              <div className="size-[34px] rounded-full bg-muted" />
              <span className="absolute text-xs font-bold text-primary">{Math.round(progressPercent)}</span>
            </div>
          )}
          {statusDetail && <span className="whitespace-pre-line break-all text-[11px] text-hint-text">{statusDetail}</span>}
        </div>

        {/* 底部下载按钮 */}
        <div className="flex justify-end">
          <Button className="h-auto gap-1.5 px-5 py-2 text-[13px] font-semibold" disabled={downloading} onClick={() => void onDownload()}>
            <ArrowDown size={15} /><span>{kind === 'modpack' ? '安装' : '下载'}</span>
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}
