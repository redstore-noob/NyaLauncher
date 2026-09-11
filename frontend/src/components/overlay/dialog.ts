/*
 * 全局浮层门面（React 版，API 语义与 Vue 版 composables/dialog.js 完全一致）。
 * 宿主挂载由 <OverlayHost /> 完成（App.tsx 中已挂）；无需 import 即挂载。
 *
 * 用法：
 *   import { alert, confirm, promptDialog, showDialogBox } from '@/components/overlay/dialog'
 *   alert('已保存');                              // 信息警示
 *   alert('失败', { severity: 'error' });
 *   if (await confirm('删除账户', '该操作不可恢复')) { ... }
 *   const name = await promptDialog('新建账户', '请输入名称', { defaultValue: 'Player_01' });
 *   const id = await showDialogBox({ title, message, severity, buttons: [{label:'甲'}, {label:'乙', default:true}] });
 */

import { showAlert, hideAlertNow, showDialog, type DialogButton, type DialogRequest } from './state'

/** 底部警示滑条。severity: 'info' | 'success' | 'warning' | 'error'。 */
export function alert(message: string, options: { severity?: string; duration?: number } = {}) {
  showAlert(message, options)
}

export { hideAlertNow }

/**
 * 确认对话框，resolve true（确认） / false（取消）。
 * 默认 warning 级别（与原版 NyaPrompt.ConfirmAsync 一致）。
 */
export function confirm(
  title: string,
  message: string,
  { confirmLabel = '确认', cancelLabel = '取消', severity = 'warning' }: { confirmLabel?: string; cancelLabel?: string; severity?: string } = {},
): Promise<boolean> {
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
 * 文本输入对话框（window.prompt 的替代品）。
 * resolve 输入字符串（取消时为 null）。
 */
export function promptDialog(
  title: string,
  message: string,
  { defaultValue = '', placeholder = '' }: { defaultValue?: string; placeholder?: string } = {},
): Promise<string | null> {
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
    if (!result || (result as { id?: string }).id !== 'ok') return null
    return (result as { value?: string }).value ?? ''
  })
}

/**
 * 通用对话框（NyaPrompt.ShowAsync）。
 * resolve 被点击按钮的 id；被新对话框顶掉时 resolve null。
 */
export function showDialogBox(request: DialogRequest): Promise<string | { id: string; value: string } | null> {
  return showDialog(request) as Promise<string | { id: string; value: string } | null>
}

export type { DialogButton, DialogRequest }
