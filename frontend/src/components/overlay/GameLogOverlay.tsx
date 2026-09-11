/*
 * 启动日志遮罩（Controls/GameLogOverlay.axaml 移植，React 版）：底部滑出全屏遮罩，
 * 只展示本次启动游戏的实时输出（LauncherAPI.GetLogText 内存日志），
 * 带级别着色、自动滚动到最新行；不浏览磁盘历史日志文件。
 */
import { useEffect, useRef, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import Icon from './Icon'
import { GetLaunchSnapshot, GetLogText } from '../../../wailsjs/go/bindings/LauncherAPI.js'
import { ClearLogs } from '../../../wailsjs/go/bindings/SystemAPI.js'
import { toast } from '@/components/ui/sonner'

interface Props {
  open: boolean
  onClose: () => void
}

interface LogLine { prefix: string; body: string; kind: string }

interface LaunchSnapshot { Phase?: number; VersionId?: string; Message?: string }

const REFRESH_MS = 1000
const PHASE_TEXTS = ['空闲', '启动中', '运行中', '失败', '已退出']

// ---- 级别着色（LaunchLogColorizer.Parse / Classify 等价实现） ----
const TIMESTAMP_PREFIX = /^(\[[^\]]*\]\s?)/
const MINECRAFT_LEVEL = /\[[^\[\]]+\/(INFO|WARN|ERROR|FATAL|DEBUG|TRACE)\]/i
const BRACKET_LEVEL = /\[(INFO|WARN(?:ING)?|ERROR|FATAL|DEBUG|TRACE)\]/i

function classify(body: string): string {
  if (/\[stderr\]/i.test(body)) return 'error'
  const mc = body.match(MINECRAFT_LEVEL)
  if (mc) return mapLevel(mc[1])
  const br = body.match(BRACKET_LEVEL)
  if (br) return mapLevel(br[1])
  if (body.includes('Done (')) return 'success'
  if (/Exception|SEVERE|失败/.test(body)) return 'error'
  if (/异常|已取消/.test(body)) return 'warning'
  return 'plain'
}

function mapLevel(level: string): string {
  return ({ INFO: 'info', WARN: 'warning', WARNING: 'warning', ERROR: 'error', FATAL: 'error' } as Record<string, string>)[level.toUpperCase()] ?? 'plain'
}

function parseLine(raw: string): LogLine {
  const line = raw ?? ''
  const match = line.match(TIMESTAMP_PREFIX)
  const prefix = match && match[1].length < line.length ? match[1] : ''
  const body = match && prefix ? line.slice(match[1].length) : line
  return { prefix, body, kind: classify(body) }
}

const KIND_CLASS: Record<string, string> = {
  plain: 'text-body-text',
  info: 'text-info',
  warning: 'text-warning',
  error: 'text-destructive',
  success: 'text-success',
}

export default function GameLogOverlay({ open, onClose }: Props) {
  const [lines, setLines] = useState<LogLine[]>([])
  const [snapshot, setSnapshot] = useState<LaunchSnapshot | null>(null)
  const [autoScroll, setAutoScroll] = useState(true)
  const [footerText, setFooterText] = useState('')
  const listRef = useRef<HTMLDivElement | null>(null)

  // 非 state 的轮询共享量（重渲染无关）
  const shared = useRef({ lastLogText: '', pollAbort: false, autoScroll })
  shared.current.autoScroll = autoScroll

  const phaseText = PHASE_TEXTS[snapshot?.Phase ?? 0] ?? '空闲'
  const subtitle = snapshot?.VersionId || '尚未启动游戏'
  const statusText = snapshot?.Message || '选择账号和游戏实例后即可启动。'

  function scrollToEnd() {
    if (!shared.current.autoScroll) return
    requestAnimationFrame(() => {
      const el = listRef.current
      if (el) el.scrollTop = el.scrollHeight
    })
  }

  useEffect(() => {
    shared.current.pollAbort = false

    async function refresh() {
      if (shared.current.pollAbort) return
      try {
        const [logText, snap] = await Promise.all([
          GetLogText(),
          GetLaunchSnapshot().catch(() => null) as Promise<LaunchSnapshot | null>,
        ])
        setSnapshot(snap)
        if (!logText || !logText.trim()) {
          if (shared.current.lastLogText.length > 0) {
            shared.current.lastLogText = ''
            setLines([])
            setFooterText('')
          }
          return
        }
        if (logText === shared.current.lastLogText) return // 日志没有变化，跳过重建
        shared.current.lastLogText = logText
        const parsed = logText.split(/\r\n|\n/).map(parseLine)
        setLines(parsed)
        setFooterText(`${parsed.length.toLocaleString()} 行 · 本次启动的实时输出`)
        scrollToEnd()
      } catch { /* 桥未启动时静默 */ }
    }

    let timer: ReturnType<typeof setInterval> | undefined
    if (open) {
      refresh()
      timer = setInterval(refresh, REFRESH_MS)
    }
    return () => {
      shared.current.pollAbort = true
      if (timer) clearInterval(timer)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  useEffect(() => { if (autoScroll) scrollToEnd() }, [autoScroll])

  async function copyAll() {
    const text = shared.current.lastLogText
    if (!text) return
    try {
      await navigator.clipboard.writeText(text)
      setFooterText('已复制全文到剪贴板。')
    } catch (e) {
      setFooterText(`复制失败：${(e as Error)?.message ?? e}`)
    }
  }

  async function clearLogs() {
    // PORTING_NOTE: LauncherAPI 无清空启动日志的方法，用 SystemAPI.ClearLogs 清启动器日志；
    // 内存游戏日志的清空待后端补齐（GameLaunchService.ClearLogText）后接入。
    try {
      const removed = await ClearLogs()
      shared.current.lastLogText = ''
      setLines([])
      setFooterText(`已清空 ${removed ?? 0} 条日志。`)
      toast.success('日志已清空')
    } catch (e) {
      toast.error(`清空失败：${(e as Error)?.message ?? e}`)
    }
  }

  if (!open) return null

  return (
    <div className={`gamelog-enter fixed inset-0 z-[90] flex flex-col bg-popover`} role="dialog" aria-label="启动日志">
      {/* 顶栏：标题 + 实例 / 阶段徽章 + 状态 + 操作按钮（原版 18,12,10,12 内边距） */}
      <div className="flex items-center gap-4 border-b border-subtle-border bg-background px-[18px] py-3 pr-2.5">
        <div className="flex flex-col gap-0.5">
          <span className="text-[16px] font-semibold text-primary-text">启动日志</span>
          <span className="text-[10px] text-subtext-text">{subtitle}</span>
        </div>

        <div className="flex min-w-0 flex-1 items-center gap-2">
          <Badge className="rounded-[7px] px-2 py-0.5 text-[9.5px] font-bold">{phaseText}</Badge>
          <span className="truncate text-[11px] text-secondary-text">{statusText}</span>
        </div>

        <label className="flex shrink-0 cursor-pointer items-center gap-1.5 text-[11px] text-secondary-text">
          <input
            type="checkbox"
            checked={autoScroll}
            onChange={(e) => setAutoScroll(e.target.checked)}
            style={{ accentColor: 'var(--accent)' }}
          />
          自动滚动
        </label>
        <Button variant="secondary" size="sm" className="shrink-0 px-3" onClick={copyAll}>复制全文</Button>
        <Button variant="secondary" size="sm" className="shrink-0 px-3" onClick={clearLogs}>清空</Button>
        <button className="window-button close" title="关闭日志" onClick={onClose}>
          <svg width="12" height="12" viewBox="0 0 12 12">
            <path d="M 2,2 L 10,10 M 10,2 L 2,10" stroke="currentColor" strokeWidth="1.4" strokeLinecap="square" fill="none" />
          </svg>
        </button>
      </div>

      {/* 日志区：WindowBg 圆角框（原版 r12 + MediumBorder） */}
      <div className="flex min-h-0 flex-1 flex-col p-4">
        <div className="relative min-h-0 flex-1 overflow-hidden rounded-xl border border-border bg-background p-2">
          {lines.length === 0 ? (
            <div className="flex h-full flex-col items-center justify-center gap-1.5">
              <span className="text-[28px] opacity-40">🖥️</span>
              <span className="text-[11px] text-hint-text">还没有启动日志，启动一次游戏后这里会实时显示输出。</span>
            </div>
          ) : (
            <div ref={listRef} className="h-full overflow-y-auto font-mono text-[11px] leading-[1.55]">
              {lines.map((ln, i) => (
                <div key={i} className="flex gap-1.5 whitespace-pre">
                  {ln.prefix ? <span className="text-hint-text">{ln.prefix}</span> : null}
                  <span className={KIND_CLASS[ln.kind] ?? KIND_CLASS.plain}>{ln.body}</span>
                </div>
              ))}
            </div>
          )}
        </div>
        <span className="mt-2.5 px-0.5 text-[10px] text-hint-text">{footerText}</span>
      </div>
    </div>
  )
}
