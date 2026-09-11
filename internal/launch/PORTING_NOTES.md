# PORTING_NOTES — internal/launch（移植自 NyaLauncher.Core/Launch）

移植范围：`Launch/` 内除 GameInstanceStore.cs、GameInstanceLayoutResolver.cs、
GameVersionDetailsService.cs、GameVersionRenameService.cs 之外的全部文件
（这四个归 internal/instance，见下文"依赖说明"）。

## 文件对应关系

| Go 文件 | C# 源 |
|---|---|
| launch_result.go | LaunchResult.cs + MinecraftLaunchException |
| launch_info.go | LaunchInfo.cs（Options/账号抽象/Plan/Result/启动器接口）|
| launch_transform.go | MinecraftLaunchTransform.cs |
| minecraft_directory_locator.go | LaunchTool.cs 内的 MinecraftDirectoryLocator |
| java_runtime_locator.go | JavaRuntimeLocator.cs |
| game_memory_settings.go + memory_windows.go / memory_other.go | GameMemorySettings.cs |
| version_profile_loader.go | Internal/MinecraftVersionProfile.cs + MinecraftVersionProfileLoader.cs |
| rule_evaluator.go | Internal/MinecraftRuleEvaluator.cs |
| argument_builder.go | Internal/MinecraftArgumentBuilder.cs |
| library_resolver.go | Internal/MinecraftLibraryResolver.cs |
| version_json_flattener.go | VersionJsonFlattener.cs |
| launch_tool.go | LaunchTool.cs（OfflineMinecraftLauncher / MicrosoftMinecraftLauncher + Java 自动下载）|
| game_launch_service.go | GameLaunchService.cs |
| game_launch_service_options.go | GameLaunchService.Options.cs |
| game_launch_service_process.go | GameLaunchService.Process.cs |
| instance_hooks.go | （新增）对 internal/instance 的最小依赖接口 |

## 对 download/launch_bridge.go 的依赖说明

**download/launch_bridge.go 可退役**。本包不再使用 launch_bridge 的任何导出符号
（ConfigGetValue / GetDefaultMinecraftDirectory / DefaultFeatures / 规则评估等均在本包
内有完整实现）。唯一保留的 download 依赖是正式功能：
`download.GameFileVerifier.VerifyAndRepair`（启动前文件校验）与
`download.QueryAvailableJavaVersions` / `JavaRuntimeInstaller.InstallCandidate` /
`SupportedJavaVersions`（Java 自动下载）。宿主接入本包后可删除 launch_bridge.go。

## 对 internal/instance 的依赖说明（最小接口）

`internal/launch` 通过 `instance_hooks.go` 中的钩子变量声明依赖，**不 import**
internal/instance（避免编译期耦合，instance 尚在移植）：

- `InstanceSnapshotProvider func() GameInstanceSnapshot` ← GameInstanceStore.Current
- `ExternalInstanceResolver func(sourcePath string) (ExternalInstanceInfo, bool)`
  ← GameInstanceLayoutResolver.TryResolveExternalInstance
- `IsolatedGameDirectoryResolver func(minecraftDirectory, sourcePath, versionId) string`
  ← GameVersionIsolation.GetGameDirectory

internal/instance 移植完成后由宿主在初始化时接线；钩子为 nil 时的降级行为：
实例快照视为"未就绪"（启动校验失败）、非外部实例、不隔离游戏目录。

## 有意的语义偏离

- **CancellationToken → context.Context**：所有异步入口第一参数为 ctx。
- **多播事件 → 单回调字段**：`GameLaunchService.OnChanged`（对应 C# Changed 事件）。
  多订阅方需自行分发（如 Wails 事件总线）。
- **C# Process → os/exec.Cmd**：进程退出观察改为"启动器内部 goroutine 执行唯一
  Wait + 结果经 `MinecraftLaunchResult.Exit()` 通道发布"；stdout/stderr 行回调通过
  `MinecraftLaunchOptions.GameOutputCallback` 注入（C# 由 GameLaunchService 直接订阅
  Process 事件，Go 版进程由 launch 包创建，属必要新增；stdout/stderr 合流语义与 C#
  一致）。Manual stop 仍为 Process.Kill；Windows taskkill /T /F 进程树兜底保留为
  `forceKillWithTaskkill`，但主路径不再自动等待/重试（Go 的管道关闭语义下 Wait
  不会被子进程继承句柄永久挂起）。
- **版本 JSON 表示**：C# JsonElement/JsonObject → `json.RawMessage` + `map[string]RawMessage`；
  VersionJsonFlattener 用 `orderedObject` 保持 C# JsonObject 的键插入顺序，输出为紧凑
  JSON（C# 为带缩进；消费方是启动器自身与 Mojang 解析器，空白差异无影响）。
- **内存采样兜底**：C# 非 Windows/Linux 用 `GC.GetGCMemoryInfo` 估算；Go 改用
  gopsutil（go.mod 既有依赖）`mem.VirtualMemory`，更精确。Windows 仍用
  GlobalMemoryStatusEx（syscall 懒加载），Linux 仍读 /proc/meminfo。
- **os.version 规则匹配**：C# 用 RuntimeInformation.OSDescription；Go 无直接等价物，
  Windows 侧用 RtlGetVersion 拼装 "Microsoft Windows 10.0.build"，其余平台用
  runtime.Version()。Mojang 规则集几乎只对 Windows 版本号做 \d+ 匹配，行为等价。
- **Java 版本探测**：java -version 可能以非 0 退出码打印版本信息，Go 版在退出码非 0
  但输出含版本行时仍解析成功（C# 同样读取输出流，语义一致）。
- **离线账号构造**：C# 抛 ArgumentException；Go `NewOfflineAccount` 返回 error。
- **AccountKind**：C# 以类型模式匹配区分账号，Go 在 `auth.MinecraftAccount` 接口上
  增加 `AccountKind() string`。
- **WithRefreshLock**：C# 泛型 `Task<T>`；Go 为 `func() error` 回调，返回值经闭包
  捕获（见 prepareMicrosoftAccount / prepareAuthlibAccount）。
- **离线 UUID / OfflinePlayer MD5**、authlib-injector 注入参数、G1 JVM 优化参数、
  -DlibraryDirectory 补发、classpath 探测窗口、脱敏正则等均按 C# 逐条保留。
