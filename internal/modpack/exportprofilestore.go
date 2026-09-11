// Package modpack 移植自 NyaLauncher.Core/Modpack：
// ModpackExportService（整合包导出）与 ModpackExportProfileStore（导出档案持久化）。
package modpack

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
)

// ModpackExportProfile 整合包导出档案：随实例存放在版本目录
// (<versionDirectory>/nya-pack.json)，记住上次打包的勾选排除项与元数据，
// 方便开发者反复调整、重新打包。JSON 字段名与 C# 完全一致（camelCase）。
// 指针字段为 nil 表示未填写（序列化时省略，对应 C# WhenWritingNull）。
type ModpackExportProfile struct {
	PackName             *string   `json:"packName,omitempty"`
	PackVersion          *string   `json:"packVersion,omitempty"`
	Author               *string   `json:"author,omitempty"`
	Description          *string   `json:"description,omitempty"`
	UpdateLink           *string   `json:"updateLink,omitempty"`
	Format               *int      `json:"format,omitempty"`
	ResolveModrinthLinks *bool     `json:"resolveModrinthLinks,omitempty"`
	ExcludedPaths        *[]string `json:"excludedPaths,omitempty"`
}

// EmptyModpackExportProfile 尚未保存过档案时的空配置。
func EmptyModpackExportProfile() ModpackExportProfile {
	return ModpackExportProfile{}
}

// exportProfileFileName 档案文件名。
const exportProfileFileName = "nya-pack.json"

// GetProfilePath 返回版本目录对应的导出档案路径。
func GetProfilePath(versionDirectory string) string {
	return filepath.Join(versionDirectory, exportProfileFileName)
}

// LoadProfile 读取导出档案；不存在或损坏时返回 Empty。
func LoadProfile(versionDirectory string) ModpackExportProfile {
	if strings.TrimSpace(versionDirectory) == "" {
		return EmptyModpackExportProfile()
	}
	profilePath := GetProfilePath(versionDirectory)
	data, err := os.ReadFile(profilePath)
	if err != nil {
		return EmptyModpackExportProfile()
	}

	// 逐字段宽容解析：类型不符的字段按缺失处理，与 C# 的手工读取一致
	var raw map[string]any
	if err := json.Unmarshal(data, &raw); err != nil {
		return EmptyModpackExportProfile()
	}

	profile := ModpackExportProfile{}
	if value, ok := rawString(raw, "packName"); ok {
		profile.PackName = &value
	}
	if value, ok := rawString(raw, "packVersion"); ok {
		profile.PackVersion = &value
	}
	if value, ok := rawString(raw, "author"); ok {
		profile.Author = &value
	}
	if value, ok := rawString(raw, "description"); ok {
		profile.Description = &value
	}
	if value, ok := rawString(raw, "updateLink"); ok {
		profile.UpdateLink = &value
	}
	if value, ok := rawInt(raw, "format"); ok {
		profile.Format = &value
	}
	if value, ok := rawBool(raw, "resolveModrinthLinks"); ok {
		profile.ResolveModrinthLinks = &value
	}
	if entries, ok := raw["excludedPaths"].([]any); ok {
		excluded := make([]string, 0, len(entries))
		for _, entry := range entries {
			if text, ok := entry.(string); ok && text != "" {
				excluded = append(excluded, text)
			}
		}
		profile.ExcludedPaths = &excluded
	}
	return profile
}

// SaveProfile 保存导出档案；失败返回 false（打包本身不受影响）。
// 先写临时文件再覆盖移动，避免半写状态的档案。
func SaveProfile(versionDirectory string, profile ModpackExportProfile) bool {
	if strings.TrimSpace(versionDirectory) == "" {
		return false
	}
	if err := os.MkdirAll(versionDirectory, 0o755); err != nil {
		return false
	}
	path := GetProfilePath(versionDirectory)
	data, err := json.MarshalIndent(profile, "", "  ")
	if err != nil {
		return false
	}
	data = append(data, '\n')
	temporaryPath := path + ".tmp"
	if err := os.WriteFile(temporaryPath, data, 0o644); err != nil {
		return false
	}
	if err := os.Rename(temporaryPath, path); err != nil {
		_ = os.Remove(temporaryPath)
		return false
	}
	return true
}

func rawString(raw map[string]any, key string) (string, bool) {
	value, ok := raw[key].(string)
	return value, ok && value != ""
}

func rawInt(raw map[string]any, key string) (int, bool) {
	value, ok := raw[key].(float64)
	return int(value), ok
}

func rawBool(raw map[string]any, key string) (bool, bool) {
	value, ok := raw[key].(bool)
	return value, ok
}
