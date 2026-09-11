/*
 * NyaToggle —— 对应 Avalonia ToggleSwitch（OnContent="开" OffContent="关"）。
 * 内部实现换装为 shadcn Switch；对外 props/回调与 Vue 版一致：
 * checked / disabled → onChange(v)。
 */
import { Switch } from '@/components/ui/switch'

export interface NyaToggleProps {
  checked?: boolean
  disabled?: boolean
  onChange?: (v: boolean) => void
}

export default function NyaToggle({ checked = false, disabled = false, onChange }: NyaToggleProps) {
  return (
    <label
      className={`inline-flex cursor-pointer items-center gap-2 select-none ${disabled ? 'cursor-not-allowed opacity-50' : ''}`}
    >
      <span className="text-[13px] text-secondary-text">{checked ? '开' : '关'}</span>
      <Switch
        checked={checked}
        disabled={disabled}
        onCheckedChange={(v) => { if (!disabled) onChange?.(v) }}
      />
    </label>
  )
}
