/*
 * NyaSlider —— 对应 Avalonia Slider（shadcn Slider 内核，value 为单值）。
 * showBadge 显示右侧强调色数值徽章；children 为补充说明文字。
 * 与 Vue 版 modelValue/change 语义一致：value + onChange(num)。
 */
import type { ReactNode } from 'react'
import { Slider } from '@/components/ui/slider'

export interface NyaSliderProps {
  value: number
  min?: number
  max?: number
  step?: number
  disabled?: boolean
  label?: string
  showBadge?: boolean
  badgeText?: string | number
  onChange?: (v: number) => void
  onValueChange?: (v: number) => void
  className?: string
  children?: ReactNode
}

export default function NyaSlider({
  value, min = 0, max = 100, step = 1, disabled = false,
  label = '', showBadge = false, badgeText = '', onChange, onValueChange, className, children,
}: NyaSliderProps) {
  return (
    <div className="flex min-w-0 flex-col gap-2">
      {label || showBadge ? (
        <div className="flex items-center justify-between gap-2">
          <span className="text-[14px] font-semibold text-secondary-text">{label}</span>
          {showBadge ? (
            <span className="rounded-sm bg-badge px-2 py-1 text-[13px] font-bold text-primary">{badgeText}</span>
          ) : null}
        </div>
      ) : null}
      <Slider
        value={[value]}
        min={min}
        max={max}
        step={step}
        disabled={disabled}
        className={`cursor-pointer ${className ?? ''}`}
        onValueChange={(v) => {
          const num = Array.isArray(v) ? v[0] : v
          if (typeof num !== 'number' || Number.isNaN(num)) return
          onValueChange?.(num)
          onChange?.(num)
        }}
      />
      {children ? <div className="text-[11px] leading-relaxed text-hint-text">{children}</div> : null}
    </div>
  )
}
