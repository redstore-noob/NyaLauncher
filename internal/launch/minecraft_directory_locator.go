package launch

import (
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// MinecraftInstallationLocation 描述一次 Minecraft 安装路径解析的结果。
// 根据传入路径是"根目录"还是"versions/版本号 独立实例目录"，字段会有不同的取值。
type MinecraftInstallationLocation struct {
	// MinecraftDirectory Minecraft 根目录（如 ~/.minecraft），始终非空。
	MinecraftDirectory string
	// PreferredVersionId 传入的是独立实例目录时，为对应的版本号；传入根目录时为空。
	PreferredVersionId string
	// GameDirectory 独立实例目录（用于隔离 mods、config、saves 等）；传入根目录时为空。
	GameDirectory string
}

// standardSubDirectories Minecraft 标准目录结构中应当存在的子文件夹列表。
// 仅包含目录骨架，不创建任何文件。
var standardSubDirectories = []string{
	"versions",
	"assets",
	"libraries",
	"saves",
	"resourcepacks",
	"mods",
	"config",
	"crash-reports",
	"logs",
	"screenshots",
	"shaderpacks",
}

// GetDefaultMinecraftDirectory 获取当前操作系统下 Minecraft 官方启动器使用的
// 默认 .minecraft 目录。
func GetDefaultMinecraftDirectory() string {
	if home := userHomeDirectory(); home != "" {
		if isWindows() {
			appData := os.Getenv("APPDATA")
			if appData != "" {
				return filepath.Join(appData, ".minecraft")
			}
			return filepath.Join(home, "AppData", "Roaming", ".minecraft")
		}
		if isMacOS() {
			// macOS：~/Library/Application Support/minecraft
			return filepath.Join(home, "Library", "Application Support", "minecraft")
		}
		// Linux 及其他未识别系统：~/.minecraft
		return filepath.Join(home, ".minecraft")
	}
	return ""
}

// EnsureDefaultMinecraftDirectory 检测默认 Minecraft 目录是否存在；不存在时在
// 平台默认路径下创建符合 Minecraft 目录结构的空文件夹骨架。
// 返回保证存在的默认 Minecraft 目录路径。
func EnsureDefaultMinecraftDirectory() string {
	defaultDir := GetDefaultMinecraftDirectory()
	if defaultDir == "" {
		return ""
	}
	if !directoryExists(defaultDir) {
		_ = os.MkdirAll(defaultDir, 0o755)
		for _, sub := range standardSubDirectories {
			_ = os.MkdirAll(filepath.Join(defaultDir, sub), 0o755)
		}
	}
	return defaultDir
}

// ResolveInstallationPath 接受 Minecraft 根目录，或 versions/<版本号> 形式的
// 独立实例目录，返回解析后的安装位置信息。
func ResolveInstallationPath(path string) (*MinecraftInstallationLocation, error) {
	// 空路径直接拒绝
	if strings.TrimSpace(path) == "" {
		return nil, newLaunchError("Minecraft 路径不能为空。")
	}

	// 规范化：去掉首尾空白与引号、展开环境变量、转换为绝对路径
	trimmed := strings.Trim(strings.TrimSpace(path), `"`)
	fullPath, err := filepath.Abs(os.ExpandEnv(trimmed))
	if err != nil {
		return nil, newLaunchError("Minecraft 路径非法：" + path)
	}
	if !directoryExists(fullPath) {
		return nil, newLaunchError("Minecraft 路径不存在：" + fullPath)
	}

	// 判断是否为 "versions/<版本号>" 形式的独立实例目录：
	// 父目录名为 versions，且目录内存在与目录同名的 <版本号>.json 版本描述文件
	directoryName := filepath.Base(fullPath)
	parent := filepath.Dir(fullPath)
	if strings.EqualFold(filepath.Base(parent), "versions") &&
		fileExists(filepath.Join(fullPath, directoryName+".json")) {
		// 版本目录的上一级（versions 的父级）即为 Minecraft 根目录
		root := filepath.Dir(parent)
		if err := validateRootDirectory(root); err != nil {
			return nil, err
		}
		return &MinecraftInstallationLocation{
			MinecraftDirectory: root,
			PreferredVersionId: directoryName,
			GameDirectory:      fullPath,
		}, nil
	}

	// 不是实例目录，则按普通根目录处理并校验
	if err := validateRootDirectory(fullPath); err != nil {
		return nil, err
	}
	return &MinecraftInstallationLocation{MinecraftDirectory: fullPath}, nil
}

// GetInstalledVersionIds 扫描指定 Minecraft 根目录下已安装的版本列表。
// 只统计"版本文件夹内存在同名 .json 版本描述文件"的完整版本，
// 可过滤掉下载中断留下的残缺目录。
// 返回按名称忽略大小写降序排列的版本 ID 列表；无 versions 目录时返回空列表。
// 注意：不隐藏被 inheritsFrom 依赖的原版版本。原版目录由完整安装产生
// （含客户端 JAR 与用户数据），是可独立启动的实例。
func GetInstalledVersionIds(minecraftDirectory string) []string {
	versionsDirectory := filepath.Join(minecraftDirectory, "versions")

	// 尚未下载任何版本时直接返回空列表
	if !directoryExists(versionsDirectory) {
		return []string{}
	}

	// 第一轮：收集所有有效版本 ID
	var allIds []string
	entries, err := os.ReadDir(versionsDirectory)
	if err != nil {
		return []string{}
	}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		id := entry.Name()
		if strings.TrimSpace(id) == "" {
			continue
		}
		if fileExists(filepath.Join(versionsDirectory, id, id+".json")) {
			allIds = append(allIds, id)
		}
	}

	sort.Slice(allIds, func(left, right int) bool {
		return strings.ToLower(allIds[left]) > strings.ToLower(allIds[right])
	})
	return allIds
}

// validateRootDirectory 校验指定路径是否为有效的 Minecraft 根目录
// （必须包含 versions 文件夹）。
func validateRootDirectory(root string) error {
	if !directoryExists(filepath.Join(root, "versions")) {
		return newLaunchError("该路径不是有效的 Minecraft 根目录，缺少 versions 文件夹：" + root)
	}
	return nil
}

func isWindows() bool { return goos == "windows" }
func isMacOS() bool   { return goos == "darwin" }

func userHomeDirectory() string {
	home, err := os.UserHomeDir()
	if err != nil {
		return ""
	}
	return home
}

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func directoryExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}
