# PORTING_NOTES — internal/content（移植自 NyaLauncher.Core/Content）

来源：`GameContentMetadataService.cs`、`GameSaveService.cs`、`CustomInstanceIconStore.cs`。

## 对其它包的依赖

- `internal/config`：StorageDirectory（content-icons / instance-icons 缓存）、
  GameVersionProfileStore.GetInstanceIconOverride。
- `internal/instance`（反向依赖，见下）：**content 不能 import instance**（instance 的
  GameVersionDetailsService 已 import content，会成环），因此：
  - `ResolveInstanceVisual` 的参数从 `GameInstanceSnapshot` 收敛为
    `InstanceContext{SourcePath, MinecraftDirectory}`；
  - 外部实例识别通过包级钩子 `content.ExternalInstanceResolver` 注入，
    由 internal/instance 的 `wire.go` 在 init 时接线；未接线（只 import content）时
    外部实例分支自动跳过。
- `DefaultInstanceIconCatalog`（内置图标符号表）与 `LevelDatReader` 随 metadata.go 一并移植。

## 有意的语义偏离

1. **静态类 → 包级函数**；`GameContentMetadataService` 的并发字典缓存改为
   `sync.Mutex + map`。
2. **CancellationToken → context.Context**：ReadMods/ReadResourcePacks/ReadShaders/ReadSaves
   首参均为 `ctx`，在每个条目解析前检查取消。
3. **失败语义**：C# 读取失败返回降级条目（unknown entry），Go 保持一致；
   `CustomInstanceIconStore.Set` 由"失败返回 null"改为返回 `error`。
4. **GameSaveService 错误通道**：C# `ExportAsync/BackupAsync` 失败返回 null、`Delete` 返回
   bool；Go 版统一改为 `(string, error)` / `error`（调用方按 error 判断失败，
   取消以 `ctx.Err()` 表达）。
5. **zip 处理**：`ZipArchive` → `archive/zip`；条目大小用 `UncompressedSize64` 自报值做
   限额预判（与 C# entry.Length 相同的"自报大小"信任模型，实际读取仍走 copyWithLimit）。
6. **mods.toml 正则**：C# 动态拼接 `Regex.Escape(key)`；Go 版用 `regexp.QuoteMeta` +
   RE2 语法重写，行为一致（三引号/单双引号字面量、`(?im)` 标志）。
7. **图标引用解析**：C# `Uri.TryCreate` 的 https / file 判定改为 `net/url` 解析；
   file:// 在 Windows 下去掉前导斜杠。
8. **时间格式化**：`yyyy-MM-dd HH:mm` → Go `2006-01-02 15:04`；
   备份时间戳 `yyyyMMdd-HHmmssfff` → `20060102-150405000`（毫秒，同秒内不互相覆盖）。
9. `LoaderGlyph`（C# 忽略入参恒返回 "material:Apps"）保持原样实现。

## 尚未移植 / 待接入

- 无已知缺口；UI 层的 GameIcons PNG 资源解码仍在前端（Core 只输出 `gameicon:{key}` 符号）。
