/*
 * SettingRow —— 设置行（等价 Vue 版 SettingRow.vue）：
 * bg-muted 控件底子卡 + subtle-border + rounded-lg；右侧控件 children，
 * extraHint 放行内动态提示（如内存决策文案）。data-title/data-hint 参与搜索。
 */
import type { ReactNode } from 'react'

export interface SettingRowProps {
  title: string
  hint?: string
  extraHint?: ReactNode
  children?: ReactNode
}

export default function SettingRow({ title, hint = '', extraHint, children }: SettingRowProps) {
  return (
    <div
      data-title={title}
      data-hint={hint}
      className="grid grid-cols-[1fr_auto] items-center gap-4 rounded-lg border border-subtle-border bg-muted px-4 py-3"
    >
      <div className="flex min-w-0 flex-col gap-1">
        <span className="text-[14px] font-semibold text-secondary-text">{title}</span>
        {hint ? <span className="text-[11px] leading-relaxed text-hint-text">{hint}</span> : null}
        {extraHint}
      </div>
      <div className="flex shrink-0 items-center gap-2">{children}</div>
    </div>
  )
}
