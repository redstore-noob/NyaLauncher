/*
 * SettingsCard —— 设置页大卡（等价 Vue 版 components/settings/SettingsCard.vue）：
 * bg-card + card-border + rounded-2xl + hover 阴影；头部 icon-chip + 标题/副标题 +
 * 右上角 actions。title 参与设置页搜索（Hub 搜索按各页 CARD_ALIASES 过滤，卡片
 * 用 data-title 标记，语义与 Vue 版一致）。
 */
import type { ReactNode } from 'react'

export interface SettingsCardProps {
  icon?: string
  title: string
  subtitle?: string
  actions?: ReactNode
  children?: ReactNode
}

export default function SettingsCard({ icon = '', title, subtitle = '', actions, children }: SettingsCardProps) {
  return (
    <section
      data-title={title}
      className="rounded-2xl border border-card-border bg-card text-card-foreground shadow-sm transition-[transform,box-shadow] duration-200 ease-[var(--ease-emphasized)] hover:shadow-md"
    >
      <div className="flex flex-col gap-5 p-6">
        <div className="grid grid-cols-[auto_1fr_auto] items-center gap-3">
          <div className="flex size-9 items-center justify-center rounded-lg bg-accent text-[17px] text-primary" aria-hidden="true">
            {icon}
          </div>
          <div className="flex flex-col">
            <h3 className="m-0 text-[17px] font-semibold text-primary-text">{title}</h3>
            {subtitle ? <p className="mt-0.5 mb-0 text-[11px] text-hint-text">{subtitle}</p> : null}
          </div>
          <div className="flex items-center gap-2">{actions}</div>
        </div>
        {children}
      </div>
    </section>
  )
}
