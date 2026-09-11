// launch_bridge.go 对尚未移植的 config / launch 包的最小本地替代实现。
// 待 internal/config 与 internal/launch 移植完成后，应改为直接依赖对应包，
// 并删除本文件（详见 PORTING_NOTES.md）。
package download

import (
	"encoding/json"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
)

// ---------------------------------------------------------------------------
// LauncherConfig 替代：内存键值 + 宿主注入钩子
// ---------------------------------------------------------------------------

var (
	configMu     sync.RWMutex
	configValues = map[string]string{}
)

// ConfigGetValue 读取配置值；宿主（Wails 侧 / 将来的 config 包）可通过
// SetConfigHooks 注入真实持久化实现。默认落到进程内内存。
func ConfigGetValue(key string) (string, bool) {
	if configGetHook != nil {
		return configGetHook(key)
	}
	configMu.RLock()
	defer configMu.RUnlock()
	v, ok := configValues[key]
	return v, ok
}

// ConfigSetValue 写入配置值（默认进程内内存）。
func ConfigSetValue(key, value string) {
	if configSetHook != nil {
		configSetHook(key, value)
		return
	}
	configMu.Lock()
	configValues[key] = value
	configMu.Unlock()
}

// 宿主注入的持久化钩子。
var (
	configGetHook func(key string) (string, bool)
	configSetHook func(key, value string)
)

// SetConfigHooks 注入配置读写钩子（由宿主在启动时调用一次）。
func SetConfigHooks(get func(key string) (string, bool), set func(key, value string)) {
	configGetHook = get
	configSetHook = set
}

// ConfigGameDirectory 游戏目录配置；空串表示未设置。
func ConfigGameDirectory() string {
	v, _ := ConfigGetValue("gameDirectory")
	return v
}

// ConfigSaveGameDirectory 保存游戏目录配置。
func ConfigSaveGameDirectory(dir string) {
	ConfigSetValue("gameDirectory", dir)
}

// ConfigDefaultVersionIsolation 全局默认版本隔离开关。
func ConfigDefaultVersionIsolation() bool {
	v, ok := ConfigGetValue("defaultVersionIsolation")
	if !ok {
		return false
	}
	return v == "true" || v == "True" || v == "1"
}

// ---------------------------------------------------------------------------
// MinecraftDirectoryLocator 替代
// ---------------------------------------------------------------------------

// GetDefaultMinecraftDirectory 默认 .minecraft 目录（Windows 为 %APPDATA%\\.minecraft）。
func GetDefaultMinecraftDirectory() string {
	switch runtime.GOOS {
	case "windows":
		appData := os.Getenv("APPDATA")
		if appData == "" {
			appData = filepath.Join(UserHomeDir(), "AppData", "Roaming")
		}
		return filepath.Join(appData, ".minecraft")
	case "darwin":
		return filepath.Join(UserHomeDir(), "Library", "Application Support", "minecraft")
	default:
		return filepath.Join(UserHomeDir(), ".minecraft")
	}
}

// EnsureDefaultMinecraftDirectory 确保默认目录存在并返回。
func EnsureDefaultMinecraftDirectory() string {
	dir := GetDefaultMinecraftDirectory()
	_ = os.MkdirAll(dir, 0o755)
	return dir
}

// UserHomeDir 用户主目录；不可用时返回空串。
func UserHomeDir() string {
	home, err := os.UserHomeDir()
	if err != nil {
		return ""
	}
	return home
}

// ---------------------------------------------------------------------------
// GameInstanceStore / GameVersionIsolation / GameInstanceLayoutResolver 替代：
// 以钩子形式暴露，宿主移植实例管理后注入；默认实现为空操作 / 目录直连。
// ---------------------------------------------------------------------------

// RefreshInstancesHook 扫描并刷新实例列表（对应 GameInstanceStore.RefreshAsync）。
var RefreshInstancesHook func(gameDirectory string) error

// SelectInstanceHook 选中实例（对应 GameInstanceStore.Select）。
var SelectInstanceHook func(instanceID string)

// RefreshInstances 刷新实例列表（无钩子时为空操作）。
func RefreshInstances(gameDirectory string) error {
	if RefreshInstancesHook != nil {
		return RefreshInstancesHook(gameDirectory)
	}
	return nil
}

// SelectInstance 选中实例（无钩子时为空操作）。
func SelectInstance(instanceID string) {
	if SelectInstanceHook != nil {
		SelectInstanceHook(instanceID)
	}
}

// ResolveContentDirectoryHook 解析实例的内容目录（对应 GameVersionIsolation.Resolve）。
// 返回 mods / resourcepacks 等的父目录。nil 时退化为 minecraftDirectory 本身。
var ResolveContentDirectoryHook func(minecraftDirectory, sourcePath, versionID string) string

// ResolveInstanceLayoutHook 解析实例布局（对应 GameInstanceLayoutResolver.Resolve）。
// 返回内容目录。参数：（目标根目录, 活跃游戏目录, 实例名, 版本ID, 全局默认隔离）。
var ResolveInstanceLayoutHook func(targetRoot, sourcePath, instanceName, versionID string, defaultIsolation bool) string

// ---------------------------------------------------------------------------
// MinecraftRuleEvaluator 最小实现：版本 JSON 库条目的 rules 过滤
// ---------------------------------------------------------------------------

// ruleJSON 版本 JSON 的 rules 数组条目。
type ruleJSON struct {
	Action   string          `json:"action"`
	OS       *ruleOSJSON     `json:"os"`
	Features map[string]bool `json:"features"`
}

type ruleOSJSON struct {
	Name string `json:"name"`
	Arch string `json:"arch"`
}

// DefaultFeatures 默认特性集（与 C# CreateDefaultFeatures(hasCustomResolution: true) 一致）。
func DefaultFeatures() map[string]bool {
	return map[string]bool{"has_custom_resolution": true}
}

// RuleEvaluatorOSName 当前系统名（Mojang 约定：windows / osx / linux）。
func RuleEvaluatorOSName() string {
	switch runtime.GOOS {
	case "windows":
		return "windows"
	case "darwin":
		return "osx"
	default:
		return "linux"
	}
}

// ruleIsAllowed 判断库条目的 rules 是否允许当前系统（与启动器使用同一套规则）。
// 无 rules 时允许。
func ruleIsAllowed(rules []ruleJSON, features map[string]bool) bool {
	if len(rules) == 0 {
		return true
	}
	allowed := false
	for _, rule := range rules {
		if rule.Features != nil {
			match := true
			for k, v := range rule.Features {
				if features[k] != v {
					match = false
					break
				}
			}
			if !match {
				continue
			}
		}
		if rule.OS != nil {
			if rule.OS.Name != "" && rule.OS.Name != RuleEvaluatorOSName() {
				continue
			}
			if rule.OS.Arch != "" {
				want := rule.OS.Arch
				have := "x86"
				if runtime.GOARCH == "arm64" {
					have = "arm64"
				}
				if want != have {
					continue
				}
			}
		}
		if rule.Action == "allow" {
			allowed = true
		} else if rule.Action == "disallow" {
			allowed = false
		}
	}
	return allowed
}

// ---------------------------------------------------------------------------
// VersionJsonFlattener 最小实现：继承链扁平化
// ---------------------------------------------------------------------------

// VersionJSON 版本 JSON 的安装期所需子集（与 Mojang 格式对应）。
type VersionJSON struct {
	ID                 string            `json:"id,omitempty"`
	InheritsFrom       string            `json:"inheritsFrom,omitempty"`
	MainClass          string            `json:"mainClass,omitempty"`
	Assets             string            `json:"assets,omitempty"`
	MinecraftArguments string            `json:"minecraftArguments,omitempty"`
	Jar                string            `json:"jar,omitempty"`
	Libraries          []libraryJSON     `json:"libraries,omitempty"`
	Arguments          json.RawMessage   `json:"arguments,omitempty"`
	Downloads          *downloadsJSON    `json:"downloads,omitempty"`
	AssetIndex         *downloadInfoJSON `json:"assetIndex,omitempty"`
	Logging            json.RawMessage   `json:"logging,omitempty"`
	ClientVersion      string            `json:"clientVersion,omitempty"`
}

type downloadsJSON struct {
	Client *downloadInfoJSON `json:"client"`
}

type downloadInfoJSON struct {
	URL  string `json:"url"`
	SHA1 string `json:"sha1"`
	Size int64  `json:"size"`
	Path string `json:"path"`
	ID   string `json:"id,omitempty"` // assetIndex 节点用（资源索引 ID）
}

type libraryDownloadsJSON struct {
	Artifact    *downloadInfoJSON            `json:"artifact"`
	Classifiers map[string]*downloadInfoJSON `json:"classifiers"`
}

type libraryJSON struct {
	Name      string                `json:"name,omitempty"`
	URL       string                `json:"url,omitempty"`
	Natives   map[string]string     `json:"natives,omitempty"`
	Rules     []ruleJSON            `json:"rules,omitempty"`
	Downloads *libraryDownloadsJSON `json:"downloads,omitempty"`
}

// flattenVersionJSON 把 inheritsFrom 继承链合并为实例自包含的版本 JSON，
// 并复制客户端 JAR（最小实现：合并 libraries、继承原版参数与 JAR）。
// 对应 C# VersionJsonFlattener.FlattenAsync。
func flattenVersionJSON(root, instanceName string) error {
	instanceDir := filepath.Join(root, "versions", instanceName)
	instanceJSONPath := filepath.Join(instanceDir, instanceName+".json")
	raw, err := os.ReadFile(instanceJSONPath)
	if err != nil {
		return err
	}
	var node map[string]json.RawMessage
	if err := json.Unmarshal(raw, &node); err != nil {
		return err
	}
	parentIDRaw, ok := node["inheritsFrom"]
	if !ok {
		return nil // 无继承链，无需扁平化
	}
	var parentID string
	if err := json.Unmarshal(parentIDRaw, &parentID); err != nil {
		return err
	}
	parentJSONPath := filepath.Join(root, "versions", parentID, parentID+".json")
	parentRaw, err := os.ReadFile(parentJSONPath)
	if err != nil {
		return err
	}
	var parentNode map[string]json.RawMessage
	if err := json.Unmarshal(parentRaw, &parentNode); err != nil {
		return err
	}

	// 继承字段：子缺则用父；libraries 合并（子在前，兼容 overrides）
	mergeInherited := func(key string) {
		if _, ok := node[key]; !ok {
			if v, ok := parentNode[key]; ok {
				node[key] = v
			}
		}
	}
	mergeInherited("minecraftArguments")
	for _, key := range []string{"assets", "assetIndex", "downloads", "logging", "arguments"} {
		mergeInherited(key)
	}
	if _, ok := node["mainClass"]; !ok {
		if v, ok := parentNode["mainClass"]; ok {
			node["mainClass"] = v
		}
	}

	var childLibs, parentLibs []libraryJSON
	if v, ok := node["libraries"]; ok {
		_ = json.Unmarshal(v, &childLibs)
	}
	if v, ok := parentNode["libraries"]; ok {
		_ = json.Unmarshal(v, &parentLibs)
	}
	merged := append(append([]libraryJSON{}, childLibs...), parentLibs...)
	if libBytes, err := json.Marshal(merged); err == nil {
		node["libraries"] = libBytes
	}

	delete(node, "inheritsFrom")

	mergedBytes, err := json.Marshal(node)
	if err != nil {
		return err
	}
	if err := os.WriteFile(instanceJSONPath, mergedBytes, 0o644); err != nil {
		return err
	}

	// 复制客户端 JAR：优先父 JAR（jar 字段指定），否则 parent 同名 JAR
	jarSourceName := parentID
	var jarRef string
	if v, ok := parentNode["jar"]; ok {
		_ = json.Unmarshal(v, &jarRef)
	}
	if jarRef != "" {
		jarSourceName = jarRef
	}
	sourceJar := filepath.Join(root, "versions", jarSourceName, jarSourceName+".jar")
	targetJar := filepath.Join(instanceDir, instanceName+".jar")
	if _, err := os.Stat(sourceJar); err == nil {
		if _, err := os.Stat(targetJar); err != nil {
			data, err := os.ReadFile(sourceJar)
			if err == nil {
				_ = os.WriteFile(targetJar, data, 0o644)
			}
		}
	}
	return nil
}

// IsVersionReferenced 检查 root/versions 下的其它版本 JSON 是否仍引用 dependencyVersionId
// （inheritsFrom 字段）。对应 C# VersionJsonFlattener.IsVersionReferenced。
func IsVersionReferenced(root, dependencyVersionID string) bool {
	versionsDir := filepath.Join(root, "versions")
	entries, err := os.ReadDir(versionsDir)
	if err != nil {
		return false
	}
	_ = dependencyVersionID // 通过反序列化比较，无需字符串搜索
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		if strings.EqualFold(entry.Name(), dependencyVersionID) {
			continue
		}
		files, err := filepath.Glob(filepath.Join(versionsDir, entry.Name(), "*.json"))
		if err != nil {
			continue
		}
		for _, f := range files {
			data, err := os.ReadFile(f)
			if err != nil {
				continue
			}
			var probe struct {
				InheritsFrom string `json:"inheritsFrom"`
			}
			if json.Unmarshal(data, &probe) == nil && probe.InheritsFrom != "" &&
				strings.EqualFold(probe.InheritsFrom, dependencyVersionID) {
				return true
			}
		}
	}
	return false
}

// ---------------------------------------------------------------------------
// JavaRuntimeLocator 最小实现
// ---------------------------------------------------------------------------

// FindJavaExecutable 查找本机可用的 java 可执行文件。
// 优先在托管运行时目录（<mcDir>/runtime）递归扫描，回退 JAVA_HOME / PATH。
// 对应 C# JavaRuntimeLocator.FindJavaExecutable。
func FindJavaExecutable(runtimeDirectory string) string {
	javaName := "java"
	if runtime.GOOS == "windows" {
		javaName = "java.exe"
	}
	// 1) 托管运行时目录递归扫描
	if runtimeDirectory != "" {
		if found := findFileInTree(runtimeDirectory, javaName, "bin"); found != "" {
			return found
		}
	}
	// 2) JAVA_HOME
	if home := os.Getenv("JAVA_HOME"); home != "" {
		candidate := filepath.Join(home, "bin", javaName)
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}
	}
	// 3) PATH
	if path, err := exec.LookPath(javaName); err == nil {
		return path
	}
	return ""
}

// findFileInTree 在目录树中查找指定文件名（要求父目录为 parentName，如 "bin"）。
func findFileInTree(root, fileName, parentName string) string {
	var found string
	_ = filepath.WalkDir(root, func(path string, d os.DirEntry, err error) error {
		if err != nil || found != "" {
			if found != "" {
				return filepath.SkipAll
			}
			return nil
		}
		if d.IsDir() || d.Name() != fileName {
			return nil
		}
		if filepath.Base(filepath.Dir(path)) == parentName {
			found = path
			return filepath.SkipAll
		}
		return nil
	})
	return found
}
