# 04 · 动画系统指南

> 本文档描述 NyaLauncher 的动效体系：**声明式 class 触发**、**编程式 helper**、
> 以及**编写新动画时必须遵守的模块化约定**。

---

## 1. 核心原则

### 1.1 动画模块化（硬性规则）

所有动画实现——附加属性、行为、helper、计时器——**必须**写在
`NyaLauncher.Avalonia.Animations/Helpers/` 下。

主工程（`NyaLauncher.Avalonia`）的页面与控件 `.cs` 里**禁止**直接写动画循环或
`RenderTransform` 逻辑。消费动画只有三条合法路径：

| 路径 | 用法 | 适用 |
|------|------|------|
| 全局 Style 绑定附加属性 | `App.axaml` 里给控件类型挂上 | 全局统一行为（如所有 Button 的回弹） |
| 给元素加 `nya-*` class | XAML 里 `Classes="nya-lift"` | 单个元素的声明式动效 |
| 调用静态 helper | `await AnimationHelper.SlideFadeInAsync(page)` | 代码控制时机的转场 |

这条规则让动效可以被统一关停、统一调参，也避免动画逻辑散落在几十个页面里。

### 1.2 统一开关 `AnimationGate`

```csharp
AnimationGate.Enabled   // 全局动画总开关
```

- **默认 `true`，动画永远开启**：设置页的「动画效果」开关已移除，
  `config.json` 里的旧键 `animationsEnabled` / `closeAnimations` 不再有人读取
- 该开关保留为**代码级闸门**：所有 helper 内部仍会先检查它，
  关闭时直接跳过动画、不改变视觉终态

所以你写新动画时，第一件事也应该是 `if (!AnimationGate.Enabled) return;`
——即使当前没有入口能关掉它，这也保证动效体系始终可以被统一关停。

另有两项独立的视觉开关，同样在启动时应用：

| 开关 | 配置项 | 默认 |
|------|--------|------|
| `AmbientGradient.AmbientGradientEnabled` | `ambientGradient`（彩虹背景） | 开 |
| `SparkleTrail.SparkleTrailEnabled` | `sparkleTrail`（星尘特效） | 开 |

### 1.3 基于 Transitions，而非逐帧循环

所有动效基于 Avalonia 的 `Transitions`（渲染线程驱动），**不要**用 UI 线程
`for` 循环逐帧改属性。时长与缓动统一取自 `MaterialMotion`（Material Design 3 令牌）：

| 令牌 | 值 | 用途 |
|------|-----|------|
| `MaterialMotion.MediumTransitionMs` | 300 | 常规过渡 |
| `MaterialMotion.LargeTransitionMs` | 400 | 大面积 / 窗口级过渡 |
| `MaterialMotion.FadeEndFraction` | 0.4 | 入场：不透明度在前 40% 完成 |
| `MaterialMotion.FadeEndFractionExit` | 0.3 | 出场：不透明度在前 30% 消失 |
| `LinearEasing` | — | 透明度用匀速 |
| `EmphasizedEasing` | — | 通用强调曲线 |
| `EmphasizedDecelerateEasing` | — | 入场（先快后慢） |
| `EmphasizedAccelerateEasing` | — | 出场（先慢后快） |

---

## 2. 声明式动效：`nya-*` class 速查

在 `App.axaml` 中已为每个 class 配好 Style，XAML 里加 `Classes` 即可生效：

```xml
<Border Classes="card nya-lift">
<TextBlock Classes="nya-marquee" Text="..." />
```

| Class | 适用的控件 | 附加属性 | 效果 |
|-------|-----------|-----------|------|
| `nya-lift` | 任意 `Control` | `TransitionEffects.HoverLift` | 卡片悬浮抬升 |
| `nya-fade` | 任意 `Control` | `TransitionEffects.FadeInOnLoad` | 加载时淡入 |
| `nya-spin` | 任意 `Control` | `TransitionEffects.Spin` | 持续旋转 |
| `nya-pulse` | 任意 `Control` | `TransitionEffects.Pulse` | 脉冲缩放 |
| `nya-marquee` | `TextBlock` | `TransitionEffects.Marquee` | 文字跑马灯（自动 `ClipToBounds`） |
| `nya-ripple` | 任意 `Control` | `Ripple.Enabled` | 点击水波纹 |
| `nya-shimmer` | 任意 `Control` | `Shimmer.Enabled` | 斜向掠光带循环扫过 |
| `nya-ambient` | 任意 `Control` | `AmbientGradient.Enabled` | 底层渐变缓慢旋转（氛围背景） |
| `nya-sparkles` | 任意 `Control` | `SparkleTrail.Enabled` | 指针划过飘小星星 |
| `nya-stagger` | `ItemsControl` | `Stagger.Enabled` | 列表项依次错峰滑入 |
| `nya-flip` | 任意 `Control` | `Flip.Enabled` | hover 绕竖轴翻转（最后一个子元素为背面） |
| `nya-magnetic` | 任意 `Control` | `Magnetic.Enabled` | 朝鼠标吸附微移，离开回弹 |
| `nya-typewriter` | `TextBlock` | `Typewriter.Enabled` | 挂载后逐字打出 |

### 2.1 全局自动挂载（无需加 class）

`App.axaml` 已经为这些控件类型全局启用，任何位置创建的实例都会自动生效：

| 选择器 | 附加属性 | 效果 |
|--------|-----------|------|
| `Button` | `GlobalAnimation.Enable` | hover 微放大 + 点击弹性回弹 |
| `ToggleButton` | `GlobalAnimation.Enable` | hover 微放大（**不挂点击回弹**，见下方说明） |
| `ComboBox` | `GlobalAnimation.Enable` | 下拉弹出动画 |
| `Button` | `Ripple.Enabled` | 点击水波纹（ScrollBar / Popup 内部按钮自动跳过） |

> 为什么 `ToggleButton` 不挂点击回弹？按下缩小会让命中区域变小，快速点击时
> 抬起事件可能落在控件外，导致开关状态偶发不切换；而且 Material 自带 ripple 反馈。
> `ToggleButton` 是 `Button` 的子类，`CheckBox` / `RadioButton` 也走这条分支。

### 2.2 可调参数

需要微调时直接在 XAML 上设附加属性：

```xml
<!-- 转慢一点的旋转 -->
<Border Classes="nya-spin" helpers:TransitionEffects.SpinDurationMs="3000" />

<!-- 脉冲幅度更大 -->
<Border Classes="nya-pulse" helpers:TransitionEffects.PulseScale="1.08"
        helpers:TransitionEffects.PulseDurationMs="900" />

<!-- 跑马灯速度 -->
<TextBlock Classes="nya-marquee" helpers:TransitionEffects.MarqueeSpeed="60" />

<!-- 磁性吸附强度 -->
<Button Classes="nya-magnetic" helpers:Magnetic.MaxOffset="12" helpers:Magnetic.Strength="0.35" />

<!-- 掠光强度与周期 -->
<Border Classes="nya-shimmer" helpers:Shimmer.DurationMs="2200" helpers:Shimmer.Intensity="0.5" />

<!-- 背景渐变周期 -->
<Grid Classes="nya-ambient" helpers:AmbientGradient.DurationMs="12000" />

<!-- 打字机逐字间隔 -->
<TextBlock Classes="nya-typewriter" helpers:Typewriter.DelayMs="45" />

<!-- 列表级联间隔 -->
<ItemsControl Classes="nya-stagger" helpers:Stagger.DelayMs="60" />

<!-- 翻转：手动控制正反面 -->
<Border Classes="nya-flip" helpers:Flip.IsBack="True" helpers:Flip.DurationMs="600" />
```

需要 `helpers` 命名空间：

```xml
xmlns:helpers="using:NyaLauncher.Avalonia.Animations.Helpers"
```

---

## 3. 编程式动效：Helper API

### 3.1 `AnimationHelper` —— 通用动效

```csharp
using NyaLauncher.Avalonia.Animations.Helpers;
```

| 方法 | 说明 |
|------|------|
| `BounceAsync(Control, double scaleUp = 1.06, int durationMs = 300)` | Q 弹回弹（放大 → 回缩 → 归位） |
| `PressAsync(Control, int durationMs = 120)` | 按下轻压到 0.97 |
| `ReleaseAsync(Control, int durationMs = 240)` | 释放回位（微过冲 1.01 → 1.0） |
| `HoverInAsync(Control, int durationMs = 200, double hoverScale = 1.02)` | 悬浮放大 |
| `HoverOutAsync(Control, int durationMs = 200)` | 悬浮还原 |
| `FadeInAsync(Visual, int durationMs = 300)` | 纯淡入 |
| `SlideFadeInAsync(Visual, int durationMs = 300, double slideOffset = 24)` | 页面入场：淡入 + 自下方上浮 |
| `SlideFadeOutAsync(Visual, int durationMs = 180, double slideOffset = 16)` | 页面退场：淡出 + 下沉 |
| `StaggerInAsync(IEnumerable<Control>, int perItemDelayMs = 45, int durationMs = 300, double slideOffset = 18)` | 列表错峰入场 |

```csharp
// 页面切换：先淡出旧的，再淡入新的
await AnimationHelper.SlideFadeOutAsync(oldPage);
await AnimationHelper.SlideFadeInAsync(newPage);
```

> **微交互幅度约定**：按压 **0.97**、松开微过冲 **1.01**、悬浮缩放 **1.02**、
> Bounce 默认 **1.06**、HoverLift **-6px + 1.02**——这是刻意收敛后的克制轻盈值，
> 新代码请沿用同一量级，勿调回夸张幅度（0.92 / 1.05 / 1.12 / -10px 均已废弃）。
> 列表错峰入场（`StaggerInAsync` 45ms/项）有总延迟封顶
> `MaterialMotion.MaxStaggerTotalDelayMs`（360ms），长列表会自动压缩逐项间隔。

> **`SlideFadeOutAsync` 结束后会保留 `Opacity = 0` 与下沉位移**，
> 由下一次入场或调用方复位。缓存页面复用前必须恢复 `Opacity = 1`。

### 3.2 `BounceBehavior` —— 手动挂载交互反馈

全局 Style 已经覆盖大部分场景，代码动态创建的控件可手动挂：

| 方法 | 说明 |
|------|------|
| `AttachBounce(Button)` | hover 放大 + 按下回缩 + 点击 Q 弹（全套） |
| `AttachHoverScale(Control, double hoverScale = 1.02)` | 仅 hover 放大 |
| `AttachClickBounce(Control)` | 仅按下/释放回弹 |
| `AttachDropDownAnimation(ComboBox)` | 下拉面板展开动画 |

### 3.3 `WindowEffects` —— 窗口级动效

| 方法 | 说明 |
|------|------|
| `Enter(Window window, int durationMs = LargeTransitionMs)` | 窗口入场「飞出来」 |
| `Exit(Window window, Action? onCompleted = null)` | 窗口退场「飞走」，播完回调 |
| `Minimize(Window window, Action? onCompleted = null)` | 最小化动效 |
| `Maximize(Window window)` | 最大化动效 |
| `Restore(Window window, bool fromMinimized = false)` | 还原动效 |

关闭窗口的正确姿势是**先播退场，播完再真正关闭**：

```csharp
WindowEffects.Exit(this, onCompleted: () => base.OnClosing(e));
```

### 3.4 `OverlayEffects` —— 遮罩层

| 成员 | 说明 |
|------|------|
| 附加属性 `PopIn` | XAML 里设 `True` 即可让遮罩内容弹入 |
| `PopOut(Control host, Action? onCompleted = null)` | 弹出层退场，播完回调 |

```xml
<Border helpers:OverlayEffects.PopIn="True"> ... </Border>
```

### 3.5 其他编程式入口

| 类型 / 方法 | 说明 |
|-------------|------|
| `SwapTransition.SwapVertical(newControl, oldControl, int durationMs = 280)` | 两个控件纵向交换的转场 |
| `Stagger.Play(ItemsControl host)` | 立即重播一次列表级联入场 |
| `Shake.Trigger(Control control, int intensity = 7)` | 抖动（适合表示输入错误） |
| `AmbientGradient.Enable(Control host)` | 代码启用背景渐变 |
| `AmbientGradient.RefreshGlobal()` / `RecreateAll()` | 开关变化后刷新 / 重建所有渐变 |
| `SparkleTrail.Enable(Control host)` / `SparkleTrail.RefreshGlobal()` | 同上，星尘特效 |
| `RippleBehavior.AttachRipple(Control control, Canvas layer)` | 把水波纹画到指定图层 |
| `RippleBehavior.GlobalRippleLayer` | 全局水波纹图层 |
| `RingProgressControl` | 环形进度控件，属性 `Value` / `Thickness` / `TrackBrush` / `ProgressBrush` |

---

## 4. 编写新动画的规范

要在 `Animations/Helpers/` 下新增一种动效时，按这个套路来：

### 4.1 文件与命名

- 文件放 `NyaLauncher.Avalonia.Animations/Helpers/` 下，一个效果一个文件
- 对外暴露 **附加属性**（供 XAML 声明式使用）或 **静态异步方法**（供代码调用）
- 若同时需要两者，附加属性负责"挂载监听"，helper 方法负责"播一次"

### 4.2 必须检查总开关

```csharp
public static async Task MyEffectAsync(Control target)
{
    if (!AnimationGate.Enabled) return;      // ← 第一道门
    // ...
}
```

### 4.3 用 Transitions，不要逐帧循环

```csharp
// ✅ 正确
target.Transitions = new Transitions
{
    new DoubleTransition { Property = Visual.OpacityProperty,
                           Duration = TimeSpan.FromMilliseconds(300),
                           Easing = MaterialMotion.LinearEasing }
};
target.Opacity = 1;
await Task.Delay(300);
target.Transitions = null;

// ❌ 错误：UI 线程逐帧循环
for (int i = 0; i <= 30; i++) { target.Opacity = i / 30.0; await Task.Delay(10); }
```

### 4.4 处理快速连续触发（代数计数器）

用户疯狂划过或连点时，旧动画的收尾清理会打断新动画。
`AnimationHelper` 用代数计数器解决：每次启动新一轮动画领取一个递增代号，
清理前先确认自己仍是最新一代。

```csharp
var generation = NextGeneration(control);   // ConditionalWeakTable<Control, Box>
// ... 播放动画 ...
await Task.Delay(durationMs);
if (IsStale(control, generation)) return;   // 已被更新的动画接管，不清理
control.Transitions = null;
```

新动画请沿用同样的模式（这两个私有 helper 在 `AnimationHelper` 内，可按需提取复用）。

### 4.5 需要初始状态先渲染一帧

设置"初始隐藏/位移"后直接改终值，Avalonia 可能合并成一次渲染而不播动画。
中间插一次低优先级布局：

```csharp
await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
```

`AnimationHelper.FlushAsync()` 就是这个作用。

### 4.6 在 `App.axaml` 注册 class

新增的声明式动效要在 `App.axaml` 里加一条 Style，才能用 `nya-*` class 触发：

```xml
<Style Selector="Control.nya-myeffect">
  <Setter Property="(helpers:MyEffect.Enabled)" Value="True" />
</Style>
```

同时记得在 [本文档第 2 节的 class 表](#2-声明式动效nya--class-速查) 里补一行。

---

## 5. 与主题的配合

- 主题切换时，`ThemeManager.ThemeChanged` 会触发 `MainWindow` 重挂载根元素（先淡出、再重挂载、再淡入）
- 需要重建的视觉效果（背景渐变、星尘）要在热重载回调里显式刷新：

```csharp
ThemeManager.ThemeChanged += () =>
{
    AmbientGradient.RecreateAll();
    _ = ThemeManager.RemountRootAsync(this);
};
```

- 动画颜色不要硬编码，用 `{DynamicResource XxxBrush}`，主题热重载才能实时跟随

---

## 6. 常见坑

| 现象 | 原因 | 解决 |
|------|------|------|
| 动画设置后直接跳变，没有过渡 | 初始状态没渲染一帧就被覆盖 | 用 `FlushAsync()` 插一次低优先级布局 |
| 快速连点后控件卡在放大状态 | 旧动画收尾时清掉了新动画的 Transitions | 用代数计数器，过期的一代不做清理 |
| 关闭动画开关后控件停在半透明 | helper 里没检查 `AnimationGate` 就把 `Opacity` 改了 | 闸门关闭时**直接 return，不改任何属性**（当前恒开，但检查逻辑必须保留） |
| 页面复用后一片空白 | `SlideFadeOutAsync` 保留了 `Opacity = 0` | 复用前手动恢复 `Opacity = 1` |
| `ToggleButton` 偶发点不动 | 挂了点击回弹导致抬起事件落在控件外 | 开关类控件只挂 hover，不挂回弹 |
| 切换主题后自定义动画颜色不跟随 | 硬编码了颜色 | 改用 `DynamicResource` 引用主题画刷 |
