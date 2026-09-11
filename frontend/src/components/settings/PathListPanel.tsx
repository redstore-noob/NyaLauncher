/*
 * PathListPanel —— 路径/Java 列表管理面板（等价 Vue 版 PathListPanel.vue）。
 * 左列表（Badge 徽章 + hover/选中弱底）+ 右侧竖排按钮 children + 底部 hint。
 * props 与 Vue 版一致：label/headHint/items/selectedIndex/emptyText → onSelect(i)。
 */
import type { ReactNode } from 'react'
import { Badge } from '@/components/ui/badge'

export interface PathListItem {
  text: string
  badgeText?: string
  badgeHighlight?: boolean
}

export interface PathListPanelProps {
  label: string
  headHint?: string
  items?: PathListItem[]
  selectedIndex?: number
  emptyText?: string
  onSelect?: (i: number) => void
  hint?: ReactNode
  children?: ReactNode
}

export default function PathListPanel({
  label, headHint = '', items = [], selectedIndex = -1, emptyText = '（空）', onSelect, hint, children,
}: PathListPanelProps) {
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex flex-col gap-1">
        <span className="text-[14px] font-semibold text-secondary-text">{label}</span>
        <span className="text-[11px] leading-relaxed text-hint-text">{headHint}</span>
      </div>
      <div className="grid grid-cols-[1fr_200px] items-start gap-3">
        <ul className="m-0 max-h-[190px] min-h-[120px] list-none overflow-y-auto rounded-md bg-muted p-2">
          {items.map((item, i) => (
            <li
              key={item.text + i}
              className={`grid cursor-pointer grid-cols-[auto_1fr] items-center gap-2 rounded-sm px-1 py-1 transition-colors duration-150 ${
                i === selectedIndex ? 'bg-accent ring-1 ring-medium-border' : 'hover:bg-accent/60'
              }`}
              onClick={() => onSelect?.(i)}
            >
              <Badge
                variant={item.badgeHighlight ? 'default' : 'secondary'}
                className={`whitespace-nowrap rounded-[5px] px-2 py-0.5 text-[10px] font-semibold ${
                  item.badgeHighlight ? 'bg-primary text-primary-foreground' : ''
                }`}
              >
                {item.badgeText}
              </Badge>
              <span className="overflow-hidden text-ellipsis whitespace-nowrap text-[11px] text-primary-text" title={item.text}>
                {item.text}
              </span>
            </li>
          ))}
          {items.length === 0 ? <li className="cursor-default px-1 py-1 text-[11px] text-hint-text">{emptyText}</li> : null}
        </ul>
        <div className="flex flex-col gap-2">{children}</div>
      </div>
      {hint ? <div className="text-[11px] leading-relaxed text-hint-text">{hint}</div> : null}
    </div>
  )
}
