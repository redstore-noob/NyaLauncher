<template>
  <!-- 音乐播放器页（MusicPlayerPage.axaml：顶栏 / 搜索+列表 / 底部控制栏 三行布局） -->
  <section class="grid h-full grid-rows-[auto_1fr_auto] bg-background">

    <!-- 顶栏 -->
    <header class="flex items-center gap-3 border-b border-subtle-border bg-background px-6 py-3.5">
      <div class="mr-auto flex min-w-0 flex-1 items-center gap-3">
        <div class="flex size-10 flex-none items-center justify-center rounded-[10px] bg-primary text-primary-foreground">
          <Icon name="music-note" :size="20" />
        </div>
        <div class="flex min-w-0 flex-col gap-0.5">
          <span class="text-lg font-bold text-foreground">音乐播放器</span>
          <span class="max-w-[380px] overflow-hidden text-[11px] text-hint-text text-ellipsis whitespace-nowrap">{{ trackCountText }}</span>
        </div>
      </div>

      <Button variant="outline" size="sm" class="border-emphasized-border" @click="selectFolder">选择文件夹</Button>
      <Button variant="secondary" size="sm" @click="refreshLibrary">刷新</Button>
      <Select v-model="sortMode" @update:model-value="onSortChanged">
        <SelectTrigger class="h-auto w-[130px] rounded-lg px-2.5 py-1 text-xs"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in SORT_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
        </SelectContent>
      </Select>
    </header>

    <!-- 搜索 + 列表 -->
    <div class="grid min-h-0 grid-rows-[auto_1fr] px-6 pt-4">
      <div class="relative mb-3">
        <span class="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-subtext-text"><Icon name="magnify" :size="16" /></span>
        <Input v-model="keyword" placeholder="搜索歌曲..." class="h-auto rounded-[10px] bg-muted py-2.5 pl-10 border-transparent focus-visible:border-ring" @input="applyFilter" />
      </div>

      <div class="relative min-h-0 overflow-hidden rounded-xl bg-card">
        <div class="h-full overflow-y-auto p-1">
          <div
            v-for="track in displayTracks"
            :key="track.FilePath"
            class="track-tile interactive mx-1 my-[3px] flex cursor-pointer items-center gap-3 rounded-md px-2.5 py-1.5 transition-colors"
            :class="[isCurrent(track) ? 'bg-accent current' : 'hover:bg-accent/60']"
            @click="onTrackClick(track)"
          >
            <div
              class="flex size-[38px] flex-none items-center justify-center rounded-md"
              :class="isCurrent(track) ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground opacity-75'"
            >
              <Icon v-if="!isCurrent(track)" name="music-note" :size="17" />
              <!-- 均衡器动画（播放中） -->
              <span v-else class="flex items-end justify-center gap-[3px] pb-[9px]">
                <span class="eq-bar eq-bar-1" :class="{ 'eq-on': isCurrent(track) && isPlaying }" />
                <span class="eq-bar eq-bar-2" :class="{ 'eq-on': isCurrent(track) && isPlaying }" />
                <span class="eq-bar eq-bar-3" :class="{ 'eq-on': isCurrent(track) && isPlaying }" />
              </span>
            </div>

            <div class="flex min-w-0 flex-1 flex-col gap-0.5">
              <span class="overflow-hidden text-[13px] font-semibold text-ellipsis whitespace-nowrap" :class="isCurrent(track) ? 'text-primary' : 'text-foreground'">{{ trackTitle(track) }}</span>
              <span class="text-[11px] text-hint-text">{{ trackMeta(track) }}</span>
            </div>

            <span v-if="isCurrent(track)" class="flex-none text-primary"><Icon name="volume-high" :size="14" /></span>
          </div>
        </div>

        <!-- 空态 -->
        <div v-if="displayTracks.length === 0" class="absolute inset-0 flex flex-col items-center justify-center gap-2.5 text-center text-subtext-text">
          <Icon name="music-note" :size="52" />
          <span class="text-[15px] font-semibold text-muted-foreground">{{ library.length > 0 ? '没有匹配的歌曲' : '还没有歌曲' }}</span>
          <span class="text-[11px] text-hint-text">{{ library.length > 0 ? '试试其他关键词，或点击上方「刷新」重新扫描' : '选择一个文件夹，扫描里面的本地音乐' }}</span>
          <Button v-if="library.length === 0" size="sm" class="h-auto rounded-lg px-[18px] py-2" @click="selectFolder">选择文件夹</Button>
        </div>
      </div>
    </div>

    <!-- 底部播放控制栏 -->
    <footer class="grid gap-3 border-t border-subtle-border bg-background px-6 pb-[18px] pt-3.5">
      <!-- 进度条 + 时间 -->
      <div class="flex items-center gap-3">
        <span class="min-w-[38px] text-center font-mono text-[11px] text-muted-foreground">{{ formatTime(positionSec) }}</span>
        <Slider
          v-model="progressValue"
          class="flex-1"
          :min="0"
          :max="progressMax"
          :step="0.1"
          :disabled="!progressEnabled"
          @pointer-down="userDragging = true"
          @pointer-up="onProgressCommit"
          @pointercancel="userDragging = false"
          @value-commit="onProgressCommit"
        />
        <span class="min-w-[38px] text-right font-mono text-[11px] text-muted-foreground">{{ durationSec > 0 ? formatTime(durationSec) : '--:--' }}</span>
      </div>

      <!-- 控制按钮 + 曲目信息 + 音量 -->
      <div class="flex items-center gap-4">
        <div class="flex flex-none items-center gap-2">
          <Tooltip :delay-duration="300">
            <TooltipTrigger as-child>
              <Button variant="secondary" size="icon" class="rounded-full active:scale-[0.88]" @click="previous"><Icon name="skip-previous" :size="16" /></Button>
            </TooltipTrigger>
            <TooltipContent>上一首</TooltipContent>
          </Tooltip>
          <Tooltip :delay-duration="300">
            <TooltipTrigger as-child>
              <Button size="icon" class="size-[46px] rounded-full active:scale-[0.88]" :title="isPlaying ? '暂停' : '播放'" @click="togglePlayPause">
                <Icon :name="isPlaying ? 'pause' : 'play'" :size="19" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{{ isPlaying ? '暂停' : '播放' }}</TooltipContent>
          </Tooltip>
          <Tooltip :delay-duration="300">
            <TooltipTrigger as-child>
              <Button variant="secondary" size="icon" class="rounded-full active:scale-[0.88]" @click="next"><Icon name="fast-forward" :size="16" /></Button>
            </TooltipTrigger>
            <TooltipContent>下一首</TooltipContent>
          </Tooltip>
          <Tooltip :delay-duration="300">
            <TooltipTrigger as-child>
              <Button variant="secondary" size="icon" class="rounded-full active:scale-[0.88]" @click="stop"><Icon name="stop" :size="15" /></Button>
            </TooltipTrigger>
            <TooltipContent>停止</TooltipContent>
          </Tooltip>
          <!-- 模式循环按钮：点击切换，Tooltip 显示当前模式 -->
          <Tooltip :delay-duration="300">
            <TooltipTrigger as-child>
              <Button
                variant="ghost"
                size="icon"
                class="relative rounded-full active:scale-[0.88]"
                :class="playbackMode !== 'Sequential' ? 'text-primary' : ''"
                @click="cycleMode"
              >
                <Icon :name="modeIcon" :size="16" />
                <span v-if="playbackMode !== 'Sequential'" class="absolute bottom-px right-px size-[9px] rounded-full bg-primary" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{{ modeTip }}（点击切换）</TooltipContent>
          </Tooltip>
        </div>

        <!-- 当前曲目信息 + 封面徽章 -->
        <div class="flex min-w-0 flex-1 items-center gap-3">
          <div
            class="relative flex size-[46px] flex-none items-center justify-center overflow-hidden rounded-xl bg-primary text-primary-foreground"
            :class="{ 'nya-cover-breathing': isPlaying }"
          >
            <span class="absolute inset-0 bg-white/15" />
            <Icon name="music-note" :size="22" />
          </div>
          <div class="flex min-w-0 flex-col gap-0.5">
            <span class="overflow-hidden text-sm font-semibold text-foreground text-ellipsis whitespace-nowrap">{{ nowTitle }}</span>
            <span class="overflow-hidden text-[11px] text-hint-text text-ellipsis whitespace-nowrap">{{ nowInfo }}</span>
          </div>
        </div>

        <!-- 音量 -->
        <div class="flex items-center gap-2">
          <span class="text-muted-foreground"><Icon :name="volumeIcon" :size="16" /></span>
          <Slider v-model="volume" class="w-[100px]" :min="0" :max="100" :step="1" @value-commit="onVolumeChanged" />
          <span class="min-w-[35px] text-[11px] text-muted-foreground">{{ volume }}%</span>
        </div>
      </div>
    </footer>
  </section>
</template>

<script setup>
/*
 * 本地音乐播放页（MusicPlayerPage.axaml + .cs 移植）。
 * 曲库扫描/排序/搜索走 MusicAPI；播放控制调 PlayTrack/Pause 等后端方法，
 * 实际发声由 composables/audio.js 的 Web Audio 桥完成（监听 music:play 等事件）。
 *
 * 差异：曲目播放经 composables/audio.js 的 Web Audio 桥完成（/localfile 流式加载）。
 */
import { computed, onMounted, onUnmounted, ref } from 'vue'
import Icon from '../components/overlay/Icon.vue'
import { SelectDirectory } from '../../wailsjs/go/bindings/SystemAPI.js'
import { alert } from '../composables/dialog.js'
import { audioState } from '../composables/audio.js'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Slider } from '@/components/ui/slider'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  GetMusicFolderPath, SetMusicFolder, ScanMusicLibrary, GetMusicTracks,
  SearchMusicTracks, GetSortedMusicTracks, GetMusicSortMode, SetMusicSortMode,
  GetMusicVolume, SetMusicVolume, GetMusicPlaybackMode, SetMusicPlaybackMode,
  PlayTrack, PausePlayback, ResumePlayback, StopPlayback, NextTrack, PreviousTrack,
  GetPlaylist, SetPlaylist, SeekPlayback,
} from '../../wailsjs/go/bindings/MusicAPI.js'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime.js'

const SORT_OPTIONS = [
  { value: 'FileName', label: '文件名 A-Z' },
  { value: 'FileNameDesc', label: '文件名 Z-A' },
  { value: 'DateModified', label: '修改时间 ↑' },
  { value: 'DateModifiedDesc', label: '修改时间 ↓' },
  { value: 'FileSize', label: '文件大小 ↑' },
  { value: 'FileSizeDesc', label: '文件大小 ↓' },
]
const MODE_CYCLE = ['Sequential', 'RepeatAll', 'RepeatOne', 'Shuffle']
const MODE_META = {
  Sequential: { icon: 'playlist-play', tip: '顺序播放' },
  RepeatAll: { icon: 'repeat', tip: '列表循环' },
  RepeatOne: { icon: 'repeat-one', tip: '单曲循环' },
  Shuffle: { icon: 'shuffle', tip: '随机播放' },
}

const library = ref([])        // 完整曲库（共享播放列表）
const displayTracks = ref([])  // 搜索过滤后的展示列表
const keyword = ref('')
const sortMode = ref('FileName')
const folderPath = ref('')
const playbackMode = ref('Sequential')
const volume = ref(80)
const currentTrack = ref(null)
const playbackState = ref('Stopped')

const userDragging = ref(false)
const progressValue = ref(0)
let lastSeekedValue = -1

// --- Web Audio 桥状态 ---
const isPlaying = computed(() => audioState.playing)
const positionSec = computed(() => {
  // 拖拽中显示目标值，其余跟随音频
  return userDragging.value ? progressValue.value : audioState.positionMs / 1000
})
const durationSec = computed(() => audioState.durationMs / 1000)

const progressEnabled = computed(() =>
  playbackState.value !== 'Stopped' && durationSec.value > 0 && audioState.canPlay)
const progressMax = computed(() => (durationSec.value > 0 ? durationSec.value : 100))

const trackCountText = computed(() => {
  if (library.value.length > 0) return `${library.value.length} 首歌曲 · ${folderPath.value}`
  if (folderPath.value) return `未找到音频文件 · ${folderPath.value}`
  return '请点击「选择文件夹」加载音乐'
})

const nowTitle = computed(() => {
  if (playbackState.value === 'Stopped' && !currentTrack.value) return '未在播放'
  if (playbackState.value === 'Stopped' && !audioState.canPlay && audioState.error) return '播放失败'
  return currentTrack.value ? trackTitle(currentTrack.value) : '未在播放'
})
const nowInfo = computed(() => {
  if (audioState.error && playbackState.value !== 'Playing' && playbackState.value !== 'Paused') {
    return audioState.error
  }
  if (audioState.error && !audioState.canPlay && currentTrack.value) return '暂不可播：无法读取本地音频文件'
  return currentTrack.value ? trackMeta(currentTrack.value) : ''
})

const modeIcon = computed(() => MODE_META[playbackMode.value]?.icon ?? 'playlist-play')
const modeTip = computed(() => MODE_META[playbackMode.value]?.tip ?? '顺序播放')
const volumeIcon = computed(() =>
  volume.value === 0 ? 'volume-off' : volume.value < 50 ? 'volume-low' : 'volume-high')

// ------------------------------------------------------------------
// 曲目显示工具（对应 music.MusicTrack.Title / MetaDisplay）
// ------------------------------------------------------------------

function trackTitle(track) {
  const base = track.FilePath.split(/[\\/]/).pop() ?? ''
  return base.replace(/\.[^.]+$/, '')
}

function trackMeta(track) {
  const ext = (track.FilePath.match(/\.([^.\\/]+)$/)?.[1] ?? '').toLowerCase()
  const size = track.FileSize >= 1048576
    ? `${(track.FileSize / 1048576).toFixed(1)} MB`
    : track.FileSize >= 1024
      ? `${Math.round(track.FileSize / 1024)} KB`
      : `${track.FileSize} B`
  return `${ext} · ${size}`
}

function isCurrent(track) {
  return !!currentTrack.value && track.FilePath === currentTrack.value.FilePath
}

function formatTime(seconds) {
  if (!Number.isFinite(seconds) || seconds <= 0) return '0:00'
  const total = Math.floor(seconds)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

// ------------------------------------------------------------------
// 曲库加载 / 排序 / 搜索
// ------------------------------------------------------------------

async function refreshLibrary() {
  try {
    ScanMusicLibrary()
    folderPath.value = await GetMusicFolderPath()
    sortMode.value = await GetMusicSortMode()
    library.value = (await GetMusicTracks()) ?? []
    displayTracks.value = (await GetSortedMusicTracks(sortMode.value)) ?? []
    // 共享播放列表 = 完整（未过滤）曲目列表，供自动切歌/上下一首使用
    SetPlaylist([...library.value])
  } catch (ex) {
    console.error('扫描曲库失败', ex)
  }
}

async function selectFolder() {
  let path = ''
  try {
    path = await SelectDirectory('选择音乐文件夹')
  } catch { /* 用户取消或选择失败 */ }
  if (!path) return
  try {
    await SetMusicFolder(path)
    await refreshLibrary()
  } catch (ex) {
    alert(`选择文件夹失败：${ex?.message ?? ex}`, { severity: 'error' })
  }
}

async function onSortChanged() {
  if (!sortMode.value) return
  SetMusicSortMode(sortMode.value)
  displayTracks.value = (await GetSortedMusicTracks(sortMode.value)) ?? []
  SetPlaylist([...library.value])
}

async function applyFilter() {
  if (!keyword.value.trim()) {
    displayTracks.value = (await GetSortedMusicTracks(sortMode.value)) ?? []
    return
  }
  displayTracks.value = (await SearchMusicTracks(keyword.value)) ?? []
}

// ------------------------------------------------------------------
// 播放控制（转给 Go 状态机，发声经 audio 桥）
// ------------------------------------------------------------------

async function onTrackClick(track) {
  // 曲目已在播放时不再重启（避免自动切歌后被打回 0:00）
  if (currentTrack.value && playbackState.value !== 'Stopped' && isCurrent(track)) return
  try {
    await PlayTrack(track)
  } catch (ex) {
    alert(`播放失败：${ex?.message ?? ex}`, { severity: 'error' })
  }
}

function togglePlayPause() {
  if (playbackState.value === 'Playing') PausePlayback()
  else if (playbackState.value === 'Paused') ResumePlayback()
  else if (displayTracks.value.length > 0) {
    // 停止态：播放选中曲目或列表首项
    PlayTrack(displayTracks.value[0]).catch(() => {})
  }
}

function previous() { PreviousTrack().catch(() => {}) }
function next() { NextTrack().catch(() => {}) }
function stop() { StopPlayback() }

async function cycleMode() {
  const nextMode = MODE_CYCLE[(MODE_CYCLE.indexOf(playbackMode.value) + 1) % MODE_CYCLE.length]
  playbackMode.value = nextMode
  try { await SetMusicPlaybackMode(nextMode) } catch { /* 状态回退由事件驱动 */ }
}

function onVolumeChanged() {
  const value = Math.round(Number(volume.value) || 0)
  SetMusicVolume(value)
}

// ------------------------------------------------------------------
// 进度条
// ------------------------------------------------------------------

function onProgressCommit() {
  userDragging.value = false
  if (!progressEnabled.value) return
  // pointerup 与 valueCommit 都会触发，同一位置只 seek 一次
  if (progressValue.value === lastSeekedValue) return
  lastSeekedValue = progressValue.value
  // SeekPlayback 会经 audioBridge 回发 music:seek 事件，由前端音频自行跳转
  SeekPlayback(Math.round(progressValue.value * 1000)).catch(() => {})
}

// 进度跟随音频（非拖拽时）
let progressSyncHandle = null
function startProgressSync() {
  progressSyncHandle = setInterval(() => {
    if (!userDragging.value && progressEnabled.value) {
      progressValue.value = audioState.positionMs / 1000
    }
  }, 250)
}

// ------------------------------------------------------------------
// 播放器状态事件
// ------------------------------------------------------------------

function onStateChanged(state) { playbackState.value = String(state) }
function onTrackChanged(track) {
  currentTrack.value = track ?? null
  if (!track) {
    progressValue.value = 0
  } else {
    // 自动切歌后跟随高亮：过滤列表里存在则滚动到可见
    const index = displayTracks.value.findIndex((item) => item.FilePath === track.FilePath)
    if (index >= 0) {
      document.querySelector('.track-tile.current')?.scrollIntoView({ block: 'nearest' })
    }
  }
}
function onModeChanged(mode) { playbackMode.value = String(mode) }

onMounted(async () => {
  EventsOn('music:stateChanged', onStateChanged)
  EventsOn('music:trackChanged', onTrackChanged)
  EventsOn('music:playbackModeChanged', onModeChanged)
  startProgressSync()

  // 加载已保存的音量与播放模式；已有文件夹则自动扫描
  try {
    const savedVolume = await GetMusicVolume()
    if (Number.isFinite(savedVolume)) volume.value = savedVolume
    playbackMode.value = await GetMusicPlaybackMode()
    folderPath.value = await GetMusicFolderPath()
    sortMode.value = await GetMusicSortMode()
    if (folderPath.value) await refreshLibrary()
    // 共享播放器已有曲目在播时同步界面
    const playlist = await GetPlaylist()
    if (playlist?.length && !currentTrack.value) {
      displayTracks.value = (await GetSortedMusicTracks(sortMode.value)) ?? []
    }
  } catch (ex) {
    console.error('音乐页初始化失败', ex)
  }
})

onUnmounted(() => {
  EventsOff('music:stateChanged')
  EventsOff('music:trackChanged')
  EventsOff('music:playbackModeChanged')
  clearInterval(progressSyncHandle)
})
</script>

<style scoped>
/* 均衡器条：默认低幅度，播放中（.eq-on）跑三段波动动画（原参数保留） */
.eq-bar {
  width: 3px;
  border-radius: 1.5px;
  background: var(--white);
  opacity: 0.45;
  height: 5px;
}
.eq-bar.eq-on { opacity: 1; }
.eq-bar-1.eq-on { animation: eq-1 0.9s ease-in-out infinite; }
.eq-bar-2.eq-on { animation: eq-2 0.7s ease-in-out infinite; }
.eq-bar-3.eq-on { animation: eq-3 1.05s ease-in-out infinite; }
@keyframes eq-1 { 0%, 100% { height: 5px; } 50% { height: 17px; } }
@keyframes eq-2 { 0%, 100% { height: 4px; } 50% { height: 13px; } }
@keyframes eq-3 { 0%, 100% { height: 6px; } 50% { height: 15px; } }

/* 封面徽章呼吸动画（2.8s ease-in-out，原参数保留） */
.nya-cover-breathing { animation: cover-breathing 2.8s ease-in-out infinite; }
@keyframes cover-breathing {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.06); }
}

@media (max-width: 720px) {
  .min-w-\[35px\] { display: none; }
  .w-\[100px\] { width: 76px; }
}
</style>
