import { reactive } from 'vue'

/*
 * 全局浮层状态（对应 NyaAlertHost / NyaPromptHost / ModalOverlayHost 的宿主注册模式）。
 * 同一时刻只有一条警示与一个提示框；新请求顶掉旧请求（旧 Promise 以 null 完成）。
 */

export const overlayState = reactive({
  /** 当前警示：{ message, severity } 或 null */
  alert: null,
  /** 警示卡片可见性（出入场过渡用） */
  alertVisible: false,
  /** 当前提示框请求：{ title, message, severity, buttons, input } 或 null */
  dialog: null,
  dialogVisible: false,
})

let alertTimer = null
let alertGeneration = 0
let alertHiding = false
let dialogResolver = null
let dialogClosing = false

const SEVERITIES = ['info', 'success', 'warning', 'error']

/** NyaNoticeSeverities.Map：级别 → 图标名 + CSS 变量色 */
export function mapSeverity(severity) {
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
export function showAlert(message, { severity = 'info', duration = 4000 } = {}) {
  const wasHiding = alertHiding
  alertGeneration++
  alertHiding = false
  clearTimeout(alertTimer)

  overlayState.alert = { message, severity }
  if (overlayState.alertVisible && !wasHiding) {
    restartAutoHide(duration)
    return
  }
  overlayState.alertVisible = true
  restartAutoHide(duration)
}

function restartAutoHide(duration) {
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
    overlayState.alertVisible = false
    alertHiding = false
  }, 200)
}

/**
 * 展示提示对话框（NyaPrompt.ShowAsync）。
 * request: { title, message, severity, buttons: [{label, id?, default?}], input? }
 * input 非空时展示一个输入框（前端 prompt() 的扩展，原版 NyaPrompt 无输入框）。
 * resolve：被点击按钮的 id（有输入框时为 { id, value }）；被新提示顶掉时 resolve(null)。
 */
export function showDialog(request) {
  if (dialogResolver) {
    const old = dialogResolver
    dialogResolver = null
    old(null)
  }
  dialogClosing = false
  const buttons = request.buttons?.length
    ? request.buttons
    : [{ label: '好的', id: 'ok', default: true }]

  overlayState.dialog = {
    title: request.title ?? '',
    message: request.message ?? '',
    severity: SEVERITIES.includes(request.severity) ? request.severity : 'info',
    buttons: buttons.map((b, i) => ({
      label: b.label,
      id: b.id ?? b.label,
      default: b.default ?? i === buttons.length - 1,
    })),
    input: request.input ?? null,
  }
  overlayState.dialogVisible = true

  return new Promise((resolve) => { dialogResolver = resolve })
}

/** 完成对话框（按钮点击时由 NyaPrompt.vue 调用）。 */
export function completeDialog(result) {
  if (dialogClosing || !dialogResolver) return
  dialogClosing = true
  const resolve = dialogResolver
  dialogResolver = null
  // 出场 180ms 后隐藏（对应 OverlayEffects.PopOut）
  setTimeout(() => {
    overlayState.dialogVisible = false
    overlayState.dialog = null
  }, 180)
  resolve(result)
}
