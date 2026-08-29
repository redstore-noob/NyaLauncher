# 02 · 主题开发指南

> 本文档描述 NyaLauncher 主题系统的全部资源绑定、热重载原理，以及创建新主题的完整步骤。
> 同时作为新建主题的编写规范——**缺任何一个资源键都可能导致运行时异常**。

---

## 1. 系统总览

主题系统由 6 个部分组成：

| 组成 | 位置 | 职责 |
|------|------|------|
| `BasePalette.axaml` | `Themes/BasePalette.axaml` | **中性兜底基底**：深 / 浅两套基础颜色（背景 / 边框 / 文字 / 遮罩 / 工作区），家族缺键时回落到这里 |
| 家族强调色文件 | `Themes/{Family}_Accents.axaml` | 强调色阶梯 + Material 次色 + 语义色（不分深浅），以及家族专属的深浅背景搭配 |
| `StyleAlter` | `Themes/StyleAlter.cs` | 加载基底与家族文件 → 按明暗模式叠加 → 派生强调消费键 → 同步 Material 强调色 |
| `ThemeManager` | `Themes/ThemeManager.cs` | 热应用主题：设置明暗 + 调 `StyleAlter` + 广播 `ThemeChanged`；家族与模式都没变时跳过；「跟随系统」时对 `ColorValuesChanged` 做 300ms 防抖 |
| `ThemeSettings` | `Pages/ThemeSettings.cs` | 读写 `config.json` 里的主题家族与模式 |
| 画刷辅助类 | `Themes/ThemeBrushes.cs`、`ThemePolygonHelper.cs` | 在 C# 代码里按资源键取画刷（带兜底值） |

### 1.1 主题文件结构

```
NyaLauncher.Avalonia/Themes/
  BasePalette.axaml                中性兜底基底（深 / 浅两套）
  HatsuneMiku_Accents.axaml        初音未来（粉色系）
  DeepSeekPurple_Accents.axaml     DeepSeek 紫
  ZhiShuBlue_Accents.axaml         植树蓝
  MojangRed_Accents.axaml          Mojang 红
```

家族文件由两部分组成：

1. **直接键**（模式无关）：强调色阶梯、`SecondaryAccentColor`（Material 次色）、语义色
2. **`ThemeDictionaries`**（模式相关）：家族专属的暗色 / 浅色背景搭配（背景 / 边框 / 文字 /
   遮罩 / 工作区 + 对应 Brush），**Dark 与 Light 必须成对写出**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 直接键：强调色阶梯 / 次色 / 语义色 -->
    <Color x:Key="AccentColor">#E94196</Color>
    <!-- ... -->
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}"/>
    <!-- ... -->

    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Dark">
            <!-- 家族专属暗色背景搭配 -->
        </ResourceDictionary>
        <ResourceDictionary x:Key="Light">
            <!-- 家族专属浅色背景搭配，键必须与 Dark 完全一致 -->
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

> 加载时 `StyleAlter` 会先把 `BasePalette` 当前模式的变体复制进 `Application.Resources`，
> 再用家族文件覆盖：直接键 + 当前模式的变体字典逐条写入。因此界面上
> `{DynamicResource XxxBrush}` 能实时跟随，家族没写的键回落到中性基底。

---

## 2. 资源键全集

以下每个 Color 键都有对应的 Brush 键（`<Name>Color` → `<Name>Brush`），
Brush 一律写成 `<SolidColorBrush x:Key="XxxBrush" Color="{StaticResource XxxColor}"/>`。

### 2.1 强调色阶梯（家族文件直接键，模式无关）

主题的核心品牌色，从深到亮排列。

| Color Key | 语义 | 典型用途 |
|---|---|---|
| `AccentDeepDarkColor` | **最深强调** | 深色按钮 hover 底色、任务进度条底色；亮色模式派生文字色的取值端 |
| `AccentDeepColor` | **深强调** | 设置页装饰条（下载设置）、任务指示器底色 |
| `AccentDarkColor` | **暗强调** | 按钮 pressed 态、窗口图标底色 |
| `AccentDarkerColor` | **较暗强调** | 暗色模式主按钮底色（派生） |
| `AccentColor` | **主题主色** | 主按钮、焦点边框、滑块、进度条、Material 主色 |
| `AccentLightColor` | **亮强调** | 按钮 hover 态、Material 次色的备选 |
| `AccentBrightColor` | **最亮强调** | 任务指示器边框、高亮闪烁；暗色模式派生文字色的取值端 |

### 2.2 Material 次色（家族文件直接键）

| Color Key | 语义 |
|---|---|
| `SecondaryAccentColor` | Material 控件的次强调色（如初音粉）。缺失时退回 `AccentLightColor` → 主色 |

### 2.3 语义色（家族文件直接键，不分深浅）

| Color Key | 语义 | 典型用途 |
|---|---|---|
| `SuccessColor` | **成功/在线** | 连接状态指示灯、下载完成 |
| `ErrorColor` | **错误/危险** | 错误提示、删除确认按钮 |
| `ErrorDarkColor` | **深色错误** | 错误背景 |
| `WarningColor` | **警告** | 警告提示 |
| `InfoColor` | **信息** | 信息提示、`NyaAlert` 信息滑条 |

### 2.4 背景色（ThemeDictionaries，家族专属）

从最深到最浅，构成界面的纵深层次。

| Color Key | 语义 | 典型用途 |
|---|---|---|
| `WindowBgColor` | **最底层背景** | Window 根背景、页面背景 |
| `BaseBgColor` | **基底背景** | 顶部标题栏、底部状态栏、侧边栏底色 |
| `CardBgColor` | **卡片内部背景** | 设置页内嵌子面板（比 PanelBg 更深一层） |
| `PanelBgColor` | **面板/卡片背景** | 设置卡片、列表容器、可折叠区域 |
| `SurfaceBgColor` | **表面层背景** | 选中态标签、弹出面板、hover 展开区 |
| `HighlightBgColor` | **高亮背景** | hover 状态、行选中 |
| `ButtonBgColor` | **按钮背景** | 次要按钮、工具栏按钮（非主色调按钮） |
| `ControlBgColor` | **控件背景** | 输入框、下拉框、滑块轨道 |
| `BadgeBgColor` | **徽章/标签背景** | 版本标签、状态徽章、数字角标 |

层级关系（暗色主题）：

```
WindowBg ← 最深（几乎看不见）
  └─ BaseBg ← 标题栏/状态栏
       └─ PanelBg ← 卡片/面板
            └─ SurfaceBg ← 选中/展开
                 └─ HighlightBg ← hover 高亮
```

> **亮色主题是反过来的**：`WindowBg` 最浅，`HighlightBg` 较深。

### 2.5 边框色（ThemeDictionaries，家族专属）

从最弱到最强，控制边框的可见程度。

| Color Key | 语义 | 典型用途 |
|---|---|---|
| `SubtleBorderColor` | **最弱边框** | 卡片外框、分隔线、设置区卡片边框 |
| `DefaultBorderColor` | **默认边框** | 通用组件边框 |
| `MediumBorderColor` | **中等边框** | 输入框边框、面板分隔 |
| `StrongBorderColor` | **强调边框** | 焦点状态、激活态边框 |
| `EmphasizedBorderColor` | **最强边框** | 次要按钮边框、选中项边框 |

使用建议：

- 卡片/面板外框 → `SubtleBorderBrush`
- 输入控件默认态 → `MediumBorderBrush`
- 输入控件聚焦态 → 用 `AccentBrush` 替代
- 次要按钮 → `EmphasizedBorderBrush` + `ButtonBgBrush` 背景

### 2.6 文字色（ThemeDictionaries，家族专属）

从最醒目到最弱，控制文字的信息层级。

| Color Key | 语义 | 典型用途 |
|---|---|---|
| `PrimaryTextColor` | **最高优先级文字** | 标题、选中标签、重要数值 |
| `SecondaryTextColor` | **次要标题** | 设置项标签、卡片标题 |
| `TertiaryTextColor` | **三级文字** | 辅助说明 |
| `BodyTextColor` | **正文文字** | 段落、描述、默认 TextBlock |
| `MutedTextColor` | **弱化文字** | 标签栏未选中项、次要信息 |
| `SubtextTextColor` | **子文字** | 列表副标题、目录模式标识 |
| `HintTextColor` | **提示文字** | 输入框下方说明、卡片描述 |
| `DisabledTextColor` | **禁用文字** | 不可操作的文字 |
| `PlaceholderTextColor` | **占位符文字** | 输入框水印 |

> `AccentText` / `LinkText` 不由家族文件定义，见 [2.9 运行时派生键](#29-运行时派生键stylealter-派生)。

### 2.7 叠加层色（ThemeDictionaries，家族专属）

| Color Key | 语义 |
|---|---|
| `DockHintBgColor` | 停靠提示背景 |
| `OverlayBgColor` | 全屏蒙层背景（半透明黑色） |
| `DialogBgColor` | 对话框背景 |
| `DialogAltBgColor` | 对话框交替区域背景 |

### 2.8 工作区色（ThemeDictionaries，家族专属）

用于组件工作区的拖拽、停靠、卡片等。

| Color Key | 语义 |
|---|---|
| `CardBgColor2` | 卡片内部区域背景（比 PanelBg 更深） |
| `HeaderBgColor` | 组件头部背景 |
| `CardBorderColor` | 卡片边框 |
| `SeamIdleColor` | 停靠缝隙空闲态 |
| `DragHandleBgColor` | 拖拽手柄背景 |
| `DragHandleGlyphColor` | 拖拽手柄图标色 |
| `IconBoxBgColor` | 图标盒子背景 |
| `ComponentBgColor` | 组件条目背景 |
| `ComponentBorderColor` | 组件条目边框 |
| `ComponentHoverBgColor` | 组件条目 hover 态 |
| `SidebarBorderColor` | 侧边栏边框 |

### 2.9 运行时派生键（StyleAlter 派生）

以下键**不由家族文件声明**，由 `StyleAlter.ApplyDerivedAccentKeys` 在应用主题时按
「明暗模式 × 家族强调色」运行时派生（暗底取亮端保证可读，亮底取深端），并**同时写入
Color 与 Brush 两个键**。主题作者无需定义，只需保证强调色阶梯完整。

| 派生键 | 取值规则（Dark / Light） |
|---|---|
| `AccentTextColor`、`LinkTextColor` | 暗底取 `AccentBright`；亮底取 `AccentDeepDark` |
| `ComponentPrimaryBgColor` | 暗底取 `AccentDarker`；亮底取 `Accent` |
| `ComponentPrimaryBorderColor` | 暗底取 `AccentDark`；亮底取 `AccentLight` |
| `ComponentPrimaryHoverBgColor` | 暗底取 `AccentDark`；亮底取 `AccentBright` |
| `DragHandleActiveColor` | 暗底取 `AccentDark`；亮底取 `Accent` |
| `DropPreviewBgColor` / `SidebarDropPreviewBgColor` | 主色加固定透明度（`#38` / `#40`），深浅同值 |
| `DropPreviewBorderColor` | 暗底取 `AccentBright`；亮底取 `Accent` |
| `SidebarDropPreviewBorderColor` | 取 `AccentLight` |
| `SystemAccentColor` + `Dark1` / `Dark2` / `Light1` | 直接映射家族强调色阶梯 |

### 2.10 基础色（BasePalette 兜底）

| Color Key | 值 | 说明 |
|---|---|---|
| `TransparentColor` | `#00000000` | 完全透明，所有主题相同 |
| `WhiteColor` | `#FFFFFF` | 纯白，所有主题相同 |

### 2.11 汇总

| 分组 | 定义位置 | 数量（每模式） |
|------|----------|------|
| 强调色阶梯 | 家族文件直接键 | 7 Color + 7 Brush |
| Material 次色 | 家族文件直接键 | 1 Color |
| 语义色 | 家族文件直接键 | 5 Color + 5 Brush |
| 背景色 | 家族 ThemeDictionaries（Dark / Light 成对） | 9 Color + 9 Brush |
| 边框色 | 同上 | 5 Color + 5 Brush |
| 文字色 | 同上 | 9 Color + 9 Brush |
| 叠加层色 | 同上 | 4 Color + 4 Brush |
| 工作区色 | 同上 | 11 Color + 11 Brush |
| 运行时派生键 | `StyleAlter` 派生，**无需定义** | — |
| 基础色 | `BasePalette.axaml` 兜底，**无需定义** | — |

> 家族文件缺任何一个 ThemeDictionaries 键时会回落到 `BasePalette.axaml` 的中性值，
> 但**风格会不统一**—— Dark 与 Light 必须成对写出完整搭配。
> 最省事的做法是复制 `HatsuneMiku_Accents.axaml` 再逐项改值。

---

## 3. 颜色取值规则

### 3.1 层级规则

- **暗色主题**：`WindowBg` 最暗（约 5% 亮度），逐级递增至 `BadgeBg`（约 20% 亮度）
- **亮色主题**：`WindowBg` 最亮（约 97% 亮度），逐级递减至 `BadgeBg`（约 85% 亮度）
- **边框色**：暗色主题比对应背景亮 5–15%；亮色主题比对应背景暗 5–15%
- **文字色**：暗色主题从白到灰递减；亮色主题从黑到灰递减

### 3.2 强调色梯度

从深到亮保持 7 级梯度：

```
AccentDeepDark  →  最深（约比主色暗 25%）
AccentDeep      →  深（约比主色暗 15%）
AccentDark      →  较暗（pressed 态）
AccentDarker    →  稍暗
Accent          →  主色
AccentLight     →  稍亮（hover 态）
AccentBright    →  最亮
```

### 3.3 派生键的取值方向

强调色阶梯是**模式无关**的（同一个家族深浅模式共用一套强调色），但「强调色上的文字」
「主按钮底色」等消费键要考虑底色明暗：

- **暗底**：文字取阶梯**亮端**（`AccentBright`），按钮底色取**深一档**（更稳重）
- **亮底**：文字取阶梯**深端**（`AccentDeepDark`），按钮底色直接用主色

---

## 4. 创建新主题

### 步骤 1：新建家族文件

在 `NyaLauncher.Avalonia/Themes/` 下创建 `{Family}_Accents.axaml`。
**推荐做法**：复制 `HatsuneMiku_Accents.axaml`，改文件名，再逐项替换颜色值。

必须完成两件事：

1. 写好直接键：强调色阶梯（7 档）+ `SecondaryAccentColor` + 语义色
2. **ThemeDictionaries 成对写出**家族专属的 Dark / Light 背景搭配
   （背景 9 + 边框 5 + 文字 9 + 叠加层 4 + 工作区 11，Color 与 Brush 双键），
   键名与初音文件完全一致

### 步骤 2：在设置页注册主题卡片

在 `NyaLauncher.Avalonia/Pages/SettingsPage.axaml` 的「个性化」卡片里加一个 theme-card：

```xml
<RadioButton x:Name="ThemeCardMyFamily"
             Classes="theme-card"
             GroupName="ThemeFamily"
             Tag="MyFamily"
             Margin="0,0,10,10"
             IsCheckedChanged="OnThemeFamilyChecked">
    <!-- 卡片内容：主题预览色块与名称 -->
</RadioButton>
```

同时在 `SettingsPage.axaml.cs` 的勾选状态同步逻辑里加一行：

```csharp
ThemeCardMyFamily.IsChecked = currentFamily == "MyFamily";
```

> `Tag` 就是家族名，**必须与文件名前缀完全一致**（`MyFamily` ↔ `MyFamily_Accents.axaml`），
> 因为 `StyleAlter` 是按 `avares://NyaLauncher.Avalonia/Themes/{Family}_Accents.axaml`
> 拼 URI 去加载的。

### 步骤 3：确保资源文件参与编译

主题文件通过 `avares://` 从程序集资源加载，确认 `.csproj` 把它作为
`AvaloniaResource` 包含（现有主题文件自动包含，新增文件通常无需手动配置）。

### 步骤 4：验证

- 明暗两个变体都要切一遍，逐页检查：设置页、下载页、版本管理、主界面组件、所有弹窗
- 重点看：文字对比度、边框可见性、进度条/滑块轨道、组件 hover 与选中态、
  派生键（强调文字、主按钮、拖放预览）在深浅两种底色上的可读性
- 家族文件缺失或加载失败时，`StyleAlter` 会**自动降级到 HatsuneMiku** 并在调试输出中记录原因

---

## 5. 运行时机制

### 5.1 热重载流程

```
用户切换主题
   ↓
ThemeManager.ApplyTheme(family, mode)
   ├── 0. 家族与模式都没变 → 直接跳过（防重复刷屏）
   ├── 1. Application.RequestedThemeVariant = Dark / Light   （标准控件明暗）
   ├── 2. StyleAlter.ApplyTheme(family, mode)                （资源字典换血）
   │        ├── 复制 BasePalette.axaml 当前模式的变体 → Application.Resources
   │        ├── 家族 {Family}_Accents.axaml 直接键 + 当前模式变体覆盖基底
   │        ├── ApplyDerivedAccentKeys() 按「明暗 × 强调色」派生消费键
   │        └── SyncMaterialTheme() 同步 Material 主色 / 次色
   └── 3. ThemeChanged?.Invoke()                             （广播）
            ↓
       MainWindow.OnThemeHotReload
            ├── AmbientGradient.RecreateAll()     重建背景渐变
            └── ThemeManager.RemountRootAsync()   淡出 → 重挂载根元素 → 淡入
```

「跟随系统」模式下，`ThemeManager` 对系统的 `ColorValuesChanged` 事件做 **300ms 防抖**，
避免系统切换明暗瞬间连续触发多次重挂载。

### 5.2 为什么要重挂载根元素

资源复制进 `Application.Resources` 后，`DynamicResource` 引用会自动刷新，
但已经解析过的 `StaticResource` 不会。因此宿主重挂载窗口根元素，
强制所有 `StaticResource` 重新解析。`RemountRootAsync` 会：

1. 保存视觉状态（`ScrollViewer` 偏移等）
2. 淡出（默认 120ms）
3. `window.Content = null` → 重新赋值
4. 延迟一帧后恢复视觉状态
5. 淡入（默认 200ms）

### 5.3 Material 控件如何跟随

两层机制配合：

**第一层** —— `App.axaml` 里的静态桥接，把 Material 画刷指向主题资源键：

```xml
<SolidColorBrush x:Key="MaterialPaperBrush" Color="{DynamicResource PanelBgColor}" />
<SolidColorBrush x:Key="MaterialBodyBrush" Color="{DynamicResource PrimaryTextColor}" />
<!-- 共 20+ 项，覆盖输入框、分隔线、Snackbar、DataGrid、验证错误等 -->
```

**第二层** —— `StyleAlter.SyncMaterialTheme()` 动态注入强调色：

```csharp
var primary   = ExtractColor(family, "AccentColor") ?? Colors.Teal;
var secondary = ExtractColor(family, "SecondaryAccentColor")
             ?? ExtractColor(family, "AccentLightColor")
             ?? primary;
var theme = Theme.Create(baseTheme, primary, secondary);
materialTheme.CurrentTheme = theme;
```

> 取色**严格来自当前家族文件**（而非 `Application.Resources`），防止跨家族残留串色。

> ⚠️ **绝对不要设置 `MaterialTheme.BaseTheme` 属性**。
> 该属性任何变化都会调度内部私有主题（XAML 里的 Teal/Pink 枚举占位）
> 在 100ms 后回写 `CurrentTheme`，把注入的家族强调色覆盖掉。
> 中性画刷的明暗由 `Theme.Create` 传入的 `IBaseTheme` 自带并全量刷新。

启动期还有一个竞态：`MaterialThemeBase.OnResourcedAccessed` 会在首个控件首次查询
主题资源时用占位主题回写 `CurrentTheme`，导致"重启后 Material 控件回到默认绿"。
`StyleAlter.InjectWithStartupGuard` 通过订阅 `CurrentThemeChanged` 检测偏离并补注一次，
守卫触发的补注必然与目标一致而自然止停，不会形成循环。

---

## 6. 在 C# 代码里取色

代码构建的 UI 无法用 `{DynamicResource}`，用这两个辅助类：

| 类 | 用途 |
|---|---|
| `ThemeBrushes`（`public`） | 工作区通用画刷：`CardBackground`、`HeaderBackground`、`Accent`、`Component*`、`DragHandle*` 等 |
| `ThemePolygonHelper`（`internal`） | Polygon 组件画刷：统一绑定到**标准主题键**（`CardBgBrush`、`ComponentBgBrush`、`ErrorBrush` 等），新增主题无需为组件单独定义颜色 |

它们内部都是 `GetBrush(key, fallback)`：先从 `Application.Resources` 取，取不到就用兜底色。
因此主题切换后重新读取即可拿到新值：

```csharp
myBorder.Background = ThemeBrushes.CardBackground;
myRing.ProgressBrush = ThemeBrushes.Accent;
```

**新增画刷时**，在对应辅助类里加一个带兜底值的属性，不要直接在业务代码里 `Brush.Parse("#...")`。

---

## 7. 页面内样式类

页面可以用 `UserControl.Styles` 定义页面级样式类。因为它们全部引用
`{StaticResource ...Brush}`，新建主题**不需要修改这些样式类**，
只要保证所有 Brush 绑定存在即可。

设置页的现有样式类：

| Style Selector | 用途 |
|---|---|
| `Border.card` | 设置卡片外框（PanelBg + SubtleBorder + 圆角 14） |
| `Border.sub-card` | 内嵌子面板（CardBg + 圆角 10） |
| `Border.accent-bar` | 左侧装饰条（5px 宽、左圆角 14） |
| `TextBlock.card-title` | 卡片标题（17px SemiBold PrimaryText） |
| `TextBlock.card-subtitle` | 副标题（11px HintText） |
| `TextBlock.field-label` | 设置项标签（14px SemiBold SecondaryText） |
| `TextBlock.hint` | 提示文字（11px HintText Wrap） |

---

## 8. 常见坑

| 现象 | 原因 | 解决 |
|------|------|------|
| 切换主题后某处颜色没变 | 用了 `StaticResource` 且未参与重挂载，或硬编码了颜色 | 改用 `DynamicResource`；代码里走 `ThemeBrushes` |
| 新增家族后设置页选了没反应 | 主题卡片 `Tag` 与文件名前缀不一致 | 两者必须完全一致（见步骤 2） |
| 某个模式下界面颜色乱 / 仍是基底灰绿色 | 家族文件只写了 Dark 或只写了 Light 变体 | ThemeDictionaries **必须成对写出**，键名与初音文件完全一致 |
| 强调文字 / 主按钮 / 拖放预览颜色不对 | 想在家族文件里手写派生键 | 不要写——派生键由 `StyleAlter` 运行时生成，保证阶梯完整即可 |
| Material 控件显示成默认绿 | 误设了 `MaterialTheme.BaseTheme`，或注入早于首帧 | 只注入 `CurrentTheme`；守卫会自动补注 |
| 跨家族串色（保留了上个家族的颜色） | 从 `Application.Resources` 反查取色 | `SyncMaterialTheme` 只认当前家族字典；自定义取色也应如此 |
| 亮色主题下层次反了 | 直接把暗色值复制过去没反转 | 亮色主题 `WindowBg` 最浅、`PrimaryText` 最深 |
| 主题加载失败 | 家族文件缺失或 XML 有误 | 查看调试输出 `[StyleAlter] Failed to load theme family ...`；会自动降级到 HatsuneMiku |
