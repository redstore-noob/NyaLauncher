/** 占位页通用组件：页面移植 agent 用真实实现替换（参考 Vue 版 src/views/ 同名页面）。 */
export default function PlaceholderView({ title, note }: { title: string; note?: string }) {
  return (
    <div className="page">
      <h1 className="page-heading">{title}</h1>
      <p className="page-placeholder">待移植：此页面尚未从 Vue 版（frontend/src/views/）迁移，请由页面移植 agent 补充实现。</p>
      {note ? <p className="page-placeholder mt-2 text-[11px]">{note}</p> : null}
    </div>
  )
}
