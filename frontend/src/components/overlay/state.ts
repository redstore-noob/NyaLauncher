/*
 * 全局浮层状态（React 版，对应 Vue 版 overlay/state.js；
 * 宿主注册模式等价——由 components/overlay/OverlayHost.tsx 挂载 NyaAlert/NyaPrompt/DownloadStatusPanel）。
 * 同一时刻只有一条警示与一个提示框；新请求顶掉旧请求（旧 Promise 以 null 完成）。
 * Vue reactive 换成 useSyncExternalStore 的轻量订阅实现。
 */
import { useSyncExternalStore } from 'react'

export interface DialogButton {
  label: string
  id?: string
  default?: boolean
}

export interface DialogRequest {
  title?: string
  message?: string
  severity?: string
  buttons?: DialogButton[]
  input?: { value?: string; placeholder?: string } | null
}

export interface DialogState {
  title: string
  message: string
  severity: string
  buttons: Required<DialogButton>[]
  input: { value: string; placeholder: string } | null
}

export interface OverlayState {
  /** 当前警示：{ message, severity } 或 null */
  alert: { message: string; severity: string } | null
  /** 警示卡片可见性（出入场过渡用） */
  alertVisible: boolean
  /** 当前提示框请求 */
  dialog: DialogState | null
  dialogVisible: boolean
}

let overlayState: OverlayState = {
  alert: null,
  alertVisible: false,
  dialog: null,
  dialogVisible: false,
}

const listeners = new Set<() => void>()

function emit() {
  overlayState = { ...overlayState }
  listeners.forEach((l) => l())
}

function set(partial: Partial<OverlayState>) {
  overlayState = { ...overlayState, ...partial }
  listeners.forEach((l) => l())
}

/** React 绑定：组件里 const s = useOverlayState() */
export function useOverlayState(): OverlayState {
  return useSyncExternalStore(
    (cb) => {
      listeners.add(cb)
      return () => listeners.delete(cb)
    },
    () => overlayState,
    () => overlayState,
  )
}

let alertTimer: ReturnType<typeof setTimeout> | undefined
let alertGeneration = 0
let alertHiding = false
let dialogResolver: ((result: unknown) => void) | null = null
let dialogClosing = false

const SEVERITIES = ['info', 'success', 'warning', 'error']

/** NyaNoticeSeverities.Map：级别 → 图标名 + CSS 变量色 */
export function mapSeverity(severity?: string): { icon: string; color: string } {
  switch (severity) {
    case 'success': return { icon: 'success', color: 'var(--success)' }
    case 'warning': return { icon: 'warning', color: 'var(--warning)' }
    case 'error': return { icon: 'error', color: 'var(--error)' }
    default: return { icon: 'info', color: 'var(--info)' }
  }
}

/**
 * 展示一条底部警示滑条（NyaAlert.Info/Success/Warning/Error）。
 * 展示中再次触发：就地换文案与配色并重置倒计时，不重播滑入动画。
 */
export function showAlert(message: string, { severity = 'info', duration = 4000 }: { severity?: string; duration?: number } = {}) {
  const wasHiding = alertHiding
  alertGeneration++
  alertHiding = false
  clearTimeout(alertTimer)

  set({ alert: { message, severity } })
  if (overlayState.alertVisible && !wasHiding) {
    restartAutoHide(duration)
    return
  }
  set({ alertVisible: true })
  restartAutoHide(duration)
}

function restartAutoHide(duration: number) {
  const generation = alertGeneration
  clearTimeout(alertTimer)
  alertTimer = setTimeout(() => {
    if (generation === alertGeneration) hideAlertNow()
  }, duration)
}

/** 立即收回警示（点关闭或到点）。 */
export function hideAlertNow() {
  clearTimeout(alertTimer)
  if (!overlayState.alertVisible || alertHiding) return
  alertHiding = true
  // 出场 200ms 后隐藏（对应 AnimateOutAsync）
  const generation = alertGeneration
  setTimeout(() => {
    if (generation !== alertGeneration) return
    set({ alertVisible: false })
    alertHiding = false
  }, 200)
}

/**
 * 展示提示对话框（NyaPrompt.ShowAsync）。
 * resolve：被点击按钮的 id（有输入框时为 { id, value }）；被新提示顶掉时 resolve(null)。
 */
export function showDialog(request: DialogRequest): Promise<unknown> {
  if (dialogResolver) {
    const old = dialogResolver
    dialogResolver = null
    old(null)
  }
  dialogClosing = false
  const buttons = request.buttons?.length
    ? request.buttons
    : [{ label: '好的', id: 'ok', default: true }]

  set({
    dialog: {
      title: request.title ?? '',
      message: request.message ?? '',
      severity: SEVERITIES.includes(request.severity ?? '') ? request.severity! : 'info',
      buttons: buttons.map((b, i) => ({
        label: b.label,
        id: b.id ?? b.label,
        default: b.default ?? i === buttons.length - 1,
      })),
      input: request.input ? { value: request.input.value ?? '', placeholder: request.input.placeholder ?? '' } : null,
    },
    dialogVisible: true,
  })

  return new Promise((resolve) => { dialogResolver = resolve })
}

/** 完成对话框（按钮点击时由 NyaPrompt 调用）。 */
export function completeDialog(result: unknown) {
  if (dialogClosing || !dialogResolver) return
  dialogClosing = true
  const resolve = dialogResolver
  dialogResolver = null
  // 出场 180ms 后隐藏（对应 OverlayEffects.PopOut）
  setTimeout(() => {
    set({ dialogVisible: false, dialog: null })
  }, 180)
  resolve(result)
}
