# PORTING_NOTES — internal/instance（移植自 NyaLauncher.Core/Launch）

来源：`GameInstanceStore.cs`、`GameInstanceLayoutResolver.cs`、`GameVersionDetailsService.cs`、
`GameVersionRenameService.cs`，以及三块被它们依赖、但原属其它 C# 文件的代码：
`GameVersionIsolation`（原在 Config/GameVersionProfileStore.cs）、
`MinecraftDirectoryLocator` / `MinecraftInstallationLocation`（原在 Launch/LaunchTool.cs）。

## 对其它包的依赖

- `internal/config`：LauncherConfig（GameDirectory/SetValue/GetValue/ClearValue/DefaultVersionIsolation/
  StorageDirectory 由 content 侧使用）、GameVersionProfileStore（Get/PruneMissingVersions/
  MigrateRenamedVersion）。**`config.DefaultMinecraftDirectoryLocator` 钩子已由本包 `init()` 接线到
  `instance.EnsureDefaultDirectory`**（config PORTING_NOTES 中的待办第 2 条已闭环）。
- `internal/content`：GameVersionDetailsService 消费内容扫描与实例图标解析。
- `internal/tools`：PathsEqual。

## 被其它包消费的接口

- `internal/world` 直接使用 `GameInstanceSnapshot` 与 `GameVersionIsolationGetGameDirectory`。
- `internal/content` 通过 `content.ExternalInstanceResolver` 钩子（由本包 `wire.go` 的 `init()`
  注入 `TryResolveExternalInstance` 适配器）使用外部实例识别，避免循环依赖。
- `internal/launch`（并行移植中）完成后，若它也需要 GameVersionIsolation /
  MinecraftDirectoryLocator，应直接复用本包导出的实现，不要重写第二份。

## 有意的语义偏离（与 C# 不同之处）

1. **静态类 → 包级函数 + 包级状态**：C# 的 `GameInstanceStore` 等静态类改为包级函数
   （`Refresh`/`Select`/`CurrentSnapshot` 等）；`Current` 属性更名为 `CurrentSnapshot`
   （`Current` 在 Go 里与变量命名习惯冲突）。
2. **Changed 事件 → SubscribeChanged**：`event Action<GameInstanceSnapshot>` 改为
   `SubscribeChanged(handler) (unsubscribe func())`，返回取消订阅函数；通知时 recover
   单个订阅者异常并写 logs（对应 C# GetInvocationList + Debug.WriteLine）。
3. **可空字符串 → 空串**：`GameInstanceSnapshot.GameDirectory / SelectedVersionId /
   ErrorMessage` 及布局结果的 nullable 字段用空串表示"无"，而非 `*string`。
4. **CancellationToken → context.Context**：`RefreshAsync` → `Refresh(ctx, path)`。
   磁盘枚举保留"独立 goroutine + select ctx.Done"以对齐 C# Task.Run + 取消语义。
5. **异常 → error**：`MinecraftLaunchException` 等异常改为 `error`（消息文本保持中文一致）；
   `Select`/`CanResolveSource` 保持 bool 返回。
6. **JSON 处理**：C# 用 `JsonNode/JsonObject`（保留键序），Go 用 `map[string]any` +
   `encoding/json`。GameVersionRenameService 重写版本 JSON 时**键顺序不保证与原文件一致**
   （缩进两空格保留，Mojang 启动器按 JSON 语义读取，无兼容影响）。
   `Path.GetInvalidFileNameChars` 用 Windows 常用非法字符集（`<>:"|?*` + 控制字符）近似。
7. **仅大小写改名 / 路径大小写语义**：沿用"Windows 不区分大小写、其余平台区分"规则，
   判断基于 `runtime.GOOS`（对应 C# OperatingSystem.IsWindows() 分支）。
8. `GameVersionIsolation` 在 C# 中位于 Config 命名空间，Go 版收敛到本包（isolation.go），
   函数名带 `GameVersionIsolation` 前缀以保持检索性。
9. C# `Directory.Move` 语义由 `os.Rename` 承担（Windows 上同样支持覆盖/同卷改名）；
   临时文件清理（`.nya-rename`）用 defer + 忽略错误，与 C# finally 块一致。

## 尚未移植 / 待接入

- `GameInstanceSnapshot` 目前不被持久化，未加 JSON tag；若未来要落盘需先定义字段名。
- launch 包的 `GameLaunchService` 等不在本次范围。
