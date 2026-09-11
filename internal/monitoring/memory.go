// Package monitoring 内存使用监控。
// 移植自 NyaLauncher.Core/Monitoring/MemoryUsageService.cs。
//
// C# 通过 System.Diagnostics.Process 读取进程工作集（WorkingSet64）；
// Go 标准库无法跨平台获取任意进程的 RSS，这里使用 gopsutil/v4/process
// （github.com/shirou/gopsutil/v4/process）获取进程内存信息，语义保持一致。
package monitoring

import (
	"os"
	"strings"

	"github.com/shirou/gopsutil/v4/process"
)

// MemorySnapshot 一次内存快照：启动器自身内存、JVM 内存与 Java 进程数。
// JSON 字段名与 C# record 属性一致（PascalCase）。
type MemorySnapshot struct {
	// LauncherMemoryMb 启动器自身工作集（MB）。
	LauncherMemoryMb float64 `json:"LauncherMemoryMb"`
	// JvmMemoryMb 所有 java/javaw 进程工作集之和（MB）。
	JvmMemoryMb float64 `json:"JvmMemoryMb"`
	// JavaProcessCount java + javaw 进程总数。
	JavaProcessCount int `json:"JavaProcessCount"`
}

// Snapshot 采集当前内存快照。
// LauncherMemoryMb 为启动器工作集；JvmMemoryMb 为所有 java/javaw 进程工作集之和；
// JavaProcessCount 为 java + javaw 进程总数。任何失败均返回零值快照。
func Snapshot() MemorySnapshot {
	var snapshot MemorySnapshot

	infos, err := process.Processes()
	if err != nil {
		// 进程枚举失败时返回零值快照。
		return MemorySnapshot{}
	}

	selfPID := int32(os.Getpid())
	for _, p := range infos {
		name, err := p.Name()
		if err != nil {
			continue // 单个进程访问失败按 0 处理
		}
		lower := strings.ToLower(name)

		if p.Pid == selfPID {
			// 启动器自身：计入 LauncherMemoryMb
			if mem, err := p.MemoryInfo(); err == nil && mem != nil {
				snapshot.LauncherMemoryMb = float64(mem.RSS) / 1024.0 / 1024.0
			}
			continue
		}
		if lower != "java" && lower != "javaw" {
			continue
		}
		snapshot.JavaProcessCount++
		if mem, err := p.MemoryInfo(); err == nil && mem != nil {
			snapshot.JvmMemoryMb += float64(mem.RSS) / 1024.0 / 1024.0
		}
	}

	return snapshot
}
