package launch

import "runtime"

// goos 平台判定用（独立变量便于测试注入）。
var goos = runtime.GOOS
