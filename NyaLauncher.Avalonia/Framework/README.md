# 功能区与组件扩展

新界面的工作区由 `FeatureAreaRegistry` 驱动。内置功能与插件使用相同的注册方式，
因此新增功能区不需要修改 `MainWindow.axaml` 或 `DockWorkspace`。

## 注册一个按钮型功能区

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
            "hello",
            "执行操作",
            "点击运行插件命令",
            "▶",
            () => RunPluginCommand())
    ]
});
```

## 注册完全自定义的功能区

将 `ContentFactory` 设置为返回任意 Avalonia `Control` 的工厂即可：

```csharp
window.FeatureAreas.Register(new FeatureAreaDefinition
{
    Id = "custom-view",
    Title = "自定义界面",
    Subtitle = "由插件完整控制内容",
    ContentFactory = () => new MyPluginView()
});
```

一个插件需要提供多个区域时，实现 `IFeatureAreaProvider`，然后调用
`FeatureAreas.Register(provider)`。运行时注册或移除区域后，工作区会自动刷新。

功能区会自动获得停靠把手。将把手拖到另一功能区的上、下、左、右侧，即可生成二维
停靠布局。相邻区域的边框接缝本身支持拖动缩放，没有额外的缩放按钮；插件不需要自行
实现布局、吸附或调整大小逻辑。

## 注册多边形组件

多边形组件的公共契约位于 `NyaLauncher.Plugin.Abstractions`。该项目以 `net10.0` 为目标，
不引用 Avalonia；第三方扩展可以独立描述形状、尺寸、内容、动作和运行时状态，再由启动器
宿主负责渲染与工作区交互。开发期间可直接添加项目引用：

```xml
<ProjectReference Include="../NyaLauncher.Plugin.Abstractions/NyaLauncher.Plugin.Abstractions.csproj" />
```

下面的示例注册一个六边形下载组件，包含标题、进度条和“暂停/继续”命令。代码使用的均为
当前公共契约类型：

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Components;

public sealed class DownloadComponentProvider : IPolygonComponentProvider
{
    public IReadOnlyList<PolygonComponentRegistration> GetPolygonComponents()
    {
        var definition = new PolygonComponentBuilder(
                "example.download/status",
                "下载状态")
            .WithDescription("显示当前任务进度")
            .WithGlyph("↓")
            .WithSize(320, 180)
            .WithShape(PolygonShapeDefinition.RegularPolygon(6))
            .WithDragHandle(new ComponentRect(0.43, 0.04, 0.14, 0.12))
            .AddAction("toggle")
            .AddText(
                "title",
                new ComponentRect(0.20, 0.20, 0.60, 0.16),
                "资源下载",
                ComponentTextRole.Title,
                16)
            .AddProgress(
                "progress",
                new ComponentRect(0.20, 0.43, 0.60, 0.20),
                "下载进度")
            .AddButton(
                "toggle-button",
                new ComponentRect(0.35, 0.70, 0.30, 0.16),
                "暂停",
                "toggle",
                isPrimary: true)
            .Build();

        return
        [
            new PolygonComponentRegistration
            {
                Definition = definition,
                Factory = new DelegatePolygonComponentFactory(
                    _ => new DownloadComponentInstance())
            }
        ];
    }
}

public sealed class DownloadComponentInstance : IPolygonComponentInstance
{
    private long _revision;
    private double _progress;
    private bool _paused;

    public DownloadComponentInstance()
    {
        CurrentState = BuildSnapshot();
    }

    public ComponentStateSnapshot CurrentState { get; private set; }

    public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

    public void ReportProgress(double value)
    {
        if (!double.IsFinite(value))
            return;

        _progress = Math.Clamp(value, 0, 100);
        Publish();
    }

    public ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                invocation.ActionId,
                "toggle",
                StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(
                ComponentActionResult.Failed("未知动作。"));
        }

        _paused = !_paused;
        Publish();
        return ValueTask.FromResult(
            ComponentActionResult.Completed(_paused ? "已暂停" : "已继续"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Publish()
    {
        CurrentState = BuildSnapshot();
        StateChanged?.Invoke(
            this,
            new ComponentStateChangedEventArgs(CurrentState));
    }

    private ComponentStateSnapshot BuildSnapshot()
    {
        return new ComponentStateSnapshot
        {
            Revision = ++_revision,
            Elements = new Dictionary<string, ComponentElementState>(
                StringComparer.OrdinalIgnoreCase)
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
}
```

在插件初始化入口将 provider 注册到一个稳定功能区：

```csharp
window.FeatureAreas.RegisterPolygonComponents(
    "example-download-area",
    "下载扩展",
    "第三方多边形组件",
    "⬡",
    new DownloadComponentProvider());
```

若 `areaId` 已存在，注册表会保留该功能区的标题、图标与旧组件，并把 provider 的组件追加到
全局组件目录；若不存在，则使用传入的元数据创建新功能区。已有个性化配置的 `ActionIds` 不会
被强制改写，因此新组件会先出现在组件库，用户可以把它拖到任意功能区。

仓库内置参考实现位于 `BuiltInFeatureAreaProvider`。其中
`nyalauncher.builtin/account-selector` 使用小型矩形、动态下拉菜单和参数化动作选择真实账号；
`nyalauncher.builtin/game-instance-selector` 使用共享状态和动态下拉菜单显示、切换当前 Minecraft
文件夹内的已安装版本，并与启动页同步持久化选择；
`nyalauncher.builtin/version-manager` 保留长按任意位置拖动、短按表面进入独立版本管理页面，
可维护多个版本文件夹并查看内容、重命名实际版本文件、编辑实例启动设置和显式切换版本隔离；
`nyalauncher.builtin/game-launch` 使用整个多边形表面动作直接启动当前实例，并通过共享启动服务
同步准备、运行、失败和退出状态；右下角圆形任务入口会在下载与启动并行时优先打开下载进度，
详情窗口左侧可切换到有界保存的实时 Java 输出日志；
`nyalauncher.builtin/skin-cape-editor` 使用两层图片裁剪显示玩家脸部与帽子层，并按正版或离线
账号生成不同菜单；
`nyalauncher.builtin/download-task-progress` 使用六边形、按钮与严格递增的 revision 演示异步
进度更新。

也可以把 `provider.GetPolygonComponents()` 赋给 `FeatureAreaDefinition.PolygonComponents`，
再通过普通的 `FeatureAreas.Register(...)` 注册。组件 ID 在全局按忽略大小写比较，第三方 ID
必须使用 `publisher.plugin/name` 形式；保持 ID 稳定，用户布局才能在升级后继续引用组件。

## 多边形定义与校验

- `PolygonShapeDefinition` 使用 `[0,1]` 归一化坐标，可描述凸或凹的简单多边形；顶点数必须为
  3–64，不支持孔洞、自相交、相邻重复点或近零面积形状。宿主使用相同轮廓裁剪视觉并执行
  指针命中测试，包围矩形中的透明角不会抢占悬停或点击。
- `PreferredSize`、`MinimumSize` 与 `MaximumSize` 使用设备无关像素，并且必须满足
  `Minimum ≤ Preferred ≤ Maximum`；Builder 可通过 `WithSize(...)` 与 `WithSizeLimits(...)`
  设置。`ComponentRect` 用归一化坐标描述元素位置。为兼容 gp3 初始契约仍保留
  `DragHandleBounds` 并要求其中心位于轮廓内，但工作区不再显示或要求固定拖动把手；短按保留
  元素动作，长按组件任意可见位置后即可拖动整个组件。
- 当前内置元素为 `TextElementDefinition`、`ImageElementDefinition`、
  `ProgressElementDefinition`、`ButtonElementDefinition` 和 `DropdownElementDefinition`。按钮与菜单项的 `ActionId` 必须
  引用定义中的动作；可选的 `SurfaceActionId` 能让整个多边形表面响应动作。
- `PolygonComponentBuilder.Build()` 会执行校验，宿主注册时也会再次调用
  `PolygonComponentValidator`。需要一次展示全部问题时，可直接检查 `Validate()` 返回的
  `ComponentValidationResult.Errors`；自动化检查应断言稳定的 `Code` 和 `Path`，不要依赖翻译后的
  `Message`。
- 当前契约版本为 `PolygonComponentDefinition.CurrentContractVersion`。宿主会拒绝不支持的契约
  版本、重复的组件 ID 或非法定义；单边尺寸范围为 16–8192 DIP，元素和动作数量也有宿主保护
  上限。同时保留传统 `FeatureAreaAction` 的矩形渲染路径。

## 图片元素

`ImageElementDefinition` 使用字符串来源保持公共契约与 Avalonia 解耦。宿主接受本地绝对路径、
绝对 HTTPS 地址或小型 `data:image/png;base64,...` 内嵌 PNG，单张图片最大 8 MiB；远程结果使用
有界缓存。`SourceRect` 是 `[0,1]` 归一化图片坐标，适合不知道源像素尺寸的素材；
`SourcePixelRect` 使用从图片左上角开始的整数像素坐标，适合 Minecraft 皮肤图集等固定布局。
二者不能同时设置。像素裁剪要求非负起点和正宽高；图片解码后，超出实际尺寸的部分会被钳制
到有效像素范围。`Pixelated` 适合像素素材，`FallbackText` 在来源为空或加载失败时提供可访问的
占位内容：

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

运行时 `ImageSource` 会覆盖定义来源；设为 `null` 时恢复定义值，空字符串则显示占位内容。图片
加载异步执行，较新的完整状态快照会取消旧请求；视图离开视觉树时会取消本地等待并释放原生
位图，重新挂载时即使 revision 未变化也会从保留快照恢复。失败不会阻塞组件动作或工作区布局。
插件应只引用自己有权使用的资源，并为网络图片准备占位内容。

## 下拉菜单元素

`DropdownElementDefinition.PinnedItems` 定义始终位于菜单顶部的固定命令；运行时通过对应元素
状态的 `MenuItems` 追加动态项目。每个菜单项都可以携带参数，宿主会把它们原样放入
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

`IconSource` 可选填本地绝对路径或 HTTPS 图片；菜单按完整图片比例缩放，加载失败时回退到
`Glyph`，当前选中项会在图标上叠加勾选标记。固定项和运行时项各自最多 128 个。定义中的未知动作会被校验器拒绝；运行时的非法、重复或
引用未知动作的菜单项会被宿主忽略。`MenuItems` 与其他状态字段一样属于完整快照，未提供时只
显示固定项。

## 实例生命周期与状态

`IPolygonComponentFactory.Create` 接收包含组件 ID 与功能区 ID 的 `ComponentInstanceContext`。
组件库和拖动预览只展示定义，不创建可交互实例；工作区中的可交互组件才使用 factory 创建
实例并调用命令。插件应把每次创建视为独立生命周期，不要在不同功能区位置之间共享可变 UI
状态。

宿主在组件进入可视树时订阅 `StateChanged`，离开时取消订阅；缩放或布局重建只会替换视图，
不会中断该摆放位置正在执行的动作。状态事件可以从后台线程发出，宿主会切换到 UI 线程。
每个 `ComponentStateSnapshot.Revision` 必须严格递增；相等或更旧的快照会被忽略。状态字典按
元素 ID 更新文本、图片来源、进度、动态菜单项、启用、可见与不确定态，有限但越界的进度值由宿主限制
到定义范围，非有限值不会覆盖定义初值。

`ComponentStateSnapshot` 是完整快照，不是增量补丁：未出现在 `Elements` 中的元素、以及
`ComponentElementState` 中为 `null` 的字段，都会回到组件定义的默认值。插件发布快照后不应再
修改其字典；宿主会在跨线程应用前复制内容。

`AllowReentry = false` 的动作在执行期间不会再次进入。组件移除、跨区移动或工作区释放实例时，
宿主会取消传给动作的令牌并调用 `DisposeAsync`；插件也应在其中停止计时器、网络请求和其他
自有资源。宿主会等待已接受的动作退出后再释放实例；命令应尽快遵守传入的
`CancellationToken`，并通过 `ComponentActionResult` 返回可供宿主显示的结果。主窗口关闭时
也会先给这些异步清理最多 5 秒的收尾时间，再允许应用退出；失控的第三方实现不能永久阻止
启动器关闭。动作和释放代码会在后台线程运行，不应直接访问 Avalonia 控件。

## 多边形组件持久化

多边形定义、工厂、运行时实例和瞬时状态不会写入 `workspace.json`。工作区仍只保存稳定
组件 ID 以及已有的 `AreaId`、`RelativeX`、`RelativeY`、`ZIndex` 放置字段；功能区偏好通过
同一个组件 ID 引用目录项。插件应在恢复用户个性化与布局前重新注册相同 ID，随后由 factory
恢复自己的业务状态。

这一规则使 gp2 的版本 2 工作区配置继续兼容：传统矩形组件不需要迁移，多边形组件也不会
把插件私有对象或正在变化的进度序列化进启动器配置。

## 用户个性化

主窗口顶边栏的“个性化”入口允许用户重命名每个功能区、自定义简介与图标，并从所有
已注册功能构成的全局目录中选择该区域显示的按钮。同一按钮可以出现在多个区域。图标
既可使用内置简约预设，也可通过文件选择器引用本地图片；图片失效时自动回退到预设。

区域使用不表达业务含义的稳定编号，例如 `area-001`。内置区域占用前三个编号；用户
在个性化窗口中新建区域时，从 `area-004` 开始继续递增。用户创建的区域定义也会写入
配置文件，并在下次启动时先恢复区域、再恢复名称、按钮和布局。

配置保存在 `%LOCALAPPDATA%\NyaLauncher\workspace.json`，内容包括：

- 功能区显示名称、简介与按钮 ID；
- 图标预设字符与可选的本地图片路径；
- 用户创建的功能区及其中性编号；
- 水平/垂直嵌套的停靠树；
- 每个停靠分组的尺寸权重；
- 全局组件缩放，以及组件所属功能区、相对坐标与永久层级。

## 自动侧栏

释放布局接缝后，工作区只在以下条件同时成立时自动折叠区域：

1. 区域宽度或高度低于对应阈值；
2. 区域有一整条自身边框贴住工作区外边缘。

每个外边缘最多保存一个侧栏。折叠时区域会从停靠树移除，其他区域占满空间；侧栏分别
使用工作区外层网格的独立行列轨道，不使用覆盖层。悬停边缘栏时对应轨道按原展开尺寸
加宽或加高，主工作区由布局系统重新测量并真实让位。配置保存侧栏区域 ID、边缘与展开
尺寸。展开侧栏后，可以拖动标题栏手柄吸附到任意窗口边缘；若目标边已有侧栏，两个侧栏
交换位置。侧栏轨道横跨对应的完整外边缘，并以短动画展开或收起。侧栏自身没有可调整的
固定尺寸；用户按住展开界面靠工作区一侧的接缝时，该区域会立即退出侧栏状态，但同一次
指针手势会继续控制恢复后功能区的接缝。松手时低于折叠阈值会重新成为侧栏，高于阈值则
保留为普通功能区。自动检测绑定在
`GridSplitter.DragCompleted`，避免鼠标释放事件被控件内部处理。

相邻边缘同时存在侧栏时，上、下侧栏拥有角落区域，左、右侧栏填充二者之间的剩余高度；
各侧栏处于不同网格单元，不依赖 `ZIndex` 相互覆盖。

## 体积与跨平台发布

项目不固定 `RuntimeIdentifier`，继续由 Avalonia 的 `UsePlatformDetect()` 支持 Windows、Linux
和 macOS。发布时应按目标平台分别指定 RID，例如 `win-x64`、`linux-x64` 或 `osx-x64`，
避免把所有平台的原生库打进同一个发行目录。构建会自动排除 Skia/HarfBuzz 的原生 PDB；
这些文件只用于框架内部调试，不影响运行、源码调试或跨平台发布。

## 个性化配置目录

个性化窗口允许用户选择统一的配置目录，`workspace.json` 保存工作区个性化，`config.json`
保存账户与启动配置。目标为空目录时会迁移两份配置；目标已有配置时，用户可采用目标配置，
并选择删除或先备份原目录配置。切换完成后，工作区与启动设置会从最终目录重新加载。

默认目录基于 `Environment.SpecialFolder.LocalApplicationData`，由运行平台映射到当前用户的
数据目录。应用仅在 `Environment.SpecialFolder.ApplicationData/NyaLauncher` 保存一个不含
个性化内容的 `workspace-location.txt`，用于下次启动时定位用户选择的目录。仓库内开发配置
使用被 Git 忽略的 `.nya-data/`。

插件提供的 `FeatureAreaAction.Id` 或 `PolygonComponentDefinition.Id` 应在全局范围内保持唯一，
才能被个性化配置稳定引用。
