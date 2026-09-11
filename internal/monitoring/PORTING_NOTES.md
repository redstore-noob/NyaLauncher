# internal/monitoring 移植说明

移植自 `NyaLauncher.Core/Monitoring/MemoryUsageService.cs`。

## 实现方式

- 依赖 `github.com/shirou/gopsutil/v4/process`（gopsutil，CGO 无关、跨平台）。
- `Snapshot()` 语义与 C# 一致：启动器自身工作集（MB）→ `LauncherMemoryMb`；
  所有 `java`/`javaw` 进程 RSS 之和 → `JvmMemoryMb`；进程总数 → `JavaProcessCount`。
  任何失败返回零值快照，单个进程访问失败按 0 处理。
- 进程名匹配为小写比较（Linux 上是 `java`，Windows 上 C# 用 `javaw`；
  gopsutil 在 Windows 返回不含扩展名的映像名，`javaw` 同样可命中）。

## 偏离点

1. C# 的 `WorkingSet64` → gopsutil `MemoryInfo().RSS`。Linux 上 RSS 与
   Windows 工作集概念接近但不完全等同（统计口径差异，量级一致）。
2. C# 通过 `Process.GetProcessesByName` 按名字枚举两次（java、javaw）；
   Go 版一次 `process.Processes()` 枚举后在循环内过滤，结果语义相同、效率更高。
3. 任务说明建议使用 gopsutil 的 `mem` 包，但 C# 语义是"进程工作集"而非
   系统物理内存总量，故改用 `process` 包（更贴近原实现）。
