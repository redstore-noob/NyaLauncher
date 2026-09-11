/*
 * 全局浮层宿主：等价 Vue 版 composables/dialog.js 的 mountOverlays()——
 * 把 NyaAlert + NyaPrompt + DownloadStatusPanel 挂载在应用根部（App.tsx 调用一次）。
 */
import NyaAlert from './NyaAlert'
import NyaPrompt from './NyaPrompt'
import DownloadStatusPanel from './DownloadStatusPanel'

export default function OverlayHost() {
  return (
    <>
      <NyaAlert />
      <NyaPrompt />
      <DownloadStatusPanel />
    </>
  )
}
