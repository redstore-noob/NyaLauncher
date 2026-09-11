<template>
  <!-- 整合包制作页（ModpackCreatorPage.axaml：页头 / 左内容+右设置 / 底部状态） -->
  <section class="grid h-full grid-rows-[auto_1fr_auto] px-[26px] pt-[22px] pb-3.5">

    <!-- 页头 -->
    <header class="mb-[18px] flex items-center justify-between gap-2">
      <div class="flex min-w-0 flex-col gap-[3px]">
        <h1 class="m-0 text-[23px] font-semibold text-foreground">整合包制作</h1>
        <span class="overflow-hidden text-[11px] text-subtext-text text-ellipsis whitespace-nowrap">{{ instanceSummary }}</span>
      </div>
      <Button variant="secondary" @click="reloadContent">重新读取内容</Button>
    </header>

    <div class="grid min-h-0 grid-cols-[1fr_430px] gap-4">

      <!-- 左：打包内容 -->
      <Card class="grid min-h-0 grid-rows-[auto_auto_1fr] gap-0 rounded-xl border-border bg-secondary/50 p-3.5">
        <div class="flex items-center gap-1.5 px-0.5 pb-2">
          <span class="min-w-0 flex-1 text-[13px] font-semibold text-secondary-text">{{ contentSummary }}</span>
          <Button variant="ghost" size="sm" class="h-auto px-2 py-[3px] text-[10px]" @click="setVisible(true)">全选</Button>
          <Button variant="ghost" size="sm" class="h-auto px-2 py-[3px] text-[10px]" @click="setVisible(false)">全不选</Button>
          <Button variant="ghost" size="sm" class="h-auto px-2 py-[3px] text-[10px]" title="恢复为上次打包时记住的勾选列表" @click="restoreDefaults">恢复默认</Button>
        </div>

        <Input v-model="filterQuery" placeholder="搜索名称或路径…" class="mx-0.5 mb-2 h-auto rounded-lg bg-muted py-[7px]" @input="applyFilter()" />

        <div class="min-h-0 overflow-y-auto">
          <div v-for="group in groups" :key="group.category" class="mb-1">
            <div class="flex items-center gap-2 py-1">
              <!-- 三态勾选框（自绘 Checkbox：全选/全不选/半选） -->
              <button
                type="button"
                class="nya-checkbox"
                :class="{ 'is-checked': group.masterChecked === true, 'is-indeterminate': group.masterChecked === null }"
                title="点击全选 / 全不选该分类"
                @click.prevent="toggleGroup(group)"
              >
                <Icon v-if="group.masterChecked === true" name="success" :size="12" />
                <span v-else-if="group.masterChecked === null" class="nya-checkbox-dash" />
              </button>
              <div class="flex min-w-0 flex-1 cursor-pointer flex-col gap-px" @click="toggleExpand(group)">
                <div class="flex items-center gap-1.5">
                  <span class="text-[13px] font-semibold text-body-text">{{ group.category }}</span>
                  <Badge class="rounded-md px-1.5 py-px text-[9px] font-normal text-subtext-text">{{ group.countDisplay }}</Badge>
                </div>
                <span class="text-[9px] text-hint-text break-words">{{ groupDescription(group.category) }}</span>
              </div>
              <span class="mr-2 text-[10px] text-hint-text">{{ group.sizeDisplay }}</span>
              <Button variant="ghost" size="sm" class="h-auto px-2 py-[3px] text-[10px]" @click="setGroup(group, true)">全选</Button>
              <Button variant="ghost" size="sm" class="h-auto px-2 py-[3px] text-[10px]" @click="setGroup(group, false)">全不选</Button>
            </div>

            <div v-if="group.isExpanded" class="pb-1 pl-7">
              <div v-for="row in group.items" :key="row.item.RelativePath" class="my-[3px] flex items-center gap-2" :title="row.item.RelativePath">
                <button
                  type="button"
                  class="nya-checkbox"
                  :class="{ 'is-checked': row.included }"
                  @click="row.included = !row.included; syncGroup(group)"
                >
                  <Icon v-if="row.included" name="success" :size="12" />
                </button>
                <div class="flex min-w-0 flex-1 flex-col gap-px">
                  <span class="overflow-hidden text-xs text-body-text text-ellipsis whitespace-nowrap">{{ row.item.Name }}</span>
                  <span class="overflow-hidden text-[9px] text-hint-text text-ellipsis whitespace-nowrap">{{ row.item.RelativePath }}</span>
                </div>
                <span class="min-w-[52px] flex-none text-right text-[9px] text-hint-text">{{ formatSize(row.item.SizeBytes) }}</span>
              </div>
            </div>
          </div>
        </div>

        <div v-if="allContent.length === 0" class="text-[13px] text-hint-text">没有可打包的内容</div>
      </Card>

      <!-- 右：打包设置 -->
      <Card class="min-h-0 overflow-y-auto rounded-xl border-border bg-card p-[18px]">
        <div class="flex flex-col gap-3">

          <div class="flex flex-col gap-1">
            <label class="text-[13px] text-muted-foreground">打包格式</label>
            <!-- RadioGroup 风格（accent-primary 单选） -->
            <div class="flex gap-3.5">
              <label class="inline-flex cursor-pointer items-center gap-1.5 text-foreground">
                <input v-model="format" type="radio" :value="0" class="size-3.5 accent-primary" /> Modrinth（.mrpack）
              </label>
              <label class="inline-flex cursor-pointer items-center gap-1.5 text-foreground">
                <input v-model="format" type="radio" :value="1" class="size-3.5 accent-primary" /> MultiMC（.zip）
              </label>
            </div>
            <span class="text-[11px] text-hint-text break-words">{{ formatHint }}</span>
          </div>

          <div class="flex flex-col gap-1">
            <label class="text-[13px] text-muted-foreground">整合包名称</label>
            <Input v-model="packName" placeholder="例如：我的究极生存包" />
          </div>

          <div class="grid grid-cols-[1fr_10px_1fr]">
            <div class="flex flex-col gap-1">
              <label class="text-[13px] text-muted-foreground">版本号</label>
              <Input v-model="packVersion" />
            </div>
            <div class="col-start-3 flex flex-col gap-1">
              <label class="text-[13px] text-muted-foreground">作者</label>
              <Input v-model="author" placeholder="你的名字" />
            </div>
          </div>

          <div class="flex flex-col gap-1">
            <label class="text-[13px] text-muted-foreground">更新链接（主页 / 发布页）</label>
            <Input v-model="updateLink" placeholder="https://…" />
          </div>

          <div class="flex flex-col gap-1">
            <label class="text-[13px] text-muted-foreground">整合包描述</label>
            <textarea
              v-model="description"
              rows="3"
              placeholder="简介会写入 mrpack 的 summary 字段"
              class="w-full rounded-md border border-input bg-background px-3 py-1.5 text-[13px] text-foreground resize-y
                     placeholder:text-placeholder-text min-h-[58px] max-h-[120px] outline-none transition-[border-color,box-shadow] duration-150
                     focus-visible:border-ring focus-visible:ring-[2px] focus-visible:ring-ring/40 select-text"
            />
          </div>

          <div class="flex flex-col gap-1">
            <label class="text-[13px] text-muted-foreground">图标（png）</label>
            <div class="flex items-center gap-2">
              <Button variant="secondary" size="sm" class="h-auto rounded-lg px-2.5 py-1.5 text-[11px]" @click="pickIcon">选择图标…</Button>
              <span v-if="iconPngPath" class="min-w-0 flex-1 truncate text-[11px] text-body-text" :title="iconPngPath">{{ iconPngPath }}</span>
              <Button v-if="iconPngPath" variant="ghost" size="sm" class="h-auto px-2 py-[3px] text-[10px]" @click="iconPngPath = ''">清除</Button>
            </div>
            <div class="text-[11px] text-hint-text break-words">Modrinth 格式写入 overrides/icon.png；MultiMC 格式写入包根 icon.png。</div>
          </div>

          <label v-if="format === 0" class="flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
            <button
              type="button"
              class="nya-checkbox"
              :class="{ 'is-checked': resolveLinks }"
              @click="resolveLinks = !resolveLinks"
            >
              <Icon v-if="resolveLinks" name="success" :size="12" />
            </button>
            优先通过 Modrinth 直链收录模组（未匹配的进 overrides）
          </label>

          <div class="rounded-lg border border-emphasized-border bg-badge p-2 px-2.5 text-[11px] text-subtext-text break-words">{{ targetInfo }}</div>

          <!-- 打包进度：确定态用 Progress，不确定态保留原动画 -->
          <Progress v-if="packing && progressTotal > 0" :model-value="progressPercent" class="h-1" />
          <div v-else-if="packing" class="nya-progress-track">
            <div class="nya-progress-indeterminate" />
          </div>

          <div v-if="packStatus" class="text-[11px] text-subtext-text break-words">{{ packStatus }}</div>

          <div class="flex gap-2.5">
            <Button class="h-auto bg-accent-dark px-[18px] py-[9px] font-semibold" :disabled="packing" @click="startPack">开始打包并保存…</Button>
            <Button v-if="packing" variant="secondary" class="h-auto px-3.5 py-[9px]" @click="cancelPack">取消</Button>
          </div>

        </div>
      </Card>
    </div>

    <div class="mt-[13px] min-h-[1em] text-[11px] text-subtext-text break-words">{{ statusText }}</div>
  </section>
</template>

<script setup>
/*
 * 整合包制作页（ModpackCreatorPage.axaml + .cs 移植）。
 * 内容勾选走 ModpackAPI.CollectExportContent；导出走 ExportModpack，
 * 进度经 modpack:exportProgress 事件展示；完成后把勾选列表与元数据写回实例档案。
 * 保存路径走 SystemAPI.SaveFile 原生对话框；打包图标经 SystemAPI.SelectFile 选择 png。
 * 导出为一次性调用（绑定层未暴露取消导出的方法）。
 */
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { alert } from '../composables/dialog.js'
import { SaveFile, SelectFile } from '../../wailsjs/go/bindings/SystemAPI.js'
import Icon from '../components/overlay/Icon.vue'
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

const CATEGORY_DESCRIPTIONS = {
  模组: 'mods 目录下的模组文件，整合包的核心内容；Modrinth 格式会尽量匹配直链，匹配不到的进 overrides。',
  配置: 'config 等配置目录，模组与游戏的规则设定都在这里，决定了整合包的实际玩法。',
  根文件: 'options.txt、servers.dat 等实例根目录文件：游戏设置、按键映射与服务器列表。',
  资源包: 'resourcepacks 目录下的材质 / 资源包，改变游戏画面与音效。',
  光影包: 'shaderpacks 目录下的光影包，需安装 Iris / OptiFine 等才能生效。',
  存档: 'saves 目录下的世界存档，体积可能很大，注意整合包大小。',
}

const details = ref(null)

const allContent = ref([])   // [{ item, included }]
const groups = ref([])       // 分组视图（跟随搜索过滤）
const filterQuery = ref('')
let expandedCategories = new Set()

// 表单
const format = ref(0)
const packName = ref('')
const packVersion = ref('1.0.0')
const author = ref('')
const updateLink = ref('')
const description = ref('')
const resolveLinks = ref(true)
const iconPngPath = ref('')

// 状态
const packing = ref(false)
const packStatus = ref('')
const statusText = ref('就绪')
const progressCurrent = ref(0)
const progressTotal = ref(0)

const progressPercent = computed(() =>
  progressTotal.value > 0 ? (100 * progressCurrent.value) / progressTotal.value : 0)

const instanceSummary = computed(() => {
  const d = details.value
  if (!d) return '选择一个实例后从这里进入'
  const loader = d.IsVanilla ? '原版' : `${d.LoaderName} ${d.LoaderVersion}`
  return `${d.VersionId} · Minecraft ${d.BaseGameVersion} · ${loader} · ${d.ContentDirectory}`
})

const targetInfo = computed(() => {
  const d = details.value
  if (!d) return 'Minecraft 版本与加载器取自当前实例。'
  return `打包要求：Minecraft ${d.BaseGameVersion}${d.IsVanilla ? '（原版）' : ` + ${d.LoaderName} ${d.LoaderVersion}`}。来源实例：${d.VersionId}。`
})

const formatHint = computed(() =>
  format.value === 0
    ? 'Modrinth 标准格式（.mrpack）：mod 优先写入 index 声明直链由导入方下载，其余文件进 overrides。'
    : 'MultiMC / PrismLauncher 格式（.zip）：全部文件打进 overrides，离线可导入。')

const contentSummary = computed(() => {
  const total = allContent.value.length
  if (total === 0) return '打包内容'
  const selected = allContent.value.filter((row) => row.included)
  const size = selected.reduce((sum, row) => sum + (row.item.SizeBytes || 0), 0)
  return `打包内容 · 已选 ${selected.length} / ${total} 项（${formatSize(size)}）`
})

function formatSize(bytes) {
  if (bytes >= 1073741824) return `${trimZeros((bytes / 1073741824).toFixed(2))} GB`
  if (bytes >= 1048576) return `${trimZeros((bytes / 1048576).toFixed(2))} MB`
  if (bytes >= 1024) return `${trimZeros((bytes / 1024).toFixed(1))} KB`
  return `${bytes} B`
}

function trimZeros(text) {
  return text.replace(/\.?0+$/, '')
}

function groupDescription(category) {
  return CATEGORY_DESCRIPTIONS[category] ?? ''
}

// ------------------------------------------------------------------
// 内容收集与分组
// ------------------------------------------------------------------

let profile = {}

function excludedSet() {
  return new Set((profile.excludedPaths ?? []).map((p) => (p || '').toLowerCase()))
}

async function reloadContent() {
  const d = details.value
  if (!d || !d.ContentDirectory) return
  statusText.value = '正在读取实例内容…'
  try {
    const items = (await CollectExportContent(d.ContentDirectory)) ?? []
    const excluded = excludedSet()
    allContent.value = items.map((item) => ({
      item,
      included: excluded.size === 0 || !excluded.has((item.RelativePath || '').toLowerCase()),
    }))
    applyFilter(true)
    const totalSize = items.reduce((sum, item) => sum + (item.SizeBytes || 0), 0)
    statusText.value = `已读取 ${items.length} 个内容条目（${formatSize(totalSize)}）。在左侧勾选要打包的内容。`
  } catch (ex) {
    statusText.value = `读取实例内容失败：${ex?.message ?? ex}`
  }
}

/**
 * 按搜索框重建分组视图：每个分类一个折叠组，只保留命中条目。
 * resetExpanded=true 时按"首次构建"规则：只展开有勾选内容的分类；搜索时全部展开。
 */
function applyFilter(resetExpanded = false) {
  const query = filterQuery.value.trim().toLowerCase()
  const searching = query.length > 0
  const result = []
  const byCategory = new Map()
  for (const row of allContent.value) {
    const category = row.item.CategoryDisplay ?? row.item.Category ?? '其他'
    if (!byCategory.has(category)) byCategory.set(category, [])
    byCategory.get(category).push(row)
  }
  for (const category of [...byCategory.keys()].sort()) {
    let visible = byCategory.get(category)
    if (searching) {
      visible = visible.filter((row) =>
        (row.item.Name || '').toLowerCase().includes(query) ||
        (row.item.RelativePath || '').toLowerCase().includes(query))
    }
    if (visible.length === 0) continue

    let isExpanded
    if (searching) {
      isExpanded = true
    } else if (resetExpanded) {
      isExpanded = visible.some((row) => row.included)
    } else {
      isExpanded = expandedCategories.has(category)
    }

    const group = reactive({
      category,
      items: visible,
      isExpanded,
      masterChecked: false,
      countDisplay: '',
      sizeDisplay: '',
    })
    syncGroup(group)
    result.push(group)
  }
  groups.value = result
  expandedCategories = new Set(result.filter((g) => g.isExpanded).map((g) => g.category))
}

/** 条目勾选变化后重算主勾选框与"已选 n / m"徽标。 */
function syncGroup(group) {
  const selected = group.items.filter((row) => row.included).length
  group.masterChecked = selected === 0 ? false : selected === group.items.length ? true : null
  group.countDisplay = `已选 ${selected} / ${group.items.length}`
  const size = group.items.reduce((sum, row) => sum + (row.item.SizeBytes || 0), 0)
  group.sizeDisplay = formatSize(size)
}

function toggleExpand(group) {
  group.isExpanded = !group.isExpanded
  if (group.isExpanded) expandedCategories.add(group.category)
  else expandedCategories.delete(group.category)
}

function setVisible(included) {
  // 只作用于当前分组视图里可见的条目，配合搜索可按需批量勾选
  for (const group of groups.value) {
    for (const row of group.items) row.included = included
    syncGroup(group)
  }
}

function toggleGroup(group) {
  // 三态主勾选框：点击时在"全选 / 全不选"间切换（半选状态视为未全选）
  const included = group.masterChecked !== true
  setGroup(group, included)
}

function setGroup(group, included) {
  for (const row of group.items) row.included = included
  syncGroup(group)
}

function restoreDefaults() {
  const excluded = excludedSet()
  for (const row of allContent.value) {
    row.included = excluded.size === 0 || !excluded.has((row.item.RelativePath || '').toLowerCase())
  }
  applyFilter()
  statusText.value = '已恢复为上次打包时的勾选列表。'
}

// ------------------------------------------------------------------
// 档案与打包
// ------------------------------------------------------------------

function applyProfile(p, d) {
  profile = p ?? {}
  packName.value = profile.packName || d.VersionId
  packVersion.value = profile.packVersion || '1.0.0'
  author.value = profile.author || ''
  updateLink.value = profile.updateLink || ''
  description.value = profile.description || ''
  resolveLinks.value = profile.resolveModrinthLinks ?? true
  format.value = profile.format ?? 0
}

async function startPack() {
  if (packing.value) return
  const d = details.value
  if (!d) {
    alert('请先在版本管理页选择一个实例。', { severity: 'warning' })
    return
  }
  if (allContent.value.every((row) => !row.included)) {
    alert('请至少勾选一项要打包的内容。', { severity: 'warning' })
    return
  }
  const name = packName.value.trim()
  if (!name) {
    alert('整合包名称不能为空。', { severity: 'warning' })
    return
  }
  const version = packVersion.value.trim() || '1.0.0'
  const isMrpack = format.value === 0
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
    Format: format.value,
    PackName: name,
    PackVersion: version,
    Author: author.value.trim(),
    UpdateLink: updateLink.value.trim(),
    Description: description.value.trim(),
    IconPngPath: iconPngPath.value,
    MinecraftVersion: d.BaseGameVersion,
    LoaderName: d.IsVanilla ? '' : d.LoaderName,
    LoaderVersion: d.IsVanilla ? '' : d.LoaderVersion,
    IncludedPaths: allContent.value.filter((row) => row.included).map((row) => row.item.RelativePath),
    ResolveModrinthLinks: resolveLinks.value,
  }
  // 档案使用打包那一刻的排除快照：导出期间用户可能已改动勾选
  const packedExclusions = allContent.value
    .filter((row) => !row.included)
    .map((row) => row.item.RelativePath)

  packing.value = true
  progressTotal.value = 0
  progressCurrent.value = 0
  packStatus.value = '准备打包…'
  try {
    const result = await ExportModpack(options, d.ContentDirectory, outputPath)
    packStatus.value = `已保存：${result.OutputPath}（声明直链 ${result.DeclaredFiles} 个、overrides ${result.OverrideFiles} 个）`
    statusText.value = result.Warnings?.length > 0
      ? `打包完成，${result.Warnings.length} 条提示：${result.Warnings.slice(0, 3).join('；')}`
      : `打包完成：${result.OutputPath}`
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
    packStatus.value = ''
    statusText.value = `打包失败：${ex?.message ?? ex}`
    alert(`整合包导出失败：${ex?.message ?? ex}`, { severity: 'error' })
  } finally {
    packing.value = false
  }
}

function cancelPack() {
  // 绑定层未暴露取消导出的方法：导出为一次性调用
  statusText.value = '当前版本导出暂不支持中途取消。'
}

async function pickIcon() {
  try {
    const path = await SelectFile('选择打包图标（png）', '图片文件', '*.png')
    if (path) iconPngPath.value = path
  } catch { /* 用户取消 */ }
}

function sanitizeFileName(name) {
  return name.trim().replace(/[<>:"/\\|?*]/g, '_')
}

function onExportProgress(value) {
  progressTotal.value = value?.Total ?? 0
  progressCurrent.value = value?.Current ?? 0
  statusText.value = (value?.Total ?? 0) > 0
    ? `${value.Phase}（${value.Current} / ${value.Total}）…`
    : `${value?.Phase ?? ''}…`
}

onMounted(async () => {
  EventsOn('modpack:exportProgress', onExportProgress)
  try {
    const snapshot = await GetCurrentInstanceSnapshot()
    const versionId = snapshot?.SelectedVersionId
    if (versionId) {
      details.value = await GetVersionDetails(versionId)
      applyProfile(await LoadExportProfile(details.value.VersionDirectory), details.value)
    }
    await reloadContent()
  } catch (ex) {
    statusText.value = `初始化失败：${ex?.message ?? ex}`
  }
})

onUnmounted(() => {
  EventsOff('modpack:exportProgress')
})
</script>

<style scoped>
/* 自绘三态勾选框（Checkbox 风格：selected 底 + 勾/横线） */
.nya-checkbox {
  width: 16px;
  height: 16px;
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 5px;
  border: 1px solid var(--default-border);
  background: var(--background);
  color: var(--accent-text-color);
  transition: background-color 120ms ease, border-color 120ms ease;
  cursor: pointer;
}
.nya-checkbox:hover { border-color: var(--accent); }
.nya-checkbox.is-checked,
.nya-checkbox.is-indeterminate {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-text-color);
}
.nya-checkbox-dash {
  width: 8px;
  height: 2px;
  border-radius: 1px;
  background: currentColor;
}

/* 不确定态进度条（原版动画参数保留：1.2s ease-in-out 无限循环） */
.nya-progress-track {
  height: 4px;
  border-radius: 2px;
  background: var(--medium-border);
  overflow: hidden;
}
.nya-progress-indeterminate {
  height: 100%;
  width: 40%;
  border-radius: 2px;
  background: var(--accent);
  animation: nya-indeterminate 1.2s ease-in-out infinite;
}
@keyframes nya-indeterminate {
  0% { margin-left: -40%; }
  100% { margin-left: 100%; }
}
</style>
