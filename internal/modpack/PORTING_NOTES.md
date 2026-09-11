# PORTING_NOTES — internal/modpack（移植自 NyaLauncher.Core/Modpack）

来源：`ModpackExportService.cs`、`ModpackExportProfileStore.cs`。

## 对其它包的依赖

- `internal/tools`：`SharedHTTPClient`（Modrinth version_file 查询）。
- 不依赖 launch/auth。

## 用户数据文件兼容（字段名与 C# 完全一致）

- `nya-pack.json`：packName / packVersion / author / description / updateLink / format /
  resolveModrinthLinks / excludedPaths（camelCase + omitempty 对应 C# WhenWritingNull）。
- `modrinth.index.json`：formatVersion / game / versionId / name / summary / files /
  dependencies / nyaLauncher（files 内 path / hashes{sha1,sha512} / env{client,server} /
  downloads / fileSize）。
- `mmc-pack.json`：formatVersion / components（cachedName / cachedVersion / important /
  uid / version）。
- `instance.cfg`：properties 文本，转义规则与 C# 相同。

## 有意的语义偏离

1. **静态类 + IProgress → 包级函数 + 回调**：`ExportAsync(..., IProgress<ModpackExportProgress>)`
   → `Export(ctx, options, contentDir, outPath, progress func(ModpackExportProgress))`；
   `Func<string, CancellationToken, Task<...>>` 解析器 → 包级变量
   `ModrinthResolver func(ctx, sha1) (*ModrinthFileMatch, error)`（测试可整体替换）。
2. **枚举 → int 常量**：`ModpackExportFormat.Modrinth/MultiMc` → `FormatModrinth/FormatMultiMc`。
3. **record 属性默认值 → 构造函数**：C# `ModpackExportOptions` 属性带默认值
   （PackVersion="1.0.0"、ResolveModrinthLinks=true）；Go 结构体零值为 ""/false，
   提供 `NewExportOptions()` 返回 C# 默认值；`Export` 内部对空 PackVersion 也兜底 "1.0.0"。
4. **可空 → 指针/空串**：`ModpackExportProfile` 全指针字段；`ModrinthFileMatch.Sha512`
   空串表示未提供；index 内 sha512 序列化为省略（与 C# WhenWritingNull 一致）。
5. **异常 → error**：`ArgumentException` 等参数校验改为显式 error；
   单文件读取失败跳过并记入 Warnings 的行为保持不变。
6. **哈希**：C# 按 `algorithmFactory` 参数化；Go 版只实现所需 `computeFileSHA1`
   （sha512 仅来自 Modrinth 响应，无需本地计算）。
7. **比较语义**：C# 大小写不敏感集合/排序（Windows 场景）在 Go 版保留
   （包含集在 Windows 上小写化、排序用 Lower 比较）；跨平台行为与文件系统语义一致。
8. **JSON 输出**：`UnsafeRelaxedJsonEscaping + WriteIndented` → `json.Encoder` 的
   `SetEscapeHTML(false) + SetIndent("", "  ")`。
9. `ModpackContentItem.CategoryDisplay` 由属性改为方法 `CategoryDisplay()`。

## 尚未移植 / 待接入

- 导入（install）方向不在本次范围；`nya-pack.json` 的 ExcludedPaths 读写已就绪，
  供前端导出面板使用。
