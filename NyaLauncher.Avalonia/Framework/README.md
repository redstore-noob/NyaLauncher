# 功能区扩展

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
- 每个停靠分组的尺寸权重。

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

个性化窗口允许用户选择统一的配置目录，`workspace.json` 中保存区域设置、布局、尺寸和
侧边栏。默认目录基于 `Environment.SpecialFolder.LocalApplicationData`，由运行平台映射到
当前用户的数据目录。应用仅在 `Environment.SpecialFolder.ApplicationData/NyaLauncher`
保存一个不含个性化内容的 `workspace-location.txt`，用于下次启动时定位用户选择的目录。
仓库内开发配置使用被 Git 忽略的 `.nya-data/`。

插件提供的 `FeatureAreaAction.Id` 应在全局范围内保持唯一，才能被个性化配置稳定引用。
