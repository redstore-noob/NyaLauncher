/*
 * Material 风提示对话框（NyaPromptHost.axaml 移植）——基于 ui/ Dialog 基座（radix）。
 */
import { useEffect, useRef, useState } from 'react'
import Icon from './Icon'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import { useOverlayState, mapSeverity, completeDialog } from './state'

export default function NyaPrompt() {
  const state = useOverlayState()
  const open = state.dialogVisible && !!state.dialog
  const inputRef = useRef<HTMLInputElement | null>(null)
  const [inputValue, setInputValue] = useState('')

  const mapped = mapSeverity(state.dialog?.severity)

  useEffect(() => {
    const dialog = state.dialog
    setInputValue(dialog?.input?.value ?? '')
    if (dialog?.input && open) {
      // 等 radix content 挂载后聚焦输入框
      const t = setTimeout(() => inputRef.current?.focus(), 30)
      return () => clearTimeout(t)
    }
  }, [state.dialog, open])

  function onClick(btn: { id: string; label: string; default?: boolean }) {
    const dialog = state.dialog
    completeDialog(dialog?.input ? { id: btn.id, value: inputValue } : btn.id)
  }

  function submit() {
    const dialog = state.dialog
    const defaultBtn = dialog?.buttons.find((b) => b.default) ?? dialog?.buttons[0]
    if (defaultBtn) onClick(defaultBtn)
  }

  return (
    <Dialog open={open}>
      <DialogContent
        className="max-w-[460px] min-w-[320px] gap-0 rounded-2xl p-6 text-center"
        showCloseButton={false}
        onEscapeKeyDown={(e) => e.preventDefault()}
        onPointerDownOutside={(e) => e.preventDefault()}
        onInteractOutside={(e) => e.preventDefault()}
      >
        <span className="mb-3 flex justify-center" style={{ color: mapped.color }}>
          <Icon name={mapped.icon} size={28} />
        </span>

        <DialogHeader className="space-y-0 text-center sm:text-center">
          <DialogTitle className="text-lg font-semibold break-words">
            {state.dialog?.title}
          </DialogTitle>
          {state.dialog?.message ? (
            <DialogDescription className="mt-2.5 text-[13px] leading-5 text-secondary-text break-words select-text">
              {state.dialog.message}
            </DialogDescription>
          ) : null}
        </DialogHeader>

        {/* 前端 prompt() 扩展：原版 NyaPromptHost 无输入框，这里补一个文本输入 */}
        {state.dialog?.input ? (
          <div className="mt-4">
            <Input
              ref={inputRef}
              type="text"
              value={inputValue}
              placeholder={state.dialog.input.placeholder || ''}
              onChange={(e) => setInputValue(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') submit() }}
            />
          </div>
        ) : null}

        <DialogFooter className="mt-5 gap-1.5 sm:justify-end">
          {(state.dialog?.buttons ?? []).map((btn) => (
            <Button
              key={btn.id}
              variant={btn.default ? 'default' : 'ghost'}
              size="sm"
              onClick={() => onClick(btn)}
            >{btn.label}</Button>
          ))}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
