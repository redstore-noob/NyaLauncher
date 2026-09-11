/*
 * 整合包制作页 —— 等价 Vue 版 ModpackView.vue（ModpackCreatorPage.axaml + .cs 移植）。
 * 内容勾选走 ModpackAPI.CollectExportContent；导出走 ExportModpack，
 * 进度经 modpack:exportProgress 事件展示；完成后把勾选列表与元数据写回实例档案
 * （SaveExportProfile）。保存路径走 SystemAPI.SaveFile 原生对话框；
 * 打包图标经 SystemAPI.SelectFile 选择 png。
 * 导出为一次性调用（绑定层未暴露取消导出的方法）。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { alert } from '@/components/overlay/dialog'
import { SaveFile, SelectFile } from '../../wailsjs/go/bindings/SystemAPI.js'
import Icon from '@/components/overlay/Icon'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Progress } from '@/components/ui/progress'
import {
  CollectExportContent, ExportModpack, LoadExportProfile, SaveExportProfile,
} from '../../wailsjs/go/bindings/ModpackAPI.js'
import {
  GetCurrentInstanceSnapshot, GetVersionDetails,
} from '../../wailsjs/go/bindings/InstanceAPI.js'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime.js'
import { NyaCheckbox } from '@/components/settings'
import './settings/settings-shared.css'

interface ContentItem {
  Name: string
  RelativePath: string
  SizeBytes: number
  Category?: string
  CategoryDisplay?: string
}
interface ContentRow { item: ContentItem; included: boolean }
interface GroupView {
  category: string
  items: ContentRow[]
  isExpanded: boolean
  masterChecked: boolean | null
  countDisplay: string
  sizeDisplay: string
}
interface VersionDetails {
  VersionId: string
  VersionDirectory: string
  ContentDirectory: string
  BaseGameVersion: string
  IsVanilla?: boolean
  LoaderName: string
  LoaderVersion: string
}

const CATEGORY_DESCRIPTIONS: Record<string, string> = {
  模组: 'mods 目录下的模组文件，整合包的核心内容；Modrinth 格式会尽量匹配直链，匹配不到的进 overrides。',
  配置: 'config 等配置目录，模组与游戏的规则设定都在这里，决定了整合包的实际玩法。',
  根文件: 'options.txt、servers.dat 等实例根目录文件：游戏设置、按键映射与服务器列表。',
  资源包: 'resourcepacks 目录下的材质 / 资源包，改变游戏画面与音效。',
  光影包: 'shaderpacks 目录下的光影包，需安装 Iris / OptiFine 等才能生效。',
  存档: 'saves 目录下的世界存档，体积可能很大，注意整合包大小。',
}

function formatSize(bytes: number): string {
  if (bytes >= 1073741824) return `${trimZeros((bytes / 1073741824).toFixed(2))} GB`
  if (bytes >= 1048576) return `${trimZeros((bytes / 1048576).toFixed(2))} MB`
  if (bytes >= 1024) return `${trimZeros((bytes / 1024).toFixed(1))} KB`
  return `${bytes} B`
}

function trimZeros(text: string): string {
  return text.replace(/\.?0+$/, '')
}

function sanitizeFileName(name: string): string {
  return name.trim().replace(/[<>:"/\\|?*]/g, '_')
}

export default function ModpackView() {
  const [details, setDetails] = useState<VersionDetails | null>(null)

  const [allContent, setAllContent] = useState<ContentRow[]>([])  // [{ item, included }]
  const [groups, setGroups] = useState<GroupView[]>([])           // 分组视图（跟随搜索过滤）
  const [filterQuery, setFilterQuery] = useState('')
  const expandedCategories = useRef<Set<string>>(new Set())
  const profile = useRef<Record<string, unknown>>({})

  // 表单
  const [format, setFormat] = useState(0)
  const [packName, setPackName] = useState('')
  const [packVersion, setPackVersion] = useState('1.0.0')
  const [author, setAuthor] = useState('')
  const [updateLink, setUpdateLink] = useState('')
  const [description, setDescription] = useState('')
  const [resolveLinks, setResolveLinks] = useState(true)
  const [iconPngPath, setIconPngPath] = useState('')

  // 状态
  const [packing, setPacking] = useState(false)
  const [packStatus, setPackStatus] = useState('')
  const [statusText, setStatusText] = useState('就绪')
  const [progressCurrent, setProgressCurrent] = useState(0)
  const [progressTotal, setProgressTotal] = useState(0)

  const progressPercent = progressTotal > 0 ? (100 * progressCurrent) / progressTotal : 0

  const instanceSummary = (() => {
    const d = details
    if (!d) return '选择一个实例后从这里进入'
    const loader = d.IsVanilla ? '原版' : `${d.LoaderName} ${d.LoaderVersion}`
    return `${d.VersionId} · Minecraft ${d.BaseGameVersion} · ${loader} · ${d.ContentDirectory}`
  })()

  const targetInfo = (() => {
    const d = details
    if (!d) return 'Minecraft 版本与加载器取自当前实例。'
    return `打包要求：Minecraft ${d.BaseGameVersion}${d.IsVanilla ? '（原版）' : ` + ${d.LoaderName} ${d.LoaderVersion}`}。来源实例：${d.VersionId}。`
  })()

  const formatHint = format === 0
    ? 'Modrinth 标准格式（.mrpack）：mod 优先写入 index 声明直链由导入方下载，其余文件进 overrides。'
    : 'MultiMC / PrismLauncher 格式（.zip）：全部文件打进 overrides，离线可导入。'

  const contentSummary = (() => {
    const total = allContent.length
    if (total === 0) return '打包内容'
    const selected = allContent.filter((row) => row.included)
    const size = selected.reduce((sum, row) => sum + (row.item.SizeBytes || 0), 0)
    return `打包内容 · 已选 ${selected.length} / ${total} 项（${formatSize(size)}）`
  })()

  function groupDescription(category: string): string {
    return CATEGORY_DESCRIPTIONS[category] ?? ''
  }

  // ------------------------------------------------------------------
  // 内容收集与分组
  // ------------------------------------------------------------------

  function excludedSet(): Set<string> {
    const excluded = (profile.current.excludedPaths as string[] | undefined) ?? []
    return new Set(excluded.map((p) => (p || '').toLowerCase()))
  }

  /**
   * 按搜索框重建分组视图：每个分类一个折叠组，只保留命中条目。
   * resetExpanded=true 时按「首次构建」规则：只展开有勾选内容的分类；搜索时全部展开。
   */
  const applyFilter = useCallback((rows: ContentRow[], query: string, resetExpanded = false) => {
    const q = query.trim().toLowerCase()
    const searching = q.length > 0
    const result: GroupView[] = []
    const byCategory = new Map<string, ContentRow[]>()
    for (const row of rows) {
      const category = row.item.CategoryDisplay ?? row.item.Category ?? '其他'
      if (!byCategory.has(category)) byCategory.set(category, [])
      byCategory.get(category)!.push(row)
    }
    for (const category of [...byCategory.keys()].sort()) {
      let visible = byCategory.get(category)!
      if (searching) {
        visible = visible.filter((row) =>
          (row.item.Name || '').toLowerCase().includes(q) ||
          (row.item.RelativePath || '').toLowerCase().includes(q))
      }
      if (visible.length === 0) continue

      let isExpanded: boolean
      if (searching) {
        isExpanded = true
      } else if (resetExpanded) {
        isExpanded = visible.some((row) => row.included)
      } else {
        isExpanded = expandedCategories.current.has(category)
      }

      const selected = visible.filter((row) => row.included).length
      const size = visible.reduce((sum, row) => sum + (row.item.SizeBytes || 0), 0)
      result.push({
        category,
        items: visible,
        isExpanded,
        masterChecked: selected === 0 ? false : selected === visible.length ? true : null,
        countDisplay: `已选 ${selected} / ${visible.length}`,
        sizeDisplay: formatSize(size),
      })
    }
    setGroups(result)
    expandedCategories.current = new Set(result.filter((g) => g.isExpanded).map((g) => g.category))
  }, [])

  const reloadContent = useCallback(async () => {
    const d = details
    if (!d || !d.ContentDirectory) return
    setStatusText('正在读取实例内容…')
    try {
      const items = ((await CollectExportContent(d.ContentDirectory)) ?? []) as ContentItem[]
      const excluded = excludedSet()
      const rows: ContentRow[] = items.map((item) => ({
        item,
        included: excluded.size === 0 || !excluded.has((item.RelativePath || '').toLowerCase()),
      }))
      setAllContent(rows)
      applyFilter(rows, filterQuery, true)
      const totalSize = items.reduce((sum, item) => sum + (item.SizeBytes || 0), 0)
      setStatusText(`已读取 ${items.length} 个内容条目（${formatSize(totalSize)}）。在左侧勾选要打包的内容。`)
    } catch (ex) {
      setStatusText(`读取实例内容失败：${(ex as Error)?.message ?? ex}`)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [details, applyFilter])

  function onFilterInput(text: string) {
    setFilterQuery(text)
    applyFilter(allContent, text)
  }

  /** 条目勾选变化后重算分组视图（等价 Vue 版 syncGroup + 整组视图刷新）。 */
  function updateRows(mutate: (rows: ContentRow[]) => void) {
    const next = allContent.map((row) => ({ ...row }))
    mutate(next)
    setAllContent(next)
    applyFilter(next, filterQuery)
  }

  function toggleExpand(group: GroupView) {
    const expanded = !group.isExpanded
    if (expanded) expandedCategories.current.add(group.category)
    else expandedCategories.current.delete(group.category)
    setGroups((prev) => prev.map((g) => (g.category === group.category ? { ...g, isExpanded: expanded } : g)))
  }

  function setVisible(included: boolean) {
    // 只作用于当前分组视图里可见的条目，配合搜索可按需批量勾选
    const visibleKeys = new Set(groups.flatMap((g) => g.items.map((row) => row.item.RelativePath)))
    updateRows((rows) => {
      for (const row of rows) {
        if (visibleKeys.has(row.item.RelativePath)) row.included = included
      }
    })
  }

  function toggleGroup(group: GroupView) {
    // 三态主勾选框：点击时在「全选 / 全不选」间切换（半选状态视为未全选）
    setGroupChecked(group, group.masterChecked !== true)
  }

  function setGroupChecked(group: GroupView, included: boolean) {
    const keys = new Set(group.items.map((row) => row.item.RelativePath))
    updateRows((rows) => {
      for (const row of rows) {
        if (keys.has(row.item.RelativePath)) row.included = included
      }
    })
  }

  function restoreDefaults() {
    const excluded = excludedSet()
    const next = allContent.map((row) => ({
      ...row,
      included: excluded.size === 0 || !excluded.has((row.item.RelativePath || '').toLowerCase()),
    }))
    setAllContent(next)
    applyFilter(next, filterQuery)
    setStatusText('已恢复为上次打包时的勾选列表。')
  }

  // ------------------------------------------------------------------
  // 档案与打包
  // ------------------------------------------------------------------

  function applyProfile(p: Record<string, unknown> | null, d: VersionDetails) {
    profile.current = p ?? {}
    setPackName((profile.current.packName as string) || d.VersionId)
    setPackVersion((profile.current.packVersion as string) || '1.0.0')
    setAuthor((profile.current.author as string) || '')
    setUpdateLink((profile.current.updateLink as string) || '')
    setDescription((profile.current.description as string) || '')
    setResolveLinks((profile.current.resolveModrinthLinks as boolean) ?? true)
    setFormat((profile.current.format as number) ?? 0)
  }

  async function startPack() {
    if (packing) return
    const d = details
    if (!d) {
      alert('请先在版本管理页选择一个实例。', { severity: 'warning' })
      return
    }
    if (allContent.every((row) => !row.included)) {
      alert('请至少勾选一项要打包的内容。', { severity: 'warning' })
      return
    }
    const name = packName.trim()
    if (!name) {
      alert('整合包名称不能为空。', { severity: 'warning' })
      return
    }
    const version = packVersion.trim() || '1.0.0'
    const isMrpack = format === 0
    const suggested = `${sanitizeFileName(name)}.${isMrpack ? 'mrpack' : 'zip'}`

    // 打包前经原生 SaveFile 对话框确认保存位置
    let outputPath = ''
    try {
      outputPath = await SaveFile(
        '选择整合包保存位置',
        suggested,
        isMrpack ? 'Modrinth 整合包' : 'MultiMC 整合包',
        isMrpack ? '*.mrpack' : '*.zip',
      )
    } catch { /* 用户取消 */ }
    if (!outputPath) return

    const options = {
      Format: format,
      PackName: name,
      PackVersion: version,
      Author: author.trim(),
      UpdateLink: updateLink.trim(),
      Description: description.trim(),
      IconPngPath: iconPngPath,
      MinecraftVersion: d.BaseGameVersion,
      LoaderName: d.IsVanilla ? '' : d.LoaderName,
      LoaderVersion: d.IsVanilla ? '' : d.LoaderVersion,
      IncludedPaths: allContent.filter((row) => row.included).map((row) => row.item.RelativePath),
      ResolveModrinthLinks: resolveLinks,
    }
    // 档案使用打包那一刻的排除快照：导出期间用户可能已改动勾选
    const packedExclusions = allContent
      .filter((row) => !row.included)
      .map((row) => row.item.RelativePath)

    setPacking(true)
    setProgressTotal(0)
    setProgressCurrent(0)
    setPackStatus('准备打包…')
    try {
      const result = await ExportModpack(options, d.ContentDirectory, outputPath)
      setPackStatus(`已保存：${result.OutputPath}（声明直链 ${result.DeclaredFiles} 个、overrides ${result.OverrideFiles} 个）`)
      setStatusText(result.Warnings?.length > 0
        ? `打包完成，${result.Warnings.length} 条提示：${result.Warnings.slice(0, 3).join('；')}`
        : `打包完成：${result.OutputPath}`)
      alert(`整合包已导出：${result.OutputPath.split(/[\\/]/).pop()}`, { severity: 'success' })

      // 把勾选列表与元数据写回实例目录，供下次导出沿用
      SaveExportProfile(d.VersionDirectory, {
        packName: options.PackName,
        packVersion: options.PackVersion,
        author: options.Author,
        description: options.Description,
        updateLink: options.UpdateLink,
        format: options.Format,
        resolveModrinthLinks: options.ResolveModrinthLinks,
        excludedPaths: packedExclusions,
      })
    } catch (ex) {
      setPackStatus('')
      setStatusText(`打包失败：${(ex as Error)?.message ?? ex}`)
      alert(`整合包导出失败：${(ex as Error)?.message ?? ex}`, { severity: 'error' })
    } finally {
      setPacking(false)
    }
  }

  function cancelPack() {
    // 绑定层未暴露取消导出的方法：导出为一次性调用
    setStatusText('当前版本导出暂不支持中途取消。')
  }

  async function pickIcon() {
    try {
      const path = await SelectFile('选择打包图标（png）', '图片文件', '*.png')
      if (path) setIconPngPath(path)
    } catch { /* 用户取消 */ }
  }

  function onExportProgress(value: { Total?: number; Current?: number; Phase?: string } | null) {
    setProgressTotal(value?.Total ?? 0)
    setProgressCurrent(value?.Current ?? 0)
    setStatusText((value?.Total ?? 0) > 0
      ? `${value?.Phase}（${value?.Current} / ${value?.Total}）…`
      : `${value?.Phase ?? ''}…`)
  }

  useEffect(() => {
    EventsOn('modpack:exportProgress', onExportProgress)
    return () => EventsOff('modpack:exportProgress')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    ;(async () => {
      try {
        const snapshot = await GetCurrentInstanceSnapshot()
        const versionId = snapshot?.SelectedVersionId
        if (versionId) {
          const d = (await GetVersionDetails(versionId)) as unknown as VersionDetails
          setDetails(d)
          applyProfile((await LoadExportProfile(d.VersionDirectory)) as Record<string, unknown>, d)
        }
        await reloadContent()
      } catch (ex) {
        setStatusText(`初始化失败：${(ex as Error)?.message ?? ex}`)
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadContent])

  const hasContent = useMemo(() => allContent.length > 0, [allContent])

  return (
    /* 整合包制作页（ModpackCreatorPage.axaml：页头 / 左内容+右设置 / 底部状态） */
    <section className="grid h-full grid-rows-[auto_1fr_auto] px-[26px] pt-[22px] pb-3.5">

      {/* 页头 */}
      <header className="mb-[18px] flex items-center justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-[3px]">
          <h1 className="m-0 text-[23px] font-semibold text-foreground">整合包制作</h1>
          <span className="overflow-hidden text-[11px] text-subtext-text text-ellipsis whitespace-nowrap">{instanceSummary}</span>
        </div>
        <Button variant="secondary" onClick={reloadContent}>重新读取内容</Button>
      </header>

      <div className="grid min-h-0 grid-cols-[1fr_430px] gap-4">

        {/* 左：打包内容 */}
        <Card className="grid min-h-0 grid-rows-[auto_auto_1fr] gap-0 rounded-xl border-border bg-secondary/50 p-3.5">
          <div className="flex items-center gap-1.5 px-0.5 pb-2">
            <span className="min-w-0 flex-1 text-[13px] font-semibold text-secondary-text">{contentSummary}</span>
            <Button variant="ghost" size="sm" className="h-auto px-2 py-[3px] text-[10px]" onClick={() => setVisible(true)}>全选</Button>
            <Button variant="ghost" size="sm" className="h-auto px-2 py-[3px] text-[10px]" onClick={() => setVisible(false)}>全不选</Button>
            <Button variant="ghost" size="sm" className="h-auto px-2 py-[3px] text-[10px]" title="恢复为上次打包时记住的勾选列表" onClick={restoreDefaults}>恢复默认</Button>
          </div>

          <Input
            value={filterQuery}
            placeholder="搜索名称或路径…"
            className="mx-0.5 mb-2 h-auto rounded-lg bg-muted py-[7px]"
            onChange={(e) => onFilterInput(e.target.value)} />

          <div className="min-h-0 overflow-y-auto">
            {groups.map((group) => (
              <div key={group.category} className="mb-1">
                <div className="flex items-center gap-2 py-1">
                  {/* 三态勾选框（NyaCheckbox：全选/全不选/半选） */}
                  <NyaCheckbox
                    checked={group.masterChecked === null ? 'indeterminate' : group.masterChecked}
                    title="点击全选 / 全不选该分类"
                    onChange={() => toggleGroup(group)} />
                  <div className="flex min-w-0 flex-1 cursor-pointer flex-col gap-px" onClick={() => toggleExpand(group)}>
                    <div className="flex items-center gap-1.5">
                      <span className="text-[13px] font-semibold text-body-text">{group.category}</span>
                      <Badge className="rounded-md px-1.5 py-px text-[9px] font-normal text-subtext-text">{group.countDisplay}</Badge>
                    </div>
                    <span className="text-[9px] break-words text-hint-text">{groupDescription(group.category)}</span>
                  </div>
                  <span className="mr-2 text-[10px] text-hint-text">{group.sizeDisplay}</span>
                  <Button variant="ghost" size="sm" className="h-auto px-2 py-[3px] text-[10px]" onClick={() => setGroupChecked(group, true)}>全选</Button>
                  <Button variant="ghost" size="sm" className="h-auto px-2 py-[3px] text-[10px]" onClick={() => setGroupChecked(group, false)}>全不选</Button>
                </div>

                {group.isExpanded ? (
                  <div className="pb-1 pl-7">
                    {group.items.map((row) => (
                      <div key={row.item.RelativePath} className="my-[3px] flex items-center gap-2" title={row.item.RelativePath}>
                        <NyaCheckbox
                          checked={row.included}
                          onChange={() => updateRows((rows) => {
                            for (const r of rows) {
                              if (r.item.RelativePath === row.item.RelativePath) r.included = !r.included
                            }
                          })} />
                        <div className="flex min-w-0 flex-1 flex-col gap-px">
                          <span className="overflow-hidden text-xs text-body-text text-ellipsis whitespace-nowrap">{row.item.Name}</span>
                          <span className="overflow-hidden text-[9px] text-hint-text text-ellipsis whitespace-nowrap">{row.item.RelativePath}</span>
                        </div>
                        <span className="min-w-[52px] flex-none text-right text-[9px] text-hint-text">{formatSize(row.item.SizeBytes)}</span>
                      </div>
                    ))}
                  </div>
                ) : null}
              </div>
            ))}
          </div>

          {!hasContent ? <div className="text-[13px] text-hint-text">没有可打包的内容</div> : null}
        </Card>

        {/* 右：打包设置 */}
        <Card className="min-h-0 overflow-y-auto rounded-xl border-border bg-card p-[18px]">
          <div className="flex flex-col gap-3">

            <div className="flex flex-col gap-1">
              <label className="text-[13px] text-muted-foreground">打包格式</label>
              {/* RadioGroup 风格（accent-primary 单选） */}
              <div className="flex gap-3.5">
                <label className="inline-flex cursor-pointer items-center gap-1.5 text-foreground">
                  <input type="radio" checked={format === 0} onChange={() => setFormat(0)} className="size-3.5 accent-primary" /> Modrinth（.mrpack）
                </label>
                <label className="inline-flex cursor-pointer items-center gap-1.5 text-foreground">
                  <input type="radio" checked={format === 1} onChange={() => setFormat(1)} className="size-3.5 accent-primary" /> MultiMC（.zip）
                </label>
              </div>
              <span className="text-[11px] break-words text-hint-text">{formatHint}</span>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-[13px] text-muted-foreground">整合包名称</label>
              <Input value={packName} placeholder="例如：我的究极生存包" onChange={(e) => setPackName(e.target.value)} />
            </div>

            <div className="grid grid-cols-[1fr_10px_1fr]">
              <div className="flex flex-col gap-1">
                <label className="text-[13px] text-muted-foreground">版本号</label>
                <Input value={packVersion} onChange={(e) => setPackVersion(e.target.value)} />
              </div>
              <div className="col-start-3 flex flex-col gap-1">
                <label className="text-[13px] text-muted-foreground">作者</label>
                <Input value={author} placeholder="你的名字" onChange={(e) => setAuthor(e.target.value)} />
              </div>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-[13px] text-muted-foreground">更新链接（主页 / 发布页）</label>
              <Input value={updateLink} placeholder="https://…" onChange={(e) => setUpdateLink(e.target.value)} />
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-[13px] text-muted-foreground">整合包描述</label>
              <textarea
                value={description}
                rows={3}
                placeholder="简介会写入 mrpack 的 summary 字段"
                onChange={(e) => setDescription(e.target.value)}
                className="w-full select-text resize-y rounded-md border border-input bg-background px-3 py-1.5 text-[13px] text-foreground outline-none transition-[border-color,box-shadow] duration-150 placeholder:text-placeholder-text min-h-[58px] max-h-[120px] focus-visible:border-ring focus-visible:ring-[2px] focus-visible:ring-ring/40"
              />
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-[13px] text-muted-foreground">图标（png）</label>
              <div className="flex items-center gap-2">
                <Button variant="secondary" size="sm" className="h-auto rounded-lg px-2.5 py-1.5 text-[11px]" onClick={pickIcon}>选择图标…</Button>
                {iconPngPath ? (
                  <span className="min-w-0 flex-1 truncate text-[11px] text-body-text" title={iconPngPath}>{iconPngPath}</span>
                ) : null}
                {iconPngPath ? (
                  <Button variant="ghost" size="sm" className="h-auto px-2 py-[3px] text-[10px]" onClick={() => setIconPngPath('')}>清除</Button>
                ) : null}
              </div>
              <div className="text-[11px] break-words text-hint-text">Modrinth 格式写入 overrides/icon.png；MultiMC 格式写入包根 icon.png。</div>
            </div>

            {format === 0 ? (
              <label className="flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                <NyaCheckbox checked={resolveLinks} onChange={() => setResolveLinks((v) => !v)} />
                优先通过 Modrinth 直链收录模组（未匹配的进 overrides）
              </label>
            ) : null}

            <div className="rounded-lg border border-emphasized-border bg-badge p-2 px-2.5 text-[11px] break-words text-subtext-text">{targetInfo}</div>

            {/* 打包进度：确定态用 Progress，不确定态保留原动画 */}
            {packing && progressTotal > 0 ? (
              <Progress value={progressPercent} className="h-1" />
            ) : packing ? (
              <div className="nya-progress-track">
                <div className="nya-progress-indeterminate" />
              </div>
            ) : null}

            {packStatus ? <div className="text-[11px] break-words text-subtext-text">{packStatus}</div> : null}

            <div className="flex gap-2.5">
              <Button className="h-auto bg-accent-dark px-[18px] py-[9px] font-semibold" disabled={packing} onClick={startPack}>开始打包并保存…</Button>
              {packing ? (
                <Button variant="secondary" className="h-auto px-3.5 py-[9px]" onClick={cancelPack}>取消</Button>
              ) : null}
            </div>

          </div>
        </Card>
      </div>

      <div className="mt-[13px] min-h-[1em] text-[11px] break-words text-subtext-text">{statusText}</div>
    </section>
  )
}
