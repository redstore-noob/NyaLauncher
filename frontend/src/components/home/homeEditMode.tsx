/*
 * 主页编辑模式 Context（等价 Vue 版 App.vue provide('homeEditMode') / inject）。
 * editMode 状态本身在 App.tsx（状态栏「编辑模式」按钮），经此 Context 下发给 ComponentCanvas。
 */
import { createContext, useContext } from 'react'

export interface HomeEditModeValue {
  editing: boolean
}

export const HomeEditModeContext = createContext<HomeEditModeValue>({ editing: false })

export function useHomeEditMode(): HomeEditModeValue {
  return useContext(HomeEditModeContext)
}
