package bindings

// 12 个 API 结构体：每个导出方法 = 一个 Wails 命令。
// 持有 wails runtime 的 ctx（由 main.go 的 OnStartup 调 API.Startup 注入），
// 各包回调经 emit 桥接为 EventsEmit 事件。

import (
	"context"
	"sync"
	"sync/atomic"

	"nyalauncher/internal/auth"
	"nyalauncher/internal/config"
	"nyalauncher/internal/download"
	"nyalauncher/internal/instance"
	"nyalauncher/internal/launch"
	"nyalauncher/internal/logs"
	"nyalauncher/internal/music"
)

type ConfigAPI struct {
	ctx context.Context
}

// Startup 注入 Wails runtime ctx（main.go OnStartup 调用）。
func (a *ConfigAPI) Startup(ctx context.Context) { a.ctx = ctx }

type LauncherAPI struct {
	ctx context.Context
	// service 启动服务（全进程唯一活动启动管线）。
	service *launch.GameLaunchService
}

func (a *LauncherAPI) Startup(ctx context.Context) { a.ctx = ctx }

type DownloadAPI struct {
	ctx context.Context
	// service 下载任务状态机（进度经 OnChanged 桥接为 download:progress 事件）。
	service *download.GameDownloadService
}

func (a *DownloadAPI) Startup(ctx context.Context) { a.ctx = ctx }

type AccountAPI struct {
	ctx context.Context
	// microsoft Microsoft 设备码认证器；authlib 皮肤站认证器。
	microsoft *auth.MicrosoftDeviceCodeAuthenticator
	authlib   *auth.AuthlibAuthenticator
	// loginMu / loginCancel 当前微软设备码登录的取消句柄（见 api_account_ext.go）。
	loginMu     sync.Mutex
	loginCancel context.CancelFunc
}

func (a *AccountAPI) Startup(ctx context.Context) { a.ctx = ctx }

type InstanceAPI struct {
	ctx context.Context
}

func (a *InstanceAPI) Startup(ctx context.Context) { a.ctx = ctx }

type WorldAPI struct {
	ctx context.Context
}

func (a *WorldAPI) Startup(ctx context.Context) { a.ctx = ctx }

type ContentAPI struct {
	ctx context.Context
}

func (a *ContentAPI) Startup(ctx context.Context) { a.ctx = ctx }

type ModpackAPI struct {
	ctx context.Context
}

func (a *ModpackAPI) Startup(ctx context.Context) { a.ctx = ctx }

type MusicAPI struct {
	ctx context.Context
	// library 曲库（扫描 / 排序 / 音量等持久化偏好）。
	library *music.MusicLibrary
	// positionNs / durationNs 前端音频实现回传的播放进度（纳秒）。
	positionNs atomic.Int64
	durationNs atomic.Int64
	// finishedMu 保护自然播完回调（由播放器状态机注册）。
	finishedMu      sync.Mutex
	onTrackFinished func()
}

func (a *MusicAPI) Startup(ctx context.Context) { a.ctx = ctx }

type MonitorAPI struct {
	ctx context.Context
}

func (a *MonitorAPI) Startup(ctx context.Context) { a.ctx = ctx }

type ServerAPI struct {
	ctx context.Context
}

func (a *ServerAPI) Startup(ctx context.Context) { a.ctx = ctx }

type SystemAPI struct {
	ctx context.Context
}

func (a *SystemAPI) Startup(ctx context.Context) { a.ctx = ctx }

//Startup 把 ctx 分发给全部 API；并执行一次性启动初始化（日志、下载源、首次实例扫描）。
func (a *API) Startup(ctx context.Context) {
	a.Config.Startup(ctx)
	a.Launcher.Startup(ctx)
	a.Download.Startup(ctx)
	a.Account.Startup(ctx)
	a.Instance.Startup(ctx)
	a.World.Startup(ctx)
	a.Content.Startup(ctx)
	a.Modpack.Startup(ctx)
	a.Music.Startup(ctx)
	a.Monitor.Startup(ctx)
	a.Server.Startup(ctx)
	a.System.Startup(ctx)

	// 一次性启动逻辑（对应 C# App 构造 / OnStartup）
	logs.Init()
	download.ApplySavedSettings()
	go instance.Refresh(ctx, instance.ResolveConfiguredSourcePath())
}

// musicConfigStore 把 music.ConfigStore 适配到 config 包的持久化键值。
type musicConfigStore struct{}

func (musicConfigStore) GetValue(key string) string { return config.GetValue(key) }
func (musicConfigStore) SetValue(key, value string) { _ = config.SetValue(key, value) }
