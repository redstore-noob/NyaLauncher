<template>
  <!--
    启动日志遮罩（Controls/GameLogOverlay.axaml 移植）：底部滑出全屏遮罩，
    只展示本次启动游戏的实时输出（LauncherAPI.GetLogText 内存日志），
    带级别着色、自动滚动到最新行；不浏览磁盘历史日志文件。
  -->
  <Teleport to="body">
    <transition name="gamelog">
      <div v-if="open" class="fixed inset-0 z-[90] flex flex-col bg-popover" role="dialog" aria-label="启动日志">
        <!-- 顶栏：标题 + 实例 / 阶段徽章 + 状态 + 操作按钮（原版 18,12,10,12 内边距） -->
        <div class="flex items-center gap-4 border-b border-subtle-border bg-background px-[18px] py-3 pr-2.5">
          <div class="flex flex-col gap-0.5">
            <span class="text-[16px] font-semibold text-primary-text">启动日志</span>
            <span class="text-[10px] text-subtext-text">{{ subtitle }}</span>
          </div>

          <div class="flex min-w-0 flex-1 items-center gap-2">
            <Badge class="rounded-[7px] px-2 py-0.5 text-[9.5px] font-bold">{{ phaseText }}</Badge>
            <span class="truncate text-[11px] text-secondary-text">{{ statusText }}</span>
          </div>

          <label class="flex shrink-0 cursor-pointer items-center gap-1.5 text-[11px] text-secondary-text">
            <input v-model="autoScroll" type="checkbox" class="accent-[var(--accent)]">
            自动滚动
          </label>
          <Button variant="secondary" size="sm" class="shrink-0 px-3" @click="copyAll">复制全文</Button>
          <Button variant="secondary" size="sm" class="shrink-0 px-3" @click="clearLogs">清空</Button>
          <button class="window-button close" title="关闭日志" @click="$emit('close')">
            <svg width="12" height="12" viewBox="0 0 12 12">
              <path d="M 2,2 L 10,10 M 10,2 L 2,10" stroke="currentColor" stroke-width="1.4" stroke-linecap="square"/>
            </svg>
          </button>
        </div>

        <!-- 日志区：WindowBg 圆角框（原版 r12 + MediumBorder） -->
        <div class="flex min-h-0 flex-1 flex-col p-4">
          <div class="relative min-h-0 flex-1 overflow-hidden rounded-xl border border-border bg-background p-2">
            <div v-if="lines.length === 0" class="flex h-full flex-col items-center justify-center gap-1.5">
              <span class="text-[28px] opacity-40">🖥️</span>
              <span class="text-[11px] text-hint-text">还没有启动日志，启动一次游戏后这里会实时显示输出。</span>
            </div>
            <div v-else ref="listEl" class="h-full overflow-y-auto font-mono text-[11px] leading-[1.55]">
              <div v-for="(ln, i) in lines" :key="i" class="flex gap-1.5 whitespace-pre">
                <span v-if="ln.prefix" :class="prefixColor(ln)">{{ ln.prefix }}</span>
                <span :class="bodyColor(ln)">{{ ln.body }}</span>
              </div>
            </div>
          </div>
          <span class="mt-2.5 px-0.5 text-[10px] text-hint-text">{{ footerText }}</span>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<script setup>
/*
 * 行为对照 GameLogOverlay.axaml.cs：定时轮询全文、无变化跳过重建、
 * 空态提示、级别着色（LaunchLogColorizer：时间戳前缀弱化 +
 * MC 日志级别 / [stderr] / 方括号级别 / 关键字启发式）、复制全文。
 * 前端补充：自动滚动暂停开关与清空（SystemAPI.ClearLogs）。
 */
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { GetLaunchSnapshot, GetLogText } from '../../../wailsjs/go/bindings/LauncherAPI'
import { ClearLogs } from '../../../wailsjs/go/bindings/SystemAPI'
import { toast } from '@/components/ui/sonner'

const props = defineProps({
  open: { type: Boolean, default: false },
})
defineEmits(['close'])

const REFRESH_MS = 1000

const lines = ref([])
const snapshot = ref(null)
const autoScroll = ref(true)
const footerText = ref('')
const listEl = ref(null)

const PHASE_TEXTS = ['空闲', '启动中', '运行中', '失败', '已退出']
const phaseText = computed(() => PHASE_TEXTS[snapshot.value?.Phase] ?? '空闲')
const subtitle = computed(() => snapshot.value?.VersionId || '尚未启动游戏')
const statusText = computed(() => snapshot.value?.Message || '选择账号和游戏实例后即可启动。')

// ---- 级别着色（LaunchLogColorizer.Parse / Classify 等价实现） ----
const TIMESTAMP_PREFIX = /^(\[[^\]]*\]\s?)/
const MINECRAFT_LEVEL = /\[[^\[\]]+\/(INFO|WARN|ERROR|FATAL|DEBUG|TRACE)\]/i
const BRACKET_LEVEL = /\[(INFO|WARN(?:ING)?|ERROR|FATAL|DEBUG|TRACE)\]/i

function classify(body) {
  if (/\[stderr\]/i.test(body)) return 'error'
  const mc = body.match(MINECRAFT_LEVEL)
  if (mc) return mapLevel(mc[1])
  const br = body.match(BRACKET_LEVEL)
  if (br) return mapLevel(br[1])
  if (body.includes('Done (')) return 'success'
  if (/Exception|SEVERE|失败/.test(body)) return 'error'
  if (/异常|已取消/.test(body)) return 'warning'
  return 'plain'
}

function mapLevel(level) {
  return { INFO: 'info', WARN: 'warning', WARNING: 'warning', ERROR: 'error', FATAL: 'error' }[level.toUpperCase()] ?? 'plain'
}

function parseLine(raw) {
  const line = raw ?? ''
  const match = line.match(TIMESTAMP_PREFIX)
  const prefix = match && match[1].length < line.length ? match[1] : ''
  const body = prefix ? line.slice(match[1].length) : line
  return { prefix, body, kind: classify(body) }
}

const KIND_CLASS = {
  plain: 'text-body-text',
  info: 'text-info',
  warning: 'text-warning',
  error: 'text-destructive',
  success: 'text-success',
}
const bodyColor = (ln) => KIND_CLASS[ln.kind] ?? KIND_CLASS.plain
const prefixColor = () => 'text-hint-text'

// ---- 轮询（打开时启动，关闭时停止；无变化跳过重建） ----
let timer = null
let lastLogText = ''
let pollAbort = false

async function refresh() {
  if (pollAbort) return
  try {
    const [logText, snap] = await Promise.all([GetLogText(), GetLaunchSnapshot().catch(() => null)])
    snapshot.value = snap
    if (!logText || !logText.trim()) {
      if (lastLogText.length > 0) {
        lastLogText = ''
        lines.value = []
        footerText.value = ''
      }
      return
    }
    if (logText === lastLogText) return // 日志没有变化，跳过重建
    lastLogText = logText
    lines.value = logText.split(/\r\n|\n/).map(parseLine)
    footerText.value = `${lines.value.length.toLocaleString()} 行 · 本次启动的实时输出`
    scrollToEnd()
  } catch { /* 桥未启动时静默 */ }
}

function scrollToEnd() {
  if (!autoScroll.value) return
  requestAnimationFrame(() => {
    const el = listEl.value
    if (el) el.scrollTop = el.scrollHeight
  })
}

watch(autoScroll, (v) => { if (v) scrollToEnd() })

watch(() => props.open, (open) => {
  clearTimeout(timer)
  pollAbort = false
  if (open) {
    refresh()
    timer = setInterval(refresh, REFRESH_MS)
  }
})

async function copyAll() {
  if (!lastLogText) return
  try {
    await navigator.clipboard.writeText(lastLogText)
    footerText.value = '已复制全文到剪贴板。'
  } catch (e) {
    footerText.value = `复制失败：${e?.message ?? e}`
  }
}

async function clearLogs() {
  // PORTING_NOTE: LauncherAPI 无清空启动日志的方法，用 SystemAPI.ClearLogs 清启动器日志；
  // 内存游戏日志的清空待后端补齐（GameLaunchService.ClearLogText）后接入。
  try {
    const removed = await ClearLogs()
    lastLogText = ''
    lines.value = []
    footerText.value = `已清空 ${removed ?? 0} 条日志。`
    toast.success('日志已清空')
  } catch (e) {
    toast.error(`清空失败：${e?.message ?? e}`)
  }
}

onBeforeUnmount(() => {
  pollAbort = true
  clearTimeout(timer)
})
</script>

<style scoped>
/* 底部滑出：300ms emphasized 上浮淡入 / 180ms accelerate 下沉淡出（原版弹层令牌） */
.gamelog-enter-active { transition: opacity 220ms var(--ease-emphasized-decelerate), transform 300ms var(--ease-emphasized-decelerate); }
.gamelog-leave-active { transition: opacity 150ms var(--ease-emphasized-accelerate), transform 180ms var(--ease-emphasized-accelerate); }
.gamelog-enter-from,
.gamelog-leave-to { opacity: 0; transform: translateY(48px); }
</style>
