package bindings

// ModpackAPI：整合包导出（Modrinth .mrpack / MultiMC .zip）与导出配置档案。
// 事件：modpack:exportProgress（ModpackExportProgress）。

import (
	"nyalauncher/internal/modpack"
)

// CollectExportContent 收集实例内容目录中可打包的内容单元。
func (a *ModpackAPI) CollectExportContent(contentDirectory string) []modpack.ModpackContentItem {
	return modpack.CollectContent(contentDirectory)
}

// ExportModpack 打包整合包到指定输出路径；进度经 modpack:exportProgress 事件推送。
func (a *ModpackAPI) ExportModpack(
	options modpack.ModpackExportOptions,
	contentDirectory, outputPath string,
) (modpack.ModpackExportResult, error) {
	return modpack.Export(callCtx(a.ctx), options, contentDirectory, outputPath, func(progress modpack.ModpackExportProgress) {
		emit(a.ctx, "modpack:exportProgress", progress)
	})
}

// NewExportOptions 带默认值的导出参数（ResolveModrinthLinks = true 等）。
func (a *ModpackAPI) NewExportOptions() modpack.ModpackExportOptions { return modpack.NewExportOptions() }

// ---- 导出配置档案（按版本目录持久化） ----

// GetExportProfilePath 导出配置文件路径。
func (a *ModpackAPI) GetExportProfilePath(versionDirectory string) string {
	return modpack.GetProfilePath(versionDirectory)
}

// LoadExportProfile 读取导出配置（无则空档案）。
func (a *ModpackAPI) LoadExportProfile(versionDirectory string) modpack.ModpackExportProfile {
	return modpack.LoadProfile(versionDirectory)
}

// SaveExportProfile 保存导出配置。
func (a *ModpackAPI) SaveExportProfile(versionDirectory string, profile modpack.ModpackExportProfile) bool {
	return modpack.SaveProfile(versionDirectory, profile)
}
