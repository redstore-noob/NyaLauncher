# NyaLauncher 主题绑定参考

> 本文档描述 NyaLauncher 主题系统中所有 Color / Brush 绑定的语义含义，同时作为新建主题的编写规范。

---

## 目录结构

```
Themes/
  HatsuneMiku_Resources.axaml   ← 初音未来主题（ResourceDictionary，含 Dark/Light 切换）
  HatsuneMiku_Dark.axaml        ← 初音未来暗色（Styles，独立完整主题文件）
  HatsuneMiku_Light.axaml       ← 初音未来亮色（Styles）
  DeepSeekPurple_Resources.axaml ← DeepSeek 紫主题（ResourceDictionary）
  DeepSeekPurple_Dark.axaml     ← DeepSeek 紫暗色（Styles）
```

每份主题文件必须定义 **全部** Color 和 Brush 绑定，缺项会导致运行时异常。

---

## 背景色（Background Colors）

从最深到最浅，构成界面的纵深层次。

| Color Key | Brush Key | 语义 | 典型用途 |
|---|---|---|---|
| `WindowBgColor` | `WindowBgBrush` | **最底层背景** | Window 根背景、页面背景 |
| `BaseBgColor` | `BaseBgBrush` | **基底背景** | 顶部标题栏、底部状态栏、侧边栏底色 |
| `CardBgColor` | `CardBgBrush` | **卡片内部背景** | 设置页内嵌子面板（比 PanelBg 更深一层） |
| `PanelBgColor` | `PanelBgBrush` | **面板/卡片背景** | 设置卡片、列表容器、可折叠区域 |
| `SurfaceBgColor` | `SurfaceBgBrush` | **表面层背景** | 选中态标签、弹出面板、hover 展开区 |
| `HighlightBgColor` | `HighlightBgBrush` | **高亮背景** | hover 状态、行选中 |
| `ButtonBgColor` | `ButtonBgBrush` | **按钮背景** | 次要按钮、工具栏按钮（非主色调按钮） |
| `ControlBgColor` | `ControlBgBrush` | **控件背景** | 输入框、下拉框、滑块轨道 |
| `BadgeBgColor` | `BadgeBgBrush` | **徽章/标签背景** | 版本标签、状态徽章、数字角标 |

### 层级关系

```
WindowBg ← 最深（几乎看不见的颜色）
  └─ BaseBg ← 标题栏/状态栏
       └─ PanelBg ← 卡片/面板
            └─ SurfaceBg ← 选中/展开
                 └─ HighlightBg ← hover 高亮
```

**亮色主题注意：** 亮色主题中这个层级关系是反的——`WindowBg` 最浅，`HighlightBg` 较深。

---

## 边框色（Border Colors）

从最弱到最强，控制边框的可见程度。

| Color Key | Brush Key | 语义 | 典型用途 |
|---|---|---|---|
| `SubtleBorderColor` | `SubtleBorderBrush` | **最弱边框** | 卡片外框、分隔线、设置区卡片边框 |
| `DefaultBorderColor` | `DefaultBorderBrush` | **默认边框** | 通用组件边框 |
| `MediumBorderColor` | `MediumBorderBrush` | **中等边框** | 输入框边框、面板分隔 |
| `StrongBorderColor` | `StrongBorderBrush` | **强调边框** | 焦点状态、激活态边框 |
| `EmphasizedBorderColor` | `EmphasizedBorderBrush` | **最强边框** | 次要按钮边框、选中项边框 |

### 使用建议

- 卡片/面板外框：`SubtleBorderBrush`
- 输入控件默认态：`MediumBorderBrush`
- 输入控件聚焦态：用 `AccentBrush` 替代
- 次要按钮边框：`EmphasizedBorderBrush` + `ButtonBgBrush` 背景

---

## 文字色（Text Colors）

从最醒目到最弱，控制文字的信息层级。

| Color Key | Brush Key | 语义 | 典型用途 |
|---|---|---|---|
| `PrimaryTextColor` | `PrimaryTextBrush` | **最高优先级文字** | 标题、选中标签、重要数值 |
| `SecondaryTextColor` | `SecondaryTextBrush` | **次要标题** | 设置项标签、卡片标题 |
| `TertiaryTextColor` | `TertiaryTextBrush` | **三级文字** | 辅助说明 |
| `LinkTextColor` | `LinkTextBrush` | **链接/可交互文字** | 超链接、可点击标签、"实例管理 ▸" 按钮 |
| `BodyTextColor` | `BodyTextBrush` | **正文文字** | 段落、描述、默认 TextBlock |
| `AccentTextColor` | `AccentTextBrush` | **强调文字** | 数值高亮（如内存值）、状态提示 |
| `MutedTextColor` | `MutedTextBrush` | **弱化文字** | 标签栏未选中项、次要信息 |
| `SubtextTextColor` | `SubtextTextBrush` | **子文字** | 列表副标题、目录模式标识 |
| `HintTextColor` | `HintTextBrush` | **提示文字** | 输入框下方说明、卡片描述 |
| `DisabledTextColor` | `DisabledTextBrush` | **禁用文字** | 不可操作的文字 |
| `PlaceholderTextColor` | `PlaceholderTextBrush` | **占位符文字** | 输入框水印 |

### 层级关系（暗色主题从亮到暗）

```
PrimaryText ← 最亮最醒目
  SecondaryText
    TertiaryText
      BodyText ← 默认正文
        MutedText
          SubtextText
            HintText ← 最暗最弱
              DisabledText
                PlaceholderText ← 几乎看不见
```

**亮色主题注意：** 亮色主题中这个关系是反的——`PrimaryText` 最深（接近黑色），`PlaceholderText` 最浅（接近灰色）。

---

## 强调色（Accent Colors）

主题的核心品牌色，从深到亮排列。

| Color Key | Brush Key | 语义 | 典型用途 |
|---|---|---|---|
| `AccentDeepDarkColor` | `AccentDeepDarkBrush` | **最深强调** | 深色按钮 hover 底色、任务进度条底色 |
| `AccentDeepColor` | `AccentDeepBrush` | **深强调** | 设置页装饰条（下载设置）、任务指示器底色 |
| `AccentDarkColor` | `AccentDarkBrush` | **暗强调** | 按钮 pressed 态、窗口图标底色 |
| `AccentDarkerColor` | `AccentDarkerBrush` | **较暗强调** | 组件主色按钮 |
| `AccentColor` | `AccentBrush` | **主题主色** | 主按钮、焦点边框、滑块、进度条、设置页装饰条（游戏设置） |
| `AccentLightColor` | `AccentLightBrush` | **亮强调** | 按钮 hover 态 |
| `AccentBrightColor` | `AccentBrightBrush` | **最亮强调** | 任务指示器边框、高亮闪烁 |

### 主题色差异

| 主题 | Accent 色调 |
|---|---|
| HatsuneMiku Dark | 青绿 `#3EC9A0` |
| HatsuneMiku Light | 粉红 `#E94196` |
| DeepSeekPurple | 紫色 `#A78BFA` |

---

## 语义色（Semantic Colors）

不随主题色调变化的固定语义色。

| Color Key | Brush Key | 语义 | 典型用途 |
|---|---|---|---|
| `SuccessColor` | `SuccessBrush` | **成功/在线** | 账户管理装饰条、连接状态指示灯、下载完成 |
| `ErrorColor` | `ErrorBrush` | **错误/危险** | 错误提示、删除确认按钮 |
| `ErrorDarkColor` | `ErrorDarkBrush` | **深色错误** | 错误背景 |
| `WarningColor` | `WarningBrush` | **警告** | 警告提示 |
| `InfoColor` | `InfoBrush` | **信息** | 信息提示 |

---

## 主题特色色（Theme-Specific Colors）

每个主题可定义独有的特色色。

| Color Key | Brush Key | 初音未来 Dark | 初音未来 Light | DeepSeek 紫 |
|---|---|---|---|---|
| `MikuTeal` | `MikuTealBrush` | `#30C0A0` 青绿 | `#39C5BB` 青绿 | — |
| `MikuPink` | `MikuPinkBrush` | `#E94196` 粉红 | `#E94196` 粉红 | — |

DeepSeek 紫主题不使用 Miku 系列色，使用自己的紫色系。

---

## 叠加层色（Overlay Colors）

用于弹窗、拖放预览、蒙层等。

| Color Key | Brush Key | 语义 |
|---|---|---|
| `OverlayBgColor` | `OverlayBgBrush` | 全屏蒙层背景（半透明黑色） |
| `DialogBgColor` | `DialogBgBrush` | 对话框背景 |
| `DialogAltBgColor` | `DialogAltBgBrush` | 对话框交替区域背景 |
| `DropPreviewBgColor` | `DropPreviewBgBrush` | 拖放预览背景（半透明强调色） |
| `DropPreviewBorderColor` | `DropPreviewBorderBrush` | 拖放预览边框 |
| `DockHintBgColor` | `DockHintBgBrush` | 停靠提示背景 |
| `SidebarDropPreviewBgColor` | `SidebarDropPreviewBgBrush` | 侧边栏拖放预览背景 |
| `SidebarDropPreviewBorderColor` | `SidebarDropPreviewBorderBrush` | 侧边栏拖放预览边框 |

---

## 工作区色（Workspace Colors）

用于组件工作区的拖拽、停靠、卡片等。

| Color Key | Brush Key | 语义 |
|---|---|---|
| `CardBgColor2` | `CardBg2Brush` | 卡片内部区域背景（比 PanelBg 更深） |
| `HeaderBgColor` | `HeaderBgBrush` | 组件头部背景 |
| `CardBorderColor` | `CardBorderBrush` | 卡片边框 |
| `SeamIdleColor` | `SeamIdleBrush` | 停靠缝隙空闲态 |
| `DragHandleBgColor` | `DragHandleBgBrush` | 拖拽手柄背景 |
| `DragHandleActiveColor` | `DragHandleActiveBrush` | 拖拽手柄激活态 |
| `DragHandleGlyphColor` | `DragHandleGlyphBrush` | 拖拽手柄图标色 |
| `IconBoxBgColor` | `IconBoxBgBrush` | 图标盒子背景 |
| `ComponentBgColor` | `ComponentBgBrush` | 组件条目背景 |
| `ComponentBorderColor` | `ComponentBorderBrush` | 组件条目边框 |
| `ComponentHoverBgColor` | `ComponentHoverBgBrush` | 组件条目 hover 态 |
| `ComponentPrimaryBgColor` | `ComponentPrimaryBgBrush` | 主要组件按钮背景 |
| `ComponentPrimaryBorderColor` | `ComponentPrimaryBorderBrush` | 主要组件按钮边框 |
| `ComponentPrimaryHoverBgColor` | `ComponentPrimaryHoverBgBrush` | 主要组件按钮 hover |
| `SidebarBorderColor` | `SidebarBorderBrush` | 侧边栏边框 |

---

## Polygon 组件色（主界面自定义组件）

主界面工作区中 Polygon 组件的专用颜色。**必须定义**，否则主界面组件会崩溃。

### 背景与表面

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonSurfaceColor` | `PolygonSurfaceBrush` | 组件表面背景（对应 SurfaceBg） |
| `PolygonSurfaceHoverColor` | — | 组件表面 hover 态 |
| `PolygonComponentCardBg` | `PolygonComponentCardBgBrush` | 组件卡片背景 |
| `PolygonComponentCardBorder` | `PolygonComponentCardBorderBrush` | 组件卡片边框 |
| `PolygonIconBoxBg` | `PolygonIconBoxBgBrush` | 图标盒子背景 |
| `PolygonEditorSurface` | `PolygonEditorSurfaceBrush` | 编辑器表面背景 |
| `PolygonCardBg` | `PolygonCardBgBrush` | 多边形卡片背景 |
| `PolygonCardBorder` | `PolygonCardBorderBrush` | 多边形卡片边框 |

### 边框与交互

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonBorderColor` | — | 组件默认边框 |
| `PolygonBorderHoverColor` | — | 组件 hover 边框（用主题强调色） |
| `PolygonEditorBorder` | `PolygonEditorBorderBrush` | 编辑器边框 |
| `PolygonProgressTrackColor` | — | 进度条轨道 |

### 拖拽

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonDragGlyph` | `PolygonDragGlyphBrush` | 拖拽手柄图标色 |
| `PolygonDragPreviewBg` | `PolygonDragPreviewBgBrush` | 拖拽预览背景（半透明强调色） |

### 启动按钮

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonLaunchSurface` | — | 启动按钮背景 |
| `PolygonLaunchSurfaceHover` | — | 启动按钮 hover |
| `PolygonLaunchBorder` | — | 启动按钮边框 |
| `PolygonLaunchBorderHover` | — | 启动按钮 hover 边框 |
| `PolygonLaunchProgressTrack` | — | 启动进度条轨道 |
| `PolygonLaunchAccentFg` | — | 启动按钮强调前景 |

### 删除操作

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonDeleteBg` | `PolygonDeleteBgBrush` | 删除按钮/区域背景 |
| `PolygonDeleteBorder` | `PolygonDeleteBorderBrush` | 删除边框（Error 色） |
| `PolygonDeleteFg` | `PolygonDeleteFgBrush` | 删除前景文字 |

### 预设与皮肤

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonPresetBg` | `PolygonPresetBgBrush` | 预设按钮背景 |
| `PolygonPresetBorder` | `PolygonPresetBorderBrush` | 预设按钮边框 |
| `PolygonSkinButtonBg` | `PolygonSkinButtonBgBrush` | 皮肤按钮背景 |
| `PolygonSkinButtonBgCurrent` | `PolygonSkinButtonBgCurrentBrush` | 当前皮肤按钮背景 |
| `PolygonSkinButtonBorder` | `PolygonSkinButtonBorderBrush` | 皮肤按钮边框 |
| `PolygonSkinButtonBorderCurrent` | `PolygonSkinButtonBorderCurrentBrush` | 当前皮肤按钮边框 |
| `PolygonSkinAvatarBg` | `PolygonSkinAvatarBgBrush` | 头像背景 |

### 任务指示器

| Color Key | Brush Key | 语义 |
|---|---|---|
| `PolygonTaskActivityDownloadingBg` | `PolygonTaskDownloadingBgBrush` | 下载中指示器背景（绿色） |
| `PolygonTaskActivityDownloadingBorder` | `PolygonTaskDownloadingBorderBrush` | 下载中指示器边框 |
| `PolygonTaskActivityLaunchingBg` | `PolygonTaskLaunchingBgBrush` | 启动中指示器背景（主题色） |
| `PolygonTaskActivityLaunchingBorder` | `PolygonTaskLaunchingBorderBrush` | 启动中指示器边框 |
| `PolygonDisabledText` | `PolygonDisabledTextBrush` | 组件禁用文字色 |

---

## 系统色覆盖（System Colors Override）

Avalonia 框架级的系统强调色，影响原生控件（如进度条、选区高亮）。

| Color Key | 典型值（Dark） | 说明 |
|---|---|---|
| `SystemAccentColor` | 与主题 Accent 相同 | 系统主强调色 |
| `SystemAccentColorDark1` | Accent 稍暗 | 系统强调色变体 |
| `SystemAccentColorDark2` | Accent 更暗 | 系统强调色变体 |
| `SystemAccentColorLight1` | Accent 稍亮 | 系统强调色变体 |

---

## 基础色

| Color Key | 值 | 说明 |
|---|---|---|
| `TransparentColor` | `#00000000` | 完全透明，所有主题相同 |
| `WhiteColor` | `#FFFFFF` | 纯白，所有主题相同 |

---

## 编写新主题的规则

### 1. 文件结构

新建一个 `.axaml` 文件，使用 `<Styles>` 根元素（适合独立切换）或 `<ResourceDictionary>`（适合明暗自动切换）。

### 2. 必须定义的绑定

以下是 **必须** 定义的完整列表（共 110+ 个 Color + Brush），缺少任何一个都会导致运行时崩溃：

- 2 个基础色（TransparentColor, WhiteColor）
- 9 个背景色 + 9 个 Brush
- 5 个边框色 + 5 个 Brush
- 11 个文字色 + 11 个 Brush
- 7 个强调色 + 7 个 Brush
- 5 个语义色 + 5 个 Brush
- 2 个特色色 + 2 个 Brush（可复用主题 Accent 色）
- 8 个叠加层色 + 8 个 Brush
- 15 个工作区色 + 15 个 Brush
- **35 个 Polygon 组件色 + 24 个 Brush**（主界面自定义组件专用，不可遗漏）
- 4 个系统色覆盖

### 3. 颜色层级规则

- **暗色主题：** `WindowBg` 最暗（~5% 亮度），逐级递增至 `BadgeBg`（~20% 亮度）
- **亮色主题：** `WindowBg` 最亮（~97% 亮度），逐级递减至 `BadgeBg`（~85% 亮度）
- **边框色：** 暗色主题比对应背景亮 5-15%；亮色主题比对应背景暗 5-15%
- **文字色：** 暗色主题从白到灰递减；亮色主题从黑到灰递减

### 4. 强调色梯度

从深到亮建议保持 7 级梯度：

```
AccentDeepDark  →  最深（约比主色暗 25%）
AccentDeep      →  深（约比主色暗 15%）
AccentDark      →  较暗（pressed 态）
AccentDarker    →  稍暗
Accent          →  主色
AccentLight     →  稍亮（hover 态）
AccentBright    →  最亮
```

### 5. 控件默认样式

主题文件底部应包含以下控件的基础样式：

```xml
<Style Selector="TextBlock">
    <Setter Property="Foreground" Value="{StaticResource BodyTextBrush}"/>
</Style>
<Style Selector="Window">
    <Setter Property="Background" Value="{StaticResource WindowBgBrush}"/>
</Style>
<Style Selector="Button">
    <Setter Property="Background" Value="{StaticResource 主题按钮色}"/>
    <Setter Property="Foreground" Value="{StaticResource PrimaryTextBrush}"/>
    <Setter Property="BorderBrush" Value="{StaticResource 主题按钮色}"/>
</Style>
<Style Selector="TextBox">
    <Setter Property="Background" Value="{StaticResource ControlBgBrush}"/>
    <Setter Property="Foreground" Value="{StaticResource PrimaryTextBrush}"/>
    <Setter Property="BorderBrush" Value="{StaticResource MediumBorderBrush}"/>
</Style>
```

### 6. 注册新主题

在 `ThemeSettings.cs` 中将主题名加入 `_knownFamilies` 字典，并在 `MainWindow.axaml.cs` 的 `App.Initialize()` 中加载对应的 ResourceDictionary 或 Styles。

---

## 页面内自定义样式（SettingsPage 示例）

设置页使用 `UserControl.Styles` 定义了页面级的样式类，新主题会自动继承这些类的颜色，因为它们全部引用 `{StaticResource ...Brush}`。

| Style Selector | 用途 |
|---|---|
| `Border.card` | 设置卡片外框（PanelBg + SubtleBorder + 圆角 14） |
| `Border.sub-card` | 内嵌子面板（CardBg + 圆角 10） |
| `Border.accent-bar` | 左侧装饰条（5px 宽、左圆角 14） |
| `TextBlock.card-title` | 卡片标题（17px SemiBold PrimaryText） |
| `TextBlock.card-subtitle` | 副标题（11px HintText） |
| `TextBlock.field-label` | 设置项标签（14px SemiBold SecondaryText） |
| `TextBlock.hint` | 提示文字（11px HintText Wrap） |

新建主题时不需要修改这些样式类——只需保证所有 Brush 绑定存在即可。
