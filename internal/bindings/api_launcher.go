package bindings

// LauncherAPI：游戏启动管线。对应 C# GameLaunchService / GameMemorySettings。

import (
	"nyalauncher/internal/launch"
)

// GetLaunchSnapshot 当前启动状态快照。
func (a *LauncherAPI) GetLaunchSnapshot() launch.GameLaunchSnapshot { return a.service.Current() }

// GetLogText 内存日志全文（启动 + 游戏输出；前端可配合 launch:changed 轮询刷新）。
func (a *LauncherAPI) GetLogText() string { return a.service.GetLogText() }

// Launch 启动当前选中的实例与账号；serverHost 非空时直接进服。
// Wails 在后台 goroutine 调用命令，阻塞至启动流程结束（或失败）不会冻结 UI。
func (a *LauncherAPI) Launch(serverHost string, serverPort *int) launch.LaunchResult {
	return a.service.LaunchSelected(callCtx(a.ctx), serverHost, serverPort)
}

// StopGame 停止运行中的游戏进程树。
func (a *LauncherAPI) StopGame() launch.LaunchResult { return a.service.TryStopGame() }

// ---- 内存策略 ----

// GetSystemMemory 物理内存快照（MB）。
func (a *LauncherAPI) GetSystemMemory() launch.SystemMemorySnapshot { return launch.GetSystemMemory() }

// GetMemoryDecision 启动内存决策；instanceMaximumMemoryMb 为实例独立上限（可 nil）。
func (a *LauncherAPI) GetMemoryDecision(instanceMaximumMemoryMb *int) launch.GameMemoryDecision {
	return launch.ResolveForLaunch(instanceMaximumMemoryMb)
}

// IsAutomaticMemoryAdjustmentEnabled 自动内存调整开关。
func (a *LauncherAPI) IsAutomaticMemoryAdjustmentEnabled() bool {
	return launch.GameMemorySettings.IsAutomaticAdjustmentEnabled()
}

// SetAutomaticMemoryAdjustmentEnabled 保存自动内存调整开关。
func (a *LauncherAPI) SetAutomaticMemoryAdjustmentEnabled(enabled bool) {
	launch.GameMemorySettings.SetAutomaticAdjustmentEnabled(enabled)
}

// GetMemorySliderMaximum 滑块上限（物理内存按 256MB 向下取整）。
func (a *LauncherAPI) GetMemorySliderMaximum() int { return launch.GameMemorySettings.SliderMaximumMemoryMb() }

// GetManualMaximumMemoryMb 手动模式生效值。
func (a *LauncherAPI) GetManualMaximumMemoryMb() int { return launch.GameMemorySettings.ManualMaximumMemoryMb() }

// SaveManualMaximumMemoryMb 保存手动内存上限。
func (a *LauncherAPI) SaveManualMaximumMemoryMb(memoryMb int) bool {
	return launch.GameMemorySettings.SaveManualMaximumMemoryMb(memoryMb)
}

// ---- 工具 ----

// DetectJavaMajorVersion 探测 Java 主版本号（失败返回 nil）。
func (a *LauncherAPI) DetectJavaMajorVersion(javaExecutable string) *int {
	return launch.TryDetectJavaMajorVersion(javaExecutable)
}

// DeleteDirectory 尝试删除目录树（用于实例目录清理）。
func (a *LauncherAPI) DeleteDirectory(directory string) { launch.TryDeleteDirectory(directory) }

// RedactLogSecrets 脱敏日志中的敏感令牌。
func (a *LauncherAPI) RedactLogSecrets(line string) string { return launch.RedactSecrets(line) }
