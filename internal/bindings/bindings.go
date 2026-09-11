// Package bindings Wails 前端绑定层：把 internal/* 各包的函数包装成
// 可被前端 invoke 的 API 结构体。每个结构体的导出方法 = 一个前端可调用命令。
//
// 命名约定（前端 agent 依赖）：wailsjs 生成路径为
//   wailsjs/go/bindings/<Struct>/<Method>
package bindings

import (
	"errors"

	"nyalauncher/internal/auth"
	"nyalauncher/internal/download"
	"nyalauncher/internal/launch"
)

// errOfflineExists 离线账号重名错误。
var errOfflineExists = errors.New("已存在同名离线账号")

// API 聚合根：main.go 把下面每个字段都注册进 Bind。
type API struct {
	Config   *ConfigAPI
	Launcher *LauncherAPI
	Download *DownloadAPI
	Account  *AccountAPI
	Instance *InstanceAPI
	World    *WorldAPI
	Content  *ContentAPI
	Modpack  *ModpackAPI
	Music    *MusicAPI
	Monitor  *MonitorAPI
	Server   *ServerAPI
	System   *SystemAPI
}

// New 构造全部 API 并完成包间接线（事件订阅、钩子注入等在各项 Init 中做）。
func New() *API {
	api := &API{
		Config:   &ConfigAPI{},
		Launcher: &LauncherAPI{service: launch.NewGameLaunchService(nil)},
		Download: &DownloadAPI{service: &download.GameDownloadService{}},
		Account:  &AccountAPI{microsoft: auth.NewMicrosoftDeviceCodeAuthenticator("", nil), authlib: auth.NewAuthlibAuthenticator(nil)},
		Instance: &InstanceAPI{},
		World:    &WorldAPI{},
		Content:  &ContentAPI{},
		Modpack:  &ModpackAPI{},
		Music:    &MusicAPI{},
		Monitor:  &MonitorAPI{},
		Server:   &ServerAPI{},
		System:   &SystemAPI{},
	}
	api.init()
	return api
}

// init 包间接线：instance 外部实例钩子、config 目录定位钩子、
// 音乐播放器事件 → Wails EventsEmit 等在这里完成。
func (a *API) init() {
	a.wireInstance()
	a.wireMusic()
}
