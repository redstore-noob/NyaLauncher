# PORTING_NOTES — internal/world（移植自 NyaLauncher.Core/World/WorldStore.cs）

## 对其它包的依赖

- `internal/instance`：`GameInstanceSnapshot`、`GameVersionIsolationGetGameDirectory`
  （对应 C# `GameVersionIsolation.GetGameDirectory`；C# 里该调用可能抛异常，Go 版该函数
  不返回 error，目录解析失败时天然回落到 snapshot.MinecraftDirectory）。

## 有意的语义偏离

1. **静态类 → 包级函数**：`WorldStore.GetRecentWorlds` → `world.GetRecentWorlds`。
2. **`DateTimeOffset` → `time.Time`**：`WorldInfo.LastPlayed` 用 `time.Time`（本地时区，
   与 C# `File.GetLastWriteTime` 语义一致）；`IconPath` 为空串表示无图标。
3. **共享目录去重的键**：C# 用 `StringComparer.OrdinalIgnoreCase` 字典；Go 版在 Windows 上
   小写化路径做键、其余平台保持原样（与文件系统大小写语义对齐）。
4. **排序**：`OrderByDescending(LastPlayed)` 用 `sort.SliceStable`（同为稳定排序）。
5. C# 外层 `try/catch` 整体吞异常返回空数组；Go 版在枚举处逐点忽略错误，效果一致。

## 尚未移植 / 待接入

- 无。本包只读磁盘与实例快照，不依赖 launch/auth。
