package bindings

// 包间接线：
//   - wireInstance：把 instance / config 注入 download 包的占位钩子
//     （launch_bridge.go 的 RefreshInstancesHook / SelectInstanceHook /
//     ResolveContentDirectoryHook / ResolveInstanceLayoutHook / SetConfigHooks）；
//   - wireMusic：构造曲库 + 前端音频桥接（AudioPlayer 由前端实现），
//     把 music.Shared 的回调转发为 Wails 事件；
//   - 各 API 的 OnChanged 等回调在 New() 里统一接事件。

import (
	"context"
	"errors"

	wailsruntime "github.com/wailsapp/wails/v2/pkg/runtime"

	"nyalauncher/internal/config"
	"nyalauncher/internal/download"
	"nyalauncher/internal/instance"
	"nyalauncher/internal/launch"
	"nyalauncher/internal/music"
)

// emit 通过 Wails runtime 推送事件；ctx 未注入（启动前）时静默丢弃。
func emit(ctx context.Context, eventName string, payload ...interface{}) {
	if ctx == nil {
		return
	}
	wailsruntime.EventsEmit(ctx, eventName, payload...)
}

// callCtx 返回可用的 context：优先 Startup 注入的 a.ctx，未注入时回退 Background。
// 绑定方法签名不再携带 ctx（Wails 反射不支持），内部改用此函数取上下文。
func callCtx(c context.Context) context.Context {
	if c == nil {
		return context.Background()
	}
	return c
}

// New 之后、Startup 之前的事件都可能在 ctx 就绪前触发；
// 这里用 API 级共享 ctx 简化：各结构体 Startup 会同步覆盖。
// 事件桥接统一读各自结构体的 ctx。

// wireInstance download 包的宿主钩子注入（launch_bridge.go 占位实现 → 正式实现）。
func (a *API) wireInstance() {
	// 配置读写钩子：download.DownloadSettings 走真实 config.json
	download.SetConfigHooks(
		func(key string) (string, bool) {
			value := config.GetValue(key)
			return value, value != ""
		},
		func(key, value string) {
			_ = config.SetValue(key, value)
		})

	// 实例扫描 / 选中钩子（GameInstanceStore）
	download.RefreshInstancesHook = func(gameDirectory string) error {
		snapshot := instance.Refresh(context.Background(), gameDirectory)
		if snapshot.ErrorMessage != "" {
			return errors.New(snapshot.ErrorMessage)
		}
		return nil
	}
	download.SelectInstanceHook = func(instanceID string) {
		instance.Select(instanceID)
	}

	// 内容目录解析钩子（GameVersionIsolation）：与启动时隔离判定一致
	download.ResolveContentDirectoryHook = func(minecraftDirectory, sourcePath, versionID string) string {
		return instance.GameVersionIsolationGetContentDirectory(instance.CurrentSnapshot(), versionID)
	}

	// Loader 安装布局解析钩子（GameInstanceLayoutResolver）
	download.ResolveInstanceLayoutHook = func(targetRoot, sourcePath, instanceName, versionID string, defaultIsolation bool) string {
		layout := instance.ResolveLayout(targetRoot, sourcePath, versionID, nil, &defaultIsolation)
		return layout.ContentDirectory
	}

	// 下载暂停状态 → download:pauseChanged 事件
	download.OnPauseStateChanged = func() {
		emit(a.Download.ctx, "download:pauseChanged", download.IsDownloadPaused())
	}

	// 下载任务快照 → download:progress 事件
	a.Download.service.OnChanged = func(snapshot download.GameDownloadSnapshot) {
		emit(a.Download.ctx, "download:progress", snapshot)
	}

	// 实例快照变更 → instance:changed 事件
	instance.SubscribeChanged(func(snapshot instance.GameInstanceSnapshot) {
		emit(a.Instance.ctx, "instance:changed", snapshot)
	})

	// 实例档案（独立内存 / 窗口尺寸等）变更 → config:profilesChanged 事件
	config.AddChangedHandler(func() {
		emit(a.Config.ctx, "config:profilesChanged")
	})

	// 启动快照变更 → launch:changed 事件
	a.Launcher.service.OnChanged = func(snapshot launch.GameLaunchSnapshot) {
		emit(a.Launcher.ctx, "launch:changed", snapshot)
	}
}

// wireMusic 音乐播放器：状态机用 Go 侧 music.Shared（重新挂上前端音频桥接），
// 实际解码输出由前端 Web Audio 完成（见 audio_bridge.go）。
func (a *API) wireMusic() {
	a.Music.library = music.NewMusicLibrary(musicConfigStore{})

	// 替换全局播放器：注入前端音频桥接（默认 Shared 构造时 audio 为 nil）
	bridge := &audioBridge{api: a.Music}
	music.Shared = music.NewMusicPlayerService(bridge)

	// 播放器状态回调 → Wails 事件
	music.Shared.OnStateChanged = func() {
		emit(a.Music.ctx, "music:stateChanged", music.Shared.State())
	}
	music.Shared.OnTrackChanged = func() {
		emit(a.Music.ctx, "music:trackChanged", music.Shared.CurrentTrack())
	}
	music.Shared.OnPlaybackModeChanged = func() {
		emit(a.Music.ctx, "music:playbackModeChanged", music.Shared.PlaybackMode())
	}
	music.Shared.OnTrackFinished = func() {
		emit(a.Music.ctx, "music:trackFinished")
	}
}
