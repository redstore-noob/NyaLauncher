package main

import (
	"context"
	"embed"

	"nyalauncher/internal/bindings"

	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/options/assetserver"
	"github.com/wailsapp/wails/v2/pkg/options/windows"
)

//go:embed all:frontend/dist
var assets embed.FS

func main() {
	api := bindings.New()

	err := wails.Run(&options.App{
		Title:     "NyaLauncher",
		Width:     1280,
		Height:    760,
		MinWidth:  800,
		MinHeight: 600,
		AssetServer: &assetserver.Options{
			Assets: assets,
			// 本地音频流回退路由：内嵌资源未命中时交给 bindings.NewLocalFileHandler，
			// 提供 /localfile?path=...（见 internal/bindings/localfile_handler.go）。
			Handler: bindings.NewLocalFileHandler(),
		},
		BackgroundColour: &options.RGBA{R: 0x0C, G: 0x13, B: 0x10, A: 1},
		OnStartup: func(ctx context.Context) {
			// 把 Wails runtime ctx 注入各 API（事件桥接依赖），并执行启动初始化
			api.Startup(ctx)
		},
		Bind: []interface{}{
			api.Config, api.Launcher, api.Download, api.Account,
			api.Instance, api.World, api.Content, api.Modpack,
			api.Music, api.Monitor, api.Server, api.System,
		},
		Windows: &windows.Options{
			WindowIsTranslucent: false,
			WebviewIsTransparent: false,
		},
	})
	if err != nil {
		println("Error:", err.Error())
	}
}
