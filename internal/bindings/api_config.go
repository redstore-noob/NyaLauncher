package bindings

// ConfigAPI：全局配置（config.json）读写。对应 C# LauncherConfig /
// GameVersionProfileStore / GlobalLaunchSettingsStore 的公开面。

import (
	"nyalauncher/internal/config"
)

// ---- 存储目录与基础路径 ----

// GetStorageDirectory 启动器存储目录。
func (a *ConfigAPI) GetStorageDirectory() string { return config.StorageDirectory() }

// GetLegacyDefaultStorageDirectory 旧版默认存储目录（迁移检测用）。
func (a *ConfigAPI) GetLegacyDefaultStorageDirectory() string { return config.LegacyDefaultStorageDirectory() }

// GetDefaultStorageDirectory 默认存储目录。
func (a *ConfigAPI) GetDefaultStorageDirectory() string { return config.DefaultStorageDirectory() }

// GetUserHome 用户主目录。
func (a *ConfigAPI) GetUserHome() string { return config.UserHome() }

// SetStorageDirectory 设置存储目录（需在启动早期调用）。
func (a *ConfigAPI) SetStorageDirectory(storageDir string) error {
	return config.SetStorageDirectory(storageDir)
}

// ---- 游戏目录 ----

// GetGameDirectory 游戏目录；空串表示未设置。
func (a *ConfigAPI) GetGameDirectory() string { return config.GameDirectory() }

// SaveGameDirectory 保存游戏目录。
func (a *ConfigAPI) SaveGameDirectory(path string) bool { return config.SaveGameDirectory(path) }

// ClearGameDirectory 清除游戏目录配置。
func (a *ConfigAPI) ClearGameDirectory() { config.ClearGameDirectory() }

// ---- Java ----

// GetJavaExecutable 全局 Java 路径。
func (a *ConfigAPI) GetJavaExecutable() string { return config.JavaExecutable() }

// GetJavaVersion 全局 Java 版本。
func (a *ConfigAPI) GetJavaVersion() string { return config.JavaVersion() }

// SaveJava 保存全局 Java。
func (a *ConfigAPI) SaveJava(javaPath, javaVersion string) bool {
	return config.SaveJava(javaPath, javaVersion)
}

// ClearJava 清除全局 Java。
func (a *ConfigAPI) ClearJava() { config.ClearJava() }

// GetJavaPaths Java 列表。
func (a *ConfigAPI) GetJavaPaths() []config.JavaPathItem { return config.GetJavaPaths() }

// AddJava 追加 Java 条目。
func (a *ConfigAPI) AddJava(javaPath, javaVersion string) bool { return config.AddJava(javaPath, javaVersion) }

// RemoveJava 删除 Java 条目。
func (a *ConfigAPI) RemoveJava(javaPath string) bool { return config.RemoveJava(javaPath) }

// SetPrimaryJava 设为主 Java。
func (a *ConfigAPI) SetPrimaryJava(javaPath string) bool { return config.SetPrimaryJava(javaPath) }

// ---- 开关与通用键值 ----

// GetDefaultVersionIsolation 全局默认版本隔离开关。
func (a *ConfigAPI) GetDefaultVersionIsolation() bool { return config.DefaultVersionIsolation() }

// SaveDefaultVersionIsolation 保存全局默认版本隔离；nil 表示清除为推断默认。
func (a *ConfigAPI) SaveDefaultVersionIsolation(value *bool) { config.SaveDefaultVersionIsolation(value) }

// GetVerifyFilesBeforeLaunch 启动前是否校验文件。
func (a *ConfigAPI) GetVerifyFilesBeforeLaunch() bool { return config.VerifyFilesBeforeLaunch() }

// SaveVerifyFilesBeforeLaunch 保存启动前校验开关。
func (a *ConfigAPI) SaveVerifyFilesBeforeLaunch(enabled bool) { config.SaveVerifyFilesBeforeLaunch(enabled) }

// SetValue 写入任意配置键。
func (a *ConfigAPI) SetValue(key, value string) bool { return config.SetValue(key, value) }

// GetValue 读取任意配置键（不存在为空串）。
func (a *ConfigAPI) GetValue(key string) string { return config.GetValue(key) }

// ClearValue 删除配置键。
func (a *ConfigAPI) ClearValue(key string) bool { return config.ClearValue(key) }

// PathsEqualNormalized 路径规范化后是否相等（Windows 忽略大小写）。
func (a *ConfigAPI) PathsEqualNormalized(left, right string) bool {
	return config.PathsEqualNormalized(left, right)
}

// ---- 全局高级启动设置 ----

// LoadGlobalLaunchSettings 读取全局高级启动设置。
func (a *ConfigAPI) LoadGlobalLaunchSettings() config.GlobalLaunchSettings {
	return config.LoadGlobalLaunchSettings()
}

// SaveGlobalLaunchSettings 保存全局高级启动设置。
func (a *ConfigAPI) SaveGlobalLaunchSettings(settings config.GlobalLaunchSettings) bool {
	return config.SaveGlobalLaunchSettings(settings)
}

// SaveGlobalWindowSize 保存全局窗口尺寸（启动器窗口，记忆用）。
func (a *ConfigAPI) SaveGlobalWindowSize(width, height int) bool {
	return config.SaveGlobalWindowSize(width, height)
}

// ---- 实例档案（独立内存 / 窗口 / JVM 参数 / 图标偏好） ----

// NewGameVersionProfile 带默认值的实例档案。
func (a *ConfigAPI) NewGameVersionProfile() config.GameVersionProfile {
	return config.NewGameVersionProfile()
}

// GetVersionProfile 读取实例档案（无则返回默认值）。
func (a *ConfigAPI) GetVersionProfile(minecraftDirectory, versionID string) config.GameVersionProfile {
	return config.Get(minecraftDirectory, versionID)
}

// SaveVersionProfile 保存实例档案。
func (a *ConfigAPI) SaveVersionProfile(profile config.GameVersionProfile) bool {
	return config.Save(profile)
}

// GetProfileFolders 额外扫描的游戏目录列表。
func (a *ConfigAPI) GetProfileFolders() []string { return config.GetFolders() }

// AddProfileFolder 追加额外游戏目录。
func (a *ConfigAPI) AddProfileFolder(path string) bool { return config.AddFolder(path) }

// RemoveProfileFolder 移除额外游戏目录。
func (a *ConfigAPI) RemoveProfileFolder(path string) bool { return config.RemoveFolder(path) }

// GetInstanceIconOverride 实例图标偏好（nil = 跟随自动）。
func (a *ConfigAPI) GetInstanceIconOverride(minecraftDirectory, versionID string) *string {
	return config.GetInstanceIconOverride(minecraftDirectory, versionID)
}

// SaveInstanceIconOverride 保存实例图标偏好。
func (a *ConfigAPI) SaveInstanceIconOverride(minecraftDirectory, versionID string, overrideValue *string) bool {
	return config.SaveInstanceIconOverride(minecraftDirectory, versionID, overrideValue)
}

// MigrateRenamedVersion 版本重命名后迁移关联配置（实例档案 / 目录 / 选中记录）。
func (a *ConfigAPI) MigrateRenamedVersion(
	minecraftDirectory, oldVersionID, newVersionID, oldVersionDirectory, newVersionDirectory string,
) {
	config.MigrateRenamedVersion(minecraftDirectory, oldVersionID, newVersionID, oldVersionDirectory, newVersionDirectory)
}
