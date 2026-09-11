/*
 * 全局浮层 composable（对应 Avalonia 的 NyaAlert / NyaPrompt 门面）。
 * 首次 import 时自动把 NyaAlert + NyaPrompt + DownloadStatusPanel 挂载到 body
 * （App.vue 不便改动，改用与原版"宿主自注册"等价的方式）。
 *
 * 用法：
 *   import { alert, confirm, promptDialog, showDialog } from '@/composables/dialog'
 *   alert('已保存');                              // 信息警示
 *   alert('失败', { severity: 'error' });
 *   if (await confirm('删除账户', '该操作不可恢复')) { ... }
 *   const name = await promptDialog('新建账户', '请输入名称', { defaultValue: 'Player_01' });
 *   const id = await showDialog({ title, message, severity, buttons: [{label:'甲'}, {label:'乙', default:true}] });
 */

import { createApp } from 'vue'
import NyaAlert from '../components/overlay/NyaAlert.vue'
import NyaPrompt from '../components/overlay/NyaPrompt.vue'
import DownloadStatusPanel from '../components/overlay/DownloadStatusPanel.vue'
import { showAlert, hideAlertNow, showDialog } from '../components/overlay/state.js'

let mounted = false

/** 把三个全局浮层宿主挂到 body（幂等；对应 MainWindow 里各 Host 控件）。 */
export function mountOverlays() {
  if (mounted || typeof document === 'undefined') return
  mounted = true
  const host = document.createElement('div')
  host.id = 'nya-overlay-host'
  document.body.appendChild(host)
  createApp(NyaAlert).mount(host.appendChild(document.createElement('div')))
  createApp(NyaPrompt).mount(host.appendChild(document.createElement('div')))
  createApp(DownloadStatusPanel).mount(host.appendChild(document.createElement('div')))
}

mountOverlays()

/** 底部警示滑条。severity: 'info' | 'success' | 'warning' | 'error'。 */
export function alert(message, options = {}) {
  showAlert(message, options)
}

export { hideAlertNow }

/**
 * 确认对话框，resolve true（确认） / false（取消）。
 * 默认 warning 级别（与原版 NyaPrompt.ConfirmAsync 一致）。
 */
export function confirm(title, message, { confirmLabel = '确认', cancelLabel = '取消', severity = 'warning' } = {}) {
  return showDialog({
    title,
    message,
    severity,
    buttons: [
      { label: cancelLabel, id: 'cancel' },
      { label: confirmLabel, id: 'ok', default: true },
    ],
  }).then((id) => id === 'ok')
}

/**
 * 文本输入对话框（window.prompt 的替代品；原版 NyaPrompt 无输入框，为迁移补齐）。
 * resolve 输入字符串（取消时为 null）。
 */
export function promptDialog(title, message, { defaultValue = '', placeholder = '' } = {}) {
  return showDialog({
    title,
    message,
    severity: 'info',
    input: { value: defaultValue, placeholder },
    buttons: [
      { label: '取消', id: 'cancel' },
      { label: '确认', id: 'ok', default: true },
    ],
  }).then((result) => {
    if (!result || result.id !== 'ok') return null
    return result.value
  })
}

/**
 * 通用对话框（NyaPrompt.ShowAsync）。
 * request: { title, message, severity, buttons: [{label, id?, default?}] }
 * resolve 被点击按钮的 id；被新对话框顶掉时 resolve null。
 */
export function showDialogBox(request) {
  return showDialog(request)
}
