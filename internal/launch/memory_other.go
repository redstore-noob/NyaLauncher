//go:build !windows

package launch

// readWindowsMemory 非 Windows 平台无 GlobalMemoryStatusEx；
// GetSystemMemory 不会调用本函数。
func readWindowsMemory() (SystemMemorySnapshot, bool) {
	return SystemMemorySnapshot{}, false
}

// windowsOSDescription 仅 Windows 平台实现。
func windowsOSDescription() string { return "" }
