/*
 * 下载状态悬浮面板：监听 download:progress / download:pauseChanged 全局事件。
 * Phase 为 int 枚举：0 Idle / 1 Preparing / 2 Downloading / 3 Completed / 4 Failed / 5 Cancelled。
 * 进行中（1-2）常驻展示；终态（3-5）停留 4 秒后自动收起。
 * 原版还提供「打开文件夹」按钮（依赖本地 shell），暂未移植。
 */
import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { EventsOn, EventsOff } from '../../../wailsjs/runtime/runtime.js'
import { CancelDownload } from '../../../wailsjs/go/bindings/DownloadAPI.js'

const TERMINAL_HIDE_MS = 4000

interface Snapshot {
  Phase: number
  StageName?: string
  VersionID?: string
  Percentage?: number
  CompletedBytes?: number
  TotalBytes?: number
  TotalFiles?: number
  CompletedFiles?: number
  BytesPerSecond?: number
  Detail?: string
}

export default function DownloadStatusPanel() {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const [paused, setPaused] = useState(false)
  const [hiding, setHiding] = useState(false)
  const terminalTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  useEffect(() => {
    EventsOn('download:progress', onProgress)
    EventsOn('download:pauseChanged', (v: unknown) => setPaused(!!v))
    return () => {
      clearTimeout(terminalTimer.current)
      EventsOff('download:progress')
      EventsOff('download:pauseChanged')
    }
  }, [])

  function onProgress(snap: Snapshot) {
    setHiding(false)
    setSnapshot(snap)
    clearTimeout(terminalTimer.current)
    if (snap && snap.Phase >= 3) {
      terminalTimer.current = setTimeout(() => { setHiding(true) }, TERMINAL_HIDE_MS)
    }
  }

  async function onCancel() {
    try { await CancelDownload() } catch { /* 取消失败保持面板展示 */ }
  }

  const s = snapshot
  const visible = !!s && s.Phase >= 1 && s.Phase <= 5 && !hiding
  const fileName = !s ? '准备就绪' : (s.StageName || s.VersionID || '下载中')
  const percent = !s ? 0
    : (s.Percentage ?? 0) > 0 ? Math.min(100, s.Percentage!)
    : (s.TotalBytes ?? 0) > 0 ? Math.min(100, (s.CompletedBytes! / s.TotalBytes!) * 100)
    : 0
  const indeterminate = !!s && (s.TotalBytes ?? 0) <= 0 && (s.Percentage ?? 0) <= 0
  const detail = (() => {
    if (!s) return ''
    const parts: string[] = []
    if ((s.BytesPerSecond ?? 0) > 0) parts.push(`${((s.BytesPerSecond ?? 0) / 1048576).toFixed(1)} MiB/s`)
    if ((s.TotalBytes ?? 0) > 0) {
      parts.push(`${((s.CompletedBytes ?? 0) / 1048576).toFixed(1)} / ${(s.TotalBytes! / 1048576).toFixed(1)} MiB`)
    } else if ((s.TotalFiles ?? 0) > 0) {
      parts.push(`${s.CompletedFiles} / ${s.TotalFiles} 个文件`)
    }
    if (s.Detail) parts.push(s.Detail)
    return parts.join(' · ')
  })()

  const R = 20
  const CIRC = 2 * Math.PI * R

  return (
    <div
      className={[
        'fixed right-5 bottom-[46px] z-[930] flex w-[220px] flex-col gap-2 rounded-xl',
        'border border-border bg-popover p-3 shadow-lg',
        visible ? 'nya-dlpanel-enter' : 'nya-dlpanel-leave',
      ].join(' ')}
      style={{ visibility: visible ? 'visible' : 'hidden' }}
      aria-hidden={!visible}
    >
      <div className="flex">
        <span className="overflow-hidden text-xs text-secondary-text text-ellipsis whitespace-nowrap" title={fileName}>{fileName}</span>
      </div>
      <div className="relative flex justify-center">
        <svg width="46" height="46" viewBox="0 0 46 46">
          <circle cx="23" cy="23" r={R} fill="none" stroke="var(--badge-bg)" strokeWidth="6" />
          <circle
            cx="23" cy="23" r={R} fill="none"
            stroke="var(--accent)" strokeWidth="6" strokeLinecap="round"
            strokeDasharray={`${CIRC}`}
            strokeDashoffset={`${CIRC * (1 - percent / 100)}`}
            transform="rotate(-90 23 23)"
          />
        </svg>
        <span
          className={`absolute inset-0 flex items-center justify-center text-xs font-bold text-primary ${indeterminate ? 'nya-dl-blink' : ''}`}
        >
          {indeterminate ? '…' : `${Math.round(percent)}%`}
        </span>
      </div>
      <div className="flex items-center gap-2">
        <span className="min-w-0 flex-1 overflow-hidden text-[11px] text-hint-text text-ellipsis whitespace-nowrap" title={detail}>
          {paused ? '已暂停 · ' : ''}{detail}
        </span>
        <Button variant="secondary" size="sm" className="h-6 flex-none px-3 text-[11px]" onClick={onCancel}>取消</Button>
      </div>
    </div>
  )
}
