/*
 * 右侧常驻「游戏启动区」：账户 + 版本 + 启动按钮（GameLaunchPanel.axaml 的 React 平移，
 * 参照 Vue 版 LaunchPanel.vue 逐功能对照）。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  GetAccounts, GetSelectedAccount, GetAccountStableKey, MoveAccountToTop, SelectAccountByStableKey,
} from '../../../wailsjs/go/bindings/AccountAPI.js'
import { GetCurrentInstanceSnapshot, SelectInstance, GetInstanceDisplayVersion } from '../../../wailsjs/go/bindings/InstanceAPI.js'
import { GetLaunchSnapshot, GetMemoryDecision, Launch } from '../../../wailsjs/go/bindings/LauncherAPI.js'
import { alert as nyaAlert } from '@/components/overlay/dialog'
import { EventsOn, EventsOff } from '../../../wailsjs/runtime/runtime.js'

interface Account { Type?: string; DisplayName?: string }

export default function LaunchPanel() {
  const router = useNavigate()

  const [accounts, setAccounts] = useState<Account[]>([])
  const [selectedAccount, setSelectedAccount] = useState<Account | null>(null)
  const [versions, setVersions] = useState<string[]>([])
  const [selectedVersion, setSelectedVersion] = useState('')
  const [displayNames, setDisplayNames] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [statusText, setStatusText] = useState('')
  const [memoryHint, setMemoryHint] = useState('')
  const mountedRef = useRef(true)

  function accountKey(a: Account) {
    // 同步 GetAccountStableKey 成本高，这里用 Type+Name 近似；选择时再走真实 StableKey
    return `${a.Type}:${a.DisplayName}`
  }

  const typeLabel = useCallback((a?: Account | null) => {
    const map: Record<string, string> = { offline: '离线', microsoft: '正版', authlib: '外置登录' }
    return (a?.Type && map[a.Type]) || a?.Type || ''
  }, [])

  const selectedAccountKey = selectedAccount ? accountKey(selectedAccount) : ''
  const avatarFallback = selectedAccount?.DisplayName ? selectedAccount.DisplayName[0].toUpperCase() : '?'
  const launchButtonText = busy ? (statusText || '正在启动…') : '启动游戏'
  const canLaunch = !busy && !!selectedAccount && !!selectedVersion

  const loadAccounts = useCallback(async () => {
    try {
      const list = (await GetAccounts()) ?? []
      const current = await GetSelectedAccount().catch(() => null)
      if (!mountedRef.current) return
      setAccounts(list)
      setSelectedAccount(current)
    } catch (e: any) {
      console.error('加载账户失败', e)
      await nyaAlert('加载账户失败：' + (e?.message ?? e), { severity: 'error' })
    }
  }, [])

  const onAccountChange = useCallback(async (key: string) => {
    const found = accounts.find((a) => accountKey(a) === key)
    if (!found) return
    setSelectedAccount(found)
    try {
      const stable = await GetAccountStableKey(found as any)
      await SelectAccountByStableKey(stable)
      await MoveAccountToTop(found as any) // 原版语义：选中即置顶
      setAccounts((await GetAccounts()) ?? [])
    } catch (e) {
      console.error('切换账户失败', e)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accounts])

  function applySnapshot(snap: any) {
    if (!snap) return
    const ids: string[] = snap.VersionIds ?? []
    setVersions(ids)
    setSelectedVersion(snap.SelectedVersionId ?? '')
    // 解析显示版本号（加载器实例目录名 ≠ MC 版本，如 fabric-loader-x 显示其继承的 MC 版本）
    for (const v of ids) {
      setDisplayNames((prev) => {
        if (prev[v]) return prev
        GetInstanceDisplayVersion(v)
          .then((d) => setDisplayNames((p) => ({ ...p, [v]: d || v })))
          .catch(() => setDisplayNames((p) => ({ ...p, [v]: v })))
        return prev
      })
    }
  }

  const onVersionChange = useCallback(async (v: string) => {
    setSelectedVersion(v)
    if (!v) return
    try {
      await SelectInstance(v)
    } catch (e) {
      console.error('切换版本失败', e)
    }
  }, [])

  function applyLaunchState(snap: any) {
    if (!snap) return
    const isBusy = !!snap.Title || !!snap.Message
    setBusy(isBusy)
    if (isBusy) setStatusText(snap.Message || '正在准备游戏环境…')
  }

  const loadMemoryHint = useCallback(async () => {
    try {
      const decision = await GetMemoryDecision(null)
      if (decision) {
        setMemoryHint(`系统内存 ${(decision.TotalMemoryMb / 1024).toFixed(1)} GiB · 自动策略上限 ${decision.MaximumMemoryMb} MiB`)
      }
    } catch (e) {
      console.error('读取内存策略失败', e)
    }
  }, [])

  const onLaunch = useCallback(async () => {
    if (!selectedAccount) return nyaAlert('请先选择账户', { severity: 'warning' })
    if (!selectedVersion) return nyaAlert('请先选择游戏版本', { severity: 'warning' })
    setBusy(true)
    setStatusText('正在启动…')
    try {
      // Launch 阻塞至启动流程结束，Wails 在后台 goroutine 执行
      const result = await Launch('', null) // ctx 占位（见 PORTING_NOTES）
      setStatusText(result?.Success ? '游戏进程已创建。' : result?.Message || '启动失败。')
    } catch (e: any) {
      setStatusText(`启动失败：${e}`)
      console.error('启动失败', e)
    } finally {
      setBusy(false)
      loadMemoryHint()
    }
  }, [selectedAccount, selectedVersion, loadMemoryHint])

  // 初始化 + 事件订阅（useEffect 清理，等价 Vue onMounted/onUnmounted）
  useEffect(() => {
    mountedRef.current = true
    ;(async () => {
      await Promise.all([loadAccounts(), loadMemoryHint()])
      try {
        applySnapshot(await GetCurrentInstanceSnapshot())
        applyLaunchState(await GetLaunchSnapshot())
      } catch (e) {
        console.error('初始化启动面板失败', e)
      }
    })()
    EventsOn('instance:changed', applySnapshot)
    EventsOn('launch:changed', applyLaunchState)
    return () => {
      mountedRef.current = false
      EventsOff('instance:changed')
      EventsOff('launch:changed')
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const goDownloads = useMemo(() => () => router('/download'), [router])
  const goAccount = useMemo(() => () => router('/settings/account'), [router])

  return (
    <aside className="h-full overflow-y-auto border-l border-subtle-border bg-background">
      <div className="mx-3.5 my-3 flex h-[calc(100%-24px)] flex-col">
        {/* 标题 */}
        <div className="mb-2.5 ml-0.5 flex items-center gap-2">
          <span className="text-[15px] font-semibold text-foreground">快速启动</span>
          <Badge>{versions.length} 个版本</Badge>
        </div>

        {/* 账户行：头像 + 账户选择 */}
        <Card className="flex items-center gap-3 rounded-xl p-2.5 px-3">
          <div className="flex size-11 shrink-0 items-center justify-center overflow-hidden rounded-md bg-badge">
            <span className="text-lg font-bold text-primary">{avatarFallback}</span>
            {/* TODO: 皮肤头像 AsyncImage（离线皮肤/皮肤站/正版档案解析）待接 */}
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <Select value={selectedAccountKey} onValueChange={onAccountChange}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="选择账户" />
              </SelectTrigger>
              <SelectContent>
                {accounts.map((a) => (
                  <SelectItem key={accountKey(a)} value={accountKey(a)}>
                    {a.DisplayName}（{typeLabel(a)}）
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <span className="truncate text-[10px] text-hint-text">
              {selectedAccount ? `${typeLabel(selectedAccount)} · ${selectedAccount.DisplayName}` : '尚无账户，点击右上角「＋」添加'}
            </span>
          </div>
        </Card>

        {/* 版本列表头 */}
        <div className="mb-1.5 ml-0.5 mt-3 text-[13px] font-semibold text-secondary-text">游戏版本</div>

        {/* 版本选择 */}
        <div className="flex flex-1 flex-col gap-1.5">
          {versions.length > 0 ? (
            <>
              <Select value={selectedVersion} onValueChange={onVersionChange}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="选择游戏版本" />
                </SelectTrigger>
                <SelectContent>
                  {versions.map((v) => (
                    <SelectItem key={v} value={v}>{displayNames[v] || v}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <span className="truncate text-[10px] text-hint-text">启动前可在此切换版本</span>
            </>
          ) : (
            <div className="flex flex-1 flex-col items-center justify-center gap-2">
              <span className="text-[10px] text-hint-text">还没有游戏版本</span>
              <Button variant="secondary" size="sm" onClick={goDownloads}>前往资源下载</Button>
            </div>
          )}
        </div>

        {/* 启动区 */}
        <div className="mt-2.5 flex flex-col items-stretch gap-1.5">
          <span className="text-center text-[10px] text-hint-text">{statusText}</span>
          {memoryHint ? <span className="text-center text-[10px] text-hint-text">{memoryHint}</span> : null}
          <Button
            className="h-11 gap-2 rounded-xl border border-accent-bright bg-accent-deep text-sm font-semibold text-[var(--white)] hover:bg-accent-deep"
            disabled={!canLaunch}
            onClick={onLaunch}
          >
            <span className="text-xs">▶</span>
            <span>{launchButtonText}</span>
          </Button>
          {!selectedAccount ? (
            <Button
              variant="link"
              size="sm"
              className="text-[11px] text-[var(--link-text-color)] no-underline hover:underline"
              onClick={goAccount}
            >
              还没有账户？去添加一个 →
            </Button>
          ) : null}
        </div>
      </div>
    </aside>
  )
}
