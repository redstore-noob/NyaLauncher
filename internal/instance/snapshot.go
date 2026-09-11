// Package instance 移植自 NyaLauncher.Core/Launch 的实例相关服务：
// GameInstanceStore、GameInstanceLayoutResolver、GameVersionDetailsService、
// GameVersionRenameService，以及它们依赖的 GameVersionIsolation 与
// MinecraftDirectoryLocator（见 PORTING_NOTES.md）。
package instance

// GameInstanceSnapshot 某一时刻的已安装实例视图；不可变，发布后不再修改。
// C# 的可空字段在 Go 中以空字符串表示"无"（见 PORTING_NOTES.md 偏离点）。
type GameInstanceSnapshot struct {
	SourcePath                          string   `json:"SourcePath"`
	MinecraftDirectory                  string   `json:"MinecraftDirectory"`
	GameDirectory                       string   `json:"GameDirectory"`
	VersionIds                          []string `json:"VersionIds"`
	SelectedVersionId                   string   `json:"SelectedVersionId"`
	UsesVersionDirectoryAsGameDirectory bool     `json:"UsesVersionDirectoryAsGameDirectory"`
	IsLoading                           bool     `json:"IsLoading"`
	ErrorMessage                        string   `json:"ErrorMessage"`
}

// EmptyGameInstanceSnapshot 尚未完成首次扫描时的空快照。
func EmptyGameInstanceSnapshot() GameInstanceSnapshot {
	return GameInstanceSnapshot{
		VersionIds: []string{},
		IsLoading:  true,
	}
}

// clone 返回深拷贝（VersionIds 切片独立），保证已发布快照不被后续修改。
func (s GameInstanceSnapshot) clone() GameInstanceSnapshot {
	ids := make([]string, len(s.VersionIds))
	copy(ids, s.VersionIds)
	s.VersionIds = ids
	return s
}

// withSelected 返回切换选中后的新快照（对应 C# record 的 with 表达式）。
func (s GameInstanceSnapshot) withSelected(match string, isolated bool, gameDirectory string) GameInstanceSnapshot {
	next := s.clone()
	next.SelectedVersionId = match
	next.UsesVersionDirectoryAsGameDirectory = isolated
	next.GameDirectory = gameDirectory
	return next
}
