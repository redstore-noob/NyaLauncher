<template>
  <!-- 下载状态悬浮条（DownloadStatusPanel.axaml：文件名 + 环形进度 + 明细 + 取消） -->
  <transition name="nya-dlpanel">
    <div
      v-if="visible"
      class="fixed right-5 bottom-[46px] z-[930] flex w-[220px] flex-col gap-2 rounded-xl
             border border-border bg-popover p-3 shadow-lg"
    >
      <div class="flex">
        <span class="overflow-hidden text-xs text-secondary-text text-ellipsis whitespace-nowrap" :title="fileName">{{ fileName }}</span>
      </div>
      <div class="relative flex justify-center">
        <svg width="46" height="46" viewBox="0 0 46 46">
          <circle cx="23" cy="23" r="20" fill="none" stroke="var(--badge-bg)" stroke-width="6" />
          <circle
            cx="23" cy="23" r="20" fill="none"
            stroke="var(--accent)" stroke-width="6" stroke-linecap="round"
            :stroke-dasharray="`${2 * Math.PI * 20}`"
            :stroke-dashoffset="`${2 * Math.PI * 20 * (1 - percent / 100)}`"
            transform="rotate(-90 23 23)"
          />
        </svg>
        <span
          class="absolute inset-0 flex items-center justify-center text-xs font-bold text-primary"
          :class="{ 'nya-dl-blink': indeterminate }"
        >
          {{ indeterminate ? '…' : `${Math.round(percent)}%` }}
        </span>
      </div>
      <div class="flex items-center gap-2">
        <span class="min-w-0 flex-1 overflow-hidden text-[11px] text-hint-text text-ellipsis whitespace-nowrap" :title="detail">
          {{ paused ? '已暂停 · ' : '' }}{{ detail }}
        </span>
        <Button variant="secondary" size="sm" class="h-6 flex-none px-3 text-[11px]" @click="onCancel">取消</Button>
      </div>
    </div>
  </transition>
</template>

<script setup>
/*
 * 下载状态悬浮面板：监听 download:progress / download:pauseChanged 全局事件。
 * 原版还提供「打开文件夹」按钮（依赖本地 shell），前端环境无对应绑定，暂未移植。
 */
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { Button } from '@/components/ui/button'
import { EventsOn, EventsOff } from '../../../wailsjs/runtime/runtime.js'
import { CancelDownload } from '../../../wailsjs/go/bindings/DownloadAPI.js'

/*
 * Phase 为 int 枚举：0 Idle / 1 Preparing / 2 Downloading / 3 Completed / 4 Failed / 5 Cancelled。
 * 进行中（1-2）常驻展示；终态（3-5）停留 4 秒后自动收起。
 */
const TERMINAL_HIDE_MS = 4000
let terminalTimer = null

const snapshot = ref(null)
const paused = ref(false)

const visible = computed(() => !!snapshot.value && snapshot.value.Phase >= 1 && snapshot.value.Phase <= 5)

const fileName = computed(() => {
  const s = snapshot.value
  if (!s) return '准备就绪'
  return s.StageName || s.VersionID || '下载中'
})

const percent = computed(() => {
  const s = snapshot.value
  if (!s) return 0
  if (s.Percentage > 0) return Math.min(100, s.Percentage)
  if (s.TotalBytes > 0) return Math.min(100, (s.CompletedBytes / s.TotalBytes) * 100)
  return 0
})

const indeterminate = computed(() => {
  const s = snapshot.value
  return !!s && s.TotalBytes <= 0 && s.Percentage <= 0
})

const detail = computed(() => {
  const s = snapshot.value
  if (!s) return ''
  const parts = []
  if (s.BytesPerSecond > 0) parts.push(`${(s.BytesPerSecond / 1048576).toFixed(1)} MiB/s`)
  if (s.TotalBytes > 0) {
    parts.push(`${(s.CompletedBytes / 1048576).toFixed(1)} / ${(s.TotalBytes / 1048576).toFixed(1)} MiB`)
  } else if (s.TotalFiles > 0) {
    parts.push(`${s.CompletedFiles} / ${s.TotalFiles} 个文件`)
  }
  if (s.Detail) parts.push(s.Detail)
  return parts.join(' · ')
})

function onProgress(snap) {
  snapshot.value = snap
  clearTimeout(terminalTimer)
  if (snap && snap.Phase >= 3) {
    terminalTimer = setTimeout(() => { snapshot.value = null }, TERMINAL_HIDE_MS)
  }
}
function onPauseChanged(value) { paused.value = !!value }

async function onCancel() {
  try { await CancelDownload() } catch { /* 取消失败保持面板展示 */ }
}

onMounted(() => {
  EventsOn('download:progress', onProgress)
  EventsOn('download:pauseChanged', onPauseChanged)
})
onUnmounted(() => {
  clearTimeout(terminalTimer)
  EventsOff('download:progress')
  EventsOff('download:pauseChanged')
})
</script>

<style scoped>
/* 不确定态闪烁（1s ease-in-out infinite，与原版一致） */
.nya-dl-blink { animation: nya-dl-blink 1s ease-in-out infinite; }
@keyframes nya-dl-blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.3; } }

.nya-dlpanel-enter-active { transition: opacity 240ms var(--ease-emphasized-decelerate), transform 240ms var(--ease-emphasized-decelerate); }
.nya-dlpanel-leave-active { transition: opacity 160ms var(--ease-emphasized-accelerate); }
.nya-dlpanel-enter-from { opacity: 0; transform: translateY(16px); }
.nya-dlpanel-leave-to { opacity: 0; }
</style>
