package bindings

// ContentAPI：实例内容（Mod / 资源包 / 光影 / 存档）扫描、自定义图标与存档操作。

import (
	"nyalauncher/internal/content"
	"nyalauncher/internal/instance"
)

// ---- 内容扫描 ----

// ReadMods 读取目录下全部 Mod。
func (a *ContentAPI) ReadMods(directory string) []content.GameContentEntry {
	return content.ReadMods(callCtx(a.ctx), directory)
}

// ReadResourcePacks 读取资源包。
func (a *ContentAPI) ReadResourcePacks(directory string) []content.GameContentEntry {
	return content.ReadResourcePacks(callCtx(a.ctx), directory)
}

// ReadShaders 读取光影包。
func (a *ContentAPI) ReadShaders(directory string) []content.GameContentEntry {
	return content.ReadShaders(callCtx(a.ctx), directory)
}

// ReadSaves 读取存档。
func (a *ContentAPI) ReadSaves(directory string) []content.GameContentEntry {
	return content.ReadSaves(callCtx(a.ctx), directory)
}

// GetInstanceVisual 解析实例图标（当前选中目录上下文）。
func (a *ContentAPI) GetInstanceVisual(versionID, loaderName string) content.GameInstanceVisual {
	snapshot := instance.CurrentSnapshot()
	return content.ResolveInstanceVisual(content.InstanceContext{
		SourcePath:         snapshot.SourcePath,
		MinecraftDirectory: snapshot.MinecraftDirectory,
	}, versionID, loaderName)
}

// ---- 自定义图标 ----

// GetCustomIconPath 自定义图标路径（未设置为空串）。
func (a *ContentAPI) GetCustomIconPath(minecraftDirectory, versionID string) string {
	return content.GetCustomIconPath(minecraftDirectory, versionID)
}

// SetCustomIcon 设置自定义图标（校验扩展名与 8MB 上限），返回存储路径。
func (a *ContentAPI) SetCustomIcon(minecraftDirectory, versionID, sourcePath string) (string, error) {
	return content.SetCustomIcon(minecraftDirectory, versionID, sourcePath)
}

// RemoveCustomIcon 移除自定义图标。
func (a *ContentAPI) RemoveCustomIcon(minecraftDirectory, versionID string) bool {
	return content.RemoveCustomIcon(minecraftDirectory, versionID)
}

// ---- 存档操作 ----

// ExportSave 把存档目录打包为 .zip。
func (a *ContentAPI) ExportSave(saveDirectory, destinationZipPath string) (string, error) {
	return content.ExportSave(callCtx(a.ctx), saveDirectory, destinationZipPath)
}

// BackupSave 备份存档到启动器存储目录，返回 .zip 路径。
func (a *ContentAPI) BackupSave(saveDirectory string) (string, error) {
	return content.BackupSave(callCtx(a.ctx), saveDirectory)
}

// DeleteSave 删除存档目录。
func (a *ContentAPI) DeleteSave(saveDirectory string) error { return content.DeleteSave(saveDirectory) }
