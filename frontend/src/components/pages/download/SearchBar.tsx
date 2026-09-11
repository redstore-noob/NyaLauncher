/*
 * 搜索栏（DownloadPage.axaml 各标签搜索框：ControlBg r10 + 放大镜 + 右侧筛选插槽）。
 * 自 Vue 版 SearchBar.vue 平移；受控输入 value + onChange。
 */
import { Search } from 'lucide-react'

interface Props {
  value: string
  placeholder?: string
  className?: string
  onChange: (v: string) => void
  children?: React.ReactNode
}

export default function SearchBar({ value, placeholder, className, onChange, children }: Props) {
  return (
    <div className={`flex items-center gap-2 rounded-md bg-muted px-3 py-2 ${className ?? ''}`}>
      <Search size={14} className="shrink-0 text-muted-text" />
      <input
        className="min-w-0 flex-1 border-none bg-transparent text-[13px] text-body-text outline-none placeholder:text-placeholder-text"
        type="text"
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
      />
      {children}
    </div>
  )
}
