// Package models 跨包共享的数据模型（与 Mojang / Modrinth API 的 JSON 契约一一对应）。
package models

import (
	"fmt"
	"time"
)

// VersionManifest Minecraft 版本清单根对象（来自 Mojang API）。
type VersionManifest struct {
	Latest   LatestVersions     `json:"latest"`
	Versions []MinecraftVersion `json:"versions"`
}

// LatestVersions 最新正式版 / 快照版版本号。
type LatestVersions struct {
	Release  string `json:"release"`
	Snapshot string `json:"snapshot"`
}

// MinecraftVersion 版本清单中单个版本条目。
type MinecraftVersion struct {
	ID          string    `json:"id"`
	Type        string    `json:"type"`
	URL         string    `json:"url"`
	Time        time.Time `json:"time"`
	ReleaseTime time.Time `json:"releaseTime"`

	// IsLatestRelease / IsLatestSnapshot 由清单加载方填充。
	IsLatestRelease  bool `json:"-"`
	IsLatestSnapshot bool `json:"-"`
}

// TypeDisplay 版本类型的中文显示名。
func (v MinecraftVersion) TypeDisplay() string {
	switch v.Type {
	case "release":
		return "正式版"
	case "snapshot":
		return "快照版"
	case "old_beta":
		return "经典 Beta"
	case "old_alpha":
		return "经典 Alpha"
	default:
		return v.Type
	}
}

// TypeIcon 版本类型对应的图标。
func (v MinecraftVersion) TypeIcon() string {
	switch v.Type {
	case "release":
		return "📦"
	case "snapshot":
		return "🧪"
	case "old_beta":
		return "🔶"
	case "old_alpha":
		return "🔷"
	default:
		return "📄"
	}
}

// DisplayName 版本显示名。
func (v MinecraftVersion) DisplayName() string {
	return fmt.Sprintf("Minecraft %s", v.ID)
}
