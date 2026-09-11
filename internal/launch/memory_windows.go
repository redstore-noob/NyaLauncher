//go:build windows

package launch

import (
	"syscall"
	"unsafe"
)

var (
	kernel32                 = syscall.NewLazyDLL("kernel32.dll")
	procGlobalMemoryStatusEx = kernel32.NewProc("GlobalMemoryStatusEx")
)

// memoryStatusEx 对应 Win32 MEMORYSTATUSEX（与 C# MemoryStatusEx 布局一致）。
type memoryStatusEx struct {
	Length                   uint32
	MemoryLoad               uint32
	TotalPhysical            uint64
	AvailablePhysical        uint64
	TotalPageFile            uint64
	AvailablePageFile        uint64
	TotalVirtual             uint64
	AvailableVirtual         uint64
	AvailableExtendedVirtual uint64
}

// readWindowsMemory 采样 Windows 物理内存；失败返回 false 交由运行时兜底。
func readWindowsMemory() (SystemMemorySnapshot, bool) {
	var status memoryStatusEx
	status.Length = uint32(unsafe.Sizeof(status))
	ret, _, _ := procGlobalMemoryStatusEx.Call(uintptr(unsafe.Pointer(&status)))
	if ret == 0 {
		return SystemMemorySnapshot{}, false
	}
	snapshot := SystemMemorySnapshot{
		TotalMemoryMb:     bytesToMb(int64(status.TotalPhysical)),
		AvailableMemoryMb: bytesToMb(int64(status.AvailablePhysical)),
	}
	if snapshot.TotalMemoryMb <= 0 {
		return SystemMemorySnapshot{}, false
	}
	return snapshot, true
}

func bytesToMb(bytesCount int64) int {
	return clampMegabytes(bytesCount / 1024 / 1024)
}

// windowsOSDescription 通过 RtlGetVersion 拼装系统描述（形如
// "Microsoft Windows 10.0.26200"），供 os.version 规则的正则匹配；
// 获取失败返回空串。
func windowsOSDescription() string {
	rtlGetVersion := syscall.NewLazyDLL("ntdll.dll").NewProc("RtlGetVersion")
	var version osVersionInfo
	if rtlGetVersion.Find() != nil {
		return ""
	}
	ret, _, _ := rtlGetVersion.Call(uintptr(unsafe.Pointer(&version)))
	if ret != 0 {
		return ""
	}
	return "Microsoft Windows " +
		itoa(int(version.MajorVersion)) + "." +
		itoa(int(version.MinorVersion)) + "." +
		itoa(int(version.BuildNumber))
}

type osVersionInfo struct {
	DwOSVersionInfoSize uint32
	MajorVersion        uint32
	MinorVersion        uint32
	BuildNumber         uint32
	PlatformID          uint32
	CSDVersionText      [128]uint16
}

func itoa(value int) string {
	if value == 0 {
		return "0"
	}
	digits := ""
	for value > 0 {
		digits = string(rune('0'+value%10)) + digits
		value /= 10
	}
	return digits
}
