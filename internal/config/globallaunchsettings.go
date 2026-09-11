package config

import (
	"encoding/json"
	"strings"
)

// GlobalLaunchSettings 全局高级启动设置的内存快照。
type GlobalLaunchSettings struct {
	WindowWidth             int
	WindowHeight            int
	JavaExecutable          string
	AdditionalJvmArguments  []string
	AdditionalGameArguments []string
}

// GlobalLaunchSettingsStore 的配置键：所有字段写入 config.json 的独立键，
// 损坏的 JSON 值回退到默认值。
const (
	windowWidthKey    = "globalLaunchWindowWidth"
	windowHeightKey   = "globalLaunchWindowHeight"
	javaExecutableKey = "globalLaunchJavaExecutable"
	jvmArgumentsKey   = "globalLaunchJvmArguments"
	gameArgumentsKey  = "globalLaunchGameArguments"

	// automaticJavaValue Java 路径的"自动选择"占位值
	// （空路径在保存时写成它，避免与未配置混淆）。
	automaticJavaValue = "$auto"

	minimumWindowWidth  = 320
	minimumWindowHeight = 240
	defaultWindowWidth  = 854
	defaultWindowHeight = 480
)

// LoadGlobalLaunchSettings 加载全局高级启动设置。
func LoadGlobalLaunchSettings() GlobalLaunchSettings {
	return GlobalLaunchSettings{
		WindowWidth:             readInt(windowWidthKey, defaultWindowWidth, minimumWindowWidth),
		WindowHeight:            readInt(windowHeightKey, defaultWindowHeight, minimumWindowHeight),
		JavaExecutable:          readJavaExecutable(),
		AdditionalJvmArguments:  readArguments(jvmArgumentsKey),
		AdditionalGameArguments: readArguments(gameArgumentsKey),
	}
}

// SaveGlobalLaunchSettings 保存整份全局设置（参数先规范化：去空白、剔除空参数）。
// 尺寸过小时拒绝保存。
func SaveGlobalLaunchSettings(settings GlobalLaunchSettings) bool {
	if settings.WindowWidth < minimumWindowWidth ||
		settings.WindowHeight < minimumWindowHeight {
		return false
	}

	normalized := settings
	normalized.JavaExecutable = strings.TrimSpace(settings.JavaExecutable)
	normalized.AdditionalJvmArguments = normalizeArguments(settings.AdditionalJvmArguments)
	normalized.AdditionalGameArguments = normalizeArguments(settings.AdditionalGameArguments)
	// 五个键一次事务落盘，避免中途失败留下半新半旧的全局设置
	return UpdateInTransaction(func(config map[string]any) bool {
		config[windowWidthKey] = itoa(normalized.WindowWidth)
		config[windowHeightKey] = itoa(normalized.WindowHeight)
		if strings.TrimSpace(normalized.JavaExecutable) == "" {
			config[javaExecutableKey] = automaticJavaValue
		} else {
			config[javaExecutableKey] = normalized.JavaExecutable
		}
		config[jvmArgumentsKey] = serializeStringList(normalized.AdditionalJvmArguments)
		config[gameArgumentsKey] = serializeStringList(normalized.AdditionalGameArguments)
		return true
	})
}

// SaveGlobalWindowSize 仅保存窗口尺寸（宽/高），不触碰 Java 路径与 JVM/游戏参数。
// 用于主窗口尺寸变更时轻量落盘，避免整份设置回写，也避免用旧值覆盖其它字段。
func SaveGlobalWindowSize(width, height int) bool {
	if width < minimumWindowWidth || height < minimumWindowHeight {
		return false
	}
	return UpdateInTransaction(func(config map[string]any) bool {
		config[windowWidthKey] = itoa(width)
		config[windowHeightKey] = itoa(height)
		return true
	})
}

func readInt(key string, fallback, minimum int) int {
	if value, err := parseInt(GetValue(key)); err == nil && value >= minimum {
		return value
	}
	return fallback
}

func readJavaExecutable() string {
	configured := GetValue(javaExecutableKey)
	if configured == automaticJavaValue {
		return ""
	}
	// 从未配置过时回退到全局探测到的 Java
	if configured == "" {
		return JavaExecutable()
	}
	return configured
}

func readArguments(key string) []string {
	value := GetValue(key)
	if strings.TrimSpace(value) == "" {
		return []string{}
	}
	var arguments []string
	if err := json.Unmarshal([]byte(value), &arguments); err != nil {
		// 用户手改 config.json 写坏数组 → 回退为空参数，不影响启动
		return []string{}
	}
	return normalizeArguments(arguments)
}
