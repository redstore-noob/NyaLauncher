/*
 * 组件库抽屉（400px，等价 Vue 版 ComponentCanvas.vue 内联抽屉 / Avalonia ComponentLibraryView）：
 * 点击或拖到画布添加；已添加的组件禁用并标注「已添加」。
 */
import type { ComponentDefinition } from './componentRegistry'

interface Props {
  definitions: ComponentDefinition[]
  placedIds: Set<string>
  onClose: () => void
  onItemPointerDown: (def: ComponentDefinition, e: React.PointerEvent) => void
}

export default function ComponentLibraryDrawer({ definitions, placedIds, onClose, onItemPointerDown }: Props) {
  return (
    <div className="absolute inset-0 z-50" onPointerDown={onClose}>
      <div className="absolute inset-0 bg-overlay" />
      <aside
        className="drawer-in absolute bottom-0 left-0 top-0 flex w-[400px] flex-col border-r border-border bg-popover shadow-xl"
        onPointerDown={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-2 border-b border-subtle-border px-4 py-3">
          <span className="text-[15px] font-semibold text-foreground">组件库</span>
          <span className="text-[10px] text-hint-text">点击或拖到画布添加</span>
          <button
            className="ml-auto grid h-7 w-7 cursor-pointer place-items-center rounded-md text-[13px] hover:bg-accent"
            onClick={onClose}
          >✕</button>
        </div>
        <div className="flex-1 space-y-1.5 overflow-y-auto p-3">
          {definitions.map((d, i) => (
            <button
              key={d.id}
              className={`btn-anim flex h-[72px] w-full items-center gap-3 rounded-[14px] border p-3 text-left ${
                d.isPrimary
                  ? 'border-accent-bright bg-accent-deep text-primary-foreground'
                  : 'border-card-border bg-card text-foreground hover:bg-accent'
              }`}
              disabled={placedIds.has(d.id)}
              style={{
                animation: 'nya-pop-in 220ms var(--ease-emphasized-decelerate) both',
                animationDelay: `${i * 40}ms`,
              }}
              onPointerDown={(e) => onItemPointerDown(d, e)}
            >
              <span className={`grid size-9 shrink-0 place-items-center rounded-lg text-[15px] ${
                d.isPrimary ? 'bg-primary-foreground/20' : 'bg-muted'
              }`}>{d.glyph}</span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-[13px] font-semibold">{d.title}</span>
                <span className="block truncate text-[10px] text-muted-foreground">{d.description}</span>
              </span>
              <span className="shrink-0 text-[11px] text-muted-foreground">
                {placedIds.has(d.id) ? '已添加' : '⠿'}
              </span>
            </button>
          ))}
        </div>
      </aside>
    </div>
  )
}
