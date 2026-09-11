# internal/music 移植说明

移植自 `NyaLauncher.Core/Music/`（MusicLibrary.cs、MusicPlayerService.cs、MusicTrack.cs）。

## 完整移植的部分

- `MusicTrack`（track.go）：元数据与显示格式化（Title / SizeDisplay / MetaDisplay / DateDisplay），
  支持扩展名列表与 `IsSupported`。C# record 值相等 → Go 结构体直接 `==` 比较。
- `MusicLibrary`（library.go）：文件夹扫描（递归、单个子目录不可读时跳过）、
  六种排序模式、模糊搜索、配置键持久化（musicFolder / musicSortMode / musicVolume /
  musicPlaybackMode，与 C# 键名一致）。
- `MusicPlayerService`（player.go）的状态管理部分：播放状态机
  （Stopped/Playing/Paused）、播放列表、四种播放模式（Sequential/RepeatAll/RepeatOne/Shuffle）、
  自动切歌 `SelectNextIndex`（纯逻辑版，便于单测）、手动/自然播完的区分（manualStop）、
  上一首/下一首/暂停/恢复/停止。

## 偏离点

1. **实际音频输出未移植**：Go 标准库没有跨平台音频播放能力，C# 的 NAudio
   （Windows）与 afplay/mpv（macOS/Linux）回退无法直接对应。音频输出抽象为
   `AudioPlayer` 接口（Play/Pause/Resume/Stop/Seek/Position/Duration/SetVolume/
   SetOnFinished），由前端 Web Audio（Wails 前端通过事件桥接）或 Wails 侧
   原生绑定实现。`NewMusicPlayerService(nil)` 可创建仅状态机实例（用于测试）。
2. **事件 → 回调函数字段**：C# 的 `event Action`（StateChanged / TrackChanged /
   PlaybackModeChanged / TrackFinished）映射为 `OnStateChanged` 等可赋值的
   回调函数字段；音频自然播完通过 `AudioPlayer.SetOnFinished` 注入，由服务内部
   统一分发（对应 C# OnPlaybackStopped）。
3. **LauncherConfig 依赖**：C# 依赖 `NyaLauncher.Core.Config.LauncherConfig`，
   Go 侧 internal/config 尚未就绪，抽象为 `ConfigStore` 接口（library 同目录
   config.go），默认提供不持久化的 `MemoryConfigStore`；待配置模块完成后注入
   持久化实现即可，键名保持与 C# 一致。
4. **单例**：`MusicPlayerService.Shared` → 包级变量 `music.Shared`。
5. **Position/Duration**：C# 直接读 NAudio 的 WaveStream 时钟；Go 版委托给
   `AudioPlayer.Position()/Duration()`，未播放时返回 0。
6. **Resume 重开文件**：C# 暂停后资源被清理时会重新打开文件；Go 版对应为
   `AudioPlayer.Resume()` 失败时降级调用 `Play(filePath)` 从头播放。

## 超时/线程语义

- 并发保护：C# `lock (_gate)` → Go `sync.Mutex`；`manualStop` 与自然播完回调
  的同步关系（防止并发 Stop 时误判自动切歌）已保留。
- `StopCore(notify)` → `stopCore(notify bool)`。
