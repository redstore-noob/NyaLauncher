# internal/download 移植说明（C# NyaLauncher.Core.Download → Go）

## 文件对照

| Go 文件 | C# 源文件 |
|---|---|
| `pause_gate.go` | Download/DownloadPauseGate.cs |
| `stall_watchdog.go` | Download/InstallStallWatchdog.cs |
| `source_provider.go` | Download/DownloadSourceProvider.cs（含 DownloadSource / DownloadSources） |
| `settings.go` | Download/DownloadSettings.cs |
| `manifest_get.go` | Download/ManifestGet.cs |
| `version_filter.go` | Download/VersionFilter.cs |
| `minecraft_version_installer.go` | Download/MinecraftVersionInstaller.cs |
| `mod_loader_metadata.go` | Download/ModLoaderMetadata.cs（含 ModLoaderType / ModLoaderVersion） |
| `mod_loader_installer.go` | Download/ModLoaderInstaller.cs |
| `mod_download_service.go` | Download/ModDownloadService.cs |
| `content_install_service.go` | Download/ContentInstallService.cs |
| `game_download_service.go` | Download/GameDownloadService.cs |
| `game_file_verifier.go` | Download/GameFileVerifier.cs |
| `java_runtime_installer.go` | Download/JavaRuntimeInstaller.cs |
| `io_util.go` / `launch_bridge.go` | 跨文件共享的小工具 / 未移植包的本地替代 |
| `modrinth/search.go` | Download/ModrinthSearch.cs |
| `modrinth/version_api.go` | Download/ModrinthVersionApi.cs |
| `internal/models/modrinth.go` | Models/ModrinthProject.cs + ModrinthVersionApi.cs 中的模型 |

## 并发 / 暂停 / 取消语义

- C# `SemaphoreSlim` + `Task.WhenAll` → goroutine + channel 信号量 + `sync.WaitGroup`；
  任一文件失败时与 C# 一致：等全部 goroutine 结束后返回第一个错误（不中断兄弟任务）。
- `DownloadPauseGate`：`TaskCompletionSource` → 可关闭 channel + 1 秒兜底轮询，
  `Wait(ctx)` 在暂停期间阻塞读取循环，ctx 取消立即返回；语义与 C# 相同
  （连接保持、恢复后原连接继续）。
- `InstallStallWatchdog`：`System.Threading.Timer` → 独立 goroutine + `time.Ticker`，
  每 15 秒检查、`Touch()` 重置空闲计时、暂停期间不计时，超时 cancel 派生 context。
- 所有取消：`CancellationToken` → `context.Context`；用户取消与看门狗取消的区分
  依赖派生 context 的 `Err()` 判断（与 C# 的双令牌结构一一对应）。
- 长时下载使用独立的无限超时 `http.Client`（`longDownloadClient`，Timeout=0），
  超时预算由各调用方通过 `context.WithTimeout` 控制；**绝不复用**
  `tools.SharedHTTPClient` 的 15s 超时（对应 C# Timeout.InfiniteTimeSpan 语义）。
  Java 安装器单独使用 30 分钟整体超时的 `javaClient`（对应 C# 同款）。

## 进度回调（对接 Wails EventsEmit）

C# 的 `IProgress<T>` / 事件 → Go 回调字段/参数：

| Go 回调 | C# 对应 | 说明 |
|---|---|---|
| `InstallProgressFunc`（`MinecraftVersionInstaller` / `ModLoaderInstaller`） | `IProgress<MinecraftInstallProgress>` | 安装阶段/字节/文件数进度 |
| `ProgressBytes`（`ModDownloadService` / `ContentInstallService`） | `IProgress<(downloaded, total)>` | 单文件字节进度 |
| `JavaProgressFunc`（`JavaRuntimeInstaller`） | `IProgress<JavaRuntimeInstallProgress>` | JDK 下载/解压进度 |
| `GameDownloadService.OnChanged func(GameDownloadSnapshot)` | `Changed` 事件 | 任务快照状态机（单订阅，多订阅方需自行分发） |
| `DownloadPauseGate` 的 `OnPauseStateChanged` | `StateChanged` 事件 | 全局暂停开关 |
| `StatusFunc`（`GameFileVerifier`） | `IProgress<string>` | 校验修复状态文本 |

**重要**：C# `Progress<T>` 会把回调封送到 UI 线程；Go 回调在下载 goroutine 上
同步触发。Wails 侧对接时应在回调内直接调 `runtime.EventsEmit`（其内部线程安全），
或在回调里只做轻量工作。进度上报保留了原有的 120ms 节流（`reportThrottled`、
Java 下载节流），避免 EventsEmit 报表风暴。

## 对未移植包（config / launch）的最小替代（launch_bridge.go）

以下为最小本地实现/钩子，待 `internal/config`、`internal/launch` 移植后应替换并删除：

- `ConfigGetValue` / `ConfigSetValue` + `SetConfigHooks(get, set)`：
  LauncherConfig 的读写入口，默认进程内内存 map；宿主注入真实持久化。
- `ConfigGameDirectory` / `ConfigSaveGameDirectory` / `ConfigDefaultVersionIsolation`。
- `GetDefaultMinecraftDirectory` / `EnsureDefaultMinecraftDirectory`：MinecraftDirectoryLocator。
- `RefreshInstancesHook` / `SelectInstanceHook`：GameInstanceStore（默认空操作）。
- `ResolveContentDirectoryHook` / `ResolveInstanceLayoutHook`：
  GameVersionIsolation / GameInstanceLayoutResolver（未注入时退化为游戏根目录）。
- `MinecraftRuleEvaluator` 最小实现（`ruleIsAllowed` / `DefaultFeatures` /
  `RuleEvaluatorOSName`）：版本 JSON 库 rules 过滤，安装与校验共用同一套规则。
- `VersionJSON` 等解析模型 + `flattenVersionJSON` / `IsVersionReferenced`：
  VersionJsonFlattener 的最小实现（合并 libraries、继承原版参数与 JAR 复制；
  比 C# 完整实现的字段合并范围窄，见下方"偏离"）。
- `FindJavaExecutable`：JavaRuntimeLocator（runtime 目录扫描 → JAVA_HOME → PATH）。

## 偏离 C# 的点

1. **版本 JSON 用强类型解析**：C# 大量用 `JsonDocument`/`JsonElement` 动态取字段；
   Go 改为 `VersionJSON`/`libraryJSON`/`ruleJSON` 等结构体（json tag 与 Mojang 契约一致）。
   语义等价，未声明的字段被忽略。
2. **事件 → 单回调**：C# 多播事件（`Changed` / `StateChanged`）→ 单个回调字段
   （`OnChanged` / `OnPauseStateChanged`）；如需多订阅请在宿主侧分发。
3. **提取式安装回退已移除**：与 C# 现行为一致——NeoForge/Forge 安装器运行失败时
   直接报错（提取式安装无法生成 SRG 客户端，C# 已不再回退）。
4. **进程树终止**：C# 用 `Process.Kill(entireProcessTree: true)`；Go 的
   `exec.Cmd.Cancel` 只杀主进程，通过 `WaitDelay` + 500ms 轮询取消兜底
   （安装器无深层子进程树时行为等价）。
5. **扁平化最小实现**：`flattenVersionJSON` 合并 libraries/继承字段的范围小于
   C# VersionJsonFlattener 完整实现（例如 arguments 的深度合并从简）；
   接入 internal/launch 后应整体替换。
6. **MeasureLatency 失败返回 -1**（C# 返回 `int?` null），避免 Go 指针噪音。
7. **错误处理**：C# 异常 → Go error；`GameDownloadService.runDownloadTask` 的
   终态发布与 C# 的 catch 分支一一对应（Cancelled / Failed）。
8. `ContentInstallService.mustSafeCombine` / installer 的 `resolveRelativePath`
   路径越界以 panic 表达（对应 C# `InvalidDataException` 的防御性断言场景），
   正常清单数据不会触发；后续可改为返回 error。
9. DownloadSettings 的存储键名（`downloadParallelDownloads` 等）与 C# 完全一致，
   但持久化介质由 config 钩子决定。

## JSON 契约

- Modrinth API（search / project version）的字段名与 C# `JsonPropertyName`
  完全一致（见 `internal/models/modrinth.go`）。
- 下载源替换、HTTPS 强制校验、Quilt 域名直连等 URL 规则逐行对照 C# 实现。
