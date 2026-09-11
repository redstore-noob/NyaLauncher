// Package info 启动器版本信息（前端版本显示的唯一来源，发版只改这里）。
package info

import "fmt"

// 版本号各段，发版只改这里的常量。
const (
	MainVersion = 1
	SubVersion  = 0
	FixVersion  = 0
	Suffix      = "preview4"
	IsUnstable  = false
	UpdateCh    = "main"
)

// Version 纯版本字符串，如 "1.0.0-preview4"；由上方字段拼接而来。
func Version() string {
	return fmt.Sprintf("%d.%d.%d-%s", MainVersion, SubVersion, FixVersion, Suffix)
}

// FormatVersionString 格式化版本号，如 "NyaLauncher版本号:1.0.0-preview4"。
func FormatVersionString() string {
	return "NyaLauncher版本号:" + Version()
}
