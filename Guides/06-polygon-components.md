# 06 · 多边形组件开发

> 多边形组件（Polygon Component）框架完整教程。
> 适用对象：内置 Dashboard 小组件开发者、NyaLauncher 插件作者。
> 参考实现：`NyaLauncher.Avalonia/Framework/BuiltIn*Component.cs`（8 个内置组件都是活样例）。

---

## 1. 核心概念

一个多边形组件 = **声明式定义（Definition）** + **运行时实例（Instance）**：

| 部分 | 职责 | 生命周期 |
|------|------|----------|
| `PolygonComponentDefinition` | 描述组件长什么样：形状、元素、动作、尺寸、主题变体 | 一次构建，全局共享，不可变 |
| `IPolygonComponentInstance` | 提供状态与行为：响应点击、发布状态 | 每个工作区卡片一个实例（实例池管理） |

宿主（`PolygonComponentView`）负责渲染、拖拽、缩放、hover 与长按拖动手势——
**你不需要写任何 UI 布局代码，只做「声明 + 状态」**。

### 1.1 契约工程

公共契约位于 `NyaLauncher.Plugin.Abstractions`，目标框架 `net10.0`，
**不引用 Avalonia**。第三方扩展可以独立描述形状、尺寸、内容、动作和运行时状态，
再由启动器宿主负责渲染与工作区交互。

开发期间可直接添加项目引用：

```xml
<ProjectReference Include="../NyaLauncher.Plugin.Abstractions/NyaLauncher.Plugin.Abstractions.csproj" />
```

当前契约版本：`PolygonComponentDefinition.CurrentContractVersion`（现为 `1`）。
宿主会拒绝不支持的契约版本、重复的组件 ID 或非法定义。

---

## 2. 归一化坐标系

所有元素的定位使用 `ComponentRect(x, y, width, height)`，
四个值都是 **0~1 的归一化比例**，相对组件当前尺寸：

```csharp
new ComponentRect(0.05, 0.07, 0.44, 0.42)
// x=5%  y=7%  宽=44%  高=42%
```

组件被拖大缩小时，内部布局自动按比例适配。

经验法则：

- 边距留 `0.04` ~ `0.07`
- 字号是**固定像素**，不随缩放变化；小卡片慎用大字号
- 想让进度条高约 10px：`高度比例 = 10 / 组件首选高度`

---

## 3. 最小可用组件

```csharp
using NyaLauncher.Plugin.Abstractions.Components;

public static PolygonComponentRegistration Create()
{
    var definition = new PolygonComponentBuilder(
            "mycompany.builtin/hello",       // 全局唯一 ID，建议 域名/名称 格式
            "你好卡片")                       // 组件库中显示的标题
        .WithDescription("第一个多边形组件")
        .WithGlyph("👋")                     // 组件库图标
        .WithSize(240, 120)                  // 首选尺寸
        .WithSizeLimits(180, 90, 480, 240)   // 允许的缩放范围
        .WithShape(PolygonShapeDefinition.Rectangle())
        .WithDragHandle(new ComponentRect(0.02, 0.35, 0.05, 0.30))
        // 组件不再自带颜色：所有画刷由宿主绑定主题资源（ComponentBgBrush / PrimaryTextBrush 等）
        .WithTheme(new PolygonComponentTheme())
        .AddText("hello-text", new ComponentRect(0.1, 0.4, 0.8, 0.2),
            "Hello Polygon!", ComponentTextRole.Title, fontSize: 14)
        .Build();

    return new PolygonComponentRegistration
    {
        Definition = definition,
        Factory = new DelegatePolygonComponentFactory(_ => new HelloInstance())
    };
}
```

> **主题**：不要在组件里硬编码或快照任何颜色。
> `PolygonComponentTheme` 只保留 `Variant`（`Default` / `Launch`）与 `BorderThickness`，
> 具体颜色由宿主映射到主题资源键（`DynamicResource` 绑定），明暗与主题切换实时跟随。

---

## 4. Builder API 速查

### 4.1 元信息

| 方法 | 说明 |
|------|------|
| `WithDescription(string)` | 组件库副标题 |
| `WithGlyph(string)` | 组件库图标（支持 Emoji 与 `material:xxx`） |
| `WithSize(double w, double h)` | 首选尺寸（DIP，单边 16~8192） |
| `WithSizeLimits(minW, minH, maxW, maxH)` | 缩放边界，必须满足 `Minimum ≤ Preferred ≤ Maximum` |
| `WithShape(PolygonShapeDefinition)` | 形状轮廓 |
| `WithDragHandle(ComponentRect)` | 兼容旧契约的拖拽把手区（当前工作区已改为长按任意位置拖动） |
| `WithTheme(PolygonComponentTheme)` | 主题变体与边框厚度 |
| `Build()` | 构建并执行校验 |

### 4.2 动作

```csharp
.AddAction("do-thing")                       // 声明动作 ID
.AddAction("streaming", allowReentry: true)  // 允许重入（连续触发）
.UseSurfaceAction("do-thing")                // 可选：整卡可点，等同触发该动作
```

- **动作必须先声明**，元素引用未声明的动作会被验证器拒绝
- 未设置 `allowReentry` 的动作在执行期间自动防重入（连点只生效一次）

### 4.3 元素

| 方法 | 签名 |
|------|------|
| `AddText` | `(string id, ComponentRect bounds, string text, ComponentTextRole role = Body, double fontSize = 12)` |
| `AddProgress` | `(string id, ComponentRect bounds, string label, double value = 0, double minimum = 0, double maximum = 100)` |
| `AddTextInput` | `(string id, ComponentRect bounds, string actionId, string value = "", string placeholder = "", int maximumLength = 256, bool isMultiline = false)` |
| `AddToggle` | `(string id, ComponentRect bounds, string label, string actionId, bool isChecked = false)` |
| `AddSlider` | `(string id, ComponentRect bounds, string label, string actionId, double minimum = 0, double maximum = 100, double value = 0, double step = 1)` |
| `AddImage` | `(string id, ComponentRect bounds, string source = "", ComponentRect? sourceRect = null, ComponentImageStretch stretch = UniformToFill, string fallbackText = "?", double cornerRadius = 0, bool pixelated = false, ComponentPixelRect? sourcePixelRect = null, bool isSkinHead = false)` |
| `AddButton` | `(string id, ComponentRect bounds, string text, string actionId, string glyph = "", bool isPrimary = false)` |
| `AddDropdown` | `(string id, ComponentRect bounds, string glyph = "⌄", IEnumerable<ComponentMenuItem>? pinnedItems = null, bool alignRight = false)` |

补充说明：

- `ComponentTextRole`：`Title` / `Body` / `Caption` / `Emphasis`
- **按钮文字不能为空**（校验器会拒绝）
- text + glyph 同时给按钮时会拼成 `" glyph text"`；只想要图标按钮就传 glyph 为主的文字
- `ComponentImageStretch`：`Uniform` / `UniformToFill` / `Fill` / `None`
- 下拉菜单的 `alignRight` 让触发按钮内容右对齐（整卡下拉场景中 chevron 靠右）

---

## 5. 形状与几何

`PolygonShapeDefinition` 使用 `[0,1]` 归一化坐标，可描述凸或凹的**简单多边形**。

```csharp
PolygonShapeDefinition.Rectangle()                 // 矩形
PolygonShapeDefinition.CutCorner(double inset = 0.12)  // 切角矩形
PolygonShapeDefinition.RegularPolygon(6)           // 正六边形
PolygonShapeDefinition.FromPoints(...)             // 自定义顶点
```

约束：

- 顶点数必须为 **3–64**
- **不支持**孔洞、自相交、相邻重复点或近零面积形状
- 宿主使用相同轮廓裁剪视觉并执行指针命中测试，**包围矩形中的透明角不会抢占悬停或点击**
- `DragHandleBounds` 为兼容旧契约保留，要求其中心位于轮廓内；但工作区不再显示或要求固定拖动把手

尺寸：

- `PreferredSize`、`MinimumSize`、`MaximumSize` 使用设备无关像素，必须满足 `Minimum ≤ Preferred ≤ Maximum`
- 单边尺寸范围 **16–8192 DIP**
- 元素和动作数量也有宿主保护上限

---

## 6. 实例与状态

### 6.1 推荐做法：继承基类

`PolygonComponentInstanceBase` 已封装 revision 递增、状态发布与释放检查：

```csharp
private sealed class HelloInstance : PolygonComponentInstanceBase
{
    public HelloInstance()
    {
        SetState(CreateState("Hello!"));   // 构造函数中发布初始状态
    }

    public override async ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ActionId != "do-thing")
            return ComponentActionResult.Failed($"未知动作：{invocation.ActionId}");

        // 动作参数：文本输入框提交（回车）携带 ["elementId", "value"]；
        // 按钮与表面点击会自动附带当前所有输入框的值（键为元素 Id），
        // 因此按钮动作同样能读取输入框内容（显式参数优先）
        invocation.Arguments?.TryGetValue("some-input", out var inputValue);

        SetState(CreateState("Done!"));
        return ComponentActionResult.Completed("搞定了。");
    }

    public override ValueTask DisposeAsync()
    {
        // 退订外部事件等清理，最后调用 base（基类负责释放标记与事件清空）
        return base.DisposeAsync();
    }

    private static ComponentStateSnapshot CreateState(string text) => new()
    {
        // Revision 留空即可，SetState 自动分配
        Elements = new Dictionary<string, ComponentElementState>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["hello-text"] = new() { Text = text }
        }
    };
}
```

也可以直接实现 `IPolygonComponentInstance`（基类本身也实现它），但不推荐——
样板代码多且容易漏释放检查。

### 6.2 状态覆盖语义

`ComponentElementState` 的每个字段都是**可空覆盖项**，
**`null` = 回退到声明默认值**：

| 字段 | 类型 | 作用 | 适用元素 |
|------|------|------|----------|
| `Text` | `string?` | 覆盖文字 | 文本 / 按钮 / 标签 |
| `Value` | `string?` | 覆盖输入值 / 滑块值（滑块用不变文化数字字符串） | 输入框 / 滑块 |
| `IsChecked` | `bool?` | 开关状态 | 开关 |
| `ImageSource` | `string?` | 覆盖图片来源（`null` → 显示 fallbackText） | 图片 |
| `ProgressValue` | `double?` | 覆盖进度 | 进度条 |
| `IsEnabled` | `bool?` | 禁用/启用 | 交互元素 |
| `IsVisible` | `bool?` | 显示/隐藏 | 全部 |
| `IsIndeterminate` | `bool?` | 不确定态 | 进度条 |
| `MenuItems` | `IReadOnlyList<ComponentMenuItem>?` | 动态菜单项 | 下拉菜单 |

只包含需要变化元素的精简快照即可——未提及的元素保持声明值。

### 6.3 快照是完整快照，不是增量补丁

```csharp
public sealed record ComponentStateSnapshot
{
    public long Revision { get; init; }
    public IReadOnlyDictionary<string, ComponentElementState> Elements { get; init; }
    public static ComponentStateSnapshot Empty { get; }   // 只读单例
}
```

- 未出现在 `Elements` 中的元素、以及 `ComponentElementState` 中为 `null` 的字段，
  **都会回到组件定义的默认值**
- 插件发布快照后**不应再修改其字典**；宿主会在跨线程应用前复制内容
- `Revision` 必须**严格递增**；相等或更旧的快照会被忽略（基类 `SetState` 自动分配）
- `ComponentStateSnapshot.Empty` 是只读单例，要发状态就 `new` 一个

### 6.4 返回值

```csharp
ComponentActionResult.Completed("已打开。")   // 成功，可带提示
ComponentActionResult.Failed("原因")          // 失败，宿主以 ToolTip 展示
```

### 6.5 线程安全

- 快照是**不可变 record**，可在任意线程调用 `SetState` 发布（基类内部处理释放检查与修订号）
- 状态事件可以从后台线程发出，宿主会切换到 UI 线程
- 释放后 `SetState` 会**静默忽略**，不会误发状态
- 动作和释放代码会在**后台线程**运行，不应直接访问 Avalonia 控件

照抄 `BuiltInMemoryUsageComponent` 的 Timer 轮询模式，
或 `BuiltInWorldLaunchComponent` 的 `Task.Run` 模式即可。

---

## 7. 图片元素

`ImageElementDefinition` 使用字符串来源保持公共契约与 Avalonia 解耦。

宿主接受：

- 本地**绝对路径**
- 绝对 **HTTPS** 地址
- 小型 `data:image/png;base64,...` 内嵌 PNG

限制与行为：

- 单张图片最大 **8 MiB**；远程结果使用有界缓存
- `SourceRect` 是 `[0,1]` 归一化图片坐标（适合不知道源像素尺寸的素材）
- `SourcePixelRect` 使用从图片左上角开始的**整数像素**坐标（适合 Minecraft 皮肤图集等固定布局）
- **二者不能同时设置**；像素裁剪要求非负起点和正宽高
- 图片解码后，超出实际尺寸的部分会被钳制到有效像素范围
- `Pixelated` 适合像素素材
- `FallbackText` 在来源为空或加载失败时提供可访问的占位内容
- `IsSkinHead` 只显示皮肤图集的头部区域（左上角 1/8）

```csharp
builder.AddImage(
    "player-face",
    new ComponentRect(0.08, 0.08, 0.84, 0.84),
    sourcePixelRect: new ComponentPixelRect(8, 8, 8, 8),
    fallbackText: "?",
    cornerRadius: 11,
    pixelated: true);

var state = new ComponentElementState
{
    ImageSource = "https://textures.example.invalid/player-skin.png",
    Text = "P"
};
```

运行时 `ImageSource` 会覆盖定义来源；设为 `null` 时恢复定义值，
空字符串则显示占位内容。

图片加载异步执行，较新的完整状态快照会取消旧请求；视图离开视觉树时会取消本地等待
并释放原生位图，重新挂载时即使 revision 未变化也会从保留快照恢复。
失败不会阻塞组件动作或工作区布局。

> 插件应只引用自己有权使用的资源，并为网络图片准备占位内容。

---

## 8. 下拉菜单元素

`DropdownElementDefinition.PinnedItems` 定义始终位于菜单顶部的固定命令；
运行时通过对应元素状态的 `MenuItems` 追加动态项目。

每个菜单项都可以携带参数，宿主会把它们原样放入
`ComponentActionInvocation.Arguments`：

```csharp
builder
    .AddAction("add-account")
    .AddAction("select-account")
    .AddDropdown(
        "account-menu",
        new ComponentRect(0.84, 0.2, 0.12, 0.6),
        pinnedItems:
        [
            new ComponentMenuItem
            {
                Id = "add",
                Text = "添加账号",
                ActionId = "add-account",
                SeparatorAfter = true
            }
        ]);

var state = new ComponentElementState
{
    MenuItems =
    [
        new ComponentMenuItem
        {
            Id = "account-0",
            Text = "Player_01",
            SecondaryText = "离线登录",
            IconSource = @"C:\plugin-data\account.png",
            ActionId = "select-account",
            Arguments = new Dictionary<string, string>
            {
                ["accountKey"] = "offline:Player_01"
            },
            IsSelected = true
        }
    ]
};
```

### `ComponentMenuItem`

| 成员 | 说明 |
|------|------|
| `Id` / `Text` | 必填 |
| `SecondaryText` | 副标题 |
| `Glyph` | 图标字符 |
| `IconSource` | 可选本地绝对路径或 HTTPS 图片；加载失败时回退到 `Glyph` |
| `IsSkinHead` | `IconSource` 是皮肤图集时只取头部区域 |
| `ActionId` | 必填，必须引用已声明的动作 |
| `Arguments` | 原样传入动作调用 |
| `IsEnabled` / `IsSelected` / `SeparatorAfter` | 显示控制 |

规则：

- 菜单按完整图片比例缩放，当前选中项会在图标上叠加勾选标记
- 固定项和运行时项**各自最多 128 个**
- 定义中的未知动作会被校验器拒绝
- 运行时的非法、重复或引用未知动作的菜单项会被宿主**忽略**
- `MenuItems` 与其他状态字段一样属于完整快照，**未提供时只显示固定项**

---

## 9. 注册

### 9.1 插件组件（推荐）

在插件初始化入口把 provider 注册到一个稳定功能区：

```csharp
window.FeatureAreas.RegisterPolygonComponents(
    "example-download-area",     // areaId
    "下载扩展",                   // title
    "第三方多边形组件",           // subtitle
    "⬡",                          // glyph
    new DownloadComponentProvider());
```

行为：

- `areaId` **已存在** → 保留该功能区的标题、图标与旧组件，把 provider 的组件**追加**到全局组件目录
- `areaId` **不存在** → 使用传入的元数据创建新功能区
- 已有个性化配置的 `ActionIds` 不会被强制改写，因此新组件会先出现在组件库，
  用户可以把它拖到任意功能区

### 9.2 内置组件

在 `NyaLauncher.Avalonia/Framework/BuiltInFeatureAreaProvider.cs` 中按功能域挂载：

```csharp
PolygonComponents =
[
    BuiltInMemoryUsageComponent.Create()
];
```

也可以把 `provider.GetPolygonComponents()` 赋给
`FeatureAreaDefinition.PolygonComponents`，再通过普通的 `FeatureAreas.Register(...)` 注册。

---

## 10. 实例生命周期

- `IPolygonComponentFactory.Create` 接收包含组件 ID 与功能区 ID 的 `ComponentInstanceContext`
- **组件库和拖动预览只展示定义，不创建可交互实例**；工作区中的可交互组件才使用 factory 创建实例
- 插件应把每次创建视为**独立生命周期**，不要在不同功能区位置之间共享可变 UI 状态
- 宿主在组件进入可视树时订阅 `StateChanged`，离开时取消订阅
- 缩放或布局重建只会替换视图，**不会中断该摆放位置正在执行的动作**
- 组件移除、跨区移动或工作区释放实例时，宿主会取消传给动作的令牌并调用 `DisposeAsync`
- 宿主会等待已接受的动作退出后再释放实例
- **主窗口关闭时会先给异步清理最多 5 秒的收尾时间**，再允许应用退出；
  失控的第三方实现不能永久阻止启动器关闭

插件必须在 `DisposeAsync` 里停止计时器、网络请求和其他自有资源，
并尽快遵守传入的 `CancellationToken`。

---

## 11. 校验规则

定义在进入渲染前会经过 `PolygonComponentValidator.ValidateAndSnapshot`，违反即抛异常：

- 元素尺寸 **16~8192 px**；字号 **1~512**；元素数 **≤ 256**；动作数 **≤ 128**
- **按钮文字不能为空**
- 动作 ID 不得重复；元素引用的动作必须已声明
- `UseSurfaceAction` 引用的动作必须存在
- 拖拽把手必须在 `0~1` 范围内
- 所有 `ComponentRect` 值 `0~1`（超出即抛）
- 形状顶点数 3–64，不支持孔洞 / 自相交 / 相邻重复点 / 近零面积

需要一次展示全部问题时，可直接检查 `Validate()` 返回的
`ComponentValidationResult.Errors`：

```csharp
var result = PolygonComponentValidator.Validate(definition);
foreach (var error in result.Errors)
    Console.WriteLine($"{error.Code} @ {error.Path}: {error.Message}");
```

> 自动化检查应断言稳定的 `Code` 和 `Path`，**不要依赖翻译后的 `Message`**。

---

## 12. 完整示例：六边形下载组件

```csharp
public sealed class DownloadComponentProvider : IPolygonComponentProvider
{
    public IReadOnlyList<PolygonComponentRegistration> GetPolygonComponents()
    {
        var definition = new PolygonComponentBuilder(
                "example.download/status", "下载状态")
            .WithDescription("显示当前任务进度")
            .WithGlyph("↓")
            .WithSize(320, 180)
            .WithShape(PolygonShapeDefinition.RegularPolygon(6))
            .WithDragHandle(new ComponentRect(0.43, 0.04, 0.14, 0.12))
            .AddAction("toggle")
            .AddText("title", new ComponentRect(0.20, 0.20, 0.60, 0.16),
                "资源下载", ComponentTextRole.Title, 16)
            .AddProgress("progress", new ComponentRect(0.20, 0.43, 0.60, 0.20), "下载进度")
            .AddButton("toggle-button", new ComponentRect(0.35, 0.70, 0.30, 0.16),
                "暂停", "toggle", isPrimary: true)
            .Build();

        return
        [
            new PolygonComponentRegistration
            {
                Definition = definition,
                Factory = new DelegatePolygonComponentFactory(_ => new DownloadComponentInstance())
            }
        ];
    }
}

public sealed class DownloadComponentInstance : PolygonComponentInstanceBase
{
    private double _progress;
    private bool _paused;

    public DownloadComponentInstance()
    {
        SetState(BuildSnapshot());
    }

    public override ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(invocation.ActionId, "toggle", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(ComponentActionResult.Failed("未知动作。"));

        _paused = !_paused;
        SetState(BuildSnapshot());
        return ValueTask.FromResult(
            ComponentActionResult.Completed(_paused ? "已暂停" : "已继续"));
    }

    public void ReportProgress(double value)
    {
        if (!double.IsFinite(value)) return;
        _progress = Math.Clamp(value, 0, 100);
        SetState(BuildSnapshot());
    }

    private ComponentStateSnapshot BuildSnapshot() => new()
    {
        Elements = new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase)
        {
            ["progress"] = new ComponentElementState
            {
                Text = _paused ? "已暂停" : "下载进度",
                ProgressValue = _progress
            },
            ["toggle-button"] = new ComponentElementState
            {
                Text = _paused ? "继续" : "暂停",
                IsEnabled = true
            }
        }
    };
}
```

---

## 13. 实战样例索引

| 组件 | 学到的模式 |
|------|-----------|
| `BuiltInWorldLaunchComponent` | 外部数据扫描 + 图片运行时覆盖 + 按钮禁用态 + 订阅外部事件刷新 |
| `BuiltInMemoryUsageComponent` | Timer 轮询 + 进度条/多文本状态发布 + 自动/手动配置取值 |
| `BuiltInPersonalizationComponent` | `UseSurfaceAction` 整卡可点 + 图标按钮 |
| `BuiltInMusicPlayerComponent` | 复杂状态机 + 下拉/滑块/切换综合运用 |
| `BuiltInGameLaunchComponent` | 后台任务 + 重入保护 + 状态文本反馈 |
| `BuiltInAccountSelectorComponent` | 动态下拉菜单 + 参数化动作 |
| `BuiltInGameInstanceSelectorComponent` | 共享状态 + 跨页面同步持久化 |
| `BuiltInSkinCapeComponent` | 两层图片裁剪 + 按账号类型生成不同菜单 |

---

## 14. 常见坑

1. **动作没 `AddAction` 声明** → 元素点击静默无效（宿主查不到动作直接返回）。
2. **忘记 `allowReentry`** → 流式动作连点第二次没反应（默认防重入是特性不是 Bug）。
3. **快照 Revision 不递增** → 宿主可能忽略同版本快照（用基类 `SetState` 可避免）。
4. **`ComponentStateSnapshot.Empty` 是单例** → 只读；要发状态就 `new` 一个。
5. **把拖拽把手盖在按钮上** → 长按拖动与点击手势打架。
6. **硬编码颜色** → 深浅模式切换后组件与宿主脱节，务必走 `PolygonComponentTheme` 的 `Variant`。
7. **`DisposeAsync` 里没退订外部事件** → 实例池会复用实例，泄漏会跨页面存续。
8. **在不同功能区位置共享可变状态** → 每次 `Create` 都是独立生命周期，不要共享。
9. **改了组件 ID** → 用户的个性化布局引用失效。ID 必须保持稳定。
10. **在动作里直接访问 Avalonia 控件** → 动作在后台线程运行，会崩。
