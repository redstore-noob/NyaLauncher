# 05 · 插件与功能区开发

> 本文档描述如何把插件的功能挂进 NyaLauncher 主界面：功能区的注册方式、
> 工作区的停靠与侧栏行为、用户个性化与持久化。
> 想做可视化组件卡片，请继续看 [06 · 多边形组件开发](06-polygon-components.md)。

---

## 1. 插件能做什么

主界面的工作区由 `FeatureAreaRegistry` 驱动。**内置功能与插件使用完全相同的注册方式**，
因此新增功能区不需要修改 `MainWindow.axaml` 或 `DockWorkspace`。

插件可以：

| 能力 | 方式 |
|------|------|
| 加一组按钮 | `FeatureAreaDefinition.Actions` |
| 加一整块自定义界面 | `FeatureAreaDefinition.ContentFactory` |
| 加可视化组件卡片 | `IPolygonComponentProvider`（见 [06](06-polygon-components.md)） |
| 一次注册多个区域 | 实现 `IFeatureAreaProvider` |

运行时注册或移除区域后，工作区会自动刷新，无需重启。

### 插件 API 版本

宿主对外展示的插件 API 版本号为 `PluginSdk.ApiVersion`（当前 **v1-p2**，
显示在插件管理页页头与插件详情中）。机器可读的兼容性判定以 plugin.json 的
`apiVersion` 主版本号为准（当前主版本 1）：**同一主版本内，宿主对插件 API
向前兼容**——旧插件在新宿主上原样可用，无需为 API 增量重新编译。

版本历史：

| 版本 | 内容 |
|------|------|
| **v1-p1** | 初始 API 集：组件、实例扩展、启动参数注入、设置、存储 |
| **v1-p2** | v1-p1 + `IPluginNotifications` 通知服务（NyaAlert / NyaPrompt），纯增量 |

### 通知服务（NyaAlert / NyaPrompt）

插件经 `Context.GetService<IPluginNotifications>()` 获取启动器托管的通知
UI（需在清单中声明 `ui.native` 能力；未授权时返回 `null`）：

```csharp
var notify = Context.GetService<IPluginNotifications>();
if (notify is null)
    return;

notify.Alert(PluginNoticeSeverity.Success, "任务完成");             // 警示条，约 4s 自动收回
await notify.ConfirmAsync("删除实例", "该操作不可撤销");              // 确认对话框 → bool
var id = await notify.PromptAsync("选择", "选一个",
    PluginNoticeSeverity.Info,
    new PluginPromptButton("甲"), new PluginPromptButton("乙", IsDefault: true));
```

全部方法可在任意线程调用（内部自动封送 UI 线程）。

---

## 2. 注册按钮型功能区

```csharp
window.FeatureAreas.Register(new FeatureAreaDefinition
{
    Id = "my-plugin",
    Title = "我的插件",
    Subtitle = "插件提供的功能",
    Glyph = "✦",
    Actions =
    [
        new FeatureAreaAction(
            "hello",                    // Id
            "执行操作",                  // Title
            "点击运行插件命令",           // Description
            "▶",                        // Glyph
            () => RunPluginCommand())   // Execute
    ]
});
```

按钮会由宿主的内置动作视图渲染（矩形按钮，默认 220×82 DIP）。

---

## 3. 注册完全自定义的功能区

把 `ContentFactory` 设为返回任意 Avalonia `Control` 的工厂即可：

```csharp
window.FeatureAreas.Register(new FeatureAreaDefinition
{
    Id = "custom-view",
    Title = "自定义界面",
    Subtitle = "由插件完整控制内容",
    ContentFactory = () => new MyPluginView()
});
```

> `ContentFactory` 与 `Actions` 二选一：提供了 `ContentFactory` 时，
> 宿主渲染你的控件；否则渲染 `Actions` 里的按钮。

---

## 4. 一次注册多个区域：IFeatureAreaProvider

```csharp
public sealed class MyPluginProvider : IFeatureAreaProvider
{
    public IReadOnlyList<FeatureAreaDefinition> GetFeatureAreas() =>
    [
        new FeatureAreaDefinition { Id = "my-plugin/main",   Title = "主控台", /* ... */ },
        new FeatureAreaDefinition { Id = "my-plugin/stats",  Title = "统计",   /* ... */ }
    ];
}

window.FeatureAreas.Register(new MyPluginProvider());
```

`Register(IFeatureAreaProvider)` 内部就是对每个区域调用 `Register(FeatureAreaDefinition)`。

---

## 5. API 参考

### 5.1 `FeatureAreaDefinition`

```csharp
public sealed class FeatureAreaDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public string Glyph { get; init; } = "material:Apps";
    public string? IconPath { get; init; }
    public Func<Control>? ContentFactory { get; init; }
    public IReadOnlyList<FeatureAreaAction> Actions { get; init; } = [];
    public IReadOnlyList<PolygonComponentRegistration> PolygonComponents { get; init; } = [];
}
```

| 成员 | 说明 |
|------|------|
| `Id` | 区域唯一标识，忽略大小写比较 |
| `Title` / `Subtitle` | 显示名称与简介，用户可在个性化窗口里改 |
| `Glyph` | 图标字符。支持 `material:Apps` 这样的 Material 图标前缀，也支持 Emoji |
| `IconPath` | 可选的本地图片路径，优先于 `Glyph` |
| `ContentFactory` | 返回自定义 `Control` 的工厂 |
| `Actions` | 按钮型动作列表 |
| `PolygonComponents` | 声明式组件，会被自动转成动作进入全局目录 |

### 5.2 `FeatureAreaAction`

```csharp
public sealed record FeatureAreaAction(
    string Id,
    string Title,
    string Description,
    string Glyph,
    Action? Execute = null,
    bool IsPrimary = false)
{
    public double BaseWidth { get; init; } = 220;
    public double BaseHeight { get; init; } = 82;
    public PolygonComponentRegistration? PolygonComponent { get; init; }
}
```

| 成员 | 说明 |
|------|------|
| `Id` | **全局唯一**，个性化配置靠它引用按钮 |
| `Execute` | 点击回调；组件型动作为 `null` |
| `IsPrimary` | 视觉强调样式 |
| `BaseWidth` / `BaseHeight` | 首选尺寸（DIP） |
| `PolygonComponent` | 非空时宿主用多边形渲染器，否则用传统矩形按钮 |

### 5.3 `FeatureAreaRegistry`

| 成员 | 说明 |
|------|------|
| `Register(FeatureAreaDefinition)` | 注册一个区域；Id 重复抛 `InvalidOperationException` |
| `Register(IFeatureAreaProvider)` | 批量注册 |
| `RegisterPolygonComponents(areaId, title, subtitle, glyph, provider)` | 插件组件的一站式入口 |
| `Unregister(string id)` | 移除区域并刷新 |
| `ApplyPersonalization(IEnumerable<FeatureAreaPreference>)` | 应用用户个性化 |
| `SynchronizeUserAreas(IEnumerable<UserFeatureAreaProfile>)` | 同步用户自建区域 |
| `SetGlobalComponentScale(double)` | 全局组件缩放，钳制在 `0.65`–`1.6` |
| `PlaceComponent(componentId, targetAreaId, sourceAreaId = null)` | 把组件放进目标区域或跨区移动 |
| `RemoveComponent(componentId, sourceAreaId)` | 从区域移除组件 |
| `CreateCurrentProfile()` / `CreateDefaultProfile()` | 生成工作区档案 |
| `Areas` | 应用个性化后的区域列表（界面实际显示） |
| `SourceAreas` | 注册的原始区域列表 |
| `AvailableActions` | 全局动作目录（按 Id 去重） |
| `Changed` | 区域变化时触发 |

---

## 6. 工作区布局

### 6.1 停靠

功能区会自动获得停靠把手。把把手拖到另一功能区的上、下、左、右侧，
即可生成二维停靠布局。相邻区域的边框接缝本身支持拖动缩放，没有额外的缩放按钮。

**插件不需要自行实现布局、吸附或调整大小逻辑**——宿主全包了。

### 6.2 自动侧栏

释放布局接缝后，工作区只在以下条件**同时成立**时自动折叠区域：

1. 区域宽度或高度低于对应阈值；
2. 区域有一整条自身边框贴住工作区外边缘。

侧栏行为：

- 每个外边缘最多保存一个侧栏
- 折叠时区域从停靠树移除，其他区域占满空间
- 侧栏使用工作区外层网格的独立行列轨道，**不使用覆盖层**
- 悬停边缘栏时对应轨道按原展开尺寸加宽/加高，主工作区由布局系统重新测量并真实让位
- 配置保存侧栏区域 ID、边缘与展开尺寸
- 展开侧栏后可拖动标题栏手柄吸附到任意窗口边缘；若目标边已有侧栏，两个侧栏交换位置
- 侧栏自身没有可调整的固定尺寸；按住展开界面靠工作区一侧的接缝时，该区域立即退出侧栏状态，
  但同一次指针手势会继续控制恢复后功能区的接缝
- 松手时低于折叠阈值会重新成为侧栏，高于阈值则保留为普通功能区
- 自动检测绑定在 `GridSplitter.DragCompleted`，避免鼠标释放事件被控件内部处理

相邻边缘同时存在侧栏时：上、下侧栏拥有角落区域，左、右侧栏填充二者之间的剩余高度；
各侧栏处于不同网格单元，不依赖 `ZIndex` 相互覆盖。

---

## 7. 用户个性化

主窗口顶边栏的「个性化」入口允许用户：

- 重命名每个功能区、自定义简介与图标
- 从所有已注册功能构成的**全局目录**中选择该区域显示哪些按钮
- 图标可用内置简约预设，也可通过文件选择器引用本地图片（图片失效时自动回退到预设）
- 新建区域

同一按钮可以出现在多个区域。

### 区域编号

区域使用不表达业务含义的稳定编号，例如 `area-001`：

| 编号 | 归属 |
|------|------|
| `area-001` ~ `area-003` | 内置区域占用 |
| `area-004` 起 | 用户在个性化窗口新建区域时递增 |

用户创建的区域定义也会写入配置文件，下次启动时**先恢复区域，再恢复名称、按钮和布局**。

---

## 8. 持久化

### 8.1 工作区档案

保存在配置目录的 `workspace.json`，内容包括：

- 功能区显示名称、简介与按钮 ID
- 图标预设字符与可选的本地图片路径
- 用户创建的功能区及其中性编号
- 水平/垂直嵌套的停靠树
- 每个停靠分组的尺寸权重
- 全局组件缩放，以及组件所属功能区、相对坐标与永久层级

### 8.2 组件不会被序列化

多边形定义、工厂、运行时实例和瞬时状态**不会**写入 `workspace.json`。
工作区只保存稳定的组件 ID 以及已有的 `AreaId`、`RelativeX`、`RelativeY`、`ZIndex` 放置字段；
功能区偏好通过同一个组件 ID 引用目录项。

**这意味着**：插件应在恢复用户个性化与布局**之前**重新注册相同 ID，
随后由 factory 恢复自己的业务状态。

这一规则让 gp2 的版本 2 工作区配置继续兼容：传统矩形组件不需要迁移，
多边形组件也不会把插件私有对象或正在变化的进度序列化进启动器配置。

### 8.3 配置目录

| 文件 | 内容 |
|------|------|
| `workspace.json` | 工作区个性化与布局 |
| `config.json` | 账户与启动配置、主题与动画开关 |

- 个性化窗口允许用户选择统一的配置目录
- 目标为空目录时会迁移两份配置；目标已有配置时，用户可采用目标配置，并选择删除或先备份原目录配置
- 切换完成后，工作区与启动设置会从最终目录重新加载
- 默认目录基于 `Environment.SpecialFolder.LocalApplicationData`（Windows 下为 `%LOCALAPPDATA%\NyaLauncher`）
- 应用仅在 `%APPDATA%/NyaLauncher` 保存一个不含个性化内容的 `workspace-location.txt`，用于下次启动时定位用户选择的目录
- 仓库内开发配置使用被 Git 忽略的 `.nya-data/`

---

## 9. ID 命名规范

**这是最重要的一条约定。**

- `FeatureAreaAction.Id` 与 `PolygonComponentDefinition.Id` 必须在**全局范围内唯一**，
  才能被个性化配置稳定引用
- 比较时**忽略大小写**
- 第三方组件的 ID 必须使用 `publisher.plugin/name` 形式：

```
nyalauncher.builtin/game-launch      ← 内置
nyalauncher.builtin/account-selector ← 内置
example.download/status              ← 第三方
acme.music/now-playing               ← 第三方
```

- **保持 ID 稳定**：用户布局在升级后靠 ID 引用组件，改 ID 等于让用户配置失效

---

## 10. 内置参考实现

`NyaLauncher.Avalonia/Framework/BuiltInFeatureAreaProvider.cs` 是最好的样例：

| 内置区域 | 内容 |
|----------|------|
| `area-001` 启动中心 | 启动、实例、世界、内存等 |
| `area-002` 资源与实例 | 下载、版本、快捷入口 |
| `area-003` 启动器工具 | 音乐、个性化等 |

内置组件一览（同时也是 [06 篇](06-polygon-components.md) 的实战样例）：

| 组件 ID | 演示的模式 |
|---------|-----------|
| `nyalauncher.builtin/account-selector` | 小型矩形、动态下拉菜单、参数化动作选择真实账号 |
| `nyalauncher.builtin/game-instance-selector` | 共享状态 + 动态下拉菜单切换已安装版本，与启动页同步持久化 |
| `nyalauncher.builtin/version-manager` | 长按拖动、短按进入独立版本管理页面 |
| `nyalauncher.builtin/game-launch` | 整个多边形表面响应动作、共享启动服务同步状态 |
| `nyalauncher.builtin/skin-cape-editor` | 两层图片裁剪（脸部与帽子层）、按账号类型生成不同菜单 |
| `nyalauncher.builtin/download-task-progress` | 六边形、按钮、严格递增的 revision 演示异步进度 |

---

## 11. 体积与跨平台发布

- 项目不固定 `RuntimeIdentifier`，继续由 Avalonia 的 `UsePlatformDetect()` 支持 Windows、Linux 和 macOS
- 发布时应按目标平台分别指定 RID（`win-x64` / `linux-x64` / `osx-x64`），
  **避免**把所有平台的原生库打进同一个发行目录
- 构建会自动排除 Skia / HarfBuzz 的原生 PDB；这些文件只用于框架内部调试，不影响运行、源码调试或跨平台发布

---

## 12. 下一步

- [06 · 多边形组件开发](06-polygon-components.md) —— 做可视化组件卡片
- [07 · 通知框架](07-notifications.md) —— 用 `NyaAlert` / `NyaPrompt` 与用户交互
- [02 · 主题开发指南](02-theming.md) —— 组件颜色如何跟随主题
