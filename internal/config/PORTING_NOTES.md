# PORTING_NOTES — internal/config（移植自 NyaLauncher.Core/Config）

## 尚未移植 / 待接入的依赖

1. **GameVersionIsolation 静态类未移植**（C# `GameVersionProfileStore.cs` 前 96 行）。
   它依赖 `NyaLauncher.Core.Launch` 的 `GameInstanceSnapshot` / `GameInstanceLayoutResolver` /
   `GameVersionLayout`（内部 instance/launch 包尚未移植）。待 launch 包移植后，
   在 `internal/launch` 侧实现 `Resolve` 入口即可，所需数据
   （`GameVersionProfile.IsVersionIsolationEnabled`、`LauncherConfig.DefaultVersionIsolation()`）均已就绪。

2. **MinecraftDirectoryLocator.GetDefaultDirectory 未接入**。
   Go 版以包级钩子 `config.DefaultMinecraftDirectoryLocator func() string` 注入；
   为 nil 时 `GetFolders` / `RemoveFolder` 不追加/不保护平台默认目录。
   移植 MinecraftDirectoryLocator 后需在程序初始化时赋值该钩子。

3. **旧版默认目录迁移**：C# `LauncherConfig.LegacyDefaultStorageDirectory` 只是静态属性，
   实际迁移（LOCALAPPDATA → USERPROFILE）由前端工作区协调流程完成；
   Go 版提供 `LegacyDefaultStorageDirectory()` 供该流程使用，迁移逻辑本身不在本包。

## 有意的语义偏离

- **JSON 文档内存表示**：C# 用 `JsonObject`（保留插入顺序），Go 用 `map[string]any`
  + `encoding/json`。保存时键顺序会变化（缩进格式保留），对配置读取兼容无影响。
- **配置值序列化**：`GlobalLaunchSettings` 的数组值用 `json.Marshal`（紧凑），与 C# 默认
  `JsonSerializer.Serialize` 输出一致；`GameVersionProfile` 用结构体 tag 显式固定为
  C# 默认 PascalCase 序列化名（`MinecraftDirectory` 等），保证与既有 config.json 互通。
  布尔值写入配置时保持 C# `bool.ToString()` 的 `True`/`False` 写法。
- **错误通道**：C# 部分方法以 `Console.WriteLine` 打日志、布尔返回结果；Go 版统一改为
  `logs.Write("ERROR"/"INFO", ...)`。
- **`GetJavaPaths` 等在底层存储初始化失败时**：C# 会向上抛异常，Go 版返回空值/false
  并记录 ERROR 日志（Go 静态入口无异常通道）。
- **`Path.GetFullPath` 的非法字符校验**：Windows 的 .NET 实现会拒绝含非法字符的路径，
  Go `filepath` 不校验；这类路径会走 `NormalizePathOrOriginal` 的原文回退而非抛错。
- **Windows 原子替换**：C# 用 `File.Replace`；Go `os.Rename` 在 Windows 上本身以
  `MoveFileEx(REPLACE_EXISTING)` 原子覆盖，行为等价。
- **同步化**：C# 中的异步 IO（若有调用方 fire-and-forget）一律同步实现，调用方自行放 goroutine。
