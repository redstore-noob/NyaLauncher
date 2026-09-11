<template>
  <!--
    ContentDownloadOverlay.vue（移植自 Controls/ContentDownloadOverlay.axaml + DownloadTargetPicker
    + DownloadStatusPanel）：Modrinth 内容（整合包/资源包/光影包）版本选择 + 目标实例选择 + 下载进度。
    整合包只能「新建独立实例」；Mod/资源包/光影可选实例或自定义保存路径。
    本地整合包导入模式（localModpackPath 非空）：跳过在线版本选择，直接安装。
  -->
  <div class="flex max-h-[600px] w-[520px] flex-col gap-[14px] overflow-y-auto rounded-2xl bg-background px-6 pb-[22px] pt-5">
    <!-- 标题栏（OverlayHeader） -->
    <div class="flex items-center gap-3">
      <div class="flex size-11 shrink-0 items-center justify-center overflow-hidden rounded-[10px] bg-badge text-[20px]">
        <img v-if="headerIcon" :src="headerIcon" class="size-full object-cover" alt="" />
        <template v-else>{{ headerGlyph }}</template>
      </div>
      <div class="flex min-w-0 flex-1 flex-col gap-0.5">
        <span class="truncate text-base font-bold text-foreground">{{ headerTitle }}</span>
        <span class="truncate text-[11px] text-hint-text">{{ headerSubtitle }}</span>
      </div>
      <button
        class="flex size-[30px] shrink-0 cursor-pointer items-center justify-center rounded-full bg-secondary text-subtext-text transition-opacity hover:opacity-[var(--hover-opacity)]"
        @click="onClose"
      >
        <X :size="14" />
      </button>
    </div>

    <!-- 版本选择（本地导入时隐藏） -->
    <div v-if="!isLocalModpack" class="flex flex-col gap-1.5">
      <span class="text-[13px] font-semibold text-secondary-text">选择版本</span>
      <div class="grid grid-cols-[auto_1fr] items-center gap-x-2 gap-y-2">
        <span class="text-[11px] text-muted-text">MC 版本</span>
        <Select v-model="gameVersionFilter" @update:model-value="applyVersionFilters">
          <SelectTrigger class="h-auto w-full rounded-lg border-none bg-muted px-2.5 py-[5px] text-xs">
            <SelectValue placeholder="所有版本" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="g in gameVersionOptions" :key="g" :value="g">{{ g }}</SelectItem>
          </SelectContent>
        </Select>
        <template v-if="loaderOptions.length > 1">
          <span class="text-[11px] text-muted-text">加载器</span>
          <Select v-model="loaderFilter" @update:model-value="applyVersionFilters">
            <SelectTrigger class="h-auto w-full rounded-lg border-none bg-muted px-2.5 py-[5px] text-xs">
              <SelectValue placeholder="全部加载器" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="l in loaderOptions" :key="l.value" :value="l.value">{{ l.label }}</SelectItem>
            </SelectContent>
          </Select>
        </template>
      </div>
      <Select v-model="versionIndex" :disabled="versionLoading || filteredVersions.length === 0" @update:model-value="onVersionSelected">
        <SelectTrigger class="h-auto w-full rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]">
          <SelectValue :placeholder="versionLoading ? '正在加载版本…' : (filteredVersions.length ? '选择版本' : '该过滤条件下没有可用版本')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="(v, i) in filteredVersions" :key="v.id" :value="String(i)">
            {{ versionLabel(v) }}{{ primaryFile(v)?.size ? ` · ${formatBytes(primaryFile(v).size)}` : '' }}
          </SelectItem>
        </SelectContent>
      </Select>
    </div>

    <!-- 下载到（DownloadTargetPicker） -->
    <div class="flex flex-col gap-1.5">
      <span class="text-[13px] font-semibold text-secondary-text">下载到</span>
      <template v-if="kind === 'modpack'">
        <Input
          v-model="newInstanceName"
          maxlength="48"
          placeholder="给新实例取个名字，例如 MyModpack"
          class="h-auto rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]"
        />
        <span class="text-[11px] leading-relaxed text-hint-text">{{ newInstanceHint }}</span>
      </template>
      <template v-else>
        <Select v-model="targetId">
          <SelectTrigger class="h-auto w-full rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]">
            <SelectValue placeholder="选择目标实例…" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in targetOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
        <span class="break-all text-[11px] leading-relaxed text-hint-text">{{ targetHint }}</span>
      </template>
    </div>

    <!-- 状态提示（错误/警告） -->
    <span v-if="statusText" class="text-xs leading-relaxed text-destructive">{{ statusText }}</span>

    <!-- 下载状态区（DownloadStatusPanel：ControlBg r12 + 环形进度） -->
    <div class="flex flex-col gap-2 rounded-xl bg-muted px-3.5 py-3">
      <span class="truncate text-xs text-secondary-text">{{ statusFileText || idleText || '选择版本后点击下载' }}</span>
      <div v-if="downloading" class="relative mx-auto grid size-[46px] place-items-center">
        <div
          class="absolute size-[46px] rounded-full"
          :style="{ background: `conic-gradient(var(--accent) ${progressPercent * 3.6}deg, var(--control-bg) 0deg)` }"
        />
        <div class="size-[34px] rounded-full bg-muted" />
        <span class="absolute text-xs font-bold text-primary">{{ Math.round(progressPercent) }}</span>
      </div>
      <span v-if="statusDetail" class="whitespace-pre-line break-all text-[11px] text-hint-text">{{ statusDetail }}</span>
    </div>

    <!-- 底部下载按钮 -->
    <div class="flex justify-end">
      <Button class="h-auto gap-1.5 px-5 py-2 text-[13px] font-semibold" :disabled="downloading" @click="onDownload">
        <ArrowDown :size="15" /><span>{{ kind === 'modpack' ? '安装' : '下载' }}</span>
      </Button>
    </div>
  </div>
</template>

<script setup>
/*
 * 移植自 Controls/ContentDownloadOverlay.axaml.cs：
 * - 版本列表拉取 api.modrinth.com/v2/project/{id}/version（对应 ModrinthVersionApi），
 *   MC 版本 / 加载器双过滤（加载器为 "minecraft" 占位值时不过滤）；
 * - 目标实例经 InstanceAPI.GetCurrentInstanceSnapshot + DownloadAPI.ResolveContentDirectoryForInstance；
 * - Mod/资源包/光影 → DownloadAPI.DownloadFileToInstance（进度 download:contentProgress）；
 *   自定义路径 → SystemAPI.SaveFile + DownloadAPI.DownloadFileToPath；
 * - 整合包 → 下载到临时目录 + DownloadAPI.InstallModpackToInstance，完成后清理临时目录；
 * - 本地整合包导入（SetupForLocalModpack）→ localModpackPath prop，ReadModpackRequirements 读取要求。
 * PORTING：原版 ResolveModpackTargetVersionAsync 会按整合包要求自动安装缺失的 MC 版本 +
 * 加载器（DownloadService.StartModLoaderAsync）；Go 已有 StartModLoaderDownload，但流程
 * 涉及多阶段编排，此处简化为仅安装到解析出的内容目录，版本要求以提示文本展示。
 * PORTING：原版下载可经 CancellationToken 取消；Go 端内容下载命令无取消入参，故未提供取消按钮。
 */
import { computed, onUnmounted, ref, watch } from 'vue'
import { ArrowDown, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  DownloadFileToInstance,
  DownloadFileToPath,
  InstallModpackToInstance,
  ReadModpackRequirements,
  ResolveContentDirectoryForInstance,
} from '../../../wailsjs/go/bindings/DownloadAPI'
import { GetCurrentInstanceSnapshot, RefreshInstances } from '../../../wailsjs/go/bindings/InstanceAPI'
import { DeleteDirectory } from '../../../wailsjs/go/bindings/LauncherAPI'
import { SaveFile as pickSaveFile } from '../../../wailsjs/go/bindings/SystemAPI'
import { alert } from '../../composables/dialog.js'
import { EventsOn, EventsOff } from '../../../wailsjs/runtime/runtime'

const props = defineProps({
  show: { type: Boolean, default: false },
  project: { type: Object, default: null }, // Modrinth 搜索结果 { project_id, title, description, icon_url }
  kind: { type: String, default: 'modpack' }, // 'modpack' | 'resourcepack' | 'shaderpack'
  localModpackPath: { type: String, default: '' }, // 非空 = 本地整合包导入模式
})
const emit = defineEmits(['close'])

const SUB_DIRS = { resourcepack: 'resourcepacks', shaderpack: 'shaderpacks' }
const GLYPHS = { modpack: '📦', resourcepack: '🎨', shaderpack: '✨' }

const isLocalModpack = computed(() => !!props.localModpackPath)

const headerTitle = computed(() => {
  if (isLocalModpack.value) return '导入整合包'
  return props.project?.title ?? ''
})
const headerSubtitle = computed(() => {
  if (isLocalModpack.value) return fileNameOf(props.localModpackPath)
  return props.project?.description ?? ''
})
const headerIcon = computed(() => (isLocalModpack.value ? '' : props.project?.icon_url ?? ''))
const headerGlyph = computed(() => GLYPHS[props.kind] ?? '📦')

// ---------- 版本列表与过滤（Setup / SetupVersionFilters / ApplyVersionFilters） ----------
const allVersions = ref([])
const filteredVersions = ref([])
const versionLoading = ref(false)
const gameVersionFilter = ref('所有版本')
const loaderFilter = ref('__all__')
const versionIndex = ref('')
const gameVersionOptions = ref(['所有版本'])
const loaderOptions = ref([])

function versionLabel(v) {
  return v.name && v.name !== v.version_number ? `${v.name}（${v.version_number}）` : v.version_number
}
function primaryFile(v) {
  return (v.files ?? []).find((f) => f.primary) ?? (v.files ?? [])[0] ?? null
}

function mcVersionSortDesc(a, b) {
  const pa = a.split('.').map((n) => parseInt(n, 10) || 0)
  const pb = b.split('.').map((n) => parseInt(n, 10) || 0)
  for (let i = 0; i < 3; i++) {
    if ((pa[i] ?? 0) !== (pb[i] ?? 0)) return (pb[i] ?? 0) - (pa[i] ?? 0)
  }
  return b.localeCompare(a)
}

function setupVersionFilters(list) {
  const gameVersions = [...new Set(list.flatMap((v) => v.game_versions ?? []))].sort(mcVersionSortDesc)
  gameVersionOptions.value = ['所有版本', ...gameVersions]
  gameVersionFilter.value = '所有版本'
  // 加载器过滤仅在数据中存在真实加载器时显示（"minecraft" 占位值不过滤）
  const loaders = [...new Set(
    (list.flatMap((v) => v.loaders ?? [])).filter((l) => l && l.toLowerCase() !== 'minecraft'),
  )]
  loaderOptions.value = [{ value: '__all__', label: '全部加载器' },
    ...loaders.map((l) => ({ value: l, label: l.charAt(0).toUpperCase() + l.slice(1) }))]
  loaderFilter.value = '__all__'
}

function applyVersionFilters() {
  const gv = gameVersionFilter.value === '所有版本' ? null : gameVersionFilter.value
  const ld = loaderFilter.value === '__all__' ? null : loaderFilter.value
  const sorted = [...allVersions.value].sort((a, b) => String(b.date_published ?? '').localeCompare(String(a.date_published ?? '')))
  filteredVersions.value = sorted.filter(
    (v) =>
      (!gv || (v.game_versions ?? []).some((g) => g.toLowerCase() === gv.toLowerCase())) &&
      (!ld || (v.loaders ?? []).some((l) => l.toLowerCase() === ld.toLowerCase())),
  )
  versionIndex.value = filteredVersions.value.length > 0 ? '0' : ''
}

async function loadVersions() {
  const pid = props.project?.project_id
  if (!pid) return
  const seq = ++loadSeq
  versionLoading.value = true
  allVersions.value = []
  filteredVersions.value = []
  try {
    const resp = await fetch(`https://api.modrinth.com/v2/project/${encodeURIComponent(pid)}/version`)
    const data = await resp.json()
    if (seq !== loadSeq) return
    allVersions.value = Array.isArray(data) ? data : []
    setupVersionFilters(allVersions.value)
    applyVersionFilters()
  } catch (e) {
    if (seq !== loadSeq) return
    statusText.value = `加载版本失败：${e?.message ?? e}`
  } finally {
    if (seq === loadSeq) versionLoading.value = false
  }
}
let loadSeq = 0

function onVersionSelected() {
  const v = filteredVersions.value[Number(versionIndex.value)]
  if (!v) return
  const f = primaryFile(v)
  idleText.value = f ? `${versionLabel(v)} · ${formatBytes(f.size)}` : versionLabel(v)
}

// ---------- 下载目标（DownloadTargetPicker） ----------
const targetId = ref('')
const newInstanceName = ref('')
const targetContentDirs = ref({}) // instanceId -> contentDir

const targetOptions = computed(() => [
  ...snapshot.value.VersionIds.map((id) => ({ value: id, label: id })),
  { value: '__custom__', label: '自定义保存路径…' },
])

const snapshot = ref(null)
const newInstanceHint = computed(() => {
  const name = newInstanceName.value.trim()
  return name
    ? `将新建独立实例「${name}」并解压安装整合包内容`
    : '将新建一个独立实例，并在上方输入它的名字'
})
const targetHint = computed(() => {
  if (targetId.value === '__custom__') return '下载时将弹出文件保存对话框'
  if (!targetId.value) return '选择要下载到的目标'
  const dir = targetContentDirs.value[targetId.value]
  const sub = SUB_DIRS[props.kind] ?? 'mods'
  return dir ? `将放入 ${dir}（${sub}）` : '实例内容目录不可用'
})

function contentDirOf(instanceId) {
  const snap = snapshot.value
  if (!snap || !instanceId) return ''
  return ResolveContentDirectoryForInstance(snap.MinecraftDirectory, snap.SourcePath, instanceId)
}

// ---------- 状态面板（DownloadStatusPanel + OverlayDownloadRunner） ----------
const idleText = ref('')
const statusFileText = ref('')
const statusDetail = ref('')
const statusText = ref('')
const downloading = ref(false)
const progressPercent = ref(0)

function beginProgress(fileName) {
  downloading.value = true
  progressPercent.value = 0
  statusFileText.value = fileName
  statusDetail.value = ''
}
function updateProgress(e) {
  if (!e || !downloading.value) return
  progressPercent.value = e.total > 0 ? Math.min(100, (e.downloaded * 100) / e.total) : 0
}

// OverlayHelpers.IsValidInstanceName
function validateInstanceName(name) {
  if (!name) return '请输入新实例的名字。'
  if (name === '.' || name === '..') return '实例名包含不安全字符，请换一个名字。'
  if (/[\\/:*?"<>|]/.test(name)) return '实例名包含不安全字符，请换一个名字。'
  return ''
}

function fileNameOf(p) {
  return String(p ?? '').split(/[\\/]/).pop() || ''
}

// ---------- 下载入口（OnDownloadClick） ----------
async function onDownload() {
  if (downloading.value) return
  statusText.value = ''
  try {
    if (isLocalModpack.value) {
      await runModpackInstall(props.localModpackPath, '', null, true)
      return
    }
    const version = filteredVersions.value[Number(versionIndex.value)]
    if (!version) {
      statusText.value = '请先选择版本。'
      return
    }
    const file = primaryFile(version)
    if (!file?.url) {
      statusText.value = '所选版本无可下载文件。'
      return
    }
    if (props.kind === 'modpack') {
      // 整合包只能安装为独立实例：用户自定义名字
      const name = newInstanceName.value.trim()
      const err = validateInstanceName(name)
      if (err) {
        statusText.value = err
        return
      }
      await downloadToTempAndInstall(file, name)
    } else if (targetId.value === '__custom__') {
      const savePath = await pickSaveFile('保存文件', file.filename, '内容文件', '*.*')
      if (!savePath) return
      beginProgress(file.filename)
      await DownloadFileToPath(file.url, savePath)
      finish(`已保存到 ${savePath}`)
    } else {
      if (!targetId.value) {
        statusText.value = '请选择下载目标。'
        return
      }
      const contentDir = contentDirOf(targetId.value)
      if (!contentDir) {
        statusText.value = '无法定位实例内容目录。'
        return
      }
      beginProgress(file.filename)
      await DownloadFileToInstance(file.url, file.filename, contentDir, SUB_DIRS[props.kind] ?? 'mods')
      finish(`已安装到 ${contentDir}`)
    }
  } catch (e) {
    downloading.value = false
    statusText.value = `操作失败：${e?.message ?? e}`
  }
}

function finish(message) {
  downloading.value = false
  progressPercent.value = 100
  statusDetail.value = message
  statusFileText.value = '完成'
}

// 整合包：下载到游戏目录下的临时缓存目录 → InstallModpackToInstance → 清理临时目录
async function downloadToTempAndInstall(file, instanceName) {
  const snap = snapshot.value
  if (!snap?.MinecraftDirectory) {
    statusText.value = '无法定位游戏目录。'
    return
  }
  const tempDir = `${snap.MinecraftDirectory.replace(/[\\/]+$/, '')}\\nya-modpack-tmp`
  const tempPath = `${tempDir}\\${file.filename || 'modpack.mrpack'}`
  beginProgress(file.filename || 'modpack.mrpack')
  await DownloadFileToPath(file.url, tempPath)
  await runModpackInstall(tempPath, instanceName, null, false, tempDir)
}

// RunModpackInstallAsync：读取要求 → 解析内容目录 → 解压安装 → 汇总
async function runModpackInstall(packPath, instanceName, requirements, keepSource, cleanupDir = '') {
  const snap = snapshot.value
  if (!snap?.MinecraftDirectory) {
    statusText.value = '无法定位游戏目录。'
    return
  }
  try {
    if (!requirements) {
      statusDetail.value = '正在解析整合包所需的游戏版本…'
      requirements = await ReadModpackRequirements(packPath).catch(() => null)
    }
    const contentDir = contentDirOf(instanceName) || snap.MinecraftDirectory
    statusDetail.value = '正在解压整合包并安装依赖…'
    const result = await InstallModpackToInstance(packPath, contentDir)
    await RefreshInstances(snap.MinecraftDirectory).catch(() => {})
    snapshot.value = await GetCurrentInstanceSnapshot().catch(() => snapshot.value)

    const loaderNames = { 0: '', 1: 'Fabric', 2: 'Quilt', 3: 'NeoForge', 4: 'Forge' }
    const reqText = !requirements?.MinecraftVersion
      ? '未识别到版本要求'
      : `目标版本：${instanceName || '(选中实例)'}（MC ${requirements.MinecraftVersion}` +
        (requirements.RawLoaderKey
          ? `，加载器 ${loaderNames[requirements.LoaderType] ?? requirements.RawLoaderKey} ${requirements.LoaderVersion ?? ''}`
          : '，原版') + '）'
    let summary = `已解压 ${result?.InstalledFiles ?? 0} 个文件`
    if ((result?.DownloadedMods ?? 0) > 0) summary += `、下载依赖 ${result.DownloadedMods} 个`
    if ((result?.Errors ?? []).length > 0) summary += `，${result.Errors.length} 项失败`
    finish(`${summary}\n${reqText}`)
    if ((result?.Errors ?? []).length > 0) {
      statusText.value = result.Errors.slice(0, 3).join('；')
    }
    alert(`整合包「${instanceName || fileNameOf(packPath)}」安装完成：${summary}`, { severity: 'success' })
  } finally {
    // 临时缓存目录清理（本地导入模式不删除用户源文件）
    if (cleanupDir) await DeleteDirectory(cleanupDir).catch(() => {})
    void keepSource
  }
}

// ---------- 本地整合包导入（SetupForLocalModpack） ----------
async function prepareLocalModpack() {
  statusDetail.value = '正在解析整合包所需的游戏版本…'
  try {
    const req = await ReadModpackRequirements(props.localModpackPath)
    if (req?.MinecraftVersion) {
      const loaderNames = { 0: '', 1: 'Fabric', 2: 'Quilt', 3: 'NeoForge', 4: 'Forge' }
      idleText.value =
        `要求 MC ${req.MinecraftVersion}` +
        (req.RawLoaderKey ? ` + ${loaderNames[req.LoaderType] ?? req.RawLoaderKey} ${req.LoaderVersion ?? ''}` : '（原版）')
    }
  } catch (e) {
    console.error('读取整合包要求失败', e)
  }
  statusDetail.value = ''
}

// ---------- 打开 / 关闭 ----------
watch(
  () => props.show,
  async (show) => {
    if (!show) return
    // ResetState
    statusText.value = ''
    statusFileText.value = ''
    statusDetail.value = ''
    idleText.value = ''
    progressPercent.value = 0
    downloading.value = false
    newInstanceName.value = ''
    targetId.value = ''
    loadSeq++
    try {
      snapshot.value = await GetCurrentInstanceSnapshot()
    } catch (e) {
      console.error('读取实例快照失败', e)
      snapshot.value = null
    }
    if (isLocalModpack.value) {
      await prepareLocalModpack()
    } else {
      await loadVersions()
    }
    EventsOn('download:contentProgress', updateProgress)
  },
)

function onClose() {
  EventsOff('download:contentProgress')
  loadSeq++
  emit('close')
}

onUnmounted(() => EventsOff('download:contentProgress'))

function formatBytes(n) {
  if (!n) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let i = 0
  let v = n
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toFixed(1)} ${units[i]}`
}
</script>
