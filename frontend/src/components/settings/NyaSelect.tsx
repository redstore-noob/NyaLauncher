/*
 * NyaSelect —— 对应 Avalonia ComboBox（shadcn Select 内核）。
 * options: [{ value, label }]；value 受控，onValueChange/onChange 回调，
 * 与 Vue 版 modelValue/change 语义一致。
 */
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

export interface NyaSelectOption { value: string; label: string }

export interface NyaSelectProps {
  value?: string
  options?: NyaSelectOption[]
  disabled?: boolean
  onValueChange?: (v: string) => void
  onChange?: (v: string) => void
}

export default function NyaSelect({ value = '', options = [], disabled = false, onValueChange, onChange }: NyaSelectProps) {
  const currentLabel = options.find((o) => o.value === value)?.label ?? ''
  return (
    <Select
      value={value || undefined}
      disabled={disabled}
      onValueChange={(v) => { onValueChange?.(v); onChange?.(v) }}
    >
      <SelectTrigger className="w-full max-w-[320px]">
        <SelectValue placeholder={currentLabel} />
      </SelectTrigger>
      <SelectContent>
        {options.map((opt) => (
          <SelectItem key={opt.value} value={opt.value}>{opt.label}</SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
