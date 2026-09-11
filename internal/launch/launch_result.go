// Package launch 移植自 NyaLauncher.Core/Launch（不含 GameInstanceStore /
// GameInstanceLayoutResolver / GameVersionDetailsService / GameVersionRenameService，
// 后四者由 internal/instance 负责）：版本 JSON 解析与扁平化、启动参数装配、
// Java 运行时定位、内存决策、进程拉起与启动管线编排。
package launch

// LaunchResult Core 层启动/停止操作的执行结果。UI 与组件边界自行映射为
// 展示层概念，业务层不反向依赖展示层。
type LaunchResult struct {
	Success bool
	Message string
}

// CompletedLaunch 启动/停止成功结果。
func CompletedLaunch(message string) LaunchResult {
	return LaunchResult{Success: true, Message: message}
}

// FailedLaunch 启动/停止失败结果。
func FailedLaunch(message string) LaunchResult {
	return LaunchResult{Success: false, Message: message}
}

// MinecraftLaunchError Minecraft 启动失败时返回的错误类型（对应 C# MinecraftLaunchException）。
type MinecraftLaunchError struct {
	Message string
	Inner   error
}

func (e *MinecraftLaunchError) Error() string { return e.Message }

func (e *MinecraftLaunchError) Unwrap() error { return e.Inner }

func newLaunchError(message string) *MinecraftLaunchError {
	return &MinecraftLaunchError{Message: message}
}

func newLaunchErrorWrap(message string, inner error) *MinecraftLaunchError {
	return &MinecraftLaunchError{Message: message, Inner: inner}
}
