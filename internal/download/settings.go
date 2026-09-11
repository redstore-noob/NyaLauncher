package download

import (
	"strconv"
	"strings"
)

// 并行下载线程数边界。
const (
	// DefaultParallelDownloads 默认并行下载线程数。
	DefaultParallelDownloads = 8
	// MinParallelDownloads 最小并行下载线程数。
	MinParallelDownloads = 1
	// MaxParallelDownloads 最大并行下载线程数。
	MaxParallelDownloads = 32
)

// DownloadSettings 下载设置的持久化入口。通过配置钩子读写（见 launch_bridge.go）。
// 对应 C# DownloadSettings（原实现走 LauncherConfig）。

// ParallelDownloads 并行下载线程数（同时下载的文件块数）。
func ParallelDownloads() int {
	value, _ := ConfigGetValue("downloadParallelDownloads")
	result, err := strconv.Atoi(strings.TrimSpace(value))
	if err == nil && result >= MinParallelDownloads && result <= MaxParallelDownloads {
		return result
	}
	return DefaultParallelDownloads
}

// SaveParallelDownloads 保存并行下载线程数。
func SaveParallelDownloads(count int) {
	if count < MinParallelDownloads {
		count = MinParallelDownloads
	}
	if count > MaxParallelDownloads {
		count = MaxParallelDownloads
	}
	ConfigSetValue("downloadParallelDownloads", strconv.Itoa(count))
}

// ActiveSourceName 当前活跃下载源名称。未配置时默认 BMCL
// （国内网络友好，Mojang 官方源国内访问常超时）。
func ActiveSourceName() string {
	value, _ := ConfigGetValue("downloadActiveSource")
	if strings.TrimSpace(value) != "" {
		return value
	}
	return DownloadSources.Bmcl.Name
}

// SaveActiveSource 保存活跃下载源。
func SaveActiveSource(source DownloadSource) {
	ConfigSetValue("downloadActiveSource", source.Name)
	SourceProvider.SetActive(source)
}

// FallbackSourceName 自动回退源名称。首次启动默认取与活跃源不同的另一个源
// （跨源容灾才有意义）；用户显式禁用时返回空串。
func FallbackSourceName() string {
	value, ok := ConfigGetValue("downloadFallbackSource")
	// 未设置过 → 默认取另一个源
	// "disabled" = 用户显式禁用 → 无回退
	// 其他非空 = 用户选择的源名称
	if !ok || strings.TrimSpace(value) == "" {
		// 旧默认固定 BMCL：在活跃源也是 BMCL 时"回退"等于重试同一 URL
		active := ActiveSourceName()
		for _, s := range AllDownloadSources {
			if !strings.EqualFold(s.Name, active) {
				return s.Name
			}
		}
		return ""
	}
	if strings.EqualFold(value, "disabled") {
		return ""
	}
	return value
}

// SaveFallbackSource 保存自动回退源。传 nil 禁用回退。
func SaveFallbackSource(source *DownloadSource) {
	// "disabled" = 用户显式禁用回退
	// "BMCL" 等 = 用户选择的源
	// 不存在 = 首次启动默认 BMCL
	if source == nil {
		ConfigSetValue("downloadFallbackSource", "disabled")
	} else {
		ConfigSetValue("downloadFallbackSource", source.Name)
	}
	SourceProvider.SetFallback(source)
}

// ApplySavedSettings 应用已保存的设置到 SourceProvider。应在启动时调用一次。
func ApplySavedSettings() {
	activeName := ActiveSourceName()
	active := DownloadSources.Bmcl
	for _, s := range AllDownloadSources {
		if strings.EqualFold(s.Name, activeName) {
			active = s
			break
		}
	}
	SourceProvider.SetActive(active)

	fallbackName := FallbackSourceName()
	if strings.TrimSpace(fallbackName) == "" {
		SourceProvider.SetFallback(nil)
	} else {
		for _, s := range AllDownloadSources {
			if strings.EqualFold(s.Name, fallbackName) {
				fallback := s
				SourceProvider.SetFallback(&fallback)
				break
			}
		}
	}
}
