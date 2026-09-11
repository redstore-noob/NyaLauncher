package bindings

// SystemAPI 扩展：原生对话框、资源管理器 / 外部打开。
// 与 api_system.go 分离存放，避免与其他改动冲突。

import (
	"os/exec"
	"path/filepath"
	"runtime"

	wailsruntime "github.com/wailsapp/wails/v2/pkg/runtime"
)

// ---- 原生对话框 ----

// SelectDirectory 打开目录选择对话框，返回选中目录（取消返回空串）。
func (a *SystemAPI) SelectDirectory(title string) (string, error) {
	return wailsruntime.OpenDirectoryDialog(callCtx(a.ctx), wailsruntime.OpenDialogOptions{
		Title: title,
	})
}

// SelectFile 打开文件选择对话框；filterName/pattern 组成文件类型过滤器，
// pattern 形如 "*.png;*.jpg"（可含多段）。取消返回空串。
func (a *SystemAPI) SelectFile(title, filterName, pattern string) (string, error) {
	opts := wailsruntime.OpenDialogOptions{Title: title}
	if pattern != "" {
		opts.Filters = []wailsruntime.FileFilter{{DisplayName: filterName, Pattern: pattern}}
	}
	return wailsruntime.OpenFileDialog(callCtx(a.ctx), opts)
}

// SaveFile 打开保存文件对话框；defaultName 为默认文件名。取消返回空串。
func (a *SystemAPI) SaveFile(title, defaultName, filterName, pattern string) (string, error) {
	opts := wailsruntime.OpenDialogOptions{Title: title, DefaultFilename: defaultName}
	if pattern != "" {
		opts.Filters = []wailsruntime.FileFilter{{DisplayName: filterName, Pattern: pattern}}
	}
	return wailsruntime.SaveFileDialog(callCtx(a.ctx), wailsruntime.SaveDialogOptions{
		Title:           opts.Title,
		DefaultFilename: defaultName,
		Filters:         opts.Filters,
	})
}

// ---- 打开资源管理器 / 外部程序 ----

// OpenInExplorer 在系统文件管理器中定位 path（文件或目录）。
// Windows: explorer /select,<path>；macOS: open -R；Linux: xdg-open 其所在目录。
// PORTING_NOTES：Linux 无通用 "选中" 语义，退化为打开父目录。
func (a *SystemAPI) OpenInExplorer(path string) error {
	if path == "" {
		return errEmptyPath
	}
	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		// 注意 "/select," 与路径分开传参，exec 会做引号转义，含空格路径安全。
		cmd = exec.Command("explorer", "/select,", path)
	case "darwin":
		cmd = exec.Command("open", "-R", path)
	default:
		cmd = exec.Command("xdg-open", filepath.Dir(path))
	}
	return cmd.Start()
}

// OpenPath 用系统默认程序打开文件或目录（Windows: rundll32 url.dll,FileProtocolHandler，
// 参数经 exec 转义，路径含空格安全；macOS: open；Linux: xdg-open）。
func (a *SystemAPI) OpenPath(path string) error {
	if path == "" {
		return errEmptyPath
	}
	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		cmd = exec.Command("rundll32", "url.dll,FileProtocolHandler", path)
	case "darwin":
		cmd = exec.Command("open", path)
	default:
		cmd = exec.Command("xdg-open", path)
	}
	return cmd.Start()
}
