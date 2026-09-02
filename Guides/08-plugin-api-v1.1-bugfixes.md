# Plugin API V1.1 Bug 修复日志

| 追溯项 | 值 |
| --- | --- |
| 修复日期 | 2026-09-02（Asia/Shanghai） |
| 最后验证日期 / 环境 | 2026-09-02 / Windows x64（10.0.26200），.NET SDK 10.0.302 |
| 插件 API 基线 | `V1` / `apiVersion: "1.0"` |
| 本次插件 API | `V1.1` / `apiVersion: "1.1"` / SDK `1.1.0` |
| API V1 首个正式并入宿主的版本 | NyaLauncher `v1.0.0-preview1` / `91825c9` |
| 最后确认受影响的宿主发行版 | NyaLauncher `v1.0.0-preview2` / `cc882f9` |
| 审计与修复工作树基线 | `dev` / `62f9724`（`docs: refresh plugin repository references`） |
| V1.1 首个宿主发行版 | 尚未分配；插件 API 名称不随该版本变化 |
| 修复提交 | 尚未提交；本文不预填不存在的提交号 |

审计范围是插件 API V1 正式进入主线之后的全部 `dev` 改动；同时回看 API V1 发布提交中
与主题、通知和组件生命周期直接相关的实现，以判断回归起点。

本文只记录已由代码路径确认并在本轮修复的错误。新增的主题与日志 API 使用方式仍以
[`NyaLauncher.Plugin.Abstractions/README.md`](../NyaLauncher.Plugin.Abstractions/README.md)
为准。

下列“可复现版本”均已核对对应标签中的源代码路径；不代表已经在所有操作系统上执行了 GUI 复现。
自动化与尚待人工验证的范围分别列在文末。宿主发行号只用于定位问题，插件 API 按 V1 → V1.1 独立演进。

## 修复项

### 1. 冷启动“跟随系统”不会继续跟随

- 确认可复现版本：`v1.0.0-preview1`（`91825c9`）、`v1.0.0-preview2`（`cc882f9`）。
- 触发：配置已保存为 `themeMode=System`，随后冷启动启动器，再切换操作系统明暗模式。
- 根因：启动路径先把 `System` 解析成 `Light/Dark`，再直接调用 `StyleAlter`；
  `ThemeManager.ResolveSystemMode` 从未执行，因此 `ColorValuesChanged` 监听没有安装。
- 修复：启动与二次初始化统一通过 `ThemeManager.ApplyTheme`，并保留原始 `System` 偏好。
- 兼容性：`Light`、`Dark` 与主题家族资源加载语义不变。

### 2. 在线插件仓库视图重复订阅静态主题事件

- 确认可复现版本：`v1.0.0-preview2`（`cc882f9`）；该视图在此版本并入插件管理页。
- 触发：视图构造后首次挂载，或主题热重载导致反复 detach/attach。
- 根因：构造函数和 `AttachedToVisualTree` 各订阅一次 `ThemeManager.ThemeChanged`，
  detach 只退订一次；挂载时回调重复执行，移除后的视图仍被静态事件持有。
- 修复：只在可视树挂载期间订阅，并用状态位保证最多一次订阅、一次退订。
- 兼容性：仓库加载、安装期间切页和主题刷新行为不变。

### 3. 插件禁用后仍可通过旧通知服务引用弹窗

- 确认可复现版本：`v1.0.0-preview1`（`91825c9`，V1 通知服务首次发布）、
  `v1.0.0-preview2`（`cc882f9`）。
- 触发：插件缓存 `IPluginNotifications`，禁用/隔离后仍有未正确停止的后台代码调用它。
- 根因：所有插件共享一个永久有效的通知单例，服务没有运行时所有权或退役状态。
- 修复：通知服务改为每个插件运行时独立实例；停止完成、启动失败、隔离和卸载后失效，
  晚到的 Alert 被忽略，Prompt/Confirm 分别安全返回 `null`/`false`。
- 兼容性：接口签名与 `ui.native` 授权门槛不变；正常运行期行为不变。

### 4. Confirm 渲染异常可能让调用方永久等待

- 确认可复现版本：`v1.0.0-preview1`（`91825c9`）、`v1.0.0-preview2`（`cc882f9`）。
- 触发：`NyaPrompt.ShowAsync` 返回 faulted task。
- 根因：旧实现的 continuation 读取 `t.Result` 后自身抛错，但没有完成返回给调用方的
  `TaskCompletionSource`。
- 修复：改为直接 `await ShowAsync`；成功结果原样映射，异常也会正常结束返回任务并向上传播。

### 5. 组件显式高 Revision 后自动 Revision 倒退

- 确认可复现版本：`v1.0.0-preview1`（`91825c9`）、`v1.0.0-preview2`（`cc882f9`）。
- 触发：`PolygonComponentInstanceBase.SetState` 先发布显式高 Revision，之后传 `0`
  请求自动分配。
- 根因：显式 Revision 没有推进基类内部计数器，下一次自动值会从 1 开始并被宿主当作旧状态丢弃。
- 修复：显式新值会原子推进内部水位，零值自动取得下一 Revision；
  显式旧值仍被忽略，不把过期异步结果当成新数据。并发发布只允许更新到更新的快照，
  达到 `long.MaxValue` 后饱和而不回绕；
  `NextRevision()` 后再 `SetState` 的既有用法保持不变。

### 6. 同一运行时停止后重启仍拒绝宿主回调

- 确认可复现版本：`v1.0.0-preview1`（`91825c9`）、`v1.0.0-preview2`（`cc882f9`）。
- 触发：宿主在不更换 `PluginRuntimeHost` 对象的情况下执行 Stop → Start。
- 根因：`_stopping` 在新一次 Start 前没有复位。
- 修复：通过全部状态检查后、进入新 Start 前复位停止状态并重新启用运行时服务。

### 7. 宿主会把尚未实现的未来 API minor 当成兼容

- 确认可复现版本：`v1.0.0-preview1`（`91825c9`）、`v1.0.0-preview2`（`cc882f9`）。
- 触发：插件或仓库版本声明 `apiVersion: "1.2"`、`"1.999"` 等当前宿主尚未实现的 V1 minor。
- 根因：本地包只解析第一个数字并检查主版本等于 1；在线仓库也只校验字符串格式，导致宿主可能
  在加载代码后才以 `TypeLoadException` / `MissingMethodException` 失败。
- 修复：插件 API 改用独立的 V1 / V1.1 版本线；当前宿主在执行 DLL 前只接受不高于 `1.1` 的
  同主版本契约，仓库兼容版本筛选使用同一规则。未来 `1.2` 索引条目可以保持有效，但不会被当前
  宿主列为可安装版本。
- 兼容性：既有 `1.0` 清单继续接受；只有原本被错误放行的未来 API 声明改为 Incompatible。

## 明确保留的预留能力

以下能力仍只记录插件意图，没有新增系统级宿主实现：`network.http`、
`system.info.read`、`user-files.write`、`process.start`，以及任意 Avalonia Control、页面或
原生窗口注入。它们涉及多平台系统边界，本轮没有改变其授权或运行语义。

## 验证

- Debug、Release 全解决方案构建与 smoke tests 均通过：**58/58**。总数包含工作树中并行开发的
  仓库镜像回归；本轮新增测试覆盖语义色快照、事件串行分发/异常隔离/解除订阅、服务授权与退役、
  同一运行时重启、日志控制字符/失败写入、Revision 水位/过期状态/溢出与未来 API minor 拒绝。
- 构建使用独立临时输出目录，避免覆盖正在运行的启动器。完整重编译有 7 条既有警告：
  `ConfigFileManager` 的 3 条可空性、`ComponentLibraryView` 的成员隐藏、`MinecraftDownloadOverlay`
  和 `DockWorkspace` 的可空性，以及 `DownloadOptionsDialog` 的运行时 XAML 加载警告；无新增构建错误。
- `NyaLauncher.Plugin.Abstractions` 的 CLR AssemblyVersion 继续固定为 `1.0.0.0`；
  V1.1 只增加新类型/可选服务，没有向既有公共接口增加成员。与 `91825c9` 隔离构建的 SDK 做反射对照：
  **73 个既有公开类型、1197 个公开/受保护成员保留**，既有接口未增加必需成员。
- 使用旧 SDK 编译的 V1 探针，在共享 V1.1 SDK 的加载上下文中完成启动、注册、组件状态发布、停止、
  再启动；另有真实 `PluginRuntimeHost` 的 Stop → Start 自动化测试。
- `git diff --check` 通过；根目录 `readme.md` 没有新增本轮修复条目。

可重复执行（关闭启动器时可省略 `--artifacts-path`）：

```powershell
$apiValidationOutput = Join-Path ([System.IO.Path]::GetTempPath()) ('NyaLauncher-api-check-' + [Guid]::NewGuid().ToString('N'))
dotnet test NyaLauncher.slnx -c Release --artifacts-path $apiValidationOutput
dotnet test NyaLauncher.slnx -c Debug --artifacts-path $apiValidationOutput
git diff --check
```

尚待发行前人工验收：Windows/macOS/Linux 下冷启动后切换系统明暗、仓库视图反复挂载/卸载、
通知实际渲染异常路径。当前验证不能宣称所有平台 GUI 均已实测或不存在任何未知问题。
