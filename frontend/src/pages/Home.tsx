/*
 * 工作区主页（Vue 版 src/views/HomeView.vue 的 React 平移）：
 * 主窗口工作区 = 左 2/3 组件画布 + 右 1/3 游戏启动区（MainWindow.axaml MainContentHost 2*,*）。
 */
import ComponentCanvas from '@/components/home/ComponentCanvas'
import LaunchPanel from '@/components/home/LaunchPanel'

export default function Home() {
  return (
    <div className="grid h-full min-h-0 grid-cols-[2fr_1fr]">
      <ComponentCanvas />
      <LaunchPanel />
    </div>
  )
}
