<template>
  <!--
    组件内容渲染器：按 component id 渲染画布组件的内部内容
    （对应 Avalonia Components/*Component.axaml）。外壳（标题手柄/边框/摆放）
    由 ComponentCanvas.vue 负责，这里只管内容，等价 IComponentView.View。
    复杂组件已逐个真实化（music-player / world-launch / server-join /
    download-task-progress / skin-cape-editor），见 componentRegistry.js 状态标注。
  -->
  <div class="flex h-full min-h-0 flex-col gap-1.5 text-foreground">
    <!-- 启动游戏（GameLaunchComponent，主卡）：账号 + 版本下拉 + 启动 -->
    <template v-if="id === 'game-launch'">
      <div class="flex items-center gap-1.5">
        <select
          v-model="accountKey"
          class="h-7 min-w-0 flex-1 rounded-md border border-input bg-muted px-1.5 text-[11px] text-foreground outline-none"
        >
          <option value="" disabled>选择账号</option>
          <option v-for="a in accounts" :key="accountKeyOf(a)" :value="accountKeyOf(a)">
            {{ a.DisplayName }}（{{ typeLabel(a) }}）
          </option>
        </select>
        <select
          v-model="versionId"
          class="h-7 min-w-0 flex-1 rounded-md border border-input bg-muted px-1.5 text-[11px] text-foreground outline-none"
        >
          <option value="" disabled>选择版本</option>
          <option v-for="v in versions" :key="v" :value="v">{{ v }}</option>
        </select>
      </div>
      <button
        class="btn btn-accent btn-anim h-8 shrink-0 rounded-lg text-[12px] font-semibold"
        :disabled="!canLaunch || busy"
        @click="onLaunch"
      >
        {{ busy ? '正在启动…' : '▶ 启动游戏' }}
      </button>
      <span v-if="statusText" class="truncate text-[10px] text-hint-text">{{ statusText }}</span>
    </template>

    <!-- 账号选择（AccountSelectorComponent） -->
    <template v-else-if="id === 'account-selector'">
      <div class="flex items-center gap-1.5">
        <!-- 头像：真实皮肤头像（AccountAPI.GetAvatarUrl），失败回退首字母 -->
        <div class="flex size-7 flex-none items-center justify-center overflow-hidden rounded-md bg-badge">
          <img
            v-if="accountAvatar"
            :src="accountAvatar"
            alt=""
            class="size-full object-contain [image-rendering:pixelated]"
          />
          <span v-else class="text-xs font-bold text-primary">{{ (currentAccount?.DisplayName || '?').trim()[0]?.toUpperCase() || '?' }}</span>
        </div>
        <select
          v-model="accountKey"
          class="h-7 min-w-0 flex-1 rounded-md border border-input bg-muted px-1.5 text-[11px] text-foreground outline-none"
        >
          <option value="" disabled>选择账号</option>
          <option v-for="a in accounts" :key="accountKeyOf(a)" :value="accountKeyOf(a)">
            {{ a.DisplayName }}（{{ typeLabel(a) }}）
          </option>
        </select>
      </div>
      <span class="truncate text-[10px] text-hint-text">
        {{ currentAccount ? `当前：${currentAccount.DisplayName}` : '尚无账号' }}
      </span>
    </template>

    <!-- 游戏实例选择（GameInstanceSelectorComponent） -->
    <template v-else-if="id === 'game-instance-selector'">
      <select
        v-model="versionId"
        class="h-7 w-full rounded-md border border-input bg-muted px-1.5 text-[11px] text-foreground outline-none"
      >
        <option value="" disabled>选择游戏版本</option>
        <option v-for="v in versions" :key="v" :value="v">{{ v }}</option>
      </select>
      <span class="truncate text-[10px] text-hint-text">{{ versions.length }} 个已安装版本</span>
    </template>

    <!-- 内存使用（MemoryUsageComponent）：MonitorAPI.GetMemorySnapshot 轮询 -->
    <template v-else-if="id === 'memory-usage'">
      <div class="flex flex-1 flex-col justify-center gap-1">
        <div class="flex items-baseline justify-between">
          <span class="text-[10px] text-muted-foreground">启动器</span>
          <span class="text-[13px] font-semibold">{{ mem.launcher.toFixed(0) }} MB</span>
        </div>
        <div class="h-1.5 overflow-hidden rounded-full bg-muted">
          <div class="h-full rounded-full bg-primary transition-[width] duration-500"
            :style="{ width: launcherPct + '%' }" />
        </div>
        <div class="flex items-baseline justify-between">
          <span class="text-[10px] text-muted-foreground">JVM（{{ mem.javaCount }} 进程）</span>
          <span class="text-[13px] font-semibold">{{ mem.jvm.toFixed(0) }} MB</span>
        </div>
        <div class="h-1.5 overflow-hidden rounded-full bg-muted">
          <div class="h-full rounded-full bg-success transition-[width] duration-500"
            :style="{ width: jvmPct + '%' }" />
        </div>
      </div>
    </template>

    <!-- 版本管理（VersionManagerComponent，lite）：当前版本 + 跳转 -->
    <template v-else-if="id === 'version-manager'">
      <div class="flex flex-1 items-center gap-2">
        <span class="truncate text-[12px] font-semibold">{{ versionId || '未选择版本' }}</span>
        <Button variant="secondary" size="sm" class="ml-auto h-7 shrink-0 text-[11px]"
          @click="router.push('/versions')">管理</Button>
      </div>
    </template>

    <!-- 时钟（前端补充组件） -->
    <template v-else-if="id === 'clock'">
      <div class="flex flex-1 flex-col items-center justify-center leading-tight">
        <span class="text-[22px] font-semibold tabular-nums">{{ clockTime }}</span>
        <span class="text-[10px] text-hint-text">{{ clockDate }}</span>
      </div>
    </template>

    <!-- 文本便签（前端补充组件；内容 localStorage 持久化） -->
    <template v-else-if="id === 'text'">
      <textarea
        v-model="textValue"
        placeholder="双击输入文字…"
        class="h-full w-full resize-none rounded-md border border-input bg-muted p-1.5 text-[11px] text-foreground outline-none"
        @pointerdown.stop
      />
    </template>

    <!-- 音乐控制（MusicPlayerComponent）：MusicAPI 播控 + audio.js 进度桥 -->
    <template v-else-if="id === 'music-player'">
      <div class="flex min-h-0 flex-1 flex-col justify-center gap-1.5">
        <span class="truncate text-center text-[11px] font-semibold" :title="music.trackName">
          {{ music.trackName || '未在播放' }}
        </span>
        <div class="group relative h-1.5 shrink-0 cursor-pointer overflow-hidden rounded-full bg-muted"
          title="点击跳转进度"
          @pointerdown.stop
          @click="seekMusic($event)"
        >
          <div class="h-full rounded-full bg-primary transition-[width] duration-200"
            :style="{ width: musicPct + '%' }" />
        </div>
        <div class="flex items-center justify-between">
          <span class="font-mono text-[9px] text-hint-text tabular-nums">
            {{ fmtMs(audioState.positionMs) }} / {{ fmtMs(audioState.durationMs) }}
          </span>
          <div class="flex items-center gap-1">
            <button class="grid size-6 cursor-pointer place-items-center rounded-md text-[12px] hover:bg-accent"
              title="上一首" @pointerdown.stop @click="musicPrev">⏮</button>
            <button class="grid size-6 cursor-pointer place-items-center rounded-md text-[12px] hover:bg-accent"
              :title="musicPlaying ? '暂停' : '播放'" @pointerdown.stop @click="musicToggle">
              {{ musicPlaying ? '⏸' : '▶' }}
            </button>
            <button class="grid size-6 cursor-pointer place-items-center rounded-md text-[12px] hover:bg-accent"
              title="下一首" @pointerdown.stop @click="musicNext">⏭</button>
          </div>
        </div>
        <button class="cursor-pointer self-center text-[9px] text-hint-text hover:text-primary"
          @pointerdown.stop @click="router.push('/music')">打开音乐播放器 →</button>
      </div>
    </template>

    <!-- 最近的世界（WorldLaunchComponent）：WorldAPI.GetRecentWorlds + 点击启动所属实例 -->
    <template v-else-if="id === 'world-launch'">
      <div v-if="worlds.length === 0" class="flex flex-1 items-center justify-center text-[10px] text-hint-text">
        {{ worldError || '还没有世界存档' }}
      </div>
      <div v-else class="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto" @pointerdown.stop>
        <button
          v-for="w in worlds" :key="w.DirectoryPath"
          class="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-md px-1.5 py-1 text-left hover:bg-accent"
          :disabled="worldLaunching"
          :title="`${w.Name}（${w.OwnerVersionId}）`"
          @click="launchWorld(w)"
        >
          <span class="text-[13px]">🌍</span>
          <span class="min-w-0 flex-1">
            <span class="block truncate text-[11px] font-medium">{{ w.Name }}</span>
            <span class="block truncate text-[9px] text-hint-text">{{ w.OwnerVersionId }}</span>
          </span>
          <span class="shrink-0 text-[9px] text-hint-text">
            {{ worldLaunching === w.DirectoryPath ? '启动中…' : '▶' }}
          </span>
        </button>
      </div>
    </template>

    <!-- 服务器快连（ServerJoinComponent）：ServerAPI.PingServer 延迟/人数监控 -->
    <template v-else-if="id === 'server-join'">
      <div class="flex min-h-0 flex-1 flex-col justify-center gap-1.5" @pointerdown.stop>
        <div class="flex items-center gap-1.5">
          <input
            v-model="server.address"
            placeholder="服务器地址，如 mc.example.com"
            class="h-7 min-w-0 flex-1 rounded-md border border-input bg-muted px-1.5 text-[11px] text-foreground outline-none"
            @keydown.enter="pingServer"
          >
          <Button variant="secondary" size="sm" class="h-7 shrink-0 px-2 text-[10px]" :disabled="server.pinging"
            @click="pingServer">{{ server.pinging ? '…' : 'Ping' }}</Button>
        </div>
        <div class="flex items-center justify-between gap-2">
          <span class="truncate text-[10px]" :class="server.error ? 'text-destructive' : 'text-hint-text'">
            {{ server.error || server.summary || '输入地址后 Ping 查看延迟与人数' }}
          </span>
          <!-- PORTING_NOTE: LauncherAPI.Launch 暂无进服参数（server 参数未暴露），先做 Ping 监控 +
               普通启动；后端补齐 --server 参数后在此直连进服。 -->
          <button class="btn btn-accent btn-anim h-6 shrink-0 rounded-md px-2.5 text-[10px] font-semibold"
            :disabled="server.busy" @click="launchForServer">
            {{ server.busy ? '启动中…' : '启动游戏' }}
          </button>
        </div>
      </div>
    </template>

    <!-- 下载任务（DownloadTaskProgressComponent）：快照 + download:progress 事件 -->
    <template v-else-if="id === 'download-task-progress'">
      <template v-if="dl.active">
        <div class="flex flex-1 flex-col justify-center gap-1">
          <span class="truncate text-[11px] font-semibold">{{ dl.stageName || '下载中' }}</span>
          <span class="truncate text-[9px] text-hint-text">{{ dl.detail }}</span>
          <div class="h-1.5 overflow-hidden rounded-full bg-muted">
            <div class="h-full rounded-full bg-primary transition-[width] duration-300"
              :style="{ width: Math.min(100, Math.max(0, dl.percentage)) + '%' }" />
          </div>
          <div class="flex items-baseline justify-between">
            <span class="text-[9px] text-hint-text">{{ dl.fileText }}</span>
            <span class="text-[13px] font-semibold tabular-nums">{{ dl.percentage.toFixed(0) }}%</span>
          </div>
        </div>
      </template>
      <div v-else class="flex flex-1 flex-col items-center justify-center gap-0.5">
        <span class="text-[11px] text-muted-foreground">{{ dl.doneText || '暂无下载任务' }}</span>
        <button class="cursor-pointer text-[9px] text-hint-text hover:text-primary"
          @pointerdown.stop @click="router.push('/download')">去资源下载 →</button>
      </div>
    </template>

    <!-- 皮肤与披风（SkinCapeEditorComponent，lite）：当前账号 + 跳转账号管理 -->
    <template v-else-if="id === 'skin-cape-editor'">
      <div class="flex flex-1 flex-col items-center justify-center gap-1">
        <div class="grid size-9 place-items-center overflow-hidden rounded-lg bg-muted text-[16px]">🧑‍🎨</div>
        <span class="max-w-full truncate px-1 text-[11px] font-semibold">
          {{ currentAccount?.DisplayName || '未登录账号' }}
        </span>
        <!-- PORTING_NOTE: 皮肤渲染（MinecraftProfileService）待后端补齐；先做账号管理快捷卡 -->
        <Button variant="link" size="sm" class="h-5 text-[10px]"
          @click="router.push('/settings/account')">管理账号与皮肤 →</Button>
      </div>
    </template>

    <!-- 兜底占位卡 -->
    <template v-else>
      <div class="flex flex-1 flex-col items-center justify-center gap-1">
        <span class="text-[11px] font-semibold text-muted-foreground">{{ title }}</span>
        <span class="px-2 text-center text-[10px] text-hint-text">该组件待移植。</span>
      </div>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Button } from '@/components/ui/button'
import {
  GetAccounts, GetSelectedAccount, GetAccountStableKey, SelectAccountByStableKey, GetAvatarUrl,
} from '../../../../wailsjs/go/bindings/AccountAPI'
import { GetCurrentInstanceSnapshot, SelectInstance } from '../../../../wailsjs/go/bindings/InstanceAPI'
import { GetLaunchSnapshot, Launch } from '../../../../wailsjs/go/bindings/LauncherAPI'
import { GetMemorySnapshot } from '../../../../wailsjs/go/bindings/MonitorAPI'
import {
  GetCurrentTrack, NextTrack, PausePlayback, PreviousTrack, ResumePlayback, SeekPlayback,
} from '../../../../wailsjs/go/bindings/MusicAPI'
import { GetRecentWorlds } from '../../../../wailsjs/go/bindings/WorldAPI'
import { PingServer } from '../../../../wailsjs/go/bindings/ServerAPI'
import { GetCurrentDownloadSnapshot } from '../../../../wailsjs/go/bindings/DownloadAPI'
import { audioState } from '@/composables/audio'
import { EventsOn, EventsOff } from '../../../../wailsjs/runtime/runtime'

const props = defineProps({
  id: { type: String, required: true },
  title: { type: String, default: '' },
})

const router = useRouter()

// ---- 账号 / 版本共享状态（account-selector / game-instance-selector /
//      game-launch 共用同一份选中值，与原版全局选中语义一致） ----
const accounts = ref([])
const currentAccount = ref(null)
const versions = ref([])
const versionId = ref('')
const accountKey = computed({
  get: () => (currentAccount.value ? accountKeyOf(currentAccount.value) : ''),
  set: (k) => selectAccount(k),
})

function accountKeyOf(a) {
  return `${a.Type}:${a.DisplayName}`
}
function typeLabel(a) {
  return { offline: '离线', microsoft: '正版', authlib: '外置登录' }[a.Type] ?? a.Type
}

// 账号头像（GetAvatarUrl 返回 8×8 头部 data URI；失败保留首字母占位）
const accountAvatar = ref('')

watch(currentAccount, (account) => {
  accountAvatar.value = ''
  if (!account) return
  GetAccountStableKey(account)
    .then((key) => GetAvatarUrl(key))
    .then((uri) => { if (uri) accountAvatar.value = uri })
    .catch(() => { /* 回退首字母占位 */ })
})

async function loadAccounts() {
  try {
    accounts.value = (await GetAccounts()) ?? []
    currentAccount.value = await GetSelectedAccount().catch(() => null)
  } catch (e) {
    console.error('[canvas] 加载账号失败', e)
  }
}

async function selectAccount(key) {
  const found = accounts.value.find((a) => accountKeyOf(a) === key)
  if (!found) return
  currentAccount.value = found
  try {
    const stable = await GetAccountStableKey(found)
    await SelectAccountByStableKey(stable)
  } catch (e) {
    console.error('[canvas] 切换账号失败', e)
  }
}

function applySnapshot(snap) {
  if (!snap) return
  versions.value = snap.VersionIds ?? []
  if (snap.SelectedVersionId) versionId.value = snap.SelectedVersionId
}

watch(versionId, async (v) => {
  if (!v) return
  try {
    await SelectInstance(v)
  } catch (e) {
    console.error('[canvas] 切换版本失败', e)
  }
})

// ---- game-launch 启动 ----
const busy = ref(false)
const statusText = ref('')
const canLaunch = computed(() => !!currentAccount.value && !!versionId.value)

async function onLaunch() {
  busy.value = true
  statusText.value = '正在启动…'
  try {
    const result = await Launch('', null) // ctx 占位（同 LaunchPanel 的移植约定）
    statusText.value = result?.Success ? '游戏进程已创建。' : result?.Message || '启动失败。'
  } catch (e) {
    statusText.value = `启动失败：${e}`
  } finally {
    busy.value = false
  }
}

// ---- memory-usage：2s 轮询（原版 DispatcherTimer 间隔 2s） ----
const mem = ref({ launcher: 0, jvm: 0, javaCount: 0 })
const launcherPct = computed(() => Math.min(100, (mem.value.launcher / 1024) * 100))
const jvmPct = computed(() => Math.min(100, (mem.value.jvm / 4096) * 100))
let memTimer = null
async function pollMemory() {
  try {
    const s = await GetMemorySnapshot()
    if (s) mem.value = { launcher: s.LauncherMemoryMb ?? 0, jvm: s.JvmMemoryMb ?? 0, javaCount: s.JavaProcessCount ?? 0 }
  } catch { /* 桥未启动时静默 */ }
}

// ---- clock ----
const now = ref(new Date())
let clockTimer = null
const clockTime = computed(() =>
  now.value.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }))
const clockDate = computed(() =>
  now.value.toLocaleDateString('zh-CN', { month: 'long', day: 'numeric', weekday: 'long' }))

// ---- text（localStorage 持久化） ----
const textValue = ref(localStorage.getItem(`canvas.text.${props.id}`) ?? '')
watch(textValue, (v) => localStorage.setItem(`canvas.text.${props.id}`, v ?? ''))

// ---- 占位提示（历史遗留，兜底分支使用） ----
const placeholderHint = computed(() => '该组件待移植。')

// ---- music-player：播控 + 曲目 + 进度（MusicAPI + composables/audio 桥） ----
const music = ref({ trackName: '' })
const musicPlaying = computed(() => audioState.playing)
const musicPct = computed(() =>
  audioState.durationMs > 0 ? Math.min(100, (audioState.positionMs / audioState.durationMs) * 100) : 0)

function trackNameOf(track) {
  const p = String(track?.FilePath ?? '')
  const base = p.split(/[\\/]/).pop() ?? ''
  return base.replace(/\.[^.]+$/, '')
}

async function loadCurrentTrack() {
  try {
    music.value.trackName = trackNameOf(await GetCurrentTrack())
  } catch { /* 桥未启动时静默 */ }
}

function musicToggle() {
  const fn = audioState.playing ? PausePlayback : ResumePlayback
  fn().catch(() => {})
}
function musicPrev() { PreviousTrack().catch(() => {}) }
function musicNext() { NextTrack().catch(() => {}) }

function seekMusic(e) {
  if (audioState.durationMs <= 0) return
  const rect = e.currentTarget.getBoundingClientRect()
  const ratio = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width))
  const ms = Math.round(ratio * audioState.durationMs)
  SeekPlayback(ms).catch(() => {})
  audioState.positionMs = ms // audio.js 桥的 <audio> 会由 music:seek 同步
}

function fmtMs(ms) {
  if (!Number.isFinite(ms) || ms <= 0) return '0:00'
  const total = Math.floor(ms / 1000)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

function onMusicTrackChanged(track) {
  music.value.trackName = track ? trackNameOf(track) : ''
}

// ---- world-launch：最近世界 + 点击启动所属实例 ----
const worlds = ref([])
const worldError = ref('')
const worldLaunching = ref('')

async function loadWorlds() {
  try {
    worlds.value = (await GetRecentWorlds(8)) ?? []
  } catch (e) {
    worldError.value = '世界列表不可用'
    console.error('[canvas] 加载最近世界失败', e)
  }
}

async function launchWorld(w) {
  if (worldLaunching.value) return
  worldLaunching.value = w.DirectoryPath
  try {
    if (w.OwnerVersionId) await SelectInstance(w.OwnerVersionId)
    const result = await Launch('', null)
    if (!result?.Success) console.error('[canvas] 启动世界失败', result?.Message)
  } catch (e) {
    console.error('[canvas] 启动世界失败', e)
  } finally {
    worldLaunching.value = ''
  }
}

// ---- server-join：地址 Ping 监控（延迟为前端计时；人数取自状态） ----
const server = reactive({
  address: localStorage.getItem('canvas.server.address') ?? '',
  pinging: false,
  busy: false,
  error: '',
  summary: '',
  latencyMs: 0,
})
watch(() => server.address, (v) => localStorage.setItem('canvas.server.address', v ?? ''))

async function pingServer() {
  const addr = server.address.trim()
  if (!addr || server.pinging) return
  server.pinging = true
  server.error = ''
  try {
    const started = performance.now()
    const status = await PingServer(addr, 5000)
    server.latencyMs = Math.round(performance.now() - started)
    if (!status) {
      server.summary = ''
      server.error = '无法连接到服务器'
    } else {
      server.summary = `${server.latencyMs}ms · ${status.OnlinePlayers ?? 0}/${status.MaxPlayers ?? 0} 在线`
        + (status.VersionName ? ` · ${status.VersionName}` : '')
    }
  } catch (e) {
    server.summary = ''
    server.error = `Ping 失败：${e?.message ?? e}`
  } finally {
    server.pinging = false
  }
}

async function launchForServer() {
  server.busy = true
  try {
    await Launch('', null)
  } catch (e) {
    console.error('[canvas] 启动失败', e)
  } finally {
    server.busy = false
  }
}

// ---- download-task-progress：快照 + download:progress 事件 ----
const DL_PHASE_TEXTS = ['空闲', '准备中', '下载中', '已完成', '失败', '已取消']
const dl = ref({
  active: false,
  stageName: '',
  detail: '',
  percentage: 0,
  fileText: '',
  doneText: '',
})

function fmtBytes(n) {
  if (!Number.isFinite(n) || n <= 0) return ''
  const units = ['B', 'KB', 'MB', 'GB']
  let v = n
  let i = 0
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toFixed(v >= 100 || i === 0 ? 0 : 1)} ${units[i]}`
}

function applyDownloadSnapshot(snap) {
  if (!snap || (snap.Phase ?? 0) === 0) {
    dl.value.active = false
    dl.value.doneText = DL_PHASE_TEXTS[snap?.Phase] && snap?.Phase > 0
      ? `上次任务：${DL_PHASE_TEXTS[snap.Phase]}`
      : ''
    return
  }
  dl.value.active = snap.Phase === 1 || snap.Phase === 2
  dl.value.stageName = `${snap.VersionID || ''} ${snap.StageName || DL_PHASE_TEXTS[snap.Phase] || ''}`.trim()
  dl.value.detail = snap.Detail || ''
  dl.value.percentage = snap.Percentage ?? 0
  const files = (snap.TotalFiles ?? 0) > 0 ? ` ${snap.CompletedFiles}/${snap.TotalFiles} 文件 ·` : ''
  dl.value.fileText = `${files} ${fmtBytes(snap.CompletedBytes)} / ${fmtBytes(snap.TotalBytes)} · ${fmtBytes(snap.BytesPerSecond)}/s`.trim()
}

async function loadDownloadSnapshot() {
  try {
    applyDownloadSnapshot(await GetCurrentDownloadSnapshot())
  } catch { /* 桥未启动时静默 */ }
}

function onDownloadProgress(snap) { applyDownloadSnapshot(snap) }

function onInstanceChanged(snap) { applySnapshot(snap) }

onMounted(async () => {
  await loadAccounts()
  try {
    applySnapshot(await GetCurrentInstanceSnapshot())
    const snap = await GetLaunchSnapshot()
    if (snap) statusText.value = snap.Message || ''
  } catch (e) {
    console.error('[canvas] 初始化组件数据失败', e)
  }
  loadWorlds()
  loadCurrentTrack()
  loadDownloadSnapshot()
  EventsOn('instance:changed', onInstanceChanged)
  EventsOn('music:trackChanged', onMusicTrackChanged)
  EventsOn('download:progress', onDownloadProgress)
  clockTimer = setInterval(() => { now.value = new Date() }, 1000)
  memTimer = setInterval(pollMemory, 2000)
  pollMemory()
})

onUnmounted(() => {
  EventsOff('instance:changed')
  EventsOff('music:trackChanged')
  EventsOff('download:progress')
  if (clockTimer) clearInterval(clockTimer)
  if (memTimer) clearInterval(memTimer)
})
</script>
