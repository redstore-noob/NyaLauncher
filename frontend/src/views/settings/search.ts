/*
 * 设置中心搜索状态（等价 Vue 版 Hub ↔ 子页 defineExpose(applySearchFilter) 的调用协议）：
 * Hub 持有 query 与各页命中数；子页（Launcher/Personalization）经 useSettingsSearchContext()
 * 读取 query，计算卡片可见性与命中数后 setCount(key, n) 回传 Hub。
 * Context 缺省时（如子页被独立渲染）返回 null，页面侧跳过搜索逻辑。
 */
import { createContext, useContext } from 'react'

export interface SettingsSearchState {
  query: string
  counts: Record<string, number>
  setCount: (key: string, count: number) => void
}

export const SettingsSearchContext = createContext<SettingsSearchState | null>(null)

export function useSettingsSearch(): SettingsSearchState | null {
  return useContext(SettingsSearchContext)
}

/** 等价 Vue 版 applySearchFilter 的别名匹配（大小写不敏感的包含匹配）。 */
export function matchAliases(aliases: string[], query: string): boolean {
  const q = (query || '').trim().toLowerCase()
  if (!q) return true
  return aliases.some((t) => t.toLowerCase().includes(q))
}
