# 01 · 项目架构总览

> 本文档描述 NyaLauncher 的工程划分、依赖方向、启动流程与数据目录。
> 动手写插件或改界面之前，建议先读这一篇。

---

## 1. 技术栈

| 组件 | 选型 |
|------|------|
| UI 框架 | Avalonia UI 12.1.1 |
| 运行时 | .NET 10 |
| 组件扩展契约 | .NET 10（**不依赖 Avalonia**） |
| Material 控件 | Material.Avalonia（Material.Styles / Material.Icons） |
| 许可证 | Apache License 2.0 |

---

## 2. 四个工程

```
NyaLauncher.Avalonia                 主程序：窗口、页面、控件、主题
   ├── NyaLauncher.Core              业务逻辑：下载 / 启动 / 配置 / 账号
   ├── NyaLauncher.Avalonia.Animations   动效库：全部动画实现
   └── NyaLauncher.Plugin.Abstractions   插件契约：纯数据描述，无 UI 依赖
```

| 工程 | 目标框架 | 职责 | 能引用什么 |
|------|----------|------|-----------|
| `NyaLauncher.Avalonia` | net10.0 | UI 主程序、主题系统、功能区宿主、工作区 | 下面三个 |
| `NyaLauncher.Avalonia.Animations` | net10.0 | 所有动效（附加属性 / 行为 / helper / 计时器） | Avalonia |
| `NyaLauncher.Core` | net10.0 | 下载、启动、配置、账号、日志、音乐、AI | 无 UI 依赖 |
| `NyaLauncher.Plugin.Abstractions` | net10.0 | 组件契约、几何、元素定义、运行时状态、校验 | **无 Avalonia 依赖** |

> ⚠️ 依赖是单向的。下层**永远不引用**上层，也不引用 Avalonia。
> 这保证了组件契约可以被任何 .NET 宿主消费，也让 Core 逻辑能在无 UI 环境下测试。

---

## 3. 目录结构

### 3.1 `NyaLauncher.Core`

| 目录 | 职责 |
|------|------|
| `Config/` | 启动器配置持久化（全局设置、版本配置档案、启动设置） |
| `Content/` | 游戏内容元数据解析（Mod / 资源包 / 光影 / 存档） |
| `Download/` | Minecraft 版本下载与安装、Mod 下载、Modrinth 搜索、Java 运行时 |
| `Launch/` | 启动流程核心（实例管理、版本详情、内存策略） |
| `Launch/Auth/` | 账号存储与微软设备码认证 |
| `Launch/Internal/` | 版本档案解析、依赖解析、参数构建、规则求值 |
| `Models/` | 共享数据模型 |
| `Tools/` | 通用工具（路径处理、PNG 编码） |
| `Ai/` | AI 辅助功能 |
| `Logs/` | 日志系统 |
| `Music/` | 音乐库与播放服务 |
| `Network/` | 服务器状态查询（Minecraft Server Ping） |

### 3.2 `NyaLauncher.Avalonia`

| 目录 | 职责 |
|------|------|
| `Controls/` | 自定义控件（`DockWorkspace`、组件库视图、下载面板、通知宿主等） |
| `Converters/` | 值转换器 |
| `Dialogs/` | 弹窗对话框（皮肤选择、披风选择、配置冲突等） |
| `Framework/` | 组件系统框架（功能区注册表、多边形组件运行时、工作区档案） |
| `Pages/` | 页面（设置中心、下载、版本管理、账户管理、音乐、关于） |
| `Themes/` | 主题资源与工具（家族资源字典、画刷、样式切换） |
| `Windows/` | 独立窗口（个性化、任务详情） |

`Framework/` 是插件系统的宿主侧，核心文件：

| 文件 | 作用 |
|------|------|
| `FeatureAreaRegistry.cs` | 功能区注册表，内置功能与插件共用同一入口 |
| `FeatureAreaDefinition.cs` | 功能区定义（Id / 标题 / 图标 / 动作 / 内容工厂） |
| `FeatureAreaAction.cs` | 动作记录（Id / 标题 / 描述 / 图标 / 执行委托） |
| `IFeatureAreaProvider.cs` | 多区域插件实现的接口 |
| `BuiltInFeatureAreaProvider.cs` | 内置功能区的注册与分区 |
| `PolygonComponentInstanceHost.cs` | 组件实例宿主（订阅状态、封送 UI 线程） |
| `PolygonComponentInstancePool.cs` | 实例池（按定义 Id 复用运行时实例） |
| `WorkspaceProfile*.cs` | 工作区档案的读写与版本迁移 |

### 3.3 `NyaLauncher.Avalonia.Animations`

全部文件都在 `Helpers/` 下，按用途分三类：

| 类别 | 文件 |
|------|------|
| 基础 | `MaterialMotion`（M3 令牌）、`AnimationGate`（总开关）、`AnimationHelper`（通用动效）、`OverlayHost` |
| 交互附加属性 | `GlobalAnimation`、`Ripple`/`RippleBehavior`、`Magnetic`、`Shake`、`Flip`、`Typewriter` |
| 视觉与转场 | `TransitionEffects`、`Stagger`、`SwapTransition`、`WindowEffects`、`OverlayEffects`、`Shimmer`、`AmbientGradient`、`SparkleTrail`、`RingProgress` |

详见 [动画系统指南](04-animations.md)。

### 3.4 `NyaLauncher.Plugin.Abstractions`

`Components/` 下只有 7 个文件，就是插件需要理解的全部契约：

| 文件 | 内容 |
|------|------|
| `ComponentGeometry.cs` | `ComponentPoint` / `ComponentSize` / `ComponentRect` / `ComponentPixelRect` / `PolygonShapeDefinition` |
| `PolygonComponentDefinition.cs` | 组件定义、元素定义（文本 / 图片 / 进度 / 按钮 / 输入框 / 开关 / 滑块 / 下拉）、动作、主题 |
| `PolygonComponentBuilder.cs` | 链式 Builder |
| `PolygonComponentRuntime.cs` | 注册项、实例上下文、动作调用、状态快照 |
| `PolygonComponentInstanceBase.cs` | 实例基类（封装 revision 递增与释放检查） |
| `PolygonComponentValidation.cs` | 校验器与错误模型 |
| `PolygonComponentDefinition.CurrentContractVersion` | 契约版本号（当前为 `1`） |

---

## 4. 启动流程

主窗口构造时按顺序完成这些事（`MainWindow.axaml.cs`）：

```
1. InitializeComponent()
2. LauncherConfig.SetStorageDirectory(...)      ← 配置目录归位（config.json 与 workspace.json 同目录）
3. DownloadSettings.ApplySavedSettings()        ← 恢复下载设置
4. AnimationGate.Enabled 默认 true，动画永远开启（设置页动画开关已移除）
   AmbientGradient.Enabled  = ThemeSettings.LoadAmbientGradient()      ← 彩虹背景
   SparkleTrail.Enabled     = ThemeSettings.LoadSparkleTrail()         ← 星尘特效
5. 创建 GameLaunchService / GameDownloadService 并订阅 Changed
6. FeatureAreas.Register(new BuiltInFeatureAreaProvider(...))   ← 内置功能区入场
7. 读取 workspace.json → SetGlobalComponentScale / SynchronizeUserAreas / ApplyPersonalization
8. Workspace.UseRegistry(FeatureAreas) + ImportLayout(...)      ← 恢复停靠布局与组件摆放
9. ComponentLibraryView.AttachRegistry(FeatureAreas)            ← 右侧组件库抽屉接上注册表
10. ThemeManager.ThemeChanged += OnThemeHotReload               ← 订阅主题热重载
11. WindowEffects.Enter(...) 播放入场动效
```

**关键顺序**：视觉开关（彩虹背景 / 星尘）必须先于任何动画挂载生效；主题订阅必须在界面构建完成后建立。
插件如果要在运行时注册功能区，参考 [插件与功能区开发](05-plugin-development.md)。

---

## 5. 配置与数据目录

| 文件 | 位置 | 内容 |
|------|------|------|
| `workspace.json` | 配置目录 | 工作区个性化：功能区名称/图标/动作、用户自建区域、停靠树、组件摆放 |
| `config.json` | 配置目录 | 账户与启动配置、主题家族与模式 |
| `workspace-location.txt` | `%APPDATA%/NyaLauncher/` | 仅记录用户选择的配置目录路径，不含个性化内容 |

- **默认配置目录**基于 `Environment.SpecialFolder.LocalApplicationData`，由运行平台映射到当前用户数据目录（Windows 下为 `%LOCALAPPDATA%\NyaLauncher`）
- 用户可在「个性化」窗口中把配置目录改到别处；目标为空目录时迁移两份配置，目标已有配置时可选择采用目标配置并删除或备份原配置
- 仓库内开发时使用被 Git 忽略的 `.nya-data/`

### 主题与动画在配置中的键

| 键 | 值 | 说明 |
|---|---|---|
| `themeFamily` | 家族名，如 `HatsuneMiku` | 默认 `HatsuneMiku` |
| `themeMode` | `Dark` / `Light` | 默认 `Dark` |
| `theme`（旧） | `Family_Mode` 合并写法 | 读取时自动迁移并拆分 |
| `ambientGradient` | `true` / `false` | 彩虹背景，默认开 |
| `sparkleTrail` | `true` / `false` | 星尘特效，默认开 |

> 旧键 `animationsEnabled` / `closeAnimations`（动画总开关）已无人读取——
> 设置页动画开关已移除，动画永远开启。

读取入口统一在 `NyaLauncher.Avalonia/Pages/ThemeSettings.cs`。

---

## 6. 构建与发布

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

发布注意事项：

- 项目**不固定** `RuntimeIdentifier`，由 Avalonia 的 `UsePlatformDetect()` 支持 Windows / Linux / macOS
- 发布时应按目标平台分别指定 RID（`win-x64` / `linux-x64` / `osx-x64` 等），**不要**把所有平台的原生库打进同一个发行目录
- 构建会自动排除 Skia / HarfBuzz 的原生 PDB；这些文件只用于框架内部调试，不影响运行与源码调试

---

## 7. 扩展点一览

想在启动器里加点东西，从这里选入口：

| 你想做的事 | 入口 | 文档 |
|-----------|------|------|
| 加一个功能按钮 | `FeatureAreas.Register(new FeatureAreaDefinition{...})` | [05](05-plugin-development.md) |
| 加一个自定义界面 | `FeatureAreaDefinition.ContentFactory` | [05](05-plugin-development.md) |
| 做一个可视化组件卡片 | 实现 `IPolygonComponentProvider` | [06](06-polygon-components.md) |
| 加一套配色 | 新增 `{Family}_Accents.axaml` | [02](02-theming.md) |
| 加一种动效 | 在 `Animations/Helpers/` 新增附加属性 | [04](04-animations.md) |
| 弹提示给用户 | `NyaAlert` / `NyaPrompt` | [07](07-notifications.md) |

---

## 8. 编码约定

| 规则 | 说明 |
|------|------|
| 颜色不硬编码 | 一律 `{DynamicResource XxxBrush}`，保证主题热生效 |
| 动画不写在页面里 | 全部收敛到 `Animations/Helpers/`，通过 class 或附加属性触发 |
| ID 全局唯一 | 动作 Id、组件 Id 在全局按忽略大小写比较；第三方组件请用 `publisher.plugin/name` |
| 后台线程不碰控件 | 动作用 `CancellationToken` 及时响应；状态事件可从后台线程发，宿主负责封送 |
| 异步清理要收敛 | `DisposeAsync` 里停计时器、退订事件、释放资源；失控的实现不能永久阻止启动器关闭 |
