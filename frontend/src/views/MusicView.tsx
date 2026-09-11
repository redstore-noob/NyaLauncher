/*
 * 本地音乐播放页 —— 等价 Vue 版 MusicView.vue（MusicPlayerPage.axaml + .cs 移植）。
 * 曲库扫描/排序/搜索走 MusicAPI；播放控制调 PlayTrack/Pause 等后端方法，
 * 实际发声由 lib/audioBridge.ts 的 <audio> 单例完成（监听 music:play 等事件）。
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import Icon from '@/components/overlay/Icon'
import { SelectDirectory } from '../../wailsjs/go/bindings/SystemAPI.js'
import { alert } from '@/components/overlay/dialog'
import { audioState, useAudioState } from '@/lib/audioBridge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Slider } from '@/components/ui/slider'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  GetMusicFolderPath, SetMusicFolder, ScanMusicLibrary, GetMusicTracks,
  SearchMusicTracks, GetSortedMusicTracks, GetMusicSortMode, SetMusicSortMode,
  GetMusicVolume, SetMusicVolume, GetMusicPlaybackMode, SetMusicPlaybackMode,
  PlayTrack, PausePlayback, ResumePlayback, StopPlayback, NextTrack, PreviousTrack,
  GetPlaylist, SetPlaylist, SeekPlayback,
} from '../../wailsjs/go/bindings/MusicAPI.js'
import { EventsOn, EventsOff } from '../../wailsjs/runtime/runtime.js'
import './MusicView.css'

type MusicTrack = import("../../wailsjs/go/models.js").music.MusicTrack

const SORT_OPTIONS = [
  { value: 'FileName', label: '文件名 A-Z' },
  { value: 'FileNameDesc', label: '文件名 Z-A' },
  { value: 'DateModified', label: '修改时间 ↑' },
  { value: 'DateModifiedDesc', label: '修改时间 ↓' },
  { value: 'FileSize', label: '文件大小 ↑' },
  { value: 'FileSizeDesc', label: '文件大小 ↓' },
]
const MODE_CYCLE = ['Sequential', 'RepeatAll', 'RepeatOne', 'Shuffle']
const MODE_META: Record<string, { icon: string; tip: string }> = {
  Sequential: { icon: 'playlist-play', tip: '顺序播放' },
  RepeatAll: { icon: 'repeat', tip: '列表循环' },
  RepeatOne: { icon: 'repeat-one', tip: '单曲循环' },
  Shuffle: { icon: 'shuffle', tip: '随机播放' },
}

// ------------------------------------------------------------------
// 曲目显示工具（对应 music.MusicTrack.Title / MetaDisplay）
// ------------------------------------------------------------------

function trackTitle(track: MusicTrack): string {
  const base = track.FilePath.split(/[\\/]/).pop() ?? ''
  return base.replace(/\.[^.]+$/, '')
}

function trackMeta(track: MusicTrack): string {
  const ext = (track.FilePath.match(/\.([^.\\/]+)$/)?.[1] ?? '').toLowerCase()
  const size = track.FileSize >= 1048576
    ? `${(track.FileSize / 1048576).toFixed(1)} MB`
    : track.FileSize >= 1024
      ? `${Math.round(track.FileSize / 1024)} KB`
      : `${track.FileSize} B`
  return `${ext} · ${size}`
}

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '0:00'
  const total = Math.floor(seconds)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

export default function MusicView() {
  const audio = useAudioState()

  const [library, setLibrary] = useState<MusicTrack[]>([])             // 完整曲库（共享播放列表）
  const [displayTracks, setDisplayTracks] = useState<MusicTrack[]>([]) // 搜索过滤后的展示列表
  const [keyword, setKeyword] = useState('')
  const [sortMode, setSortMode] = useState('FileName')
  const [folderPath, setFolderPath] = useState('')
  const [playbackMode, setPlaybackMode] = useState('Sequential')
  const [volume, setVolume] = useState(80)
  const [currentTrack, setCurrentTrack] = useState<MusicTrack | null>(null)
  const [playbackState, setPlaybackState] = useState('Stopped')

  const [userDragging, setUserDragging] = useState(false)
  const [progressValue, setProgressValue] = useState(0)
  const lastSeekedValue = useRef(-1)

  // 引用最新状态的 ref（事件回调与定时器内读取）
  const sortModeRef = useRef(sortMode); sortModeRef.current = sortMode
  const displayTracksRef = useRef(displayTracks); displayTracksRef.current = displayTracks
  const currentTrackRef = useRef(currentTrack); currentTrackRef.current = currentTrack
  const playbackStateRef = useRef(playbackState); playbackStateRef.current = playbackState

  // --- Web Audio 桥状态 ---
  const isPlaying = audio.playing
  // 拖拽中显示目标值，其余跟随音频
  const positionSec = userDragging ? progressValue : audio.positionMs / 1000
  const durationSec = audio.durationMs / 1000

  const progressEnabled = playbackState !== 'Stopped' && durationSec > 0 && audio.canPlay
  const progressMax = durationSec > 0 ? durationSec : 100

  const trackCountText = library.length > 0
    ? `${library.length} 首歌曲 · ${folderPath}`
    : folderPath
      ? `未找到音频文件 · ${folderPath}`
      : '请点击「选择文件夹」加载音乐'

  let nowTitle: string
  if (playbackState === 'Stopped' && !currentTrack) nowTitle = '未在播放'
  else if (playbackState === 'Stopped' && !audio.canPlay && audio.error) nowTitle = '播放失败'
  else nowTitle = currentTrack ? trackTitle(currentTrack) : '未在播放'

  let nowInfo: string
  if (audio.error && playbackState !== 'Playing' && playbackState !== 'Paused') nowInfo = audio.error
  else if (audio.error && !audio.canPlay && currentTrack) nowInfo = '暂不可播：无法读取本地音频文件'
  else nowInfo = currentTrack ? trackMeta(currentTrack) : ''

  const modeIcon = MODE_META[playbackMode]?.icon ?? 'playlist-play'
  const modeTip = MODE_META[playbackMode]?.tip ?? '顺序播放'
  const volumeIcon = volume === 0 ? 'volume-off' : volume < 50 ? 'volume-low' : 'volume-high'

  function isCurrent(track: MusicTrack): boolean {
    return !!currentTrack && track.FilePath === currentTrack.FilePath
  }

  // ------------------------------------------------------------------
  // 曲库加载 / 排序 / 搜索
  // ------------------------------------------------------------------

  const refreshLibrary = useCallback(async () => {
    try {
      ScanMusicLibrary()
      setFolderPath(await GetMusicFolderPath())
      const mode = await GetMusicSortMode()
      setSortMode(mode)
      const full = ((await GetMusicTracks()) ?? []) as MusicTrack[]
      setLibrary(full)
      setDisplayTracks(((await GetSortedMusicTracks(mode)) ?? []) as MusicTrack[])
      // 共享播放列表 = 完整（未过滤）曲目列表，供自动切歌/上下一首使用
      SetPlaylist([...full])
    } catch (ex) {
      console.error('扫描曲库失败', ex)
    }
  }, [])

  async function selectFolder() {
    let path = ''
    try {
      path = await SelectDirectory('选择音乐文件夹')
    } catch { /* 用户取消或选择失败 */ }
    if (!path) return
    try {
      await SetMusicFolder(path)
      await refreshLibrary()
    } catch (ex) {
      alert(`选择文件夹失败：${(ex as Error)?.message ?? ex}`, { severity: 'error' })
    }
  }

  async function onSortChanged(mode: string) {
    if (!mode) return
    setSortMode(mode)
    SetMusicSortMode(mode)
    setDisplayTracks(((await GetSortedMusicTracks(mode)) ?? []) as MusicTrack[])
    SetPlaylist([...library])
  }

  async function applyFilter(text: string) {
    setKeyword(text)
    if (!text.trim()) {
      setDisplayTracks(((await GetSortedMusicTracks(sortModeRef.current)) ?? []) as MusicTrack[])
      return
    }
    setDisplayTracks(((await SearchMusicTracks(text)) ?? []) as MusicTrack[])
  }

  // ------------------------------------------------------------------
  // 播放控制（转给 Go 状态机，发声经 audioBridge）
  // ------------------------------------------------------------------

  async function onTrackClick(track: MusicTrack) {
    // 曲目已在播放时不再重启（避免自动切歌后被打回 0:00）
    const cur = currentTrackRef.current
    if (cur && playbackStateRef.current !== 'Stopped' && cur.FilePath === track.FilePath) return
    try {
      await PlayTrack(track)
    } catch (ex) {
      alert(`播放失败：${(ex as Error)?.message ?? ex}`, { severity: 'error' })
    }
  }

  function togglePlayPause() {
    if (playbackStateRef.current === 'Playing') PausePlayback()
    else if (playbackStateRef.current === 'Paused') ResumePlayback()
    else if (displayTracksRef.current.length > 0) {
      // 停止态：播放选中曲目或列表首项
      PlayTrack(displayTracksRef.current[0]).catch(() => { /* ignore */ })
    }
  }

  function previous() { PreviousTrack().catch(() => { /* ignore */ }) }
  function next() { NextTrack().catch(() => { /* ignore */ }) }
  function stop() { StopPlayback() }

  async function cycleMode() {
    const nextMode = MODE_CYCLE[(MODE_CYCLE.indexOf(playbackMode) + 1) % MODE_CYCLE.length]
    setPlaybackMode(nextMode)
    try { await SetMusicPlaybackMode(nextMode) } catch { /* 状态回退由事件驱动 */ }
  }

  function onVolumeChanged(v: number) {
    const value = Math.round(Number(v) || 0)
    setVolume(value)
    SetMusicVolume(value)
  }

  // ------------------------------------------------------------------
  // 进度条
  // ------------------------------------------------------------------

  function onProgressCommit() {
    setUserDragging(false)
    if (!progressEnabled) return
    // pointerup 与 valueCommit 都会触发，同一位置只 seek 一次
    if (progressValue === lastSeekedValue.current) return
    lastSeekedValue.current = progressValue
    // SeekPlayback 会经 audioBridge 回发 music:seek 事件，由前端音频自行跳转
    SeekPlayback(Math.round(progressValue * 1000)).catch(() => { /* ignore */ })
  }

  // ------------------------------------------------------------------
  // 播放器状态事件
  // ------------------------------------------------------------------

  function onStateChanged(state: unknown) { setPlaybackState(String(state)) }
  function onTrackChanged(track: MusicTrack | null) {
    setCurrentTrack(track ?? null)
    if (!track) {
      setProgressValue(0)
    } else {
      // 自动切歌后跟随高亮：过滤列表里存在则滚动到可见
      const index = displayTracksRef.current.findIndex((item) => item.FilePath === track.FilePath)
      if (index >= 0) {
        document.querySelector('.track-tile.current')?.scrollIntoView({ block: 'nearest' })
      }
    }
  }
  function onModeChanged(mode: unknown) { setPlaybackMode(String(mode)) }

  // 进度跟随音频（非拖拽时，250ms 轮询）
  useEffect(() => {
    const handle = setInterval(() => {
      if (!userDragging && progressEnabled) {
        setProgressValue(audioState.positionMs / 1000)
      }
    }, 250)
    return () => clearInterval(handle)
  }, [userDragging, progressEnabled])

  useEffect(() => {
    EventsOn('music:stateChanged', onStateChanged)
    EventsOn('music:trackChanged', onTrackChanged)
    EventsOn('music:playbackModeChanged', onModeChanged)
    // 事件订阅必须清理（README-react 注意事项 1）
    return () => {
      EventsOff('music:stateChanged')
      EventsOff('music:trackChanged')
      EventsOff('music:playbackModeChanged')
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 初始化：加载已保存的音量与播放模式；已有文件夹则自动扫描
  useEffect(() => {
    ;(async () => {
      try {
        const savedVolume = await GetMusicVolume()
        if (Number.isFinite(savedVolume)) setVolume(savedVolume)
        setPlaybackMode(await GetMusicPlaybackMode())
        setFolderPath(await GetMusicFolderPath())
        setSortMode(await GetMusicSortMode())
        const savedFolder = await GetMusicFolderPath()
        if (savedFolder) await refreshLibrary()
        // 共享播放器已有曲目在播时同步界面
        const playlist = await GetPlaylist()
        if (playlist?.length && !currentTrackRef.current) {
          setDisplayTracks(((await GetSortedMusicTracks(sortModeRef.current)) ?? []) as MusicTrack[])
        }
      } catch (ex) {
        console.error('音乐页初始化失败', ex)
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    /* 音乐播放器页（MusicPlayerPage.axaml：顶栏 / 搜索+列表 / 底部控制栏 三行布局） */
    <section className="grid h-full grid-rows-[auto_1fr_auto] bg-background">

      {/* 顶栏 */}
      <header className="flex items-center gap-3 border-b border-subtle-border bg-background px-6 py-3.5">
        <div className="mr-auto flex min-w-0 flex-1 items-center gap-3">
          <div className="flex size-10 flex-none items-center justify-center rounded-[10px] bg-primary text-primary-foreground">
            <Icon name="music-note" size={20} />
          </div>
          <div className="flex min-w-0 flex-col gap-0.5">
            <span className="text-lg font-bold text-foreground">音乐播放器</span>
            <span className="max-w-[380px] overflow-hidden text-[11px] text-hint-text text-ellipsis whitespace-nowrap">{trackCountText}</span>
          </div>
        </div>

        <Button variant="outline" size="sm" className="border-emphasized-border" onClick={selectFolder}>选择文件夹</Button>
        <Button variant="secondary" size="sm" onClick={refreshLibrary}>刷新</Button>
        <Select value={sortMode} onValueChange={onSortChanged}>
          <SelectTrigger className="h-auto w-[130px] rounded-lg px-2.5 py-1 text-xs"><SelectValue /></SelectTrigger>
          <SelectContent>
            {SORT_OPTIONS.map((option) => (
              <SelectItem key={option.value} value={option.value}>{option.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </header>

      {/* 搜索 + 列表 */}
      <div className="grid min-h-0 grid-rows-[auto_1fr] px-6 pt-4">
        <div className="relative mb-3">
          <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-subtext-text"><Icon name="magnify" size={16} /></span>
          <Input
            value={keyword}
            onChange={(e) => applyFilter(e.target.value)}
            placeholder="搜索歌曲..."
            className="h-auto rounded-[10px] bg-muted py-2.5 pl-10 border-transparent focus-visible:border-ring" />
        </div>

        <div className="relative min-h-0 overflow-hidden rounded-xl bg-card">
          <div className="h-full overflow-y-auto p-1">
            {displayTracks.map((track) => (
              <div
                key={track.FilePath}
                className={`track-tile interactive mx-1 my-[3px] flex cursor-pointer items-center gap-3 rounded-md px-2.5 py-1.5 transition-colors ${
                  isCurrent(track) ? 'bg-accent current' : 'hover:bg-accent/60'
                }`}
                onClick={() => onTrackClick(track)}
              >
                <div
                  className={`flex size-[38px] flex-none items-center justify-center rounded-md ${
                    isCurrent(track) ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground opacity-75'
                  }`}
                >
                  {!isCurrent(track) ? <Icon name="music-note" size={17} /> : (
                    /* 均衡器动画（播放中） */
                    <span className="flex items-end justify-center gap-[3px] pb-[9px]">
                      <span className={`eq-bar eq-bar-1 ${isCurrent(track) && isPlaying ? 'eq-on' : ''}`} />
                      <span className={`eq-bar eq-bar-2 ${isCurrent(track) && isPlaying ? 'eq-on' : ''}`} />
                      <span className={`eq-bar eq-bar-3 ${isCurrent(track) && isPlaying ? 'eq-on' : ''}`} />
                    </span>
                  )}
                </div>

                <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span className={`overflow-hidden text-[13px] font-semibold text-ellipsis whitespace-nowrap ${isCurrent(track) ? 'text-primary' : 'text-foreground'}`}>
                    {trackTitle(track)}
                  </span>
                  <span className="text-[11px] text-hint-text">{trackMeta(track)}</span>
                </div>

                {isCurrent(track) ? <span className="flex-none text-primary"><Icon name="volume-high" size={14} /></span> : null}
              </div>
            ))}
          </div>

          {/* 空态 */}
          {displayTracks.length === 0 ? (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-2.5 text-center text-subtext-text">
              <Icon name="music-note" size={52} />
              <span className="text-[15px] font-semibold text-muted-foreground">{library.length > 0 ? '没有匹配的歌曲' : '还没有歌曲'}</span>
              <span className="text-[11px] text-hint-text">
                {library.length > 0 ? '试试其他关键词，或点击上方「刷新」重新扫描' : '选择一个文件夹，扫描里面的本地音乐'}
              </span>
              {library.length === 0 ? (
                <Button size="sm" className="h-auto rounded-lg px-[18px] py-2" onClick={selectFolder}>选择文件夹</Button>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>

      {/* 底部播放控制栏 */}
      <footer className="grid gap-3 border-t border-subtle-border bg-background px-6 pb-[18px] pt-3.5">
        {/* 进度条 + 时间 */}
        <div className="flex items-center gap-3">
          <span className="min-w-[38px] text-center font-mono text-[11px] text-muted-foreground">{formatTime(positionSec)}</span>
          <Slider
            value={[progressValue]}
            min={0}
            max={progressMax}
            step={0.1}
            disabled={!progressEnabled}
            className="flex-1"
            onValueChange={(v) => { setProgressValue(Array.isArray(v) ? v[0] : v) }}
            onPointerDown={() => setUserDragging(true)}
            onPointerUp={onProgressCommit}
            onPointerCancel={() => setUserDragging(false)}
          />
          <span className="min-w-[38px] text-right font-mono text-[11px] text-muted-foreground">{durationSec > 0 ? formatTime(durationSec) : '--:--'}</span>
        </div>

        {/* 控制按钮 + 曲目信息 + 音量 */}
        <div className="flex items-center gap-4">
          <div className="flex flex-none items-center gap-2">
            <Tooltip delayDuration={300}>
              <TooltipTrigger asChild>
                <Button variant="secondary" size="icon" className="rounded-full active:scale-[0.88]" onClick={previous}>
                  <Icon name="skip-previous" size={16} />
                </Button>
              </TooltipTrigger>
              <TooltipContent>上一首</TooltipContent>
            </Tooltip>
            <Tooltip delayDuration={300}>
              <TooltipTrigger asChild>
                <Button size="icon" className="size-[46px] rounded-full active:scale-[0.88]" onClick={togglePlayPause}>
                  <Icon name={isPlaying ? 'pause' : 'play'} size={19} />
                </Button>
              </TooltipTrigger>
              <TooltipContent>{isPlaying ? '暂停' : '播放'}</TooltipContent>
            </Tooltip>
            <Tooltip delayDuration={300}>
              <TooltipTrigger asChild>
                <Button variant="secondary" size="icon" className="rounded-full active:scale-[0.88]" onClick={next}>
                  <Icon name="fast-forward" size={16} />
                </Button>
              </TooltipTrigger>
              <TooltipContent>下一首</TooltipContent>
            </Tooltip>
            <Tooltip delayDuration={300}>
              <TooltipTrigger asChild>
                <Button variant="secondary" size="icon" className="rounded-full active:scale-[0.88]" onClick={stop}>
                  <Icon name="stop" size={15} />
                </Button>
              </TooltipTrigger>
              <TooltipContent>停止</TooltipContent>
            </Tooltip>
            {/* 模式循环按钮：点击切换，Tooltip 显示当前模式 */}
            <Tooltip delayDuration={300}>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className={`relative rounded-full active:scale-[0.88] ${playbackMode !== 'Sequential' ? 'text-primary' : ''}`}
                  onClick={cycleMode}
                >
                  <Icon name={modeIcon} size={16} />
                  {playbackMode !== 'Sequential' ? <span className="absolute bottom-px right-px size-[9px] rounded-full bg-primary" /> : null}
                </Button>
              </TooltipTrigger>
              <TooltipContent>{modeTip}（点击切换）</TooltipContent>
            </Tooltip>
          </div>

          {/* 当前曲目信息 + 封面徽章 */}
          <div className="flex min-w-0 flex-1 items-center gap-3">
            <div
              className={`relative flex size-[46px] flex-none items-center justify-center overflow-hidden rounded-xl bg-primary text-primary-foreground ${isPlaying ? 'nya-cover-breathing' : ''}`}
            >
              <span className="absolute inset-0 bg-white/15" />
              <Icon name="music-note" size={22} />
            </div>
            <div className="flex min-w-0 flex-col gap-0.5">
              <span className="overflow-hidden text-sm font-semibold text-foreground text-ellipsis whitespace-nowrap">{nowTitle}</span>
              <span className="overflow-hidden text-[11px] text-hint-text text-ellipsis whitespace-nowrap">{nowInfo}</span>
            </div>
          </div>

          {/* 音量 */}
          <div className="flex items-center gap-2">
            <span className="text-muted-foreground"><Icon name={volumeIcon} size={16} /></span>
            <Slider
              value={[volume]}
              min={0}
              max={100}
              step={1}
              className="w-[100px]"
              onValueChange={(v) => setVolume(Array.isArray(v) ? v[0] : v)}
              onValueCommit={(v) => onVolumeChanged(Array.isArray(v) ? v[0] : v)}
            />
            <span className="min-w-[35px] text-[11px] text-muted-foreground">{volume}%</span>
          </div>
        </div>
      </footer>
    </section>
  )
}
