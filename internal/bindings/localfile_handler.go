package bindings

// 本地音频流 Handler：挂在 Wails assetserver.Options.Handler（内嵌资源未命中时的
// 回退处理器），提供 /localfile?path=... 路由，把本地音频文件流式返回给 WebView。
// 背景：WebView 内 <audio>/new Audio 无法直接访问 file:// 盘符路径，需经应用内
// HTTP 路由中转。安全模型：这是本地单用户桌面应用，无远程访问面；仍做两层校验——
// 仅接受存在文件的绝对路径，且扩展名在音频白名单内，避免被当作任意文件读取通道。

import (
	"net/http"
	"os"
	"path/filepath"
	"strings"
)

// audioExtWhitelist 允许流式返回的音频扩展名。
var audioExtWhitelist = map[string]bool{
	".mp3": true, ".ogg": true, ".wav": true, ".flac": true, ".m4a": true,
}

// imageExtWhitelist 允许流式返回的图片扩展名（皮肤/头像贴图经 /localfile 展示）。
var imageExtWhitelist = map[string]bool{
	".png": true,
}

// contentTypeByExt 音频 Content-Type（http.ServeContent 可凭扩展名猜测，这里显式覆盖）。
var contentTypeByExt = map[string]string{
	".mp3": "audio/mpeg", ".ogg": "audio/ogg", ".wav": "audio/wav",
	".flac": "audio/flac", ".m4a": "audio/mp4",
	".png": "image/png",
}

// NewLocalFileHandler 返回 assetserver 回退处理器：
// 命中 /localfile 时按查询参数 path 流式返回本地音频文件，其余请求 404。
// 前端用法：new Audio('/localfile?path=' + encodeURIComponent(p))
func NewLocalFileHandler() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/localfile" {
			http.NotFound(w, r)
			return
		}
		p := r.URL.Query().Get("path")
		if p == "" || !filepath.IsAbs(p) {
			http.Error(w, "invalid path", http.StatusBadRequest)
			return
		}
		ext := strings.ToLower(filepath.Ext(p))
		if !audioExtWhitelist[ext] && !imageExtWhitelist[ext] {
			http.Error(w, "extension not allowed", http.StatusForbidden)
			return
		}
		if info, err := os.Stat(p); err != nil || info.IsDir() {
			http.NotFound(w, r)
			return
		}
		if ct, ok := contentTypeByExt[ext]; ok {
			w.Header().Set("Content-Type", ct)
		}
		// ServeFile 支持 Range（拖动进度条）与流式传输。
		http.ServeFile(w, r, p)
	})
}
