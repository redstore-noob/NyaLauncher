//go:build !windows

package auth

import "runtime"

func runtimeIsWindows() bool { return runtime.GOOS == "windows" }
