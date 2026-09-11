package bindings

// MonitorAPI / ServerAPI / SystemAPI：内存监控、服务器状态查询、版本与日志。

import (
	"nyalauncher/internal/info"
	"nyalauncher/internal/logs"
	"nyalauncher/internal/monitoring"
	"nyalauncher/internal/network"
)

// ---- MonitorAPI ----

// GetMemorySnapshot 内存快照：启动器 / JVM 内存与 Java 进程数。
func (a *MonitorAPI) GetMemorySnapshot() monitoring.MemorySnapshot { return monitoring.Snapshot() }

// ---- ServerAPI ----

// PingServer 查询 Minecraft 服务器状态（Server List Ping）。
func (a *ServerAPI) PingServer(host string, port int) (network.MinecraftServerStatus, error) {
	return network.Ping(host, port)
}

// ParseServerAddress 解析 "host" / "host:port" 形式的服务器地址。
func (a *ServerAPI) ParseServerAddress(input string) (string, int, error) {
	return network.ParseAddress(input)
}

// GetUnreachableStatus 构造不可达状态（Motd 为原因）。
func (a *ServerAPI) GetUnreachableStatus(reason string) network.MinecraftServerStatus {
	return network.Unreachable(reason)
}

// ---- SystemAPI ----

// GetAppVersion 纯版本字符串，如 "1.0.0-preview4"。
func (a *SystemAPI) GetAppVersion() string { return info.Version() }

// GetFormattedVersion 格式化版本号，如 "NyaLauncher版本号:1.0.0-preview4"。
func (a *SystemAPI) GetFormattedVersion() string { return info.FormatVersionString() }

// AddLog 写入一条日志；type 为 "ERROR" 时同时返回 error 语义（false）。
func (a *SystemAPI) AddLog(infoText, typ string) bool {
	return logs.AddLogs(infoText, nil, typ)
}

// WriteLog 直接写入共享日志文件。
func (a *SystemAPI) WriteLog(typ, infoText string) bool { return logs.Write(typ, infoText) }

// ClearLogs 清空日志目录，返回删除的文件数。
func (a *SystemAPI) ClearLogs() int { return logs.ClearLogs() }
