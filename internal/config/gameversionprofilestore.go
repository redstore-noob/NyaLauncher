package config

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"nyalauncher/internal/logs"
	"nyalauncher/internal/tools"
)

// GameVersionProfile 单个游戏实例（Minecraft 目录 + 版本 ID）的可编辑启动设置。
// JSON 字段名与 C# 记录的默认序列化名（PascalCase）保持一致，兼容读取既有 config.json。
type GameVersionProfile struct {
	MinecraftDirectory           string `json:"MinecraftDirectory"`
	VersionId                    string `json:"VersionId"`
	MinimumMemoryMb              int    `json:"MinimumMemoryMb"`
	MaximumMemoryMb              int    `json:"MaximumMemoryMb"`
	UseIndependentMemorySettings bool   `json:"UseIndependentMemorySettings"`
	FollowGlobalAdvancedSettings bool   `json:"FollowGlobalAdvancedSettings"`
	WindowWidth                  int    `json:"WindowWidth"`
	WindowHeight                 int    `json:"WindowHeight"`
	// IsVersionIsolationEnabled null 保留旧版"直接选择 versions/id 目录"的推断默认值；
	// true/false 是用户对该实例的显式选择。
	IsVersionIsolationEnabled *bool    `json:"IsVersionIsolationEnabled"`
	JavaExecutable            string   `json:"JavaExecutable"`
	AdditionalJvmArguments    []string `json:"AdditionalJvmArguments"`
	AdditionalGameArguments   []string `json:"AdditionalGameArguments"`
	// InstanceIconOverride 实例图标偏好：null 表示"跟随加载器自动"；
	// "gameicon:{key}" 表示显式选择某个内置图标；"custom" 表示使用自定义图标。
	InstanceIconOverride *string `json:"InstanceIconOverride"`
}

// NewGameVersionProfile 返回带 C# 默认值的新实例档案。
func NewGameVersionProfile() GameVersionProfile {
	return GameVersionProfile{
		MinimumMemoryMb:              512,
		MaximumMemoryMb:              4096,
		FollowGlobalAdvancedSettings: true,
		WindowWidth:                  854,
		WindowHeight:                 480,
		AdditionalJvmArguments:       []string{},
		AdditionalGameArguments:      []string{},
	}
}

const (
	foldersKey          = "gameVersionFolders"
	profilesKey         = "gameVersionProfiles"
	selectedInstanceKey = "selectedGameInstance"
)

var (
	profileGate sync.Mutex
	// changedHandlers 配置发生任何变更后触发的订阅者（对应 C# Changed 事件，
	// 异常被隔离，不影响其他订阅者）。
	changedMu       sync.Mutex
	changedHandlers []func()
)

// AddChangedHandler 注册变更通知订阅者（对应 C# Changed 事件订阅）。
func AddChangedHandler(handler func()) {
	changedMu.Lock()
	defer changedMu.Unlock()
	changedHandlers = append(changedHandlers, handler)
}

// DefaultMinecraftDirectoryLocator 平台默认 Minecraft 目录的定位钩子。
// C# 版依赖 Core 中的 MinecraftDirectoryLocator（尚未移植）；
// 移植后应在此注入实现，未注入时 GetFolders 不追加默认目录。
var DefaultMinecraftDirectoryLocator func() string

// GetFolders 汇总游戏目录列表：当前活跃目录排最前，用户添加的目录随后，
// 平台默认目录兜底在最后。
func GetFolders() []string {
	profileGate.Lock()
	defer profileGate.Unlock()
	return getFoldersLocked()
}

// getFoldersLocked 需持 profileGate 调用（供 AddFolder/RemoveFolder 复用）。
func getFoldersLocked() []string {
	folders := deserializeStringList(GetValue(foldersKey))
	configured := GameDirectory()
	if strings.TrimSpace(configured) != "" {
		folders = append([]string{configured}, folders...)
	}

	if DefaultMinecraftDirectoryLocator != nil {
		defaultDir := DefaultMinecraftDirectoryLocator()
		if dirExists(defaultDir) {
			folders = append(folders, defaultDir)
		}
	}

	result := make([]string, 0, len(folders))
	for _, path := range folders {
		if strings.TrimSpace(path) == "" {
			continue
		}
		normalized := normalizePathOrOriginal(path)
		duplicated := false
		for _, existing := range result {
			if pathsEqualFold(existing, normalized) {
				duplicated = true
				break
			}
		}
		if !duplicated {
			result = append(result, normalized)
		}
	}
	return result
}

// AddFolder 向文件夹列表添加目录。目录不存在或路径非法时返回 false。
func AddFolder(path string) bool {
	if strings.TrimSpace(path) == "" {
		logs.Write("ERROR", "AddFolder: path 不能为空")
		return false
	}
	normalized, ok := normalizeExistingPath(path)
	if !ok {
		return false
	}

	profileGate.Lock()
	defer profileGate.Unlock()
	folders := getFoldersLocked()
	for _, existing := range folders {
		if pathsEqualFold(existing, normalized) {
			return saveFoldersAndNotify(folders)
		}
	}
	folders = append(folders, normalized)
	return saveFoldersAndNotify(folders)
}

// RemoveFolder 从文件夹列表中移除指定目录。不允许移除平台默认 Minecraft 目录；
// 若移除的是当前活跃游戏目录，则自动切换到列表中剩余的首个目录。
// 移除成功返回 true；目录不在列表中或为默认目录时返回 false。
func RemoveFolder(path string) bool {
	if strings.TrimSpace(path) == "" {
		logs.Write("ERROR", "RemoveFolder: path 不能为空")
		return false
	}
	normalized, ok := normalizeExistingPath(path)
	if !ok {
		return false
	}

	if DefaultMinecraftDirectoryLocator != nil {
		defaultDir := DefaultMinecraftDirectoryLocator()
		if tools.PathsEqual(normalized, trimTrailingSeparator(filepath.Clean(absolutePath(defaultDir)))) {
			return false
		}
	}

	profileGate.Lock()
	folders := getFoldersLocked()
	found := false
	kept := make([]string, 0, len(folders))
	for _, folder := range folders {
		if pathsEqualFold(folder, normalized) {
			found = true
			continue
		}
		kept = append(kept, folder)
	}
	if !found {
		profileGate.Unlock()
		return false
	}
	if !saveFolders(kept) {
		profileGate.Unlock()
		return false
	}

	// 移除的是当前活跃目录 → 自动切换到列表中剩余的首个目录（没有则清空）
	if pathsEqualFold(GameDirectory(), normalized) {
		next := ""
		for _, folder := range kept {
			if !pathsEqualFold(folder, normalized) {
				next = folder
				break
			}
		}
		if strings.TrimSpace(next) == "" {
			ClearGameDirectory()
		} else {
			SaveGameDirectory(next)
		}
	}
	profileGate.Unlock()

	raiseChanged()
	return true
}

// Get 读取实例配置；未配置或参数无效时返回默认档案。
func Get(minecraftDirectory, versionId string) GameVersionProfile {
	if strings.TrimSpace(minecraftDirectory) == "" || strings.TrimSpace(versionId) == "" {
		profile := NewGameVersionProfile()
		profile.VersionId = versionId
		return profile
	}

	normalizedDirectory := normalizePathOrOriginal(minecraftDirectory)
	profileGate.Lock()
	defer profileGate.Unlock()
	if profile, ok := findProfile(normalizedDirectory, versionId); ok {
		return profile
	}
	profile := NewGameVersionProfile()
	profile.MinecraftDirectory = normalizedDirectory
	profile.VersionId = versionId
	return profile
}

// PruneMissingVersions 清理指定 Minecraft 目录下已不存在版本的实例配置。
// 版本文件夹可能被用户在启动器外手动删除或改名，实例扫描完成后调用本方法，
// 防止这些版本的隔离、内存等残留设置无限累积在 config.json 中被后续逻辑误读。
// 版本存在性以扫描结果（实例列表实际展示的版本）为准，比较不区分大小写。
// minecraftDirectory 为本次扫描的 Minecraft 根目录，只清理该目录下的配置；
// existingVersionIds 为扫描到的仍实际存在的版本 Id 集合（空表示全部清除）。
// 返回清理掉的配置条数（0 表示无需清理）。
func PruneMissingVersions(minecraftDirectory string, existingVersionIds []string) int {
	if strings.TrimSpace(minecraftDirectory) == "" {
		return 0
	}

	normalizedDirectory := normalizePathOrOriginal(minecraftDirectory)
	// 空集合 → 目录下已无任何有效版本，该目录全部实例配置都应清除
	existing := make(map[string]struct{}, len(existingVersionIds))
	for _, id := range existingVersionIds {
		existing[strings.ToLower(id)] = struct{}{}
	}

	removed := 0
	profileGate.Lock()
	profiles := loadProfiles()
	kept := make([]GameVersionProfile, 0, len(profiles))
	for _, profile := range profiles {
		// 其他 Minecraft 目录的实例配置不受本次扫描影响
		if matchesDirectory(profile, normalizedDirectory) {
			if _, exists := existing[strings.ToLower(profile.VersionId)]; !exists {
				removed++
				continue
			}
		}
		kept = append(kept, profile)
	}
	if removed > 0 {
		SetValue(profilesKey, serializeProfiles(kept))
	}
	profileGate.Unlock()

	if removed > 0 {
		raiseChanged()
	}
	return removed
}

// Save 新增或覆盖实例配置；校验失败或落盘失败返回 false。
func Save(profile GameVersionProfile) bool {
	if strings.TrimSpace(profile.MinecraftDirectory) == "" ||
		strings.TrimSpace(profile.VersionId) == "" ||
		profile.MinimumMemoryMb < 256 ||
		profile.MaximumMemoryMb < profile.MinimumMemoryMb ||
		profile.WindowWidth < 320 ||
		profile.WindowHeight < 240 {
		return false
	}

	normalized := profile
	normalized.MinecraftDirectory = normalizePathOrOriginal(profile.MinecraftDirectory)
	normalized.JavaExecutable = strings.TrimSpace(profile.JavaExecutable)
	normalized.AdditionalJvmArguments = normalizeArguments(profile.AdditionalJvmArguments)
	normalized.AdditionalGameArguments = normalizeArguments(profile.AdditionalGameArguments)

	profileGate.Lock()
	profiles := loadProfiles()
	profiles = removeAllProfiles(profiles, normalized.MinecraftDirectory, normalized.VersionId)
	profiles = append(profiles, normalized)
	saved := SetValue(profilesKey, serializeProfiles(profiles))
	profileGate.Unlock()
	if !saved {
		return false
	}

	raiseChanged()
	return true
}

// MigrateRenamedVersion 版本被重命名后迁移关联配置：实例配置、目录列表、
// 活跃游戏目录与当前选中实例。
func MigrateRenamedVersion(
	minecraftDirectory string,
	oldVersionId, newVersionId string,
	oldVersionDirectory, newVersionDirectory string) {

	profileGate.Lock()
	profiles := loadProfiles()
	for index := range profiles {
		profile := profiles[index]
		if matchesDirectory(profile, minecraftDirectory) &&
			matchesVersion(profile, oldVersionId) {
			profile.VersionId = newVersionId
			profiles[index] = profile
		}
	}
	SetValue(profilesKey, serializeProfiles(profiles))

	folders := deserializeStringList(GetValue(foldersKey))
	for index := range folders {
		if pathsEqualFold(folders[index], oldVersionDirectory) {
			folders[index] = newVersionDirectory
		}
	}
	saveFolders(distinctPaths(folders))

	if pathsEqualFold(GameDirectory(), oldVersionDirectory) {
		SaveGameDirectory(newVersionDirectory)
	}
	// 仅当被重命名的实例确实是当前选中项时才迁移选中记录：
	// 重命名其他实例不应悄悄改掉用户的当前选择。
	currentSelected := GetValue(selectedInstanceKey)
	if strings.EqualFold(currentSelected, oldVersionId) {
		SetValue(selectedInstanceKey, newVersionId)
	}
	profileGate.Unlock()

	raiseChanged()
}

// GetInstanceIconOverride 读取实例图标偏好（见 GameVersionProfile.InstanceIconOverride）。
func GetInstanceIconOverride(minecraftDirectory, versionId string) *string {
	return Get(minecraftDirectory, versionId).InstanceIconOverride
}

// SaveInstanceIconOverride 仅更新实例图标偏好，不触碰其他设置；失败返回 false。
func SaveInstanceIconOverride(minecraftDirectory, versionId string, overrideValue *string) bool {
	if strings.TrimSpace(minecraftDirectory) == "" || strings.TrimSpace(versionId) == "" {
		return false
	}

	normalized := normalizePathOrOriginal(minecraftDirectory)
	profileGate.Lock()
	defer profileGate.Unlock()
	profiles := loadProfiles()
	// 只更新最后一条匹配（与 Get 的 LastOrDefault 语义一致），更新后移到末尾
	index := findProfileIndex(profiles, normalized, versionId)
	var profile GameVersionProfile
	if index >= 0 {
		profile = profiles[index]
		profile.InstanceIconOverride = overrideValue
		profiles = append(profiles[:index], profiles[index+1:]...)
	} else {
		profile = NewGameVersionProfile()
		profile.MinecraftDirectory = normalized
		profile.VersionId = versionId
		profile.InstanceIconOverride = overrideValue
	}
	profiles = append(profiles, profile)
	return SetValue(profilesKey, serializeProfiles(profiles))
}

// loadProfiles 从 config.json 读取全部实例配置，需持 profileGate 调用。
func loadProfiles() []GameVersionProfile {
	raw := GetValue(profilesKey)
	if strings.TrimSpace(raw) == "" {
		return []GameVersionProfile{}
	}
	var profiles []GameVersionProfile
	// config.json 中被手改坏的 JSON 视作未配置
	if err := json.Unmarshal([]byte(raw), &profiles); err != nil {
		return []GameVersionProfile{}
	}
	if profiles == nil {
		profiles = []GameVersionProfile{}
	}
	return profiles
}

// findProfile 按（目录, 版本 ID）取最后一条匹配的实例配置。
func findProfile(directory, versionId string) (GameVersionProfile, bool) {
	profiles := loadProfiles()
	for i := len(profiles) - 1; i >= 0; i-- {
		if matchesDirectory(profiles[i], directory) && matchesVersion(profiles[i], versionId) {
			return profiles[i], true
		}
	}
	return GameVersionProfile{}, false
}

func findProfileIndex(profiles []GameVersionProfile, directory, versionId string) int {
	for i := len(profiles) - 1; i >= 0; i-- {
		if matchesDirectory(profiles[i], directory) && matchesVersion(profiles[i], versionId) {
			return i
		}
	}
	return -1
}

func removeAllProfiles(profiles []GameVersionProfile, directory, versionId string) []GameVersionProfile {
	kept := profiles[:0]
	for _, profile := range profiles {
		if matchesDirectory(profile, directory) && matchesVersion(profile, versionId) {
			continue
		}
		kept = append(kept, profile)
	}
	return kept
}

func matchesDirectory(profile GameVersionProfile, directory string) bool {
	return tools.PathsEqual(profile.MinecraftDirectory, directory)
}

func matchesVersion(profile GameVersionProfile, versionId string) bool {
	return strings.EqualFold(profile.VersionId, versionId)
}

// saveFoldersAndNotify 在锁内保存文件夹列表并触发变更通知（AddFolder 复用）。
func saveFoldersAndNotify(folders []string) bool {
	if !saveFolders(folders) {
		return false
	}
	raiseChanged()
	return true
}

// normalizeExistingPath 规范化用户输入的目录；目录不存在或路径非法时返回 false。
func normalizeExistingPath(path string) (string, bool) {
	trimmed := trimTrailingSeparator(filepath.Clean(absolutePath(strings.TrimSpace(path))))
	if !dirExists(trimmed) {
		return "", false
	}
	return trimmed, true
}

// saveFolders 持 profileGate 调用（调用方已持锁）。
func saveFolders(folders []string) bool {
	if folders == nil {
		folders = []string{}
	}
	return SetValue(foldersKey, serializeStringList(folders))
}

// deserializeStringList 反序列化文件夹列表；坏 JSON 视作未配置（空列表）。
func deserializeStringList(raw string) []string {
	if strings.TrimSpace(raw) == "" {
		return []string{}
	}
	var list []string
	if err := json.Unmarshal([]byte(raw), &list); err != nil {
		return []string{}
	}
	if list == nil {
		list = []string{}
	}
	return list
}

func serializeStringList(list []string) string {
	if list == nil {
		list = []string{}
	}
	data, err := json.Marshal(list)
	if err != nil {
		return "[]"
	}
	return string(data)
}

// serializeProfiles 序列化实例配置列表（与 C# JsonSerializer.Serialize 的
// PascalCase 默认命名一致，保证新旧版本互相可读）。
func serializeProfiles(profiles []GameVersionProfile) string {
	if profiles == nil {
		profiles = []GameVersionProfile{}
	}
	data, err := json.Marshal(profiles)
	if err != nil {
		return "[]"
	}
	return string(data)
}

// normalizeArguments 参数规范化：剔除空白项、逐项 Trim；空结果归一为非 nil 空数组。
func normalizeArguments(arguments []string) []string {
	normalized := make([]string, 0, len(arguments))
	for _, argument := range arguments {
		if strings.TrimSpace(argument) == "" {
			continue
		}
		normalized = append(normalized, strings.TrimSpace(argument))
	}
	return normalized
}

// distinctPaths 保序去重（PathComparer 语义：Windows 忽略大小写）。
func distinctPaths(paths []string) []string {
	result := make([]string, 0, len(paths))
	for _, path := range paths {
		duplicated := false
		for _, existing := range result {
			if pathsEqualFold(existing, path) {
				duplicated = true
				break
			}
		}
		if !duplicated {
			result = append(result, path)
		}
	}
	return result
}

// normalizePathOrOriginal 规范化路径；非法路径时回退为 Trim 后的原文。
func normalizePathOrOriginal(path string) string {
	trimmed := strings.TrimSpace(path)
	abs, err := filepath.Abs(trimmed)
	if err != nil {
		return trimmed
	}
	return trimTrailingSeparator(filepath.Clean(abs))
}

// dirExists 判断目录是否存在。
func dirExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}

// raiseChanged 触发变更通知；单个订阅者 panic 被隔离，不影响其他订阅者。
func raiseChanged() {
	changedMu.Lock()
	handlers := make([]func(), len(changedHandlers))
	copy(handlers, changedHandlers)
	changedMu.Unlock()
	for _, handler := range handlers {
		func() {
			defer func() {
				if r := recover(); r != nil {
					logs.Write("ERROR", "GameVersionProfileStore.Changed 订阅者异常")
				}
			}()
			handler()
		}()
	}
}
