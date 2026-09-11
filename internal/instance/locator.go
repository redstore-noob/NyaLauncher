// Minecraft 安装目录定位：移植自 NyaLauncher.Core/Launch/LaunchTool.cs 内的
// MinecraftInstallationLocation / MinecraftDirectoryLocator。
// 原本属于 launch 侧的公共依赖，因 instance/world/content 均需消费且不能依赖
// 尚在移植中的 internal/launch，先落在 instance 包内（见 PORTING_NOTES.md）。
package instance

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"

	"nyalauncher/internal/config"
)

// MinecraftInstallationLocation 描述一次 Minecraft 安装路径解析的结果。
// 根据传入路径是"根目录"还是"versions/版本号 独立实例目录"，字段会有不同的取值。
type MinecraftInstallationLocation struct {
	// MinecraftDirectory Minecraft 根目录（如 ~/.minecraft），始终非空。
	MinecraftDirectory string
	// PreferredVersionId 传入的是独立实例目录时为对应的版本号；传入根目录时为空串。
	PreferredVersionId string
	// GameDirectory 独立实例目录（用于隔离 mods、config、saves 等）；传入根目录时为空串。
	GameDirectory string
}

// standardSubDirectories Minecraft 标准目录结构中应当存在的子文件夹列表。
var standardSubDirectories = []string{
	"versions", "assets", "libraries", "saves", "resourcepacks", "mods",
	"config", "crash-reports", "logs", "screenshots", "shaderpacks",
}

// GetDefaultDirectory 获取当前操作系统下 Minecraft 官方启动器使用的默认 .minecraft 目录。
func GetDefaultDirectory() string {
	home := toolsHome()
	if runtime.GOOS == "windows" {
		appData := os.Getenv("APPDATA")
		if appData == "" && home != "" {
			appData = filepath.Join(home, "AppData", "Roaming")
		}
		return filepath.Join(appData, ".minecraft")
	}
	if runtime.GOOS == "darwin" {
		return filepath.Join(home, "Library", "Application Support", "minecraft")
	}
	// Linux 及其他未识别系统：~/.minecraft
	return filepath.Join(home, ".minecraft")
}

func toolsHome() string {
	home, err := os.UserHomeDir()
	if err != nil {
		return ""
	}
	return home
}

// EnsureDefaultDirectory 检测默认 Minecraft 目录是否存在；不存在时在平台默认路径下
// 创建符合 Minecraft 目录结构的空文件夹骨架。返回保证存在的默认目录路径。
func EnsureDefaultDirectory() string {
	defaultDir := GetDefaultDirectory()
	if info, err := os.Stat(defaultDir); err != nil || !info.IsDir() {
		_ = os.MkdirAll(defaultDir, 0o755)
		for _, sub := range standardSubDirectories {
			_ = os.MkdirAll(filepath.Join(defaultDir, sub), 0o755)
		}
	}
	return defaultDir
}

func init() {
	// 供 internal/config 的目录目录保护逻辑使用（C# 侧由 MinecraftDirectoryLocator 提供）。
	config.DefaultMinecraftDirectoryLocator = EnsureDefaultDirectory
}

// ResolveInstallationPath 接受 Minecraft 根目录，或 versions/<版本号> 形式的独立实例目录。
// 路径为空、不存在或不是有效的 Minecraft 目录时返回 error（对应 C# MinecraftLaunchException）。
func ResolveInstallationPath(path string) (MinecraftInstallationLocation, error) {
	var location MinecraftInstallationLocation
	// 空路径直接拒绝
	if strings.TrimSpace(path) == "" {
		return location, fmt.Errorf("Minecraft 路径不能为空")
	}

	// 规范化：去掉首尾空白与引号、展开环境变量（%VAR%/$VAR）、转换为绝对路径
	trimmed := strings.TrimSpace(path)
	trimmed = strings.Trim(trimmed, `"`)
	expanded := os.ExpandEnv(trimmed)
	if runtime.GOOS == "windows" {
		expanded = expandWindowsEnv(expanded)
	}
	fullPath, err := filepath.Abs(expanded)
	if err != nil {
		return location, fmt.Errorf("Minecraft 路径不存在：%s", fullPath)
	}
	if info, err := os.Stat(fullPath); err != nil || !info.IsDir() {
		return location, fmt.Errorf("Minecraft 路径不存在：%s", fullPath)
	}

	// 判断是否为 "versions/<版本号>" 形式的独立实例目录：
	// 父目录名为 versions，且目录内存在与目录同名的 <版本号>.json 版本描述文件
	directoryName := filepath.Base(fullPath)
	parent := filepath.Dir(fullPath)
	if equalFoldOnWindows(filepath.Base(parent), "versions") &&
		fileExists(filepath.Join(fullPath, directoryName+".json")) {
		// 版本目录的上一级（versions 的父级）即为 Minecraft 根目录
		root := filepath.Dir(parent)
		if root == parent {
			return location, fmt.Errorf("无法确定版本目录对应的 Minecraft 根目录")
		}
		// 根目录仍需通过校验（必须包含 versions 文件夹）
		if err := validateRootDirectory(root); err != nil {
			return location, err
		}
		return MinecraftInstallationLocation{MinecraftDirectory: root, PreferredVersionId: directoryName, GameDirectory: fullPath}, nil
	}

	// 不是实例目录，则按普通根目录处理并校验
	if err := validateRootDirectory(fullPath); err != nil {
		return location, err
	}
	return MinecraftInstallationLocation{MinecraftDirectory: fullPath}, nil
}

// expandWindowsEnv 展开 %VAR% 形式的环境变量（os.ExpandEnv 只处理 $VAR）。
func expandWindowsEnv(path string) string {
	for {
		start := strings.Index(path, "%")
		if start < 0 {
			return path
		}
		end := strings.Index(path[start+1:], "%")
		if end < 0 {
			return path
		}
		name := path[start+1 : start+1+end]
		value, ok := os.LookupEnv(name)
		if !ok {
			return path
		}
		path = path[:start] + value + path[start+end+2:]
	}
}

// GetInstalledVersionIds 扫描指定 Minecraft 根目录下已安装的版本列表。
// 只统计"版本文件夹内存在同名 .json 版本描述文件"的完整版本，
// 可过滤掉下载中断留下的残缺目录。按名称忽略大小写降序排列。
func GetInstalledVersionIds(minecraftDirectory string) []string {
	versionsDirectory := filepath.Join(minecraftDirectory, "versions")
	// 尚未下载任何版本时直接返回空列表
	if info, err := os.Stat(versionsDirectory); err != nil || !info.IsDir() {
		return []string{}
	}

	// 注意：不隐藏被 inheritsFrom 依赖的原版版本。原版目录由完整安装产生
	// （含客户端 JAR 与用户数据），是可独立启动的实例；把它从列表里滤掉
	// 会被用户理解为"安装 Loader 时覆盖了原版实例"，且删除 Loader 实例后
	// 原版也将无处可见。
	var allIds []string
	entries, err := os.ReadDir(versionsDirectory)
	if err != nil {
		return []string{}
	}
	for _, entry := range entries {
		if !entry.IsDir() || entry.Name() == "" {
			continue
		}
		id := entry.Name()
		if fileExists(filepath.Join(versionsDirectory, id, id+".json")) {
			allIds = append(allIds, id)
		}
	}
	sort.Slice(allIds, func(i, j int) bool {
		return strings.ToLower(allIds[i]) > strings.ToLower(allIds[j])
	})
	if allIds == nil {
		allIds = []string{}
	}
	return allIds
}

// validateRootDirectory 校验指定路径是否为有效的 Minecraft 根目录（必须包含 versions 文件夹）。
func validateRootDirectory(root string) error {
	if info, err := os.Stat(filepath.Join(root, "versions")); err != nil || !info.IsDir() {
		return fmt.Errorf("该路径不是有效的 Minecraft 根目录，缺少 versions 文件夹：%s", root)
	}
	return nil
}
