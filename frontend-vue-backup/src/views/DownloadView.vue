<template>
  <!-- 下载大厅（DownloadPage.axaml）：Grid PageMargin，行 = 标题区 / TabControl -->
  <section class="relative flex h-full flex-col gap-0 p-[40px] pl-8">
    <!-- 标题区 -->
    <div class="mb-6 flex items-end justify-between gap-4">
      <div class="flex min-w-0 flex-1 flex-col gap-2">
        <h1 class="m-0 text-[28px] font-bold text-foreground">下载大厅</h1>
        <Progress
          v-if="downloadActive"
          :model-value="progressPercent"
          class="h-1"
        />
        <div class="flex items-center justify-between gap-2">
          <span class="truncate text-[11px] text-primary">{{ taskStatusText }}</span>
          <Button v-if="downloadActive" variant="secondary" size="sm" @click="onCancelDownload">取消下载</Button>
          <Button v-if="finishedVersion" variant="secondary" size="sm" @click="openDownloadFolder">打开文件夹</Button>
        </div>
      </div>
      <Button variant="secondary" class="mb-0.5" @click="onRefresh">
        <RotateCw :size="14" /><span>刷新</span>
      </Button>
    </div>

    <!-- TabControl：左侧竖排标签 -->
    <div class="grid min-h-0 flex-1 grid-cols-[auto_1fr] overflow-hidden rounded-2xl bg-[var(--panel-bg)]">
      <Tabs
        orientation="vertical"
        :model-value="activeTab"
        @update:model-value="switchTab"
      >
        <TabsList
          class="h-auto flex-col items-stretch gap-1 rounded-none border-r border-subtle-border bg-transparent p-3"
        >
          <TabsTrigger
            v-for="t in tabNames"
            :key="t"
            :value="t"
            class="whitespace-nowrap px-4 py-2 text-[13px] font-normal data-[state=active]:bg-accent data-[state=active]:font-semibold data-[state=active]:text-primary data-[state=active]:shadow-none"
          >
            {{ t }}
          </TabsTrigger>
        </TabsList>
      </Tabs>

      <div class="flex flex-col gap-4 overflow-y-auto px-6 pb-6 pt-5">
        <!-- ===== Minecraft 本体 ===== -->
        <template v-if="activeTab === 'Minecraft 本体'">
          <SearchBar
            v-model="versionQuery"
            placeholder="搜索版本号..."
          >
            <Select v-model="versionTypeFilter" @update:model-value="applyVersionFilter">
              <SelectTrigger class="h-8 w-auto rounded-sm border-none bg-[var(--panel-bg)] px-2 text-[11px]">
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
          <div v-if="versionPageItems.length === 0" class="my-10 flex flex-col items-center gap-2">
            <span class="text-[38px] text-muted-text">👻</span>
            <span class="text-sm text-hint-text">没有找到匹配的版本</span>
            <span class="text-[13px] text-muted-text">换个关键词或筛选条件试试吧~</span>
          </div>
          <div v-else class="flex flex-col gap-1">
            <button
              v-for="v in versionPageItems"
              :key="v.id"
              class="flex cursor-pointer items-center gap-2 rounded-md bg-muted px-3 py-3 text-left transition-colors hover:bg-accent"
              @click="downloadMinecraft(v)"
            >
              <span class="flex size-10 shrink-0 items-center justify-center rounded-sm bg-accent-deep text-lg">⛏</span>
              <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                <span class="truncate text-[15px] font-semibold text-foreground">{{ v.id }}</span>
                <span class="truncate text-[13px] text-hint-text">{{ v.type }} · {{ formatDate(v.releaseTime) }}</span>
              </div>
              <span class="mx-2 ml-1 text-muted-text">⬇</span>
            </button>
          </div>
          <div class="text-center text-[13px] text-muted-text">共 {{ versionFiltered.length }} 个版本</div>
          <Pager
            :page="versionPage"
            :total-pages="versionTotalPages"
            @prev="versionPage--"
            @next="versionPage++"
          />
        </template>

        <!-- ===== Modrinth 内容类（Mod / 整合包 / 光影包 / 材质包） ===== -->
        <template v-else-if="modrinthTabs.includes(activeTab)">
          <div class="flex items-center gap-3">
            <SearchBar v-model="contentQuery" class="flex-1" :placeholder="`搜索${activeTab}...`" />
            <Button v-if="activeTab === '整合包'" variant="secondary" class="shrink-0" @click="importLocalModpack">
              <PackageOpen :size="14" /><span>导入本地整合包</span>
            </Button>
          </div>
          <div v-if="contentState.loading" class="my-10 flex flex-col items-center gap-2">
            <span class="animate-spin text-[38px] text-muted-text">⏳</span>
            <span class="text-sm text-hint-text">正在搜索 {{ activeTab }}…</span>
          </div>
          <div v-else-if="contentPageItems.length === 0" class="my-10 flex flex-col items-center gap-2">
            <span class="text-[38px] text-muted-text">👻</span>
            <span class="text-sm text-hint-text">没有找到匹配的{{ activeTab }}</span>
            <span class="text-[13px] text-muted-text">换个关键词或筛选条件试试吧~</span>
          </div>
          <div v-else class="flex flex-col gap-1">
            <button
              v-for="p in contentPageItems"
              :key="p.project_id"
              class="flex cursor-pointer items-center gap-2 rounded-md bg-muted px-3 py-3 text-left transition-colors hover:bg-accent"
              @click="downloadContent(p)"
            >
              <img v-if="p.icon_url" class="size-10 shrink-0 rounded-sm object-cover" :src="p.icon_url" alt="" />
              <span v-else class="flex size-10 shrink-0 items-center justify-center rounded-sm text-lg" :class="tabIconClass">{{ tabGlyph }}</span>
              <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                <span class="truncate text-[15px] font-semibold text-foreground">{{ p.title }}</span>
                <span class="truncate text-[13px] text-hint-text">{{ p.description }}</span>
              </div>
              <div class="mr-2 flex flex-col items-end gap-1">
                <span class="text-[11px] text-body-text">⬇ {{ formatCount(p.downloads) }}</span>
                <span class="text-[11px] text-muted-text">♥ {{ formatCount(p.follows) }}</span>
              </div>
            </button>
          </div>
          <div class="text-center text-[13px] text-muted-text">来自 Modrinth · 共 {{ contentFiltered.length }} 个{{ activeTab }}</div>
          <Pager
            :page="contentPage"
            :total-pages="contentTotalPages"
            @prev="contentPage--"
            @next="contentPage++"
          />
        </template>

        <!-- ===== Java ===== -->
        <template v-else-if="activeTab === 'Java'">
          <div class="flex flex-col gap-4">
            <Card class="flex flex-col gap-2 rounded-2xl p-4 px-5">
              <div class="text-sm font-semibold text-foreground">版本选择建议</div>
              <div v-for="row in javaAdvice" :key="row.ver" class="grid grid-cols-[80px_1fr] gap-3">
                <span class="text-[13px] font-semibold text-primary">{{ row.ver }}</span>
                <span class="text-[13px] text-body-text">{{ row.text }}</span>
              </div>
              <div class="text-[11px] text-hint-text">下载多套 Java 不会冲突，启动器会按版本自动选择最合适的一套。</div>
            </Card>

            <Card class="flex flex-col gap-2 rounded-2xl p-4 px-5">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <div class="text-sm font-semibold text-foreground">下载 JDK</div>
                  <div class="text-[11px] text-hint-text">按当前平台自动选择安装包（zip / tar.gz），下载完成后校验并安装</div>
                </div>
                <Button variant="outline" size="sm" class="text-[11px] text-[var(--link-text-color)]" @click="loadJavaCandidates">
                  <RotateCw :size="12" /> 刷新列表
                </Button>
              </div>
              <div class="grid grid-cols-[200px_1fr] gap-4">
                <div class="flex flex-col gap-2">
                  <div class="text-[13px] font-semibold text-secondary-text">JDK 提供商</div>
                  <Select v-model="javaVendor" @update:model-value="loadJavaCandidates">
                    <SelectTrigger class="w-full rounded-sm"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="v in javaVendors" :key="v" :value="v">{{ v }}</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div class="flex flex-col gap-2">
                  <div class="text-[13px] font-semibold text-secondary-text">可用版本（实时获取）</div>
                  <div class="flex max-h-[220px] min-h-[120px] flex-col gap-2 overflow-y-auto rounded-sm bg-muted p-2">
                    <button
                      v-for="c in javaCandidates"
                      :key="c.DetailText"
                      class="flex cursor-pointer flex-col gap-0.5 rounded-sm bg-card p-2 text-left transition-opacity hover:opacity-[var(--hover-opacity)]"
                      :class="javaSelection === c ? 'ring-2 ring-ring' : ''"
                      @click="javaSelection = c"
                    >
                      <span class="text-[13px] font-semibold text-foreground">{{ c.DisplayName }}</span>
                      <span class="text-[11px] text-hint-text">{{ c.DetailText }}</span>
                    </button>
                  </div>
                  <div class="flex items-center justify-between gap-3">
                    <span class="truncate text-[13px] text-primary">{{ javaSelection ? javaSelection.DisplayName : '' }}</span>
                    <Button size="sm" :disabled="!javaSelection" @click="installJava">
                      ⬇ 下载所选
                    </Button>
                  </div>
                </div>
              </div>
              <Progress v-if="javaProgressVisible" :model-value="javaProgressPercent" class="h-1" />
              <div v-if="javaStatusText" class="truncate text-[13px] text-primary">{{ javaStatusText }}</div>
            </Card>

            <Card class="flex flex-col gap-2 rounded-2xl p-4 px-5">
              <div class="text-sm font-semibold text-foreground">已安装的 Java 运行时</div>
              <div class="text-[11px] text-hint-text">「使用此 Java」设为全局路径；「删除」移除此运行时</div>
              <div v-if="javaRuntimes.length === 0" class="text-[11px] text-hint-text">
                尚未安装自动下载的 Java 运行时，选择上方供应商与版本后开始下载。
              </div>
              <div
                v-for="rt in javaRuntimes"
                :key="rt.DirectoryPath"
                class="flex items-center gap-2 rounded-md bg-card px-3 py-2"
              >
                <span class="shrink-0 text-[13px] font-semibold text-foreground">{{ rt.MajorVersion ? `Java ${rt.MajorVersion}` : 'Java' }}</span>
                <span class="min-w-0 flex-1 truncate text-[11px] text-hint-text">{{ rt.JavaExecutablePath }}</span>
                <Button variant="outline" size="sm" class="text-[11px] text-[var(--link-text-color)]" @click="useJavaRuntime(rt)">使用此 Java</Button>
                <Button variant="outline" size="sm" class="text-[11px] text-destructive" @click="removeJavaRuntime(rt)">删除</Button>
              </div>
            </Card>
          </div>
        </template>
      </div>
    </div>

    <!-- 全屏加载遮罩（首次进入） -->
    <div v-if="loadingOverlay" class="absolute inset-0 z-10 flex flex-col items-center justify-center gap-4 bg-overlay">
      <span class="animate-spin text-[38px] text-muted-text">⏳</span>
      <span class="text-lg font-semibold text-body-text">正在加载资源…</span>
      <span class="text-[11px] text-hint-text">正在连接 Modrinth API…</span>
    </div>

    <!-- 版本下载确认遮罩（MinecraftDownloadOverlay.axaml） -->
    <ModalOverlayHost :show="mcOverlayVersion !== null">
      <MinecraftDownloadOverlay
        :version="mcOverlayVersion"
        @confirm="onMcOverlayConfirm"
        @close="mcOverlayVersion = null"
      />
    </ModalOverlayHost>

    <!-- 内容下载遮罩（ContentDownloadOverlay.axaml：版本选择 + 目标实例 + 进度） -->
    <ModalOverlayHost :show="contentOverlay !== null">
      <ContentDownloadOverlay
        v-if="contentOverlay"
        :project="contentOverlay.project"
        :kind="contentOverlay.kind"
        :local-modpack-path="contentOverlay.localPath || ''"
        @close="contentOverlay = null"
      />
    </ModalOverlayHost>
  </section>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { PackageOpen, RotateCw } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ApplyVersionFilter, CancelDownload, GetCurrentDownloadSnapshot, GetVersions, InstallJavaRuntime, QueryAvailableJavaVersions, StartDownload, StartModLoaderDownload, GetInstalledJavaRuntimes, DeleteJavaRuntime } from '../../wailsjs/go/bindings/DownloadAPI'
import { SaveJava, GetGameDirectory } from '../../wailsjs/go/bindings/ConfigAPI'
import { OpenInExplorer, OpenPath, SelectFile } from '../../wailsjs/go/bindings/SystemAPI'
import { alert, confirm as nyaConfirm } from '../composables/dialog.js'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime'
import SearchBar from '../components/pages/download/SearchBar.vue'
import Pager from '../components/pages/download/Pager.vue'
import ModalOverlayHost from '../components/overlay/ModalOverlayHost.vue'
import MinecraftDownloadOverlay from '../components/overlay/MinecraftDownloadOverlay.vue'
import ContentDownloadOverlay from '../components/overlay/ContentDownloadOverlay.vue'

const PAGE_SIZE = 50
let searchDebounce = null
const tabNames = ['Minecraft 本体', 'Mod', '整合包', '光影包', '材质包', 'Java']
const modrinthTabs = ['Mod', '整合包', '光影包', '材质包']

// Modrinth project_type 与加载器 facets（对应 ModrinthSearch.SearchAsync）
const modrinthConfig = {
  Mod: { type: 'mod', loaders: ['fabric', 'forge', 'quilt', 'neoforge'] },
  整合包: { type: 'modpack', loaders: null },
  光影包: { type: 'shader', loaders: null },
  材质包: { type: 'resourcepack', loaders: null },
}

// ---------- 标签切换 ----------
const activeTab = ref('Minecraft 本体')
const loadingOverlay = ref(true)

async function switchTab(t) {
  activeTab.value = t
  if (modrinthTabs.includes(t) && !contentCache.value[t]) {
    await searchModrinth(t, '')
  }
}

const tabGlyph = computed(() => ({ Mod: '🧩', 整合包: '📦', 光影包: '✨', 材质包: '🖼️' }[activeTab.value] ?? '📦'))
const tabIconClass = computed(() => ({ Mod: 'bg-accent-deep', 整合包: 'bg-destructive', 光影包: 'bg-success', 材质包: 'bg-badge' }[activeTab.value] ?? ''))

// ---------- Minecraft 版本 ----------
const allVersions = ref([])
const versionQuery = ref('')
const versionTypeFilter = ref('release')
const versionPage = ref(1)

const versionFiltered = ref([])
async function recomputeVersionFilter() {
  const q = versionQuery.value.trim().toLowerCase()
  let list = allVersions.value
  if (q) list = list.filter((v) => v.id.toLowerCase().includes(q))
  try {
    // 与原版一致：类型筛选走后端 DownloadAPI.ApplyVersionFilter（all/release/snapshot/old）
    if (versionTypeFilter.value !== 'all') {
      list = (await ApplyVersionFilter(list, versionTypeFilter.value)) ?? list
    }
  } catch (e) {
    console.error('版本筛选失败，回退本地筛选', e)
    list = localVersionFilter(list, versionTypeFilter.value)
  }
  versionFiltered.value = list
}
// 后端筛选不可用时的本地近似（C# VersionFilter 语义）
function localVersionFilter(list, key) {
  const map = {
    release: (v) => v.type === 'release',
    snapshot: (v) => v.type === 'snapshot',
    old: (v) => ['old_alpha', 'old_beta'].includes(v.type),
  }
  const pred = map[key]
  return pred ? list.filter(pred) : list
}
const versionTotalPages = computed(() => Math.max(1, Math.ceil(versionFiltered.value.length / PAGE_SIZE)))
const versionPageItems = computed(() =>
  versionFiltered.value.slice((versionPage.value - 1) * PAGE_SIZE, versionPage.value * PAGE_SIZE)
)
watch([versionQuery, versionTypeFilter, allVersions], () => {
  versionPage.value = 1
  recomputeVersionFilter()
})

async function loadVersions() {
  try {
    // 优先走后端清单（跟随下载源镜像），失败回退 Mojang 官方
    allVersions.value = (await GetVersions()) ?? []
  } catch (e) {
    console.error('获取版本清单失败', e)
    allVersions.value = []
  }
}

async function downloadMinecraft(v) {
  // 打开版本下载确认遮罩（MinecraftDownloadOverlay），确认后再启动下载
  mcOverlayVersion.value = v
}

// MinecraftDownloadOverlay 确认回调：原版 StartDownload；带加载器 StartModLoaderDownload
async function onMcOverlayConfirm(options) {
  const v = mcOverlayVersion.value
  mcOverlayVersion.value = null
  if (!v) return
  try {
    taskStatusText.value = `开始下载 ${options.instanceName || v.id}`
    if (options.loaderType === 0) {
      await StartDownload(v)
    } else {
      await StartModLoaderDownload(v, options.loaderVersion, options.instanceName, options.skipFabricApi)
    }
  } catch (e) {
    console.error('启动版本下载失败', e)
    alert('下载失败：' + e, { severity: 'error' })
  }
}

// ---------- Modrinth ----------
const contentQuery = ref('')
const contentPage = ref(1)
const contentCache = ref({}) // tab -> { all: [], loading: bool }

const contentState = computed(() => contentCache.value[activeTab.value] ?? { all: [], loading: false })
const contentFiltered = computed(() => {
  const q = contentQuery.value.trim().toLowerCase()
  const all = contentState.value.all
  if (!q) return all
  return all.filter(
    (p) => p.title.toLowerCase().includes(q) || (p.description ?? '').toLowerCase().includes(q)
  )
})
const contentTotalPages = computed(() => Math.max(1, Math.ceil(contentFiltered.value.length / PAGE_SIZE)))
const contentPageItems = computed(() =>
  contentFiltered.value.slice((contentPage.value - 1) * PAGE_SIZE, contentPage.value * PAGE_SIZE)
)
watch(contentQuery, () => { contentPage.value = 1 })

async function searchModrinth(tab, query) {
  const cfg = modrinthConfig[tab]
  contentCache.value = { ...contentCache.value, [tab]: { all: [], loading: true } }
  try {
    const facets = [[`project_type:${cfg.type}`]]
    if (cfg.loaders) facets.push(cfg.loaders.map((l) => `categories:${l}`))
    // 与原版一致：一次拉取前 100 条，之后客户端过滤 + 50 条/页分页
    const url =
      'https://api.modrinth.com/v2/search?limit=100&index=relevance' +
      `&query=${encodeURIComponent(query)}` +
      `&facets=${encodeURIComponent(JSON.stringify(facets))}`
    const resp = await fetch(url)
    const data = await resp.json()
    contentCache.value = { ...contentCache.value, [tab]: { all: data.hits ?? [], loading: false } }
  } catch (e) {
    console.error(`搜索 ${tab} 失败`, e)
    contentCache.value = { ...contentCache.value, [tab]: { all: [], loading: false } }
  }
}
watch(contentQuery, (q) => {
  // 原版搜索为防抖联网搜索；这里对 Modrinth 标签页做 300ms 防抖重查
  if (!modrinthTabs.includes(activeTab.value)) return
  clearTimeout(searchDebounce)
  searchDebounce = setTimeout(() => searchModrinth(activeTab.value, q.trim()), 300)
})

// ---------- 内容下载遮罩（ContentDownloadOverlay） ----------
// { project, kind: 'mod'|'modpack'|'resourcepack'|'shaderpack', localPath }
const contentOverlay = ref(null)
const mcOverlayVersion = ref(null)

const TAB_KINDS = { Mod: 'mod', 整合包: 'modpack', 光影包: 'shaderpack', 材质包: 'resourcepack' }

function downloadContent(p) {
  contentOverlay.value = { project: p, kind: TAB_KINDS[activeTab.value] ?? 'mod', localPath: '' }
}

// 整合包标签页：导入本地整合包（.mrpack / CurseForge .zip）→ ContentDownloadOverlay 安装流程
async function importLocalModpack() {
  try {
    const path = await SelectFile('选择整合包文件', '整合包', '*.mrpack;*.zip')
    if (!path) return
    contentOverlay.value = { project: null, kind: 'modpack', localPath: path }
  } catch (e) {
    console.error('选择整合包文件失败', e)
    alert('选择文件失败：' + e, { severity: 'error' })
  }
}

// ---------- 下载进度（download:progress / download:pauseChanged） ----------
const downloadActive = ref(false)
const progressPercent = ref(0)
const taskStatusText = ref('当前任务:无')

function applyDownloadSnapshot(snap) {
  if (!snap) return
  const pct = Math.min(100, snap.Percentage ?? 0)
  const running = !!snap.VersionID && pct > 0 && pct < 100
  downloadActive.value = running
  if (running) {
    progressPercent.value = pct
    taskStatusText.value = `${snap.VersionID} · ${snap.StageName ?? ''} ${snap.Detail ?? ''} ` +
      `${formatBytes(snap.CompletedBytes)}/${formatBytes(snap.TotalBytes)} · ${formatBytes(snap.BytesPerSecond)}/s`
  } else if (snap.VersionID && pct >= 100) {
    // 下载完成：记录版本号，展示「打开文件夹」入口
    finishedVersion.value = snap.VersionID
    taskStatusText.value = `${snap.VersionID} 下载完成`
  }
}

const finishedVersion = ref('')

async function openDownloadFolder() {
  const version = finishedVersion.value
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

function joinPath(dir, name) {
  if (!dir) return name
  return dir.replace(/[\\/]+$/, '') + '\\' + name
}

async function onCancelDownload() {
  try {
    await CancelDownload()
    taskStatusText.value = '已取消'
    downloadActive.value = false
  } catch (e) {
    console.error('取消下载失败', e)
  }
}

// ---------- Java ----------
const javaAdvice = [
  { ver: 'Java 8', text: '适用于 Minecraft 1.8 ~ 1.16.x（老版本必须使用 Java 8）' },
  { ver: 'Java 11', text: '适用于 1.12 ~ 1.16.x 的部分模组环境' },
  { ver: 'Java 17', text: '适用于 Minecraft 1.17 ~ 1.20.x 及常见模组' },
  { ver: 'Java 21', text: '适用于 Minecraft 1.20.5+、最新快照与 NeoForge 21.x' },
  { ver: 'Java 25', text: '适用于最新快照与未来版本（Zulu/Temurin 提供）' },
]
const javaVendors = ['Azul Zulu', 'Oracle OpenJDK', 'Eclipse Temurin'] // 顺序必须与 Go JavaVendor 枚举一致：Zulu=0 / Oracle=1 / Temurin=2
const javaVendor = ref(javaVendors[0])
const javaCandidates = ref([])
const javaSelection = ref(null)
const javaRuntimes = ref([])
const javaProgressVisible = ref(false)
const javaProgressPercent = ref(0)
const javaStatusText = ref('')

async function loadJavaCandidates() {
  javaStatusText.value = '正在获取可用版本…'
  try {
    // JavaVendor 枚举按索引传给后端（QueryAvailableJavaVersions(ctx, vendor)）
    const vendorIndex = Math.max(0, javaVendors.indexOf(javaVendor.value))
    javaCandidates.value = (await QueryAvailableJavaVersions(vendorIndex)) ?? []
    javaStatusText.value = ''
  } catch (e) {
    javaStatusText.value = `获取失败：${e}`
    console.error('获取 Java 版本失败', e)
  }
}

async function installJava() {
  if (!javaSelection.value) return
  javaProgressVisible.value = true
  javaProgressPercent.value = 0
  javaStatusText.value = `开始安装 ${javaSelection.value.DisplayName}…`
  try {
    await InstallJavaRuntime(javaSelection.value)
    javaStatusText.value = '安装完成。'
    await loadJavaRuntimes()
  } catch (e) {
    javaStatusText.value = `安装失败：${e}`
    console.error('安装 Java 失败', e)
  } finally {
    javaProgressVisible.value = false
  }
}

async function loadJavaRuntimes() {
  try {
    javaRuntimes.value = (await GetInstalledJavaRuntimes()) ?? []
  } catch (e) {
    console.error('读取已安装 Java 失败', e)
  }
}

async function useJavaRuntime(rt) {
  try {
    await SaveJava(rt.JavaExecutablePath, rt.MajorVersion ? `Java ${rt.MajorVersion}` : 'Java')
    taskStatusText.value = '已设为全局 Java'
  } catch (e) {
    console.error('设置全局 Java 失败', e)
  }
}

async function removeJavaRuntime(rt) {
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
  if (activeTab.value === 'Minecraft 本体') loadVersions()
  else if (modrinthTabs.includes(activeTab.value)) searchModrinth(activeTab.value, contentQuery.value.trim())
  else if (activeTab.value === 'Java') { loadJavaCandidates(); loadJavaRuntimes() }
}

function formatCount(n) {
  if (n >= 1e6) return (n / 1e6).toFixed(1) + 'M'
  if (n >= 1e3) return (n / 1e3).toFixed(1) + 'k'
  return String(n ?? 0)
}
function formatBytes(n) {
  if (!n) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let i = 0
  let v = n
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toFixed(1)} ${units[i]}`
}
function formatDate(t) {
  const d = t ? new Date(t) : null
  return d && !isNaN(d) ? d.toLocaleDateString() : '—'
}

let offProgress = null
onMounted(async () => {
  try {
    await Promise.all([loadVersions(), loadJavaRuntimes(), loadJavaCandidates()])
    applyDownloadSnapshot(await GetCurrentDownloadSnapshot())
  } catch (e) {
    console.error('下载页初始化失败', e)
  } finally {
    loadingOverlay.value = false
    recomputeVersionFilter()
  }
  EventsOn('download:progress', applyDownloadSnapshot)
  EventsOn('download:javaProgress', (p) => {
    if (p) {
      javaProgressVisible.value = true
      javaProgressPercent.value = Math.min(100, p.Percentage ?? 0)
      javaStatusText.value = p.Detail ?? javaStatusText.value
    }
  })
})

onUnmounted(() => {
  EventsOff('download:progress')
  EventsOff('download:javaProgress')
})
</script>
