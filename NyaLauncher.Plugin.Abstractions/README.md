# NyaLauncher 第三方插件开发规范（API v1）

本文档对应 `manifestVersion: 1`、`apiVersion: "1.0"` 和当前
`NyaLauncher.Plugin.Abstractions` 契约。插件能够提供自定义 Polygon 组件、声明式设置、
Minecraft 实例持久修改动作以及每次启动时的启动计划修改。

> 插件程序集与启动器运行在同一进程中。能力授权用于控制宿主 API 和记录用户同意，
> **不是安全沙箱**。只安装可信来源的插件，并在发布前审查依赖与更新包。

## 1. 安装目录与持久数据

插件根目录始终跟随启动器当前生效的配置存储目录。用户切换到自定义存储目录后，
启动器也从新目录中的 `plugins` 读取插件；不要在插件代码中猜测系统默认路径。
通过启动器执行配置存储迁移时，插件包、状态和私有数据会作为 `plugins` 整体迁移；
迁移发生冲突时应先由用户处理，插件不能自行在新旧配置根之间复制数据。

```text
<当前配置存储目录>/
└─ plugins/
   ├─ packages/                         # 用户安装的插件包；一个子目录一个插件
   │  └─ dev.example.toolbox/
   │     ├─ plugin.json                 # 必需，且必须位于包根目录
   │     ├─ lib/
   │     │  ├─ Example.Toolbox.dll     # 入口程序集
   │     │  └─ ...                     # 插件自己的依赖
   │     └─ assets/
   │        └─ icon.png
   ├─ data/                             # 启动器管理的插件持久数据
   │  └─ dev.example.toolbox/
   │     ├─ settings.json               # 声明式设置值，由宿主管理
   │     ├─ data/                       # IPluginStorage.DataDirectory
   │     │  └─ settings-files/          # File 设置由宿主导入的私有副本
   │     └─ cache/                      # IPluginStorage.CacheDirectory，可被清理
   └─ state.json                        # 启停、授权和错误状态，由宿主管理
```

约束如下：

- 扫描范围是 `packages` 的直接子目录；不要把两个插件塞在同一个目录中。
- `PackageDirectory` 应视为只读。升级时可以替换包，持久数据仍放在独立的 `data` 目录。
- 不要直接编辑 `state.json`、`settings.json`，也不要依赖它们的内部 JSON 结构。
- 使用 `Context.Storage.GetDataPath(relativePath)` 和 `GetCachePath(relativePath)` 解析私有路径。
  绝对路径、`..` 越界路径会被拒绝。
- 安装或替换包后，在“插件列表”页面点击“重新扫描”。更新正在运行的插件通常需要重启；
  最稳妥的流程是先禁用、替换整个包，再重新扫描并启用。

## 2. 插件包清单 `plugin.json`

下面示例列出了 v1 的全部顶层字段，并展示常用设置约束。JSON 属性名大小写不敏感，
但建议统一使用示例中的 camelCase。宿主支持并推荐 `kind` 和 `scope` 的可读字符串枚举名称；
数值枚举只用于兼容，不建议新插件继续使用。

```json
{
  "manifestVersion": 1,
  "id": "dev.example.toolbox",
  "name": "Example Toolbox",
  "version": "1.2.0",
  "apiVersion": "1.0",
  "minimumLauncherVersion": "0.1.0",
  "description": "组件、实例动作和自定义启动器示例。",
  "authors": ["Example Team"],
  "homepage": "https://example.dev/toolbox",
  "license": "MIT",
  "icon": "assets/icon.png",
  "entryAssembly": "lib/Example.Toolbox.dll",
  "entryType": "Example.Toolbox.PluginEntry",
  "requiredCapabilities": [
    "ui.components",
    "minecraft.instance.modify",
    "minecraft.launch.modify"
  ],
  "optionalCapabilities": [
    "network.http",
    "user-files.read"
  ],
  "settings": [
    {
      "key": "display.title",
      "title": "组件标题",
      "description": "显示在组件顶部的短标题。",
      "kind": "Text",
      "scope": "Global",
      "defaultValue": "工具箱",
      "required": true,
      "minimum": null,
      "maximum": null,
      "step": null,
      "maximumLength": 40,
      "pattern": "^[^\\r\\n]+$",
      "placeholder": "输入标题",
      "options": [],
      "fileExtensions": []
    },
    {
      "key": "refresh.seconds",
      "title": "刷新间隔",
      "description": "后台数据的刷新间隔（秒）。",
      "kind": "Integer",
      "scope": "Global",
      "defaultValue": 30,
      "required": true,
      "minimum": 5,
      "maximum": 3600,
      "step": 5,
      "maximumLength": null,
      "pattern": null,
      "placeholder": null,
      "options": [],
      "fileExtensions": []
    },
    {
      "key": "loading.image",
      "title": "启动页图片",
      "description": "选择要导入插件私有数据目录的加载页图片。",
      "kind": "File",
      "scope": "Global",
      "defaultValue": null,
      "required": false,
      "minimum": null,
      "maximum": null,
      "step": null,
      "maximumLength": 1024,
      "pattern": null,
      "placeholder": "选择 PNG 或 JPEG 文件",
      "options": [],
      "fileExtensions": [".png", ".jpg", ".jpeg"]
    },
    {
      "key": "channel",
      "title": "更新通道",
      "description": "选择插件自己的资源更新通道。",
      "kind": "Choice",
      "scope": "Global",
      "defaultValue": "stable",
      "required": true,
      "minimum": null,
      "maximum": null,
      "step": null,
      "maximumLength": null,
      "pattern": null,
      "placeholder": null,
      "options": [
        { "value": "stable", "label": "稳定", "description": "只接收稳定资源。" },
        { "value": "preview", "label": "预览", "description": "允许测试资源。" }
      ],
      "fileExtensions": []
    }
  ]
}
```

### 2.1 顶层字段

| 字段 | 必需 | 规则 |
| --- | --- | --- |
| `manifestVersion` | 否 | 当前只能是 `1`，省略时 SDK 默认也是 `1`。 |
| `id` | 是 | 稳定、小写反向域名，例如 `dev.example.toolbox`；在同一 `packages` 根下全局唯一。发布后不要更改。 |
| `name` / `version` | 是 | 非空。`version` 建议使用语义化版本。 |
| `apiVersion` | 否 | 当前兼容主版本 `1`，推荐明确写 `1.0`。 |
| `minimumLauncherVersion` | 否 | 至少两段的数字语义版本，例如 `0.1.0`；高于当前启动器时拒绝加载。 |
| `description` / `authors` | 否 | 展示在插件详情页。 |
| `homepage` / `license` | 否 | 发布信息；当前宿主只作为元数据读取。 |
| `icon` | 否 | 包目录内的相对路径，不得是绝对路径或逃逸包目录。 |
| `entryAssembly` | 是 | 包目录内已存在的 `.dll` 相对路径。 |
| `entryType` | 是 | 实现 `INyaLauncherPlugin` 的非抽象类型全名；必须有公共无参构造函数。 |
| `requiredCapabilities` | 否 | 缺少任一授权时插件不得启动。 |
| `optionalCapabilities` | 否 | 插件必须用 `IsCapabilityGranted` 检查并能在未授权时降级。 |
| `settings` | 否 | 由宿主渲染和校验的设置定义，`key` 在插件内不区分大小写且不得重复。 |

`entryType` 可以写完整类型名，也可以写程序集限定名；通常完整类型名更便于包升级。

## 3. 创建项目与最小完整入口

当前 SDK 目标框架为 `net10.0`。仓库内开发可以直接引用项目；仓库外开发则引用与目标启动器
完全相同版本的 `NyaLauncher.Plugin.Abstractions.dll`。不要把另一版本的 SDK DLL 当作插件私有依赖发布，
宿主会共享自己的契约程序集。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>Example.Toolbox</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- 路径按插件项目位置调整；Private=false 避免把宿主 SDK 复制进插件包。 -->
    <ProjectReference Include="..\NyaLauncher.Plugin.Abstractions\NyaLauncher.Plugin.Abstractions.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>
</Project>
```

下面入口只注册一个可交互 Polygon 组件，是能够被当前宿主加载的最小完整骨架。
组件、实例扩展和启动贡献都必须在 `StartAsync`（或 `PluginBase.OnStartAsync`）期间注册。

```csharp
using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace Example.Toolbox;

public sealed class PluginEntry : PluginBase
{
    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var componentId = $"{Context.Manifest.Id}/status";
        var definition = new PolygonComponentBuilder(componentId, "插件状态")
            .WithDescription("一个最小的动态 Polygon 组件")
            .WithShape(PolygonShapeDefinition.CutCorner(0.1))
            .WithSize(300, 170)
            .AddAction("refresh")
            .AddText(
                "title",
                new ComponentRect(0.08, 0.12, 0.84, 0.18),
                "Example Toolbox",
                ComponentTextRole.Title,
                18)
            .AddText(
                "value",
                new ComponentRect(0.08, 0.40, 0.84, 0.18),
                "等待刷新")
            .AddButton(
                "refresh-button",
                new ComponentRect(0.58, 0.70, 0.34, 0.18),
                "刷新",
                "refresh",
                isPrimary: true)
            .Build();

        Context.Registrar.AddComponentArea(new PluginComponentArea
        {
            // 功能区 ID 也应保持全局稳定；组件 ID 必须以“插件 ID/”开头。
            Id = $"{Context.Manifest.Id}.area",
            Title = "Example Toolbox",
            Subtitle = "第三方插件组件",
            Components =
            [
                new PolygonComponentRegistration
                {
                    Definition = definition,
                    Factory = new DelegatePolygonComponentFactory(
                        _ => new StatusComponentInstance())
                }
            ]
        });

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        // 在此取消并等待入口对象自己创建的后台任务，解除事件订阅。
        return ValueTask.CompletedTask;
    }
}

internal sealed class StatusComponentInstance : IPolygonComponentInstance
{
    private long _revision;

    public ComponentStateSnapshot CurrentState { get; private set; } =
        CreateState(0, "等待刷新");

    public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

    public ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(invocation.ActionId, "refresh", StringComparison.Ordinal))
            return ValueTask.FromResult(ComponentActionResult.Failed("未知动作。"));

        CurrentState = CreateState(
            Interlocked.Increment(ref _revision),
            DateTimeOffset.Now.ToString("HH:mm:ss"));
        StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(CurrentState));
        return ValueTask.FromResult(ComponentActionResult.Completed("已刷新。"));
    }

    public ValueTask DisposeAsync()
    {
        // 每个可视实例在插件禁用或工作区关闭时单独释放。
        return ValueTask.CompletedTask;
    }

    private static ComponentStateSnapshot CreateState(long revision, string text) => new()
    {
        Revision = revision,
        Elements = new Dictionary<string, ComponentElementState>
        {
            ["value"] = new() { Text = text }
        }
    };
}
```

构建后，把入口 DLL、它的私有依赖、资源和 `plugin.json` 复制到同一个插件包目录结构中。
入口 DLL 的实际相对路径必须与 `entryAssembly` 完全一致。

## 4. 生命周期与注册规则

生命周期按以下顺序执行：

1. 宿主先读取、校验清单，不执行插件代码。
2. 用户启用插件并授予必要能力后，宿主在可卸载的程序集上下文中创建入口对象。
3. 宿主调用 `StartAsync`，此时 `Context.Registrar` 开放。
4. 只有 `StartAsync` 成功完成，组件、实例扩展和启动贡献才会一次性发布。
5. 禁用时先停止新的组件调用、等待组件实例释放，再调用 `StopAsync` 并尝试卸载程序集。

开发要求：

- 入口类型必须有无参构造函数；构造函数只做轻量初始化，不访问 UI、网络或 Minecraft。
- `StartAsync` 应尊重取消令牌，不要用 `.Wait()`、`.Result` 或无限阻塞。
- 注册窗口关闭后继续持有 `Registrar` 并调用会失败；v1 不支持运行中动态增删注册项。
- `StopAsync` 必须取消并等待插件自己的任务，解除静态事件和计时器引用。
- 不要在静态字段、线程或宿主事件中长期保留组件实例，否则可卸载上下文无法回收。
- 当前宿主的启动、停止、单个启动贡献和实例动作有超时保护。进程内代码无法被安全强杀；
  超时插件会被隔离，显示“需要重启”，本次相关功能不再继续运行。

禁用不会删除设置、私有数据或用户的组件布局。相同插件 ID、功能区 ID 和组件 ID 再次启用时，
宿主会尽量恢复原位置；因此这些 ID 都是持久协议的一部分。

## 5. 能力声明与安全边界

当前已知能力名如下：

| 能力 | 用途与当前边界 |
| --- | --- |
| `ui.components` | 注册框架无关的 Polygon 组件；`AddComponentArea` 必须具备。 |
| `ui.native` | 为未来原生 UI 扩展预留；v1 当前没有可获取的原生 UI 宿主服务。 |
| `network.http` | 声明插件会访问网络；当前不提供统一 HTTP 服务。 |
| `system.info.read` | 声明读取系统信息。 |
| `user-files.read` / `user-files.write` | 声明访问实例目录之外的用户文件。 |
| `process.start` | 声明会启动外部进程。 |
| `minecraft.instance.read` | 声明读取 Minecraft 实例。 |
| `minecraft.instance.modify` | 注册持久实例修改扩展；`AddMinecraftInstanceExtension` 必须具备。 |
| `minecraft.launch.modify` | 注册每次启动贡献；`AddMinecraftLaunchContributor` 必须具备。 |

必要能力放在 `requiredCapabilities`；缺少时不要启动。可选能力放在 `optionalCapabilities`，
并在代码中检查：

```csharp
if (Context.IsCapabilityGranted(PluginCapabilities.NetworkHttp))
{
    // 可以启用插件自己的联网功能。
}
```

首次启用且缺少必要授权时，启动器会显示确认窗。取消不会修改授权或启用状态；用户确认后，
必要能力按插件 ID 持久保存，即使随后插件启动失败，已经作出的授权决定仍会保留，而
`Enabled` 只有在启动成功后才会写入。必要授权被撤销或不再完整时，插件会保持禁用并要求重新确认。
可选能力不会被自动批准，插件必须始终提供未授权时的降级路径。

`IPluginContext.GetService<TService>()` 是预留扩展点，当前宿主对未知或未实现服务返回 `null`；
不要把它当作已提供 HTTP、可由插件任意调用的文件选择器、原生控件或进程服务的承诺。

再次强调：插件是普通进程内 .NET 程序集，理论上可以绕过 SDK 直接调用系统 API。
能力授权并不能阻止恶意代码；它只为受控宿主入口和用户知情提供边界。

## 6. Polygon 自定义组件

`ui.components` 是 v1 稳定的前端扩展面。它不暴露 Avalonia 类型，而是由三部分组成：

- `PolygonComponentDefinition`：不可变的形状、尺寸、主题、元素和动作声明。
- `IPolygonComponentFactory`：为工作区中的每个可视组件位置创建独立运行实例。
- `IPolygonComponentInstance`：提供完整状态快照、状态事件和动作处理。

当前声明式元素包括 `Text`、`Progress`、`TextInput`、`Toggle`、`Slider`、`Image`、`Button`
和 `Dropdown`。组件轮廓支持矩形、切角、正多边形或自定义简单多边形；坐标和元素
`Bounds` 使用 `[0,1]` 归一化坐标。图片来源可以是本地路径或绝对 HTTPS URL，并支持
归一化或像素裁剪。`ComponentMenuItem.IconSource` 也可为下拉菜单项提供本地绝对路径或
HTTPS 图片；宿主保持完整纵横比并在失败时回退到 `Glyph`。

交互输入使用 Builder 的稳定 API。例如，一个可输入提示词的简化 AI 组件可以这样声明：

```csharp
var definition = new PolygonComponentBuilder(
        "dev.example.toolbox/ai", "AI 助手")
    .AddAction("ask-ai")
    .AddAction("toggle-stream")
    .AddAction("set-temperature")
    .AddTextInput(
        "prompt", new ComponentRect(0.06, 0.12, 0.88, 0.30), "ask-ai",
        placeholder: "输入问题，Ctrl+Enter 发送",
        maximumLength: 2000, isMultiline: true)
    .AddToggle(
        "stream", new ComponentRect(0.06, 0.48, 0.40, 0.12),
        "流式回复", "toggle-stream", isChecked: true)
    .AddSlider(
        "temperature", new ComponentRect(0.50, 0.46, 0.44, 0.18),
        "温度", "set-temperature",
        minimum: 0, maximum: 2, value: 0.7, step: 0.1)
    .AddText(
        "answer", new ComponentRect(0.06, 0.66, 0.88, 0.26), "等待提问…")
    .Build();
```

这三个元素触发动作时，`ComponentActionInvocation.Arguments` 都包含：

- `elementId`：触发动作的元素 ID；
- `value`：TextInput 的文本、Toggle 的小写 `true`/`false`，或 Slider 的 invariant-culture 数字。

`InvokeAsync` 中应同时验证动作和元素来源：

```csharp
if (invocation.Arguments is not { } arguments ||
    !arguments.TryGetValue("elementId", out var elementId) ||
    !arguments.TryGetValue("value", out var value))
{
    return ComponentActionResult.Failed("输入动作缺少 elementId/value。");
}
```

单行 TextInput 按 Enter 提交；多行输入保留 Enter 换行，并用 Ctrl+Enter 提交。插件可在
`ask-ai` 中读取 `value`，调用自己声明并获准的联网功能，再用 `answer` 元素的 `Text` 状态显示结果，
因此 AI 前端可以直接接受用户输入，而不必把提示词放进设置页。

关键规则：

- 第三方组件 ID 必须是 `pluginId/local-id`，且只含合法 ID 字符；同一插件内不得重复。
- 元素 ID 和动作 ID 在一个组件内唯一。按钮、菜单和表面动作只能引用已声明的动作。
- `CurrentState` 和 `StateChanged` 发布的是**完整快照**，不是增量补丁。发布后不要再修改集合。
- `ComponentStateSnapshot.Scale` 可请求当前实例相对于首选尺寸的缩放；宿主只接受有限正数，
  并按组件的最小/最大尺寸钳制。保持为 `null` 时继续使用启动器全局组件缩放。
- 状态字典中的键对应元素 ID；缺失元素使用定义中的默认值。
- 动作成功结果本身不等于状态确认。TextInput 和 Slider 的权威值写入
  `ComponentElementState.Value`，Toggle 写入 `IsChecked`；插件应更新 `CurrentState`，并在异步状态变化时
  发布 `StateChanged`。Toggle/Slider 会先显示用户值，动作完成时若 `CurrentState` 仍未确认就回到之前的
  权威值；TextInput 会保留本地草稿，直到插件发布接受或修正后的值。
- `AllowReentry: false` 是默认值；耗时动作仍应异步、可取消，不能阻塞宿主线程。
- 自定义功能可以在插件代码中获取数据并更新声明式状态，例如时钟、图片框、播放器控制、
  在线状态或可输入的 AI 前端；但当前公共 SDK 仍不支持注入任意 Avalonia `Control`，
  也没有通用音频或任意原生控件契约。

## 7. 声明式设置

清单支持以下 `kind`：`Boolean`、`Integer`、`Number`、`Text`、`MultilineText`、`Secret`、
`Choice`、`File`、`Directory`。范围 `scope` 有：

- `Global`：调用 `TryGet` / `Get` / `SetAsync` 时 `instanceId` 必须为 `null`。
- `MinecraftInstance`：必须传稳定的 `MinecraftInstanceDescriptor.InstanceId`。

```csharp
var interval = Context.Settings.Get("refresh.seconds", 30);

// 对 Global File 设置传入绝对源文件时，宿主会导入私有副本并保存相对路径。
await Context.Settings.SetAsync(
    "loading.image",
    selectedAbsolutePath,
    cancellationToken: cancellationToken);

var importedRelativePath = Context.Settings.Get("loading.image", string.Empty);
var importedImagePath = string.IsNullOrWhiteSpace(importedRelativePath)
    ? null
    : Context.Storage.GetDataPath(importedRelativePath);
```

宿主根据 `required`、类型、`minimum`、`maximum`、`step`、`maximumLength`、`pattern`、
`options` 和 `fileExtensions` 校验值，并用原子替换保存设置文件。`DefaultValue` 的 JSON 类型必须与
`kind` 匹配。设置变化通过 `Changed` 事件通知，插件停止时要解除订阅。

当前“插件列表”详情页会自动渲染和保存全局设置：

- 同时声明有限 `minimum` 和 `maximum` 的 `Integer` / `Number` 会渲染为滑块，并按 `step`
  吸附；缺少完整有限范围时回退为文本输入并继续由宿主校验。

- Global `File` 提供系统文件选择器。保存时宿主以流式方式校验并把不超过 512 MiB 的文件复制到
  `DataDirectory/settings-files/...`，不修改原文件；设置值保存为 DataDirectory 下的相对路径，
  插件用 `Context.Storage.GetDataPath(value)` 得到私有副本的绝对路径。此流程不把外部文件路径
  持续暴露给插件，因此单独使用 File 设置不要求 `user-files.read`。
  单个插件的 `settings-files`（包括事务临时文件）最多 512 个文件、总计 2 GiB，且目录树不能包含
  符号链接/reparse point。更换扩展名、清空设置或调用 `ResetAsync` 后，宿主会在新 `settings.json`
  成功落盘后尽力回收该设置键不再引用的旧 `value.*` 副本；清理失败只会留下受上述配额约束的孤儿，
  不会删除当前仍被设置引用的文件。
- Global `Directory` 不复制目录，保存的是已存在、未经过符号链接/reparse point 的绝对路径。
  只有插件已经获得 `user-files.read` 授权时目录选择器才开放；需要当前 UI 提供此能力的插件应把它
  声明为必要能力。该授权只表示读取，不包含写入权限。

实例范围设置已经可通过 SDK 读写，但尚未自动生成实例设置页面。需要当前设置页文件选择器的
`loading.image` 因而在上例中声明为 `Global`，再由实例动作把同一私有图片应用到用户选中的实例。

`Secret` 只会在当前设置界面中遮挡输入，v1 的本地 `settings.json` 不是操作系统密钥库；
不要在其中保存长期令牌、账户密码或无法撤销的密钥。

## 8. 持久修改 Minecraft 实例

持久修改必须注册 `IMinecraftInstanceExtension`，并声明
`minecraft.instance.modify`。扩展公开一个或多个用户可见动作，宿主调用动作时提供
`IMinecraftEditSession`。已启用插件的动作会自动出现在版本详情页的“插件操作”标签中；
用户选择一个可用实例后，可以查看动作来源、说明和风险标记并执行。

下面是契约签名一致的最小动作示例：

```csharp
using System.Text;
using NyaLauncher.Plugin.Abstractions.Minecraft;

internal sealed class MarkerExtension : IMinecraftInstanceExtension
{
    public string Id => "dev.example.toolbox/instance-tools";

    public IReadOnlyList<MinecraftInstanceActionDefinition> Actions =>
    [
        new()
        {
            Id = "install-marker",
            Title = "写入示例标记",
            Description = "在所选游戏目录中创建插件标记。",
            IsDestructive = false,
            ConfirmationMessage = "确认修改这个 Minecraft 实例吗？"
        }
    ];

    public async ValueTask<MinecraftInstanceActionResult> InvokeAsync(
        MinecraftInstanceActionContext context,
        CancellationToken cancellationToken)
    {
        if (context.ActionId != "install-marker")
            return MinecraftInstanceActionResult.Failed("未知动作。");

        var path = new MinecraftInstancePath(
            MinecraftPathRoot.GameDirectory,
            "nya-plugins/dev.example.toolbox/installed.txt");
        using var content = new MemoryStream(
            Encoding.UTF8.GetBytes("installed=true\n"),
            writable: false);

        await context.EditSession.WriteFileAsync(
            path,
            content,
            MinecraftFileWriteMode.CreateOrReplace,
            cancellationToken);

        // 只有显式 CommitAsync 才会把暂存修改发布到实例。
        await context.EditSession.CommitAsync(cancellationToken);
        return MinecraftInstanceActionResult.Completed("标记已写入。");
    }
}
```

在入口的 `OnStartAsync` 中注册：

```csharp
Context.Registrar.AddMinecraftInstanceExtension(new MarkerExtension());
```

事务与路径规则：

- 每个实例扩展最多声明 128 个动作；动作 ID 在该扩展内不区分大小写且不得重复，标题、说明和
  确认文案应保持简短、明确。插件启停后，“插件操作”列表会随宿主快照自动更新。
- 当前版本详情页使用所选实例构造 `MinecraftInstanceDescriptor`，调用动作时不提供自定义
  `Arguments`。需要用户参数的流程应先使用声明式设置，并在动作中校验实例 ID、版本和设置值。
- `IsDestructive == true` 或提供了 `ConfirmationMessage` 时，页面会在调用插件前显示确认窗口；
  两者都未设置的动作会直接执行。动作返回后，页面会重新扫描实例并刷新详情。
- `MinecraftDirectory` 与 `GameDirectory` 是仅有的两个可访问实例根。
- `MinecraftInstancePath.RelativePath` 必须是非空相对路径，不得越界；符号链接、junction 和
  reparse point 会被拒绝或跳过。
- 写入和删除先暂存；同一会话读取时可以看见自己的暂存状态。成功路径必须显式调用
  `CommitAsync`，未提交就释放会话只会丢弃暂存内容。
- 当前实现只操作文件，不操作目录；单文件上限为 512 MiB，单次会话最多 2048 个文件操作，
  暂存总量和提交备份总量各不超过 2 GiB。这些是宿主保护上限，不应被当作插件可长期依赖的配额。
- 提交前会检查目标是否被其他进程修改；进程仍正常运行时，失败会尝试回滚已应用文件。
  当前没有跨进程崩溃恢复 journal；如果启动器进程、操作系统或设备在提交中途崩溃，实例仍可能留下
  部分修改。这不是数据库 ACID 保证，插件必须保存可核验的安装清单/原文件副本，并提供“检查并修复”
  或“恢复”动作。
- `ConfirmationMessage` 和 `IsDestructive` 是给调用界面的提示元数据。扩展仍应验证
  `ActionId`、参数、版本、文件哈希和当前状态，不能只依赖 UI。
- 不要释放宿主传入的 `EditSession`；它的生命周期由宿主负责。

### 8.1 “用户图片替换 Minecraft 启动/加载页”的正确流程

不同 Minecraft 版本和自定义运行时没有统一的加载页文件路径，因此插件必须声明支持范围，
不能盲目覆盖某个固定文件。推荐流程如下（这里只描述能力流程，不提供完整插件）：

1. 通过 Global `loading.image` File 设置让用户选择图片。保存后读取设置中的私有相对路径，并用
   `Context.Storage.GetDataPath` 解析；宿主已经复制文件，因此无需持续读取原始用户路径。
2. 校验私有副本的图片格式、尺寸、文件大小和解码结果，拒绝伪装扩展名或不支持的版本。
   只有改用 Directory 或其他持续外部读取流程时才声明并请求 `user-files.read`。
3. 根据 `MinecraftInstanceDescriptor.VersionId`、元数据和只读实例文件选择该版本的**加载页**
   修改策略，明确区分加载页与主菜单资源。
4. 在插件私有 `DataDirectory` 保存原文件哈希和必要的可恢复副本；不要把恢复信息放进可清理缓存。
5. 在一个实例编辑会话中写入转换后的资源、版本化标记和配置；全部成功后再提交。
6. 另提供“恢复”动作，核对当前哈希后事务性恢复；发现用户或其他插件已改动时停止并提示冲突。

如果某版本必须通过自定义引导类才能替换加载画面，则把持久资源安装放在实例动作中，
把引导类的 classpath/main class 调整放在下一节的启动贡献中，不要在每次启动时永久改文件。

## 9. 每次启动的临时贡献

`IMinecraftLaunchContributor` 只修改本次 Java 启动计划，不写实例文件。注册它需要
`minecraft.launch.modify`：

```csharp
using NyaLauncher.Plugin.Abstractions.Minecraft;

internal sealed class LoaderLaunchContributor : IMinecraftLaunchContributor
{
    public string Id => "dev.example.toolbox/custom-loader-launch";

    // 数字越小越先合并；相同 Order 再按插件 ID、贡献 ID 排序。
    public int Order => 100;

    public async ValueTask<MinecraftLaunchContribution> BuildAsync(
        MinecraftLaunchContext context,
        CancellationToken cancellationToken)
    {
        var marker = new MinecraftInstancePath(
            MinecraftPathRoot.GameDirectory,
            "nya-plugins/dev.example.toolbox/loader.enabled");
        if (!await context.Files.ExistsAsync(marker, cancellationToken))
            return MinecraftLaunchContribution.Empty;

        var loaderPath = new MinecraftInstancePath(
            MinecraftPathRoot.GameDirectory,
            "nya-plugins/dev.example.toolbox/loader/custom-loader.jar");
        if (!await context.Files.ExistsAsync(loaderPath, cancellationToken))
            throw new FileNotFoundException("自有加载器 JAR 缺失，请先重新安装。");

        var loaderJar = Path.GetFullPath(Path.Combine(
            context.Instance.GameDirectory,
            "nya-plugins/dev.example.toolbox/loader/custom-loader.jar"));

        return new MinecraftLaunchContribution
        {
            // 完全自有加载器通常显式给出完整且非空的 classpath。
            ReplaceClasspath = [loaderJar],
            MainClass = "dev.example.loader.Main",
            AppendGameArguments =
            [
                "--nya-instance",
                context.Instance.InstanceId
            ],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["NYA_CUSTOM_LOADER"] = "1"
            }
        };
    }
}
```

在入口中注册：

```csharp
Context.Registrar.AddMinecraftLaunchContributor(new LoaderLaunchContributor());
```

可贡献字段包括：

- 完整 `ReplaceClasspath`，或精确 `ReplaceClasspathEntries`、`RemoveClasspath`、
  `PrependClasspath`、`AppendClasspath`；
- `MainClass`、`JavaExecutable`、`WorkingDirectory`；
- JVM 参数和游戏参数的前置/后置列表；
- 子进程环境变量；值为 `null` 表示删除该变量。

所有 classpath、Java 和工作目录路径都应是已存在的绝对路径。完整 classpath 不能最终为空。
精确替换或删除的源项必须真实存在于最终解析的基准 classpath 中，否则本次启动失败。

多个贡献按 `Order → 插件 ID → 贡献 ID` 确定性合并。不同插件对完整 classpath、main class、
Java 路径、工作目录、同一精确 classpath 项或同一环境变量给出不同值时，宿主报告冲突并中止启动，
不会静默选择一个。参数列表按顺序累加。

`CurrentPlan` 只表示此前插件已经贡献的计划。`IsClasspathReplaced == false` 时，
`CurrentPlan.Classpath` **不是已经解析完成的原版完整 classpath**；需要原版条目的插件应根据实例元数据
自行确定精确绝对路径，或使用完整替换。贡献必须快速、只读、可取消；失败会中止本次启动，
超时会隔离对应插件并要求重启启动器。

## 10. 从零实现全新规范的模组加载器

这里的“模组加载器”可以是插件作者从零定义的一套新协议和 Java 运行时，**不依赖 Forge、Fabric、
NeoForge、Quilt 或任何现有加载器，也不是这些加载器的适配器**。NyaLauncher 插件负责安装与编排；
真正的模组发现、依赖解析和类加载应由作者自己的加载器运行时完成。

这一扩展面不会绕过 NyaLauncher Core 的实例有效性检查：最终启动仍要求用户选择一个拥有可解析
基础版本元数据的 Minecraft 实例；Core 解析失败就会中止。插件可以替换 classpath/main class、
实现完全自有的模组协议与 Java 加载过程，但不能把缺失或无法解析基础版本元数据的目录直接变成
可启动实例。

推荐保持以下分层，避免把全部逻辑堆进一个插件入口文件：

1. **插件入口层**：只处理生命周期、能力检查和注册三个宿主扩展点。
2. **自有协议层**：定义自己的加载器清单、模组描述、依赖/冲突、版本范围和入口点规范；
   给协议单独版本号，不与 NyaLauncher `apiVersion` 混用。
3. **获取与验证层**：导入或下载加载器 JAR、库和模组，校验大小、SHA-256/签名、来源和兼容性；
   联网与用户文件访问分别声明相应能力。
4. **实例安装层**：通过一个明确的 `IMinecraftInstanceExtension` 动作，把运行时、锁文件和
   安装标记事务性写入命名空间目录，例如 `nya-loaders/<你的协议 ID>/`。
5. **启动计划层**：`IMinecraftLaunchContributor` 读取已提交标记，构造完整 classpath、main class、
   JVM/游戏参数和环境变量。不要在这里下载大文件或永久改实例。
6. **Java 加载器层**：由你的 main class 按自有规范扫描 JAR、解析依赖图、建立类加载隔离并调用
   自有模组入口；这部分协议完全由插件作者控制。
7. **升级与恢复层**：按实例保存已安装协议版本、文件哈希和锁文件；升级使用新事务，失败保留旧版；
   提供显式卸载/恢复动作，绝不直接删除未知文件。

一次典型流程是：用户启用插件并授权 → 在实例上执行“安装自有加载器”动作 → 插件验证并提交
加载器运行时与协议清单 → 启动贡献检测安装标记 → 启动器用贡献的完整 classpath/main class
启动你的 Java 加载器 → Java 加载器按你的规范加载用户 JAR 模组。

设计时还应处理：

- Minecraft 版本、Java 主版本、操作系统与架构兼容矩阵；
- 模组 ID 唯一性、依赖环、冲突、加载顺序和确定性锁文件；
- 远程索引签名、下载重放、哈希固定和离线模式；
- 不受信任 JAR 的风险。Java 模组同样不是天然沙箱；不要宣称仅靠类加载器即可安全隔离恶意代码；
- 与其他启动贡献的冲突提示。全量 classpath/main class 所有权应清晰，避免用户同时启用两个
  互斥的自有加载器插件。

## 11. 启停、更新、错误与兼容性

### 启用与禁用

- 插件详情页展示清单、能力、诊断和全局设置；开关操作由宿主串行执行。
- 必要能力必须全部授权后才会启动；可选能力不能成为启动前提。
- 启动失败时贡献不会部分发布。禁用成功后组件变为休眠占位，布局和数据保留。
- 如果插件没有在时限内启动/停止或仍有代码执行，宿主不会冒险强制卸载，会隔离它并要求重启。

### 更新

- 保持 `id`、设置 key、扩展 ID、组件 ID 和动作语义稳定。
- 先禁用再替换整个包；不要在运行中的 DLL 上原地覆盖部分依赖。
- 设置 schema 变更要向后兼容。读取旧值时提供回退，删除 key 前先发布迁移版本。
- 包资源属于版本，用户内容和恢复数据属于 `DataDirectory`；缓存可以随时重建。

### 错误处理

- 清单无效、入口 DLL 缺失、API 主版本不兼容或插件 ID 重复时，宿主只显示诊断，不执行代码。
- 组件定义会在发布前完整校验；一个无效定义会使本次插件启动失败，而不是发布半套 UI。
- 实例动作应返回可操作的失败信息。异常或未提交事务不得留下预期内的半成品。
- 启动贡献异常、冲突或无效路径会中止本次游戏启动；不要吞掉会造成错误启动计划的异常。

### 路径与文件安全

- 清单资源、入口程序集、插件私有路径和实例路径都必须使用对应根下的相对路径解析机制。
- 拒绝绝对路径、`..` 越界、符号链接/junction 绕过和意外目录目标。
- 外部输入先校验再写入；使用临时/事务文件，不把用户给出的文件名直接拼进目标路径。
- 下载内容固定哈希并设置大小上限；不要从插件包或网络自动执行未经验证的二进制文件。

### 版本兼容

- `manifestVersion` 描述清单格式；`apiVersion` 描述 SDK 二进制契约；插件自己的数据、加载器和模组协议
  必须各自单独版本化。
- 当前宿主只接受 manifest v1 和 API 主版本 1。设置 `minimumLauncherVersion` 可以阻止旧启动器误载。
- 插件应面向实际引用的 SDK 构建并测试，不要通过反射依赖 `NyaLauncher.Avalonia` 或
  `NyaLauncher.Core` 的内部类型；这些类型不属于插件兼容承诺。

## 12. 发布前检查清单

- [ ] 包根有一个合法 `plugin.json`，入口 DLL 和图标相对路径真实存在。
- [ ] 插件 ID 为稳定的小写反向域名，所有插件自有组件/扩展/贡献 ID 以 `pluginId/` 开头。
- [ ] 只声明实际使用的能力，可选能力拒绝后能降级。
- [ ] `StartAsync`、`StopAsync`、组件动作和启动贡献都尊重取消且不会同步阻塞。
- [ ] 所有后台任务、计时器、事件和组件实例在停止/释放时清理。
- [ ] 设置默认值与类型一致，升级旧设置经过测试；`Secret` 中没有长期敏感凭据。
- [ ] 实例修改只使用编辑会话，成功时显式提交，并有冲突、崩溃后检查/恢复和不支持版本路径。
- [ ] 启动贡献使用绝对有效路径，在多插件冲突时给出清晰错误。
- [ ] 自有模组加载器协议、安装数据和 Java 运行时各自版本化，不冒充或隐式依赖现有加载器。
- [ ] 在启用、禁用、更新、配置目录迁移、离线、取消和错误恢复场景中完成测试。
