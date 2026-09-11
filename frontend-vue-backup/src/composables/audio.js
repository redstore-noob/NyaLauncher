/*
 * 音乐前端播放桥（audio_bridge.go 的前端对端）。
 *
 * Go 侧 music.Shared 只保留状态机 + 播放列表，实际发声由本模块的 <audio> 元素完成：
 *   - music:play   {filePath}      → 加载并播放该文件
 *   - music:pause / resume / stop  → 暂停 / 恢复 / 停止
 *   - music:seek   {positionMs}    → 跳转
 *   - music:volume {percent}       → 音量
 * 进度经 Music.ReportPlaybackProgress(positionMs, durationMs) 回传；
 * 自然播完调 Music.NotifyTrackFinished()（触发 Go 侧自动切歌）。
 *
 * 本地文件经 Go 侧 AssetServer 回退路由 /localfile?path=... 流式返回
 * （internal/bindings/localfile_handler.go，音频扩展名白名单）。
 */

import { reactive } from 'vue'
import { EventsOn } from '../../wailsjs/runtime/runtime.js'
import {
  ReportPlaybackProgress,
  NotifyTrackFinished,
} from '../../wailsjs/go/bindings/MusicAPI.js'

export const audioState = reactive({
  filePath: '',
  url: '',
  canPlay: false,
  playing: false,
  paused: false,
  positionMs: 0,
  durationMs: 0,
  error: '',
})

let audio = null
let started = false

function ensureAudio() {
  if (audio) return audio
  audio = new Audio()
  audio.preload = 'auto'

  audio.addEventListener('timeupdate', () => {
    audioState.positionMs = Math.round(audio.currentTime * 1000)
    audioState.durationMs = Number.isFinite(audio.duration) ? Math.round(audio.duration * 1000) : 0
    // ~250ms 节流回传进度（对应原版 250ms 进度计时器）
    if (!audioState._lastReport || Date.now() - audioState._lastReport >= 250) {
      audioState._lastReport = Date.now()
      ReportPlaybackProgress(audioState.positionMs, audioState.durationMs).catch(() => {})
    }
  })
  audio.addEventListener('durationchange', () => {
    audioState.durationMs = Number.isFinite(audio.duration) ? Math.round(audio.duration * 1000) : 0
  })
  audio.addEventListener('playing', () => {
    audioState.playing = true
    audioState.paused = false
  })
  audio.addEventListener('pause', () => {
    audioState.playing = false
    audioState.paused = true
  })
  audio.addEventListener('ended', () => {
    audioState.playing = false
    audioState.paused = false
    audioState.positionMs = 0
    NotifyTrackFinished().catch(() => {})
  })
  audio.addEventListener('error', () => {
    audioState.canPlay = false
    audioState.playing = false
    audioState.error = '音频加载失败：文件不存在或格式不受支持，暂不可播。'
  })
  return audio
}

/** 本地路径 → 应用内流式播放 URL（Go 侧 /localfile 回退路由）。 */
export function resolveTrackUrl(filePath) {
  if (!filePath) return ''
  return `/localfile?path=${encodeURIComponent(filePath)}`
}

function play(filePath) {
  const el = ensureAudio()
  audioState.filePath = filePath || ''
  audioState.url = resolveTrackUrl(filePath)
  audioState.canPlay = !!audioState.url
  audioState.error = ''
  audioState.positionMs = 0
  audioState.durationMs = 0
  if (!audioState.url) return
  el.src = audioState.url
  el.play().catch(() => {
    // /localfile 加载失败（文件缺失、格式不支持等）情形
    audioState.canPlay = false
    audioState.error = '音频加载失败：文件不存在或格式不受支持，暂不可播。'
  })
}

export function startAudioBridge() {
  if (started || typeof window === 'undefined') return
  started = true
  ensureAudio()

  EventsOn('music:play', (payload) => play(payload?.filePath))
  EventsOn('music:pause', () => { audio?.pause() })
  EventsOn('music:resume', () => { audio?.play().catch(() => {}) })
  EventsOn('music:stop', () => {
    if (!audio) return
    audio.pause()
    audio.currentTime = 0
    audioState.positionMs = 0
    audioState.playing = false
    audioState.paused = false
  })
  EventsOn('music:seek', (payload) => {
    if (audio && Number.isFinite(payload?.positionMs)) {
      audio.currentTime = Math.max(0, payload.positionMs / 1000)
      audioState.positionMs = payload.positionMs
    }
  })
  EventsOn('music:volume', (payload) => {
    if (audio && Number.isFinite(payload?.percent)) {
      audio.volume = Math.min(1, Math.max(0, payload.percent / 100))
    }
  })
}

/** 主动提交一次进度（拖动进度条结束后由页面调用）。 */
export function reportProgressNow() {
  const el = ensureAudio()
  const durationMs = Number.isFinite(el.duration) ? Math.round(el.duration * 1000) : 0
  ReportPlaybackProgress(Math.round(el.currentTime * 1000), durationMs).catch(() => {})
}

startAudioBridge()
