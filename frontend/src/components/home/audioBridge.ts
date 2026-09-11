/*
 * 音乐前端播放桥（Vue 版 src/composables/audio.js 的 React 平移，模块级单例）。
 *
 * Go 侧 music.Shared 只保留状态机 + 播放列表，实际发声由本模块的 <audio> 元素完成：
 *   - music:play {filePath}      → 加载并播放该文件
 *   - music:pause / resume / stop → 暂停 / 恢复 / 停止
 *   - music:seek {positionMs}    → 跳转
 *   - music:volume {percent}     → 音量
 * 进度经 Music.ReportPlaybackProgress(positionMs, durationMs) 回传；
 * 自然播完调 Music.NotifyTrackFinished()（触发 Go 侧自动切歌）。
 * 本地文件经 Go 侧 AssetServer 回退路由 /localfile?path=... 流式返回。
 *
 * React 差异：Vue 用 reactive，这里用可订阅外部存储（配 useSyncExternalStore），
 * 快照为不可变对象，字段语义与 Vue 版 audioState 一致。
 */
import { EventsOn } from '../../../wailsjs/runtime/runtime.js'
import { ReportPlaybackProgress, NotifyTrackFinished } from '../../../wailsjs/go/bindings/MusicAPI.js'

export interface AudioSnapshot {
  filePath: string
  url: string
  canPlay: boolean
  playing: boolean
  paused: boolean
  positionMs: number
  durationMs: number
  error: string
}

let snapshot: AudioSnapshot = {
  filePath: '',
  url: '',
  canPlay: false,
  playing: false,
  paused: false,
  positionMs: 0,
  durationMs: 0,
  error: '',
}

const listeners = new Set<() => void>()

function setSnap(patch: Partial<AudioSnapshot>) {
  snapshot = { ...snapshot, ...patch }
  listeners.forEach((l) => l())
}

export function subscribeAudio(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getAudioSnapshot(): AudioSnapshot {
  return snapshot
}

let audio: HTMLAudioElement | null = null
let started = false
let lastReportAt = 0

function ensureAudio(): HTMLAudioElement {
  if (audio) return audio
  const el = new Audio()
  el.preload = 'auto'

  el.addEventListener('timeupdate', () => {
    const positionMs = Math.round(el.currentTime * 1000)
    const durationMs = Number.isFinite(el.duration) ? Math.round(el.duration * 1000) : 0
    setSnap({ positionMs, durationMs })
    // ~250ms 节流回传进度（对应原版 250ms 进度计时器）
    if (!lastReportAt || Date.now() - lastReportAt >= 250) {
      lastReportAt = Date.now()
      ReportPlaybackProgress(positionMs, durationMs).catch(() => {})
    }
  })
  el.addEventListener('durationchange', () => {
    setSnap({ durationMs: Number.isFinite(el.duration) ? Math.round(el.duration * 1000) : 0 })
  })
  el.addEventListener('playing', () => setSnap({ playing: true, paused: false }))
  el.addEventListener('pause', () => setSnap({ playing: false, paused: true }))
  el.addEventListener('ended', () => {
    setSnap({ playing: false, paused: false, positionMs: 0 })
    NotifyTrackFinished().catch(() => {})
  })
  el.addEventListener('error', () => {
    setSnap({ canPlay: false, playing: false, error: '音频加载失败：文件不存在或格式不受支持，暂不可播。' })
  })
  audio = el
  return el
}

/** 本地路径 → 应用内流式播放 URL（Go 侧 /localfile 回退路由）。 */
export function resolveTrackUrl(filePath: string): string {
  if (!filePath) return ''
  return `/localfile?path=${encodeURIComponent(filePath)}`
}

function play(filePath: string) {
  const el = ensureAudio()
  const url = resolveTrackUrl(filePath)
  setSnap({ filePath: filePath || '', url, canPlay: !!url, error: '', positionMs: 0, durationMs: 0 })
  if (!url) return
  el.src = url
  el.play().catch(() => {
    // /localfile 加载失败（文件缺失、格式不支持等）情形
    setSnap({ canPlay: false, error: '音频加载失败：文件不存在或格式不受支持，暂不可播。' })
  })
}

/** 乐观进度预览（进度条点击后立即更新显示；audio 的 timeupdate 会随后校正）。 */
export function previewSeek(ms: number) {
  setSnap({ positionMs: ms })
}

export function startAudioBridge() {
  if (started || typeof window === 'undefined') return
  started = true
  ensureAudio()

  EventsOn('music:play', (payload: { filePath?: string } | null) => play(payload?.filePath ?? ''))
  EventsOn('music:pause', () => { audio?.pause() })
  EventsOn('music:resume', () => { audio?.play().catch(() => {}) })
  EventsOn('music:stop', () => {
    if (!audio) return
    audio.pause()
    audio.currentTime = 0
    setSnap({ positionMs: 0, playing: false, paused: false })
  })
  EventsOn('music:seek', (payload: { positionMs?: number } | null) => {
    if (audio && Number.isFinite(payload?.positionMs)) {
      audio.currentTime = Math.max(0, (payload!.positionMs as number) / 1000)
      setSnap({ positionMs: payload!.positionMs as number })
    }
  })
  EventsOn('music:volume', (payload: { percent?: number } | null) => {
    if (audio && Number.isFinite(payload?.percent)) {
      audio.volume = Math.min(1, Math.max(0, (payload!.percent as number) / 100))
    }
  })
}

startAudioBridge()
