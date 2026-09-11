<template>
  <!-- 版本管理页（VersionManagerPage.axaml）：Grid Margin 26,22，行 = 头部 / 主体 / 状态栏 -->
  <section class="flex h-full flex-col px-[26px] py-[22px]">
    <!-- 头部：标题 + 文件夹选择 + 重新扫描 + 打开游戏文件夹 -->
    <div class="mb-[18px] flex items-center gap-2">
      <h1 class="mr-[18px] text-[23px] font-semibold text-foreground">版本管理</h1>
      <Select v-model="selectedFolder" @update:model-value="onFolderChange">
        <SelectTrigger class="h-[42px] min-w-[280px] flex-1">
          <SelectValue placeholder="选择已添加的 Minecraft 文件夹" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="f in folders" :key="f" :value="f">{{ f }}</SelectItem>
        </SelectContent>
      </Select>
      <Button variant="secondary" size="lg" @click="onRefresh">重新扫描</Button>
      <DropdownMenu :open="folderMenuOpen" @update:open="folderMenuOpen = $event">
        <DropdownMenuTrigger as-child>
          <Button variant="secondary" size="lg">
            打开游戏文件夹 <ChevronDown :size="14" class="text-muted-foreground" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" class="max-h-[340px] min-w-60 overflow-y-auto">
          <DropdownMenuItem
            v-for="item in folderMenuItems"
            :key="item.tag"
            @select="onOpenSubFolder(item)"
          >
            <span>{{ item.glyph }}</span>{{ item.label }}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>

    <!-- 主体：左 250px 实例列表 + 右详情卡 -->
    <div class="grid min-h-0 flex-1 grid-cols-[250px_16px_1fr]">
      <!-- 左：实例版本列表 -->
      <div class="flex min-h-0 flex-col rounded-[13px] border border-border bg-[var(--panel-bg)] p-3">
        <div class="mx-1 mb-2.5 mt-0.5 text-[13px] font-semibold text-secondary-text">实例版本</div>
        <div class="flex flex-1 flex-col gap-0.5 overflow-y-auto">
          <button
            v-for="v in versions"
            :key="v"
            class="flex w-full items-center gap-2 rounded-sm px-3 py-2 text-left text-[13px] text-body-text transition-colors"
            :class="
              v === selectedVersion
                ? 'bg-accent font-semibold text-primary'
                : 'hover:bg-accent'
            "
            @click="selectVersion(v)"
            @contextmenu.prevent="openInstanceMenu($event, v)"
          >
            <!-- 实例图标（ContentAPI.GetInstanceVisual；本地路径经 /localfile?path= 流） -->
            <img
              v-if="visualIconUrl(v)"
              :src="visualIconUrl(v)"
              class="size-7 shrink-0 rounded-sm object-cover"
              alt=""
              @error="onVisualImgError(v)"
            />
            <span
              v-else
              class="flex size-7 shrink-0 items-center justify-center rounded-sm bg-badge font-bold text-primary"
            >{{ visualGlyph(v) ?? glyphOf(v) }}</span>
            <span class="truncate">{{ v }}</span>
          </button>
        </div>
        <div class="mx-1 mb-0.5 mt-2.5 text-[10px] text-muted-text">{{ versions.length }} 个实例</div>
      </div>

      <!-- 右：详情卡 -->
      <Card class="col-start-3 flex min-h-0 flex-col p-5">
        <!-- 空态 -->
        <div v-if="!selectedVersion" class="flex flex-1 flex-col items-center justify-center gap-[7px]">
          <span class="text-[40px] text-secondary-text">▦</span>
          <span class="text-[21px] font-semibold text-secondary-text">请选择一个实例版本</span>
          <span class="text-[11px] text-subtext-text">选择后可以查看详情、内容与启动设置</span>
        </div>

        <template v-else>
          <!-- 详情头部 -->
          <div class="mb-3.5 flex items-center gap-3">
            <img
              v-if="selectedIconUrl"
              :src="selectedIconUrl"
              class="size-12 shrink-0 rounded-md object-cover"
              alt=""
              @error="selectedIconBroken = true"
            />
            <div
              v-else
              class="flex size-12 shrink-0 items-center justify-center rounded-md bg-badge text-[20px] font-bold text-primary"
            >{{ visualGlyph(selectedVersion) ?? glyphOf(selectedVersion) }}</div>
            <div class="flex min-w-0 flex-col gap-[3px]">
              <span class="truncate text-[21px] font-semibold text-foreground">{{ selectedVersion }}</span>
              <span class="text-[11px] text-subtext-text">{{ details?.BaseGameVersion || 'Minecraft' }}</span>
            </div>
            <Button variant="secondary" size="sm" @click="openVersionFolder">打开文件夹</Button>
            <Button variant="secondary" size="sm" @click="openLogs">查看日志</Button>
          </div>

          <!-- MD3 标签页 -->
          <Tabs :model-value="activeTab" @update:model-value="activeTab = $event">
            <TabsList class="h-9 w-full justify-start rounded-lg">
              <TabsTrigger v-for="t in tabs" :key="t" :value="t">{{ t }}</TabsTrigger>
            </TabsList>
          </Tabs>

          <div class="flex min-h-0 flex-1 flex-col">
            <!-- 版本 -->
            <div v-if="activeTab === '版本'" class="nya-tab-scroll">
              <div v-for="row in detailRows" :key="row.label" class="grid grid-cols-[150px_1fr] gap-2">
                <span class="text-[13px] text-muted-foreground">{{ row.label }}</span>
                <span class="break-all text-[13px] text-body-text">{{ row.value || '—' }}</span>
              </div>
            </div>

            <!-- 启动设置 -->
            <div v-else-if="activeTab === '启动设置'" class="nya-tab-scroll">
              <label class="flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                <Switch v-model="profile.IsVersionIsolationEnabled" />
                开启版本隔离（模组、资源包、光影、配置和存档使用识别到的实例内容目录）
              </label>
              <div class="flex flex-col gap-2">
                <label class="flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                  <Switch v-model="profile.UseIndependentMemorySettings" />
                  独立调整（关闭时锁定下方滑块并使用全局内存设置）
                </label>
                <div class="flex items-center justify-between">
                  <span class="text-[13px] text-muted-foreground">实例最小内存</span>
                  <span class="text-[13px] font-semibold text-secondary-text">{{ profile.MinimumMemoryMb }} MiB</span>
                </div>
                <Slider
                  v-model="profile.MinimumMemoryMb"
                  :min="256" :max="4096" :step="256"
                  :disabled="!profile.UseIndependentMemorySettings"
                />
                <div class="mt-1 flex items-center justify-between">
                  <span class="text-[13px] text-muted-foreground">实例最大内存</span>
                  <span class="text-[13px] font-semibold text-secondary-text">{{ profile.MaximumMemoryMb }} MiB</span>
                </div>
                <Slider
                  v-model="profile.MaximumMemoryMb"
                  :min="512" :max="4096" :step="256"
                  :disabled="!profile.UseIndependentMemorySettings"
                />
                <span class="text-[11px] text-hint-text">实际最大内存不会超过设置页中的全局上限。{{ memoryPolicyText }}</span>
              </div>
              <details class="nya-advanced">
                <summary>高级选项</summary>
                <label class="mt-2.5 flex cursor-pointer items-center gap-2 text-[13px] text-secondary-text">
                  <Switch v-model="profile.FollowGlobalAdvancedSettings" />
                  跟随全局高级启动设置（默认开启；关闭后才能自定义）
                </label>
                <div class="mt-2.5 grid grid-cols-[1fr_12px_1fr]">
                  <div>
                    <div class="nya-field-label">窗口宽度</div>
                    <Input v-model.number="profile.WindowWidth" type="number" />
                  </div>
                  <div>
                    <div class="nya-field-label">窗口高度</div>
                    <Input v-model.number="profile.WindowHeight" type="number" />
                  </div>
                </div>
                <div class="nya-field-label mt-2.5">Java 可执行文件（留空时自动检测）</div>
                <Input v-model="profile.JavaExecutable" type="text" />
                <div class="nya-field-label mt-2.5">额外 JVM 参数（每行一个参数）</div>
                <textarea v-model="jvmArgsText" rows="3" class="nya-area"></textarea>
                <div class="nya-field-label mt-2.5">额外游戏参数（每行一个参数）</div>
                <textarea v-model="gameArgsText" rows="3" class="nya-area"></textarea>
              </details>
              <Button class="h-auto self-start px-[18px] py-2" @click="saveSettings">保存实例设置</Button>
            </div>

            <!-- 编辑实例 -->
            <div v-else-if="activeTab === '编辑实例'" class="nya-tab-scroll">
              <div class="nya-field-label">实例名称（将直接重命名版本文件夹与文件）</div>
              <div class="grid grid-cols-[1fr_auto] gap-2">
                <Input v-model="newName" type="text" placeholder="输入新的实例名称" />
                <Button variant="secondary" @click="renameInstance">重命名</Button>
              </div>
              <div class="nya-field-label">导出为整合包</div>
              <Button variant="secondary" class="self-start" @click="$router.push('/modpack')">制作整合包</Button>
              <div class="text-[11px] text-hint-text">
                跳转到整合包制作页：选择 Modrinth / MultiMC 格式、勾选内容并填写作者与描述。
              </div>
              <div class="nya-field-label">危险操作</div>
              <Button variant="destructive" class="h-auto self-start px-[18px] py-2" @click="deleteInstance">删除实例</Button>
              <div class="text-[11px] text-hint-text">删除操作不可恢复，将移除版本文件夹及其全部内容。</div>
            </div>

            <!-- 内容类标签页（已安装模组 / 资源包 / 光影 / 存档） -->
            <template v-else>
              <div class="flex items-center gap-3 px-3.5 pt-3.5">
                <span class="shrink-0 text-[13px] font-semibold text-secondary-text">{{ contentSummary }}</span>
                <Input
                  v-model="contentSearch"
                  type="text"
                  class="flex-1"
                  :placeholder="`搜索${activeTab}名称…`"
                />
              </div>
              <div v-if="filteredContent.length === 0" class="flex flex-1 items-center justify-center text-[13px] text-hint-text">没有匹配的{{ activeTab }}</div>
              <div v-else class="flex flex-1 flex-col gap-1 overflow-y-auto px-3.5 pb-3.5 pt-2">
                <div
                  v-for="entry in filteredContent"
                  :key="entry.SourcePath"
                  class="flex cursor-default items-center gap-3 rounded-sm bg-transparent px-3 py-3 transition-colors hover:bg-accent"
                >
                  <span class="flex size-8 shrink-0 items-center justify-center rounded-sm bg-badge">{{ entry.FallbackGlyph || '📦' }}</span>
                  <div class="flex min-w-0 flex-col gap-0.5">
                    <span class="truncate text-[13px] font-semibold text-foreground" :class="entry.IsDisabled ? 'line-through opacity-60' : ''">{{ entry.Name }}</span>
                    <span class="truncate text-[10px] text-hint-text">{{ entry.MetadataLine }}</span>
                  </div>
                  <div class="ml-auto flex shrink-0 items-center gap-2">
                    <!-- 存档操作（ContentAPI.BackupSave / ExportSave / DeleteSave） -->
                    <template v-if="activeTab === '游戏存档'">
                      <Button variant="outline" size="sm" class="text-[11px]" @click="backupSave(entry)">备份</Button>
                      <Button variant="outline" size="sm" class="text-[11px]" @click="exportSave(entry)">导出</Button>
                      <Button variant="outline" size="sm" class="text-[11px] text-destructive" @click="deleteSave(entry)">删除</Button>
                    </template>
                    <template v-else>
                      <span class="text-[10px] text-hint-text">{{ entry.IsDisabled ? '已禁用' : '已启用' }}</span>
                      <Switch :model-value="!entry.IsDisabled" :disabled="contentToggling === entry.SourcePath"
                        aria-label="启用/禁用" @update:model-value="toggleContent(entry, $event)" />
                    </template>
                  </div>
                </div>
              </div>
            </template>
          </div>
        </template>
      </Card>
    </div>

    <!-- 底部状态栏 -->
    <div class="mt-[13px] break-all text-[11px] text-subtext-text">{{ statusText }}</div>

    <!-- 实例右键菜单（VersionManagerPage 实例行 ContextMenu：打开文件夹/重命名/删除/设为当前） -->
    <div
      v-if="instanceMenu.open"
      class="fixed z-[800]"
      :style="{ left: instanceMenu.x + 'px', top: instanceMenu.y + 'px' }"
    >
      <DropdownMenu :open="instanceMenu.open" @update:open="closeInstanceMenu">
        <DropdownMenuTrigger as-child>
          <span class="block size-px" aria-hidden="true" />
        </DropdownMenuTrigger>
        <DropdownMenuContent
          align="start"
          side="right"
          :side-offset="4"
          class="min-w-44"
          @contextmenu.prevent
        >
          <DropdownMenuItem @select="openInstanceFolder(instanceMenu.version)">
            <span>📂</span>打开文件夹
          </DropdownMenuItem>
          <DropdownMenuItem @select="renameInstanceMenu(instanceMenu.version)">
            <span>✏️</span>重命名
          </DropdownMenuItem>
          <DropdownMenuItem @select="setCurrentInstance(instanceMenu.version)">
            <span>▶</span>设为当前
          </DropdownMenuItem>
          <DropdownMenuItem class="text-destructive" @select="deleteInstanceMenu(instanceMenu.version)">
            <span>🗑</span>删除
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  </section>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ChevronDown } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Slider } from '@/components/ui/slider'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { GetGameDirectory, GetProfileFolders, GetVersionProfile, SaveGameDirectory, SaveVersionProfile, LoadGlobalLaunchSettings } from '../../wailsjs/go/bindings/ConfigAPI'
import { GetCurrentInstanceSnapshot, GetInstanceContentDirectory, GetVersionDetails, RefreshInstances, RenameInstance, SelectInstance, DeleteInstance, GetInstanceGameDirectory } from '../../wailsjs/go/bindings/InstanceAPI'
import { GetInstanceVisual } from '../../wailsjs/go/bindings/ContentAPI'
import { BackupSave, DeleteSave, ExportSave, ToggleContentEntry } from '../../wailsjs/go/bindings/ContentAPI'
import { GetMemoryDecision } from '../../wailsjs/go/bindings/LauncherAPI'
import { OpenInExplorer, SaveFile as pickSavePath } from '../../wailsjs/go/bindings/SystemAPI'
import { alert, confirm as nyaConfirm, promptDialog } from '../composables/dialog.js'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime'

const tabs = ['版本', '启动设置', '编辑实例', '已安装模组', '资源包', '光影', '游戏存档']

// ---------- 文件夹 ----------
const folders = ref([])
const selectedFolder = ref('')
const folderMenuOpen = ref(false)
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
// folderMenuItems 中 tag 语义（对照原版 VersionManagerPage Actions 菜单）：
//   ''                → 游戏根目录；versions/libraries/assets → 游戏根目录下的公共目录
//   其余（saves/mods/…）→ 实例内容目录（版本隔离时为实例专属目录，共享时回落游戏目录）
const INSTANCE_SCOPED_TAGS = new Set(['saves', 'mods', 'resourcepacks', 'shaderpacks', 'screenshots', 'config', 'logs', 'crash-reports'])

function joinPath(dir, name) {
  if (!dir) return name
  return dir.replace(/[\\/]+$/, '') + '\\' + name
}

async function onOpenSubFolder(item) {
  const base = selectedFolder.value
  if (!base) {
    alert('请先选择一个 Minecraft 文件夹。', { severity: 'warning' })
    return
  }
  try {
    let target
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
    alert(`打开文件夹失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

// ---------- 实例列表 ----------
const versions = ref([])
const selectedVersion = ref('')
const statusText = ref('请选择一个 Minecraft 文件夹')

// ---------- 详情 ----------
const details = ref(null)
const activeTab = ref('版本')
const profile = ref({
  MinimumMemoryMb: 512,
  MaximumMemoryMb: 4096,
  UseIndependentMemorySettings: false,
  FollowGlobalAdvancedSettings: true,
  IsVersionIsolationEnabled: false,
  WindowWidth: 854,
  WindowHeight: 480,
  JavaExecutable: '',
  AdditionalJvmArguments: [],
  AdditionalGameArguments: [],
})
const newName = ref('')
const memoryPolicyText = ref('')
const contentSearch = ref('')

const jvmArgsText = computed({
  get: () => (profile.value.AdditionalJvmArguments ?? []).join('\n'),
  set: (v) => { profile.value.AdditionalJvmArguments = v.split('\n').filter(Boolean) },
})
const gameArgsText = computed({
  get: () => (profile.value.AdditionalGameArguments ?? []).join('\n'),
  set: (v) => { profile.value.AdditionalGameArguments = v.split('\n').filter(Boolean) },
})

const detailRows = computed(() => {
  const d = details.value ?? {}
  return [
    { label: '实际版本 ID', value: d.VersionId },
    { label: 'Minecraft 基础版本', value: d.BaseGameVersion },
    { label: '版本类型', value: d.VersionType },
    { label: '模组加载器', value: d.LoaderName },
    { label: '模组加载器版本', value: d.LoaderVersion },
    { label: '版本隔离', value: d.IsIsolated ? '已开启' : '已关闭' },
    { label: '布局识别来源', value: d.LayoutProvider },
    { label: '实例内容目录', value: d.ContentDirectory },
    { label: '发布时间', value: d.ReleaseTime },
    { label: 'Java 要求', value: d.JavaRequirement },
    { label: '主类', value: d.MainClass },
  ]
})

const contentMap = computed(() => ({
  已安装模组: { list: details.value?.Mods ?? [], unit: '个模组' },
  资源包: { list: details.value?.ResourcePacks ?? [], unit: '个资源包' },
  光影: { list: details.value?.Shaders ?? [], unit: '个光影' },
  游戏存档: { list: details.value?.Saves ?? [], unit: '个存档' },
}))
const contentSummary = computed(() => {
  const c = contentMap.value[activeTab.value]
  return c ? `${c.list.length} ${c.unit}` : ''
})
const filteredContent = computed(() => {
  const c = contentMap.value[activeTab.value]
  if (!c) return []
  const q = contentSearch.value.trim().toLowerCase()
  if (!q) return c.list
  return c.list.filter(
    (e) => (e.Name ?? '').toLowerCase().includes(q) || (e.Description ?? '').toLowerCase().includes(q)
  )
})

// ---------- 实例图标（ContentAPI.GetInstanceVisual；无图标时回退首字母） ----------
function glyphOf(versionId) {
  return (versionId ?? '?')[0].toUpperCase()
}

const visuals = ref({}) // versionId -> { IconPath, FallbackGlyph }
const brokenIcons = ref(new Set()) // /localfile 流加载失败的图标（回退字形）

function visualOf(v) {
  return visuals.value[v] ?? null
}
function visualGlyph(v) {
  return visualOf(v)?.FallbackGlyph || null
}
// 图标文件为本地路径 → 经 /localfile?path= 流返回（Go localfile_handler）。
// 注意：该 handler 扩展名白名单目前仅含音频（.mp3/.ogg/.wav/.flac/.m4a），
// .png/.jpg 会被 403 拒绝 → onerror 回退字形；待白名单扩充后无需改前端。
function visualIconUrl(v) {
  const path = visualOf(v)?.IconPath
  if (!path || brokenIcons.value.has(v)) return ''
  return `/localfile?path=${encodeURIComponent(path)}`
}
function onVisualImgError(v) {
  const next = new Set(brokenIcons.value)
  next.add(v)
  brokenIcons.value = next
}

async function loadInstanceVisuals() {
  const results = await Promise.allSettled(
    versions.value.map(async (v) => [v, await GetInstanceVisual(v, details.value?.LoaderName ?? '')]),
  )
  const map = {}
  for (const r of results) {
    if (r.status === 'fulfilled' && r.value?.[1]) map[r.value[0]] = r.value[1]
  }
  visuals.value = map
}

const selectedIconUrl = computed(() => (selectedVersion.value ? visualIconUrl(selectedVersion.value) : ''))

// ---------- 实例右键菜单 ----------
const instanceMenu = ref({ open: false, x: 0, y: 0, version: '' })

function openInstanceMenu(e, v) {
  instanceMenu.value = { open: true, x: e.clientX, y: e.clientY, version: v }
}

function closeInstanceMenu(open) {
  if (!open) instanceMenu.value = { open: false, x: 0, y: 0, version: '' }
}

async function openInstanceFolder(v) {
  closeInstanceMenu(false)
  try {
    const snap = await GetCurrentInstanceSnapshot()
    await OpenInExplorer(joinPath(joinPath(snap.MinecraftDirectory || selectedFolder.value, 'versions'), v))
  } catch (e) {
    alert(`打开文件夹失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

async function renameInstanceMenu(v) {
  closeInstanceMenu(false)
  const name = await promptDialog('重命名实例', `输入「${v}」的新名称：`, { defaultValue: v })
  if (!name || name.trim() === v) return
  try {
    const finalId = await RenameInstance(v, name.trim())
    statusText.value = `已重命名为 ${finalId}`
    await refresh(selectedFolder.value)
  } catch (e) {
    statusText.value = `重命名失败：${e?.message ?? e}`
    alert(`重命名失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

async function setCurrentInstance(v) {
  closeInstanceMenu(false)
  try {
    await SelectInstance(v)
    statusText.value = `已设为当前实例：${v}`
    await refresh(selectedFolder.value)
  } catch (e) {
    alert(`设置当前实例失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

async function deleteInstanceMenu(v) {
  closeInstanceMenu(false)
  if (!(await nyaConfirm('删除实例', `确定删除实例「${v}」？将移除版本文件夹及其全部内容，该操作不可恢复。`, { confirmLabel: '删除' }))) return
  try {
    await DeleteInstance(v, selectedFolder.value)
    statusText.value = `已删除实例：${v}`
    if (selectedVersion.value === v) {
      selectedVersion.value = ''
      details.value = null
      activeTab.value = '版本'
    }
    await refresh(selectedFolder.value)
  } catch (e) {
    alert(`删除实例失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

// ---------- 存档操作（ContentAPI.BackupSave / ExportSave / DeleteSave） ----------
const contentToggling = ref('')
async function toggleContent(entry, enabled) {
  contentToggling.value = entry.SourcePath
  try {
    await ToggleContentEntry(entry.SourcePath, !enabled)
    entry.IsDisabled = !enabled
    statusText.value = `已${enabled ? '启用' : '禁用'}「${entry.Name}」`
  } catch (e) {
    statusText.value = `切换失败：${e?.message ?? e}`
    alert(`切换「${entry.Name}」状态失败：${e?.message ?? e}`, { severity: 'error' })
  } finally {
    contentToggling.value = ''
  }
}

async function backupSave(entry) {
  try {
    statusText.value = `正在备份存档「${entry.Name}」…`
    const zipPath = await BackupSave(entry.SourcePath)
    statusText.value = `已备份：${zipPath}`
    alert(`存档已备份到 ${zipPath}`, { severity: 'success' })
  } catch (e) {
    statusText.value = `备份失败：${e?.message ?? e}`
    alert(`备份存档失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

async function exportSave(entry) {
  try {
    // SystemAPI.SaveFile(title, defaultName, filterName, pattern) 选导出路径
    const dest = await pickSavePath('导出存档', `${entry.Name}.zip`, 'ZIP 压缩包', '*.zip')
    if (!dest) return
    statusText.value = `正在导出存档「${entry.Name}」…`
    const zipPath = await ExportSave(entry.SourcePath, dest)
    statusText.value = `已导出：${zipPath || dest}`
    alert(`存档已导出到 ${zipPath || dest}`, { severity: 'success' })
  } catch (e) {
    statusText.value = `导出失败：${e?.message ?? e}`
    alert(`导出存档失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

async function deleteSave(entry) {
  if (!(await nyaConfirm('删除存档', `确定删除存档「${entry.Name}」？该操作不可恢复。`, { confirmLabel: '删除' }))) return
  try {
    await DeleteSave(entry.SourcePath)
    statusText.value = `已删除存档：${entry.Name}`
    await loadDetails(selectedVersion.value)
  } catch (e) {
    alert(`删除存档失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

// ---------- 查看日志（GameLogOverlay，动态导入；缺失时降级提示） ----------
async function openLogs() {
  if (!selectedVersion.value) return
  try {
    const mod = await import('../components/overlay/GameLogOverlay.vue')
    const mountGameLogOverlay = mod.mountGameLogOverlay ?? mod.default?.mountGameLogOverlay
    if (typeof mountGameLogOverlay === 'function') {
      mountGameLogOverlay({ versionId: selectedVersion.value })
    } else if (mod.default) {
      // 组件默认导出：由使用方自行挂载（当前通过全局宿主方式接入）
      window.dispatchEvent(new CustomEvent('nya:open-game-log', { detail: { versionId: selectedVersion.value } }))
    }
  } catch (e) {
    console.error('日志查看器加载失败', e)
    alert('日志查看器尚未就绪（GameLogOverlay 组件缺失）。', { severity: 'warning' })
  }
}

// ---------- 数据加载 ----------
async function loadFolders() {
  try {
    folders.value = (await GetProfileFolders()) ?? []
    const configured = await GetGameDirectory()
    selectedFolder.value =
      folders.value.find((f) => f === configured) ?? folders.value[0] ?? ''
  } catch (e) {
    console.error('加载文件夹列表失败', e)
  }
}

async function refresh(path) {
  statusText.value = '正在扫描实例…'
  try {
    const snap = await RefreshInstances(path)
    applySnapshot(snap)
  } catch (e) {
    statusText.value = `扫描失败：${e}`
    console.error('扫描实例失败', e)
  }
}

function applySnapshot(snap) {
  if (!snap) return
  versions.value = snap.VersionIds ?? []
  selectedVersion.value = snap.SelectedVersionId ?? ''
  statusText.value = snap.ErrorMessage || `游戏目录：${snap.GameDirectory ?? '—'}`
  loadInstanceVisuals()
}

async function onFolderChange() {
  if (!selectedFolder.value) return
  try {
    await SaveGameDirectory(selectedFolder.value)
  } catch (e) {
    console.error('保存游戏目录失败', e)
  }
  await refresh(selectedFolder.value)
}

const onRefresh = () => refresh(selectedFolder.value)

async function selectVersion(v) {
  selectedVersion.value = v
  contentSearch.value = ''
  await SelectInstance(v).catch((e) => console.error('选中实例失败', e))
  await loadDetails(v)
  await loadProfile(v)
}

async function loadDetails(versionId) {
  try {
    details.value = await GetVersionDetails(versionId)
  } catch (e) {
    details.value = null
    console.error('读取实例详情失败', e)
  }
}

async function loadProfile(versionId) {
  try {
    const loaded = await GetVersionProfile(selectedFolder.value, versionId)
    if (loaded) profile.value = { ...profile.value, ...loaded }
    await refreshMemoryPolicy()
  } catch (e) {
    console.error('读取实例设置失败', e)
  }
}

async function refreshMemoryPolicy() {
  try {
    const decision = await GetMemoryDecision(null)
    const fmt = (mb) => (mb >= 1024 ? `${(mb / 1024).toFixed(2).replace(/\.?0+$/, '')} GiB` : `${mb} MiB`)
    if (!decision) return
    memoryPolicyText.value = profile.value.UseIndependentMemorySettings
      ? `独立调整已开启；按当前可用内存估算，本实例最大使用 ${fmt(decision.MaximumMemoryMb)}，启动时会重新计算。`
      : `独立调整已关闭；本实例使用全局策略估算最大 ${fmt(decision.MaximumMemoryMb)}。`
  } catch (e) {
    console.error('读取内存策略失败', e)
  }
}

watch(() => profile.value.UseIndependentMemorySettings, refreshMemoryPolicy)

async function saveSettings() {
  if (profile.value.MinimumMemoryMb < 256 || profile.value.MaximumMemoryMb < profile.value.MinimumMemoryMb) {
    statusText.value = '保存失败：最小内存至少为 256 MiB，最大内存不能小于最小内存。'
    alert(statusText.value, { severity: 'warning' })
    return
  }
  try {
    await SaveVersionProfile(profile.value)
    statusText.value = '实例设置已保存。'
  } catch (e) {
    statusText.value = `保存失败：${e}`
    console.error('保存实例设置失败', e)
  }
}

async function renameInstance() {
  const name = newName.value.trim()
  if (!name || !selectedVersion.value) return
  if (!(await nyaConfirm('重命名实例', `确定将「${selectedVersion.value}」重命名为「${name}」吗？`))) return
  try {
    const finalId = await RenameInstance(selectedVersion.value, name)
    statusText.value = `已重命名为 ${finalId}`
    newName.value = ''
    await refresh(selectedFolder.value)
  } catch (e) {
    statusText.value = `重命名失败：${e}`
    console.error('重命名失败', e)
  }
}

async function deleteInstance() {
  if (!selectedVersion.value) return
  if (!(await nyaConfirm(
    '删除实例',
    `确定删除实例「${selectedVersion.value}」？将移除版本文件夹及其全部内容，该操作不可恢复。`,
    { confirmLabel: '删除' },
  ))) return
  try {
    await DeleteInstance(selectedVersion.value, selectedFolder.value)
    statusText.value = `已删除实例：${selectedVersion.value}`
    selectedVersion.value = ''
    newName.value = ''
    details.value = null
    activeTab.value = '版本'
    await refresh(selectedFolder.value)
  } catch (e) {
    statusText.value = `删除失败：${e?.message ?? e}`
    alert(`删除实例失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

async function openVersionFolder() {
  // 打开当前选中实例的游戏目录（版本隔离时为实例内容目录）
  if (!selectedVersion.value || !selectedFolder.value) return
  try {
    const snap = await GetCurrentInstanceSnapshot()
    const dir = await GetInstanceGameDirectory(snap, selectedFolder.value)
    await OpenInExplorer(dir || selectedFolder.value)
  } catch (e) {
    alert(`打开文件夹失败：${e?.message ?? e}`, { severity: 'error' })
  }
}

function onInstanceChanged(snap) {
  applySnapshot(snap)
}

onMounted(async () => {
  await loadFolders()
  try {
    applySnapshot(await GetCurrentInstanceSnapshot())
  } catch (e) {
    console.error('读取实例快照失败', e)
  }
  EventsOn('instance:changed', onInstanceChanged)
  EventsOn('config:profilesChanged', loadFolders)
})

onUnmounted(() => {
  EventsOff('instance:changed')
  EventsOff('config:profilesChanged')
})
</script>

<style scoped>
/* 标签页内容滚动区（保持原 tab-scroll 布局） */
.nya-tab-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: var(--space-12);
}
/* 高级选项折叠块：SubtleBorder + r9 */
.nya-advanced {
  border: 1px solid var(--subtle-border);
  border-radius: var(--radius-md);
  padding: var(--space-12);
}
.nya-advanced summary {
  cursor: pointer;
  color: var(--secondary-text);
  font-size: var(--font-body);
  font-weight: 600;
}
.nya-field-label {
  color: var(--muted-text);
  font-size: var(--font-body);
  margin-bottom: 5px;
}
/* 多行参数输入（无 ui Textarea 组件，沿用 Input 视觉语言） */
.nya-area {
  width: 100%;
  box-sizing: border-box;
  background: var(--control-bg);
  border: 1px solid var(--default-border);
  border-radius: var(--radius-sm);
  color: var(--body-text);
  font-size: var(--font-body);
  padding: 7px var(--space-12);
  outline: none;
  resize: vertical;
  transition: border-color 150ms;
}
.nya-area:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--accent) 40%, transparent);
}
</style>
