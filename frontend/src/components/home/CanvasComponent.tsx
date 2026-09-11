/*
 * 组件内容渲染器：按 component id 渲染画布组件的内部内容
 * （对应 Avalonia Components/*Component.axaml；外壳由 ComponentCanvas.tsx 负责）。
 * 12 个组件语义与 Vue 版 CanvasComponentView.vue 逐个对照：
 * game-launch / account-selector / game-instance-selector / memory-usage /
 * version-manager / clock / text / music-player / world-launch / server-join /
 * download-task-progress / skin-cape-editor。
 */
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  GetAccounts, GetSelectedAccount, GetAccountStableKey, SelectAccountByStableKey, GetAvatarUrl,
} from '../../../wailsjs/go/bindings/AccountAPI.js'
import { GetCurrentInstanceSnapshot, SelectInstance } from '../../../wailsjs/go/bindings/InstanceAPI.js'
import { GetLaunchSnapshot, Launch } from '../../../wailsjs/go/bindings/LauncherAPI.js'
import { GetMemorySnapshot } from '../../../wailsjs/go/bindings/MonitorAPI.js'
import {
  GetCurrentTrack, NextTrack, PausePlayback, PreviousTrack, ResumePlayback, SeekPlayback,
} from '../../../wailsjs/go/bindings/MusicAPI.js'
import { GetRecentWorlds } from '../../../wailsjs/go/bindings/WorldAPI.js'
import { PingServer } from '../../../wailsjs/go/bindings/ServerAPI.js'
import { GetCurrentDownloadSnapshot } from '../../../wailsjs/go/bindings/DownloadAPI.js'
import { EventsOn, EventsOff } from '../../../wailsjs/runtime/runtime.js'
import { getAudioSnapshot, subscribeAudio, previewSeek } from './audioBridge'

interface Account { Type?: string; DisplayName?: string }
interface WorldInfo { DirectoryPath?: string; Name?: string; OwnerVersionId?: string }

function accountKeyOf(a: Account) {
  return `${a.Type}:${a.DisplayName}`
}
function typeLabel(a?: Account | null) {
  const map: Record<string, string> = { offline: '离线', microsoft: '正版', authlib: '外置登录' }
  return (a?.Type && map[a.Type]) || a?.Type || ''
}

export default function CanvasComponent({ id, title }: { id: string; title?: string }) {
  const router = useNavigate()

  // ---- 账号 / 版本共享状态（account-selector / game-instance-selector /
  //      game-launch 共用同一份选中值，与原版全局选中语义一致） ----
  const [accounts, setAccounts] = useState<Account[]>([])
  const [currentAccount, setCurrentAccount] = useState<Account | null>(null)
  const [versions, setVersions] = useState<string[]>([])
  const [versionId, setVersionId] = useState('')
  const [accountAvatar, setAccountAvatar] = useState('')

  // ---- game-launch 启动 ----
  const [busy, setBusy] = useState(false)
  const [statusText, setStatusText] = useState('')

  // ---- memory-usage：2s 轮询（原版 DispatcherTimer 间隔 2s） ----
  const [mem, setMem] = useState({ launcher: 0, jvm: 0, javaCount: 0 })

  // ---- clock ----
  const [now, setNow] = useState(new Date())

  // ---- text（localStorage 持久化） ----
  const [textValue, setTextValue] = useState(() => localStorage.getItem(`canvas.text.${id}`) ?? '')

  // ---- music-player：播控 + 曲目 + 进度（MusicAPI + audioBridge 外部存储） ----
  const audio = useSyncExternalStore(subscribeAudio, getAudioSnapshotFn)
  const [trackName, setTrackName] = useState('')

  // ---- world-launch ----
  const [worlds, setWorlds] = useState<WorldInfo[]>([])
  const [worldError, setWorldError] = useState('')
  const [worldLaunching, setWorldLaunching] = useState('')

  // ---- server-join ----
  const [serverAddress, setServerAddress] = useState(() => localStorage.getItem('canvas.server.address') ?? '')
  const [serverPinging, setServerPinging] = useState(false)
  const [serverBusy, setServerBusy] = useState(false)
  const [serverError, setServerError] = useState('')
  const [serverSummary, setServerSummary] = useState('')

  // ---- download-task-progress ----
  const [dl, setDl] = useState({ active: false, stageName: '', detail: '', percentage: 0, fileText: '', doneText: '' })

  const mountedRef = useRef(true)

  // 账号头像（GetAvatarUrl 返回 8×8 头部 data URI；失败保留首字母占位）
  useEffect(() => {
    setAccountAvatar('')
    if (!currentAccount) return
    GetAccountStableKey(currentAccount as any)
      .then((key) => GetAvatarUrl(key))
      .then((uri) => { if (mountedRef.current && uri) setAccountAvatar(uri) })
      .catch(() => { /* 回退首字母占位 */ })
  }, [currentAccount])

  // text 持久化
  useEffect(() => {
    localStorage.setItem(`canvas.text.${id}`, textValue ?? '')
  }, [id, textValue])

  // server 地址持久化
  useEffect(() => {
    localStorage.setItem('canvas.server.address', serverAddress ?? '')
  }, [serverAddress])

  async function selectAccount(key: string) {
    const found = accounts.find((a) => accountKeyOf(a) === key)
    if (!found) return
    setCurrentAccount(found)
    try {
      const stable = await GetAccountStableKey(found as any)
      await SelectAccountByStableKey(stable)
    } catch (e) {
      console.error('[canvas] 切换账号失败', e)
    }
  }

  // 版本切换（watch versionId → SelectInstance）
  useEffect(() => {
    if (!versionId) return
    SelectInstance(versionId).catch((e) => console.error('[canvas] 切换版本失败', e))
  }, [versionId])

  function applySnapshot(snap: any) {
    if (!snap) return
    setVersions(snap.VersionIds ?? [])
    if (snap.SelectedVersionId) setVersionId(snap.SelectedVersionId)
  }

  async function onLaunch() {
    setBusy(true)
    setStatusText('正在启动…')
    try {
      const result = await Launch('', null) // ctx 占位（同 LaunchPanel 的移植约定）
      setStatusText(result?.Success ? '游戏进程已创建。' : result?.Message || '启动失败。')
    } catch (e: any) {
      setStatusText(`启动失败：${e}`)
    } finally {
      setBusy(false)
    }
  }

  async function pollMemory() {
    try {
      const s = await GetMemorySnapshot()
      if (mountedRef.current && s) {
        setMem({ launcher: s.LauncherMemoryMb ?? 0, jvm: s.JvmMemoryMb ?? 0, javaCount: s.JavaProcessCount ?? 0 })
      }
    } catch { /* 桥未启动时静默 */ }
  }

  function trackNameOf(track: any) {
    const p = String(track?.FilePath ?? '')
    const base = p.split(/[\\/]/).pop() ?? ''
    return base.replace(/\.[^.]+$/, '')
  }

  async function loadCurrentTrack() {
    try {
      setTrackName(trackNameOf(await GetCurrentTrack()))
    } catch { /* 桥未启动时静默 */ }
  }

  const musicToggle = useCallback(() => {
    const fn = getAudioSnapshot().playing ? PausePlayback : ResumePlayback
    fn().catch(() => {})
  }, [])
  const musicPrev = useCallback(() => { PreviousTrack().catch(() => {}) }, [])
  const musicNext = useCallback(() => { NextTrack().catch(() => {}) }, [])

  const seekMusic = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
    if (getAudioSnapshot().durationMs <= 0) return
    const rect = e.currentTarget.getBoundingClientRect()
    const ratio = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width))
    const ms = Math.round(ratio * getAudioSnapshot().durationMs)
    SeekPlayback(ms).catch(() => {})
    previewSeek(ms) // audioBridge 的 <audio> 会由 music:seek / timeupdate 同步
  }, [])

  const musicPlaying = audio.playing
  const musicPct = audio.durationMs > 0 ? Math.min(100, (audio.positionMs / audio.durationMs) * 100) : 0

  async function loadWorlds() {
    try {
      setWorlds((await GetRecentWorlds(8)) ?? [])
    } catch (e) {
      setWorldError('世界列表不可用')
      console.error('[canvas] 加载最近世界失败', e)
    }
  }

  async function launchWorld(w: WorldInfo) {
    if (worldLaunching) return
    setWorldLaunching(w.DirectoryPath ?? '')
    try {
      if (w.OwnerVersionId) await SelectInstance(w.OwnerVersionId)
      const result = await Launch('', null)
      if (!result?.Success) console.error('[canvas] 启动世界失败', result?.Message)
    } catch (e) {
      console.error('[canvas] 启动世界失败', e)
    } finally {
      setWorldLaunching('')
    }
  }

  async function pingServer() {
    const addr = serverAddress.trim()
    if (!addr || serverPinging) return
    setServerPinging(true)
    setServerError('')
    try {
      const started = performance.now()
      const status = await PingServer(addr, 5000)
      const latencyMs = Math.round(performance.now() - started)
      if (!status) {
        setServerSummary('')
        setServerError('无法连接到服务器')
      } else {
        setServerSummary(`${latencyMs}ms · ${status.OnlinePlayers ?? 0}/${status.MaxPlayers ?? 0} 在线`
          + (status.VersionName ? ` · ${status.VersionName}` : ''))
      }
    } catch (e: any) {
      setServerSummary('')
      setServerError(`Ping 失败：${e?.message ?? e}`)
    } finally {
      setServerPinging(false)
    }
  }

  async function launchForServer() {
    setServerBusy(true)
    try {
      await Launch('', null)
    } catch (e) {
      console.error('[canvas] 启动失败', e)
    } finally {
      setServerBusy(false)
    }
  }

  // ---- download-task-progress：快照 + download:progress 事件 ----
  const DL_PHASE_TEXTS = ['空闲', '准备中', '下载中', '已完成', '失败', '已取消']

  function applyDownloadSnapshot(snap: any) {
    if (!snap || (snap.Phase ?? 0) === 0) {
      const doneText = DL_PHASE_TEXTS[snap?.Phase] && snap?.Phase > 0
        ? `上次任务：${DL_PHASE_TEXTS[snap.Phase]}`
        : ''
      setDl({ active: false, stageName: '', detail: '', percentage: 0, fileText: '', doneText })
      return
    }
    const files = (snap.TotalFiles ?? 0) > 0 ? ` ${snap.CompletedFiles}/${snap.TotalFiles} 文件 ·` : ''
    setDl({
      active: snap.Phase === 1 || snap.Phase === 2,
      stageName: `${snap.VersionID || ''} ${snap.StageName || DL_PHASE_TEXTS[snap.Phase] || ''}`.trim(),
      detail: snap.Detail || '',
      percentage: snap.Percentage ?? 0,
      fileText: `${files} ${fmtBytes(snap.CompletedBytes)} / ${fmtBytes(snap.TotalBytes)} · ${fmtBytes(snap.BytesPerSecond)}/s`.trim(),
      doneText: '',
    })
  }

  // 初始化 + 事件订阅（等价 Vue onMounted / onUnmounted，useEffect 清理）
  useEffect(() => {
    mountedRef.current = true
    ;(async () => {
      try {
        const list = (await GetAccounts()) ?? []
        const current = await GetSelectedAccount().catch(() => null)
        if (mountedRef.current) { setAccounts(list); setCurrentAccount(current) }
      } catch (e) {
        console.error('[canvas] 加载账号失败', e)
      }
      try {
        applySnapshot(await GetCurrentInstanceSnapshot())
        const snap = await GetLaunchSnapshot()
        if (snap) setStatusText(snap.Message || '')
      } catch (e) {
        console.error('[canvas] 初始化组件数据失败', e)
      }
    })()
    loadWorlds()
    loadCurrentTrack()
    GetCurrentDownloadSnapshot()
      .then((snap) => { if (mountedRef.current) applyDownloadSnapshot(snap) })
      .catch(() => { /* 桥未启动时静默 */ })
    EventsOn('instance:changed', applySnapshot)
    EventsOn('music:trackChanged', (track: any) => setTrackName(track ? trackNameOf(track) : ''))
    EventsOn('download:progress', (snap: any) => applyDownloadSnapshot(snap))
    const clockTimer = setInterval(() => setNow(new Date()), 1000)
    const memTimer = setInterval(pollMemory, 2000)
    pollMemory()
    return () => {
      mountedRef.current = false
      EventsOff('instance:changed')
      EventsOff('music:trackChanged')
      EventsOff('download:progress')
      clearInterval(clockTimer)
      clearInterval(memTimer)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const clockTime = useMemo(() =>
    now.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }), [now])
  const clockDate = useMemo(() =>
    now.toLocaleDateString('zh-CN', { month: 'long', day: 'numeric', weekday: 'long' }), [now])

  const canLaunch = !!currentAccount && !!versionId
  const launcherPct = Math.min(100, (mem.launcher / 1024) * 100)
  const jvmPct = Math.min(100, (mem.jvm / 4096) * 100)

  const selectCls = 'h-7 min-w-0 rounded-md border border-input bg-muted px-1.5 text-[11px] text-foreground outline-none'

  return (
    <div className="flex h-full min-h-0 flex-col gap-1.5 text-foreground">
      {/* 启动游戏（GameLaunchComponent，主卡）：账号 + 版本下拉 + 启动 */}
      {id === 'game-launch' ? (
        <>
          <div className="flex items-center gap-1.5">
            <select
              className={`${selectCls} flex-1`}
              value={currentAccount ? accountKeyOf(currentAccount) : ''}
              onChange={(e) => selectAccount(e.target.value)}
            >
              <option value="" disabled>选择账号</option>
              {accounts.map((a) => (
                <option key={accountKeyOf(a)} value={accountKeyOf(a)}>{a.DisplayName}（{typeLabel(a)}）</option>
              ))}
            </select>
            <select
              className={`${selectCls} flex-1`}
              value={versionId}
              onChange={(e) => setVersionId(e.target.value)}
            >
              <option value="" disabled>选择版本</option>
              {versions.map((v) => (
                <option key={v} value={v}>{v}</option>
              ))}
            </select>
          </div>
          <button
            className="btn btn-accent btn-anim h-8 shrink-0 cursor-pointer rounded-lg text-[12px] font-semibold"
            disabled={!canLaunch || busy}
            onClick={onLaunch}
          >
            {busy ? '正在启动…' : '▶ 启动游戏'}
          </button>
          {statusText ? <span className="truncate text-[10px] text-hint-text">{statusText}</span> : null}
        </>
      ) : null}

      {/* 账号选择（AccountSelectorComponent） */}
      {id === 'account-selector' ? (
        <>
          <div className="flex items-center gap-1.5">
            {/* 头像：真实皮肤头像（AccountAPI.GetAvatarUrl），失败回退首字母 */}
            <div className="flex size-7 flex-none items-center justify-center overflow-hidden rounded-md bg-badge">
              {accountAvatar ? (
                <img src={accountAvatar} alt="" className="size-full object-contain [image-rendering:pixelated]" />
              ) : (
                <span className="text-xs font-bold text-primary">
                  {(currentAccount?.DisplayName || '?').trim()[0]?.toUpperCase() || '?'}
                </span>
              )}
            </div>
            <select
              className={`${selectCls} flex-1`}
              value={currentAccount ? accountKeyOf(currentAccount) : ''}
              onChange={(e) => selectAccount(e.target.value)}
            >
              <option value="" disabled>选择账号</option>
              {accounts.map((a) => (
                <option key={accountKeyOf(a)} value={accountKeyOf(a)}>{a.DisplayName}（{typeLabel(a)}）</option>
              ))}
            </select>
          </div>
          <span className="truncate text-[10px] text-hint-text">
            {currentAccount ? `当前：${currentAccount.DisplayName}` : '尚无账号'}
          </span>
        </>
      ) : null}

      {/* 游戏实例选择（GameInstanceSelectorComponent） */}
      {id === 'game-instance-selector' ? (
        <>
          <select
            className={`${selectCls} w-full`}
            value={versionId}
            onChange={(e) => setVersionId(e.target.value)}
          >
            <option value="" disabled>选择游戏版本</option>
            {versions.map((v) => (
              <option key={v} value={v}>{v}</option>
            ))}
          </select>
          <span className="truncate text-[10px] text-hint-text">{versions.length} 个已安装版本</span>
        </>
      ) : null}

      {/* 内存使用（MemoryUsageComponent）：MonitorAPI.GetMemorySnapshot 轮询 */}
      {id === 'memory-usage' ? (
        <div className="flex flex-1 flex-col justify-center gap-1">
          <div className="flex items-baseline justify-between">
            <span className="text-[10px] text-muted-foreground">启动器</span>
            <span className="text-[13px] font-semibold">{mem.launcher.toFixed(0)} MB</span>
          </div>
          <div className="h-1.5 overflow-hidden rounded-full bg-muted">
            <div className="h-full rounded-full bg-primary transition-[width] duration-500" style={{ width: `${launcherPct}%` }} />
          </div>
          <div className="flex items-baseline justify-between">
            <span className="text-[10px] text-muted-foreground">JVM（{mem.javaCount} 进程）</span>
            <span className="text-[13px] font-semibold">{mem.jvm.toFixed(0)} MB</span>
          </div>
          <div className="h-1.5 overflow-hidden rounded-full bg-muted">
            <div className="h-full rounded-full bg-success transition-[width] duration-500" style={{ width: `${jvmPct}%` }} />
          </div>
        </div>
      ) : null}

      {/* 版本管理（VersionManagerComponent，lite）：当前版本 + 跳转 */}
      {id === 'version-manager' ? (
        <div className="flex flex-1 items-center gap-2">
          <span className="truncate text-[12px] font-semibold">{versionId || '未选择版本'}</span>
          <Button variant="secondary" size="sm" className="ml-auto h-7 shrink-0 text-[11px]"
            onClick={() => router('/versions')}>管理</Button>
        </div>
      ) : null}

      {/* 时钟（前端补充组件） */}
      {id === 'clock' ? (
        <div className="flex flex-1 flex-col items-center justify-center leading-tight">
          <span className="text-[22px] font-semibold tabular-nums">{clockTime}</span>
          <span className="text-[10px] text-hint-text">{clockDate}</span>
        </div>
      ) : null}

      {/* 文本便签（前端补充组件；内容 localStorage 持久化） */}
      {id === 'text' ? (
        <textarea
          value={textValue}
          placeholder="双击输入文字…"
          onChange={(e) => setTextValue(e.target.value)}
          onPointerDown={(e) => e.stopPropagation()}
          className="h-full w-full resize-none rounded-md border border-input bg-muted p-1.5 text-[11px] text-foreground outline-none"
        />
      ) : null}

      {/* 音乐控制（MusicPlayerComponent）：MusicAPI 播控 + audioBridge 进度桥 */}
      {id === 'music-player' ? (
        <div className="flex min-h-0 flex-1 flex-col justify-center gap-1.5">
          <span className="truncate text-center text-[11px] font-semibold" title={trackName}>
            {trackName || '未在播放'}
          </span>
          <div
            className="group relative h-1.5 shrink-0 cursor-pointer overflow-hidden rounded-full bg-muted"
            title="点击跳转进度"
            onPointerDown={(e) => e.stopPropagation()}
            onClick={seekMusic}
          >
            <div className="h-full rounded-full bg-primary transition-[width] duration-200" style={{ width: `${musicPct}%` }} />
          </div>
          <div className="flex items-center justify-between">
            <span className="font-mono text-[9px] text-hint-text tabular-nums">
              {fmtMs(audio.positionMs)} / {fmtMs(audio.durationMs)}
            </span>
            <div className="flex items-center gap-1">
              <button className="grid size-6 cursor-pointer place-items-center rounded-md text-[12px] hover:bg-accent"
                title="上一首" onPointerDown={(e) => e.stopPropagation()} onClick={musicPrev}>⏮</button>
              <button className="grid size-6 cursor-pointer place-items-center rounded-md text-[12px] hover:bg-accent"
                title={musicPlaying ? '暂停' : '播放'} onPointerDown={(e) => e.stopPropagation()} onClick={musicToggle}>
                {musicPlaying ? '⏸' : '▶'}
              </button>
              <button className="grid size-6 cursor-pointer place-items-center rounded-md text-[12px] hover:bg-accent"
                title="下一首" onPointerDown={(e) => e.stopPropagation()} onClick={musicNext}>⏭</button>
            </div>
          </div>
          <button
            className="self-center cursor-pointer text-[9px] text-hint-text hover:text-primary"
            onPointerDown={(e) => e.stopPropagation()}
            onClick={() => router('/music')}
          >打开音乐播放器 →</button>
        </div>
      ) : null}

      {/* 最近的世界（WorldLaunchComponent）：WorldAPI.GetRecentWorlds + 点击启动所属实例 */}
      {id === 'world-launch' ? (
        worlds.length === 0 ? (
          <div className="flex flex-1 items-center justify-center text-[10px] text-hint-text">
            {worldError || '还没有世界存档'}
          </div>
        ) : (
          <div className="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto" onPointerDown={(e) => e.stopPropagation()}>
            {worlds.map((w) => (
              <button
                key={w.DirectoryPath}
                className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-md px-1.5 py-1 text-left hover:bg-accent"
                disabled={!!worldLaunching}
                title={`${w.Name}（${w.OwnerVersionId}）`}
                onClick={() => launchWorld(w)}
              >
                <span className="text-[13px]">🌍</span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-[11px] font-medium">{w.Name}</span>
                  <span className="block truncate text-[9px] text-hint-text">{w.OwnerVersionId}</span>
                </span>
                <span className="shrink-0 text-[9px] text-hint-text">
                  {worldLaunching === w.DirectoryPath ? '启动中…' : '▶'}
                </span>
              </button>
            ))}
          </div>
        )
      ) : null}

      {/* 服务器快连（ServerJoinComponent）：ServerAPI.PingServer 延迟/人数监控 */}
      {id === 'server-join' ? (
        <div className="flex min-h-0 flex-1 flex-col justify-center gap-1.5" onPointerDown={(e) => e.stopPropagation()}>
          <div className="flex items-center gap-1.5">
            <input
              value={serverAddress}
              placeholder="服务器地址，如 mc.example.com"
              onChange={(e) => setServerAddress(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') pingServer() }}
              className={`${selectCls} flex-1`}
            />
            <Button variant="secondary" size="sm" className="h-7 shrink-0 px-2 text-[10px]" disabled={serverPinging}
              onClick={pingServer}>{serverPinging ? '…' : 'Ping'}</Button>
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className={`truncate text-[10px] ${serverError ? 'text-destructive' : 'text-hint-text'}`}>
              {serverError || serverSummary || '输入地址后 Ping 查看延迟与人数'}
            </span>
            {/* PORTING_NOTE: LauncherAPI.Launch 暂无进服参数（server 参数未暴露），先做 Ping 监控 +
                 普通启动；后端补齐 --server 参数后在此直连进服。 */}
            <button
              className="btn btn-accent btn-anim h-6 shrink-0 cursor-pointer rounded-md px-2.5 text-[10px] font-semibold"
              disabled={serverBusy}
              onClick={launchForServer}
            >
              {serverBusy ? '启动中…' : '启动游戏'}
            </button>
          </div>
        </div>
      ) : null}

      {/* 下载任务（DownloadTaskProgressComponent）：快照 + download:progress 事件 */}
      {id === 'download-task-progress' ? (
        dl.active ? (
          <div className="flex flex-1 flex-col justify-center gap-1">
            <span className="truncate text-[11px] font-semibold">{dl.stageName || '下载中'}</span>
            <span className="truncate text-[9px] text-hint-text">{dl.detail}</span>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted">
              <div className="h-full rounded-full bg-primary transition-[width] duration-300"
                style={{ width: `${Math.min(100, Math.max(0, dl.percentage))}%` }} />
            </div>
            <div className="flex items-baseline justify-between">
              <span className="text-[9px] text-hint-text">{dl.fileText}</span>
              <span className="text-[13px] font-semibold tabular-nums">{dl.percentage.toFixed(0)}%</span>
            </div>
          </div>
        ) : (
          <div className="flex flex-1 flex-col items-center justify-center gap-0.5">
            <span className="text-[11px] text-muted-foreground">{dl.doneText || '暂无下载任务'}</span>
            <button
              className="cursor-pointer text-[9px] text-hint-text hover:text-primary"
              onPointerDown={(e) => e.stopPropagation()}
              onClick={() => router('/download')}
            >去资源下载 →</button>
          </div>
        )
      ) : null}

      {/* 皮肤与披风（SkinCapeEditorComponent，lite）：当前账号 + 跳转账号管理 */}
      {id === 'skin-cape-editor' ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-1">
          <div className="grid size-9 place-items-center overflow-hidden rounded-lg bg-muted text-[16px]">🧑‍🎨</div>
          <span className="max-w-full truncate px-1 text-[11px] font-semibold">
            {currentAccount?.DisplayName || '未登录账号'}
          </span>
          {/* PORTING_NOTE: 皮肤渲染（MinecraftProfileService）待后端补齐；先做账号管理快捷卡 */}
          <Button variant="link" size="sm" className="h-5 text-[10px]"
            onClick={() => router('/settings/account')}>管理账号与皮肤 →</Button>
        </div>
      ) : null}

      {/* 兜底占位卡 */}
      {!COMPONENT_IDS.includes(id) ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-1">
          <span className="text-[11px] font-semibold text-muted-foreground">{title}</span>
          <span className="px-2 text-center text-[10px] text-hint-text">该组件待移植。</span>
        </div>
      ) : null}
    </div>
  )
}

const COMPONENT_IDS = [
  'game-launch', 'account-selector', 'game-instance-selector', 'memory-usage', 'version-manager',
  'clock', 'text', 'music-player', 'world-launch', 'server-join', 'download-task-progress', 'skin-cape-editor',
]

function getAudioSnapshotFn() {
  return getAudioSnapshot()
}

function fmtMs(ms: number) {
  if (!Number.isFinite(ms) || ms <= 0) return '0:00'
  const total = Math.floor(ms / 1000)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

function fmtBytes(n: number) {
  if (!Number.isFinite(n) || n <= 0) return ''
  const units = ['B', 'KB', 'MB', 'GB']
  let v = n
  let i = 0
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v.toFixed(v >= 100 || i === 0 ? 0 : 1)} ${units[i]}`
}
