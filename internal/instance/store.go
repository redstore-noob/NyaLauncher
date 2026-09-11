// 已安装实例的共享状态仓库：扫描、选中与快照发布。
// 移植自 NyaLauncher.Core/Launch/GameInstanceStore.cs。
package instance

import (
	"context"
	"fmt"
	"os"
	"strings"
	"sync"

	"nyalauncher/internal/config"
	"nyalauncher/internal/logs"
	"nyalauncher/internal/tools"
)

// selectedVersionConfigKey config.json 中保存选中版本的字段名。
const selectedVersionConfigKey = "selectedGameInstance"

var (
	// gate 保护 current / latestRefreshID / changedHandlers 的锁。
	gate sync.Mutex
	// current 当前已发布的实例快照。
	current = EmptyGameInstanceSnapshot()
	// latestRefreshID 最新一次刷新请求的序号，过期扫描结果不得覆盖新请求。
	latestRefreshID uint64
	// changedHandlers 对应 C# Changed 事件：订阅者列表，通知时异常被隔离。
	changedHandlers = map[int]func(GameInstanceSnapshot){}
	// nextSubscriptionID 订阅序号，用于取消订阅。
	nextSubscriptionID int
)

// CurrentSnapshot 返回当前已发布的实例快照（对应 C# GameInstanceStore.Current）。
func CurrentSnapshot() GameInstanceSnapshot {
	gate.Lock()
	defer gate.Unlock()
	return current
}

// SubscribeChanged 订阅快照变更事件（对应 C# Changed 事件）；返回取消订阅函数。
func SubscribeChanged(handler func(GameInstanceSnapshot)) (unsubscribe func()) {
	gate.Lock()
	defer gate.Unlock()
	id := nextSubscriptionID
	nextSubscriptionID++
	changedHandlers[id] = handler
	return func() {
		gate.Lock()
		defer gate.Unlock()
		delete(changedHandlers, id)
	}
}

// ResolveConfiguredSourcePath 当前应扫描的游戏目录来源：优先用户配置，其次环境变量，
// 最后落到默认 .minecraft 位置。启动时与组件刷新共用，保证各入口扫描的是同一个目录。
func ResolveConfiguredSourcePath() string {
	fromConfig := config.GameDirectory()
	if strings.TrimSpace(fromConfig) != "" {
		return fromConfig
	}
	if fromEnvironment := os.Getenv("NYALAUNCHER_MINECRAFT_DIR"); fromEnvironment != "" {
		return fromEnvironment
	}
	return EnsureDefaultDirectory()
}

// Refresh 扫描并发布新的实例快照（对应 C# RefreshAsync；CancellationToken → context）。
// 过期的扫描结果无法覆盖更新的请求；调用方通过返回值获得最新已发布快照。
func Refresh(ctx context.Context, sourcePath string) GameInstanceSnapshot {
	normalizedSource := strings.TrimSpace(sourcePath)
	previous, _, refreshID := beginRefresh(normalizedSource)
	raiseChanged(CurrentSnapshot())

	scanned, err := scanWithCancel(ctx, normalizedSource, previous)
	if err != nil {
		if ctx.Err() != nil {
			// 取消时若加载中快照仍是最新发布，恢复上一个快照，
			// 避免存储停留在 IsLoading=true 导致启动被"仍在扫描"拒绝
			if tryPublish(refreshID, previous) {
				raiseChanged(previous)
			}
			return CurrentSnapshot()
		}
		failed := GameInstanceSnapshot{
			SourcePath:   normalizedSource,
			VersionIds:   []string{},
			IsLoading:    false,
			ErrorMessage: err.Error(),
		}
		if tryPublish(refreshID, failed) {
			raiseChanged(failed)
		}
		return CurrentSnapshot()
	}

	// 已有更新的刷新请求在跑：丢弃本次结果，交由最新请求发布
	if !tryPublish(refreshID, scanned) {
		return CurrentSnapshot()
	}

	persistSelection(previous, normalizedSource, scanned)
	raiseChanged(scanned)
	return scanned
}

// scanWithCancel 在独立 goroutine 中执行磁盘枚举并等待完成，
// 保留 C# Task.Run + CancellationToken 的语义（取消时中断等待）。
func scanWithCancel(ctx context.Context, sourcePath string, previous GameInstanceSnapshot) (GameInstanceSnapshot, error) {
	type scanResult struct {
		snapshot GameInstanceSnapshot
		err      error
	}
	done := make(chan scanResult, 1)
	go func() {
		snapshot, err := scan(sourcePath, previous)
		done <- scanResult{snapshot, err}
	}()
	select {
	case result := <-done:
		return result.snapshot, result.err
	case <-ctx.Done():
		return GameInstanceSnapshot{}, ctx.Err()
	}
}

// Select 选中指定版本并写回配置；扫描中 / 出错 / 版本不存在时返回 false。
func Select(versionID string) bool {
	if strings.TrimSpace(versionID) == "" {
		return false
	}

	gate.Lock()
	outcome, published := applySelect(versionID)
	gate.Unlock()

	switch outcome {
	case selectRejected:
		return false
	case selectRefreshed:
		config.SetValue(selectedVersionConfigKey, published.SelectedVersionId)
		raiseChanged(published)
		return true
	default: // selectUnchanged：重复选中已选版本
		return true
	}
}

type selectOutcome int

const (
	// selectRejected 扫描中 / 出错 / 版本不存在，无法选中。
	selectRejected selectOutcome = iota
	// selectUnchanged 该版本已经是当前选中，无需任何变更。
	selectUnchanged
	// selectRefreshed 选中已切换到新快照，需要发布通知。
	selectRefreshed
)

// applySelect 在锁内应用选中。published 在拒绝时为零值。
func applySelect(versionID string) (selectOutcome, GameInstanceSnapshot) {
	currentSnapshot := current
	if currentSnapshot.IsLoading || currentSnapshot.ErrorMessage != "" {
		return selectRejected, GameInstanceSnapshot{}
	}

	// 大小写不敏感匹配：版本目录在 Windows 文件系统上大小写不敏感
	var match string
	for _, id := range currentSnapshot.VersionIds {
		if strings.EqualFold(id, versionID) {
			match = id
			break
		}
	}
	if match == "" {
		return selectRejected, GameInstanceSnapshot{}
	}
	if strings.EqualFold(currentSnapshot.SelectedVersionId, match) {
		return selectUnchanged, GameInstanceSnapshot{}
	}

	layout := GameVersionIsolationResolve(currentSnapshot, match)
	gameDirectory := ""
	if layout.IsIsolated {
		gameDirectory = layout.ContentDirectory
	}
	published := currentSnapshot.withSelected(match, layout.IsIsolated, gameDirectory)
	current = published
	return selectRefreshed, published
}

// CanResolveSource 路径是否能被解析为有效的 Minecraft 安装位置或外部实例。
func CanResolveSource(sourcePath string) bool {
	if _, err := ResolveInstallationPath(sourcePath); err == nil {
		return true
	}
	// 常规解析不认的路径再尝试外部实例识别
	_, ok := TryResolveExternalInstance(sourcePath)
	return ok
}

// scan 枚举磁盘上的版本并组装快照（C# GameInstanceStore.Scan）。
func scan(sourcePath string, previous GameInstanceSnapshot) (GameInstanceSnapshot, error) {
	location, err := ResolveInstallationPath(sourcePath)
	if err != nil {
		if external, ok := TryResolveExternalInstance(sourcePath); ok {
			return createExternalSnapshot(sourcePath, external), nil
		}
		return GameInstanceSnapshot{}, err
	}

	versions := GetInstalledVersionIds(location.MinecraftDirectory)

	// 版本文件夹可能被用户在启动器外手动删除（或改名后残留旧配置）。
	// 扫描出实际存在的版本后立即清理该目录下已消失版本的实例配置，
	// 防止其隔离、内存等设置残留在 config.json 中被后续启动逻辑误读。
	config.PruneMissingVersions(location.MinecraftDirectory, versions)

	selected := resolveSelectedVersion(versions, previous, location)
	return createStandardSnapshot(sourcePath, location, versions, selected), nil
}

// createExternalSnapshot 外部实例快照：单版本，目录来源即实例内容目录。
func createExternalSnapshot(sourcePath string, external ExternalGameInstanceLayout) GameInstanceSnapshot {
	return GameInstanceSnapshot{
		SourcePath:                          sourcePath,
		MinecraftDirectory:                  external.LauncherRoot,
		GameDirectory:                       external.ContentDirectory,
		VersionIds:                          []string{external.InstanceId},
		SelectedVersionId:                   external.InstanceId,
		UsesVersionDirectoryAsGameDirectory: true,
	}
}

// resolveSelectedVersion 选中版本优先级：上次会话的选中（限同一目录）> 全局保存的选中 >
// 安装位置自带的推荐版本 > 扫描出的第一个版本。
func resolveSelectedVersion(versions []string, previous GameInstanceSnapshot, location MinecraftInstallationLocation) string {
	sameDirectory := tools.PathsEqual(previous.MinecraftDirectory, location.MinecraftDirectory)
	previousSelection := ""
	if sameDirectory {
		previousSelection = previous.SelectedVersionId
	}
	savedSelection := config.GetValue(selectedVersionConfigKey)

	if found := findInstalled(versions, previousSelection); found != "" {
		return found
	}
	if found := findInstalled(versions, savedSelection); found != "" {
		return found
	}
	if found := findInstalled(versions, location.PreferredVersionId); found != "" {
		return found
	}
	if len(versions) > 0 {
		return versions[0]
	}
	return ""
}

// createStandardSnapshot 组装普通（非外部）实例快照，并解析选中版本的隔离布局。
func createStandardSnapshot(sourcePath string, location MinecraftInstallationLocation, versions []string, selected string) GameInstanceSnapshot {
	// 先用"无内容目录"的中间快照解析隔离布局，再据此填 GameDirectory
	provisional := GameInstanceSnapshot{
		SourcePath:                          sourcePath,
		MinecraftDirectory:                  location.MinecraftDirectory,
		GameDirectory:                       "",
		VersionIds:                          versions,
		SelectedVersionId:                   selected,
		UsesVersionDirectoryAsGameDirectory: location.GameDirectory != "",
	}
	layout := GameVersionLayout{}
	if selected != "" {
		layout = GameVersionIsolationResolve(provisional, selected)
	}

	isIsolated := selected != "" && layout.IsIsolated
	gameDirectory := ""
	if isIsolated {
		gameDirectory = layout.ContentDirectory
	}
	result := GameInstanceSnapshot{
		SourcePath:                          sourcePath,
		MinecraftDirectory:                  location.MinecraftDirectory,
		GameDirectory:                       gameDirectory,
		VersionIds:                          versions,
		SelectedVersionId:                   selected,
		UsesVersionDirectoryAsGameDirectory: isIsolated,
	}
	return result
}

// findInstalled 在版本列表中按不区分大小写查找候选版本，未找到返回空串。
func findInstalled(versions []string, candidate string) string {
	if strings.TrimSpace(candidate) == "" {
		return ""
	}
	for _, version := range versions {
		if strings.EqualFold(version, candidate) {
			return version
		}
	}
	return ""
}

// beginRefresh 发布"加载中"快照，返回上一个快照、加载中快照与本次刷新序号。
func beginRefresh(normalizedSource string) (GameInstanceSnapshot, GameInstanceSnapshot, uint64) {
	gate.Lock()
	defer gate.Unlock()
	previous := current
	latestRefreshID++
	refreshID := latestRefreshID
	loading := GameInstanceSnapshot{
		SourcePath: normalizedSource,
		VersionIds: []string{},
		IsLoading:  true,
	}
	current = loading
	return previous, loading, refreshID
}

// persistSelection 将选中版本写回配置：有选中则记录；扫不到任何版本且扫描的就是当前目录时，
// 清除指向已删除版本的选中记录（用户切到空文件夹时不清其它目录的记录）。
func persistSelection(previous GameInstanceSnapshot, normalizedSource string, scanned GameInstanceSnapshot) {
	if strings.TrimSpace(scanned.SelectedVersionId) != "" {
		config.SetValue(selectedVersionConfigKey, scanned.SelectedVersionId)
		return
	}

	// 该目录下已无任何有效实例（例如唯一实例被用户在启动器外手动删除）。
	if tools.PathsEqual(previous.SourcePath, normalizedSource) {
		config.ClearValue(selectedVersionConfigKey)
	}
}

// tryPublish 仅当 refreshID 仍是最新刷新时才发布快照，防止过期扫描覆盖新请求。
func tryPublish(refreshID uint64, snapshot GameInstanceSnapshot) bool {
	gate.Lock()
	defer gate.Unlock()
	if refreshID != latestRefreshID {
		return false
	}
	current = snapshot
	return true
}

// raiseChanged 逐个通知订阅者；单个订阅者异常不扩散，只写入日志。
func raiseChanged(snapshot GameInstanceSnapshot) {
	gate.Lock()
	handlers := make([]func(GameInstanceSnapshot), 0, len(changedHandlers))
	for _, handler := range changedHandlers {
		handlers = append(handlers, handler)
	}
	gate.Unlock()

	for _, subscriber := range handlers {
		func() {
			defer func() {
				if err := recover(); err != nil {
					logs.Write("DEBUG", fmt.Sprintf("GameInstanceStore.Changed 订阅者异常：%v", err))
				}
			}()
			subscriber(snapshot)
		}()
	}
}
