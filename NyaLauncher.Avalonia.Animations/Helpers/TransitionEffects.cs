using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 声明式动效集合（全部逻辑只在本模块内实现，主工程只能用 class / 附加属性引用）。
/// 通过 App.axaml 的全局 Style 把下列 class 绑定到对应附加属性即可启用：
///   nya-lift    → HoverLift   卡片/面板悬浮时轻微抬升（translateY + 微缩放，Transitions 渲染线程驱动）
///   nya-fade    → FadeInOnLoad 首次进入可视树时淡入 + 自下方轻微上浮（Transitions 渲染线程驱动）
///   nya-spin    → Spin         持续旋转（用于加载图标 / 加载指示，计时器驱动）
///   nya-pulse   → Pulse        呼吸式脉冲放大（用于重点召唤元素，计时器驱动）
///   nya-marquee → Marquee      单行长文本横向滚动（用于标题 / 状态 / 歌名，计时器驱动）
/// 缓动曲线与时长统一取自 <see cref="MaterialMotion"/>（M3 令牌）。
/// </summary>
public static class TransitionEffects
{
    #region HoverLift（悬浮抬升）

    public static readonly AttachedProperty<bool> HoverLiftProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "HoverLift", typeof(TransitionEffects), false);

    public static void SetHoverLift(AvaloniaObject element, bool value) =>
        element.SetValue(HoverLiftProperty, value);

    public static bool GetHoverLift(AvaloniaObject element) =>
        element.GetValue(HoverLiftProperty);

    #endregion

    #region FadeInOnLoad（入场淡入上滑）

    public static readonly AttachedProperty<bool> FadeInOnLoadProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "FadeInOnLoad", typeof(TransitionEffects), false);

    public static void SetFadeInOnLoad(AvaloniaObject element, bool value) =>
        element.SetValue(FadeInOnLoadProperty, value);

    public static bool GetFadeInOnLoad(AvaloniaObject element) =>
        element.GetValue(FadeInOnLoadProperty);

    #endregion

    #region Spin（持续旋转）

    public static readonly AttachedProperty<bool> SpinProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "Spin", typeof(TransitionEffects), false);

    /// <summary>每圈毫秒数，默认 1100ms。</summary>
    public static readonly AttachedProperty<double> SpinDurationMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "SpinDurationMs", typeof(TransitionEffects), 1100.0);

    public static void SetSpin(AvaloniaObject element, bool value) =>
        element.SetValue(SpinProperty, value);

    public static bool GetSpin(AvaloniaObject element) =>
        element.GetValue(SpinProperty);

    public static void SetSpinDurationMs(AvaloniaObject element, double value) =>
        element.SetValue(SpinDurationMsProperty, value);

    public static double GetSpinDurationMs(AvaloniaObject element) =>
        element.GetValue(SpinDurationMsProperty);

    #endregion

    #region Pulse（呼吸脉冲）

    public static readonly AttachedProperty<bool> PulseProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "Pulse", typeof(TransitionEffects), false);

    /// <summary>脉冲峰值缩放，默认 1.06。</summary>
    public static readonly AttachedProperty<double> PulseScaleProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "PulseScale", typeof(TransitionEffects), 1.06);

    /// <summary>单次脉冲周期毫秒数，默认 1400ms。</summary>
    public static readonly AttachedProperty<double> PulseDurationMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "PulseDurationMs", typeof(TransitionEffects), 1400.0);

    public static void SetPulse(AvaloniaObject element, bool value) =>
        element.SetValue(PulseProperty, value);

    public static bool GetPulse(AvaloniaObject element) =>
        element.GetValue(PulseProperty);

    public static void SetPulseScale(AvaloniaObject element, double value) =>
        element.SetValue(PulseScaleProperty, value);

    public static double GetPulseScale(AvaloniaObject element) =>
        element.GetValue(PulseScaleProperty);

    public static void SetPulseDurationMs(AvaloniaObject element, double value) =>
        element.SetValue(PulseDurationMsProperty, value);

    public static double GetPulseDurationMs(AvaloniaObject element) =>
        element.GetValue(PulseDurationMsProperty);

    #endregion

    #region Marquee（跑马灯）

    public static readonly AttachedProperty<bool> MarqueeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "Marquee", typeof(TransitionEffects), false);

    /// <summary>滚动速度（像素/秒），默认 40。</summary>
    public static readonly AttachedProperty<double> MarqueeSpeedProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "MarqueeSpeed", typeof(TransitionEffects), 40.0);

    public static void SetMarquee(AvaloniaObject element, bool value) =>
        element.SetValue(MarqueeProperty, value);

    public static bool GetMarquee(AvaloniaObject element) =>
        element.GetValue(MarqueeProperty);

    public static void SetMarqueeSpeed(AvaloniaObject element, double value) =>
        element.SetValue(MarqueeSpeedProperty, value);

    public static double GetMarqueeSpeed(AvaloniaObject element) =>
        element.GetValue(MarqueeSpeedProperty);

    #endregion

    // HoverLift 与 FadeIn 各用独立的附加表：若共用同一张表，同一元素同时加
    // nya-lift + nya-fade 时，后设置的属性会被前一个「已附加」标记吞掉而不生效。
    private static readonly ConditionalWeakTable<Control, object> HoverAttached = new();
    private static readonly ConditionalWeakTable<Control, object> FadeAttached = new();

    static TransitionEffects()
    {
        HoverLiftProperty.Changed.AddClassHandler<Control>(OnHoverLiftChanged);
        FadeInOnLoadProperty.Changed.AddClassHandler<Control>(OnFadeInChanged);
        SpinProperty.Changed.AddClassHandler<Control>(OnSpinChanged);
        PulseProperty.Changed.AddClassHandler<Control>(OnPulseChanged);
        MarqueeProperty.Changed.AddClassHandler<Control>(OnMarqueeChanged);
    }

    #region 通用：等待挂载到可视树后执行一次

    private static void WhenAttached(Control control, Action run)
    {
        if (control.IsAttachedToVisualTree())
            run();
        else
        {
            void Handler(object? s, VisualTreeAttachmentEventArgs e)
            {
                control.AttachedToVisualTree -= Handler;
                run();
            }
            control.AttachedToVisualTree += Handler;
        }
    }

    #endregion

    #region HoverLift 实现（Transitions 渲染线程驱动）

    private static void OnHoverLiftChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (HoverAttached.TryGetValue(control, out _)) return;
        HoverAttached.Add(control, new object());

        control.PointerEntered += (_, _) => AnimateLift(control, true);
        control.PointerExited += (_, _) => AnimateLift(control, false);
    }

    private static void AnimateLift(Control control, bool up)
    {
        if (!AnimationGate.Enabled) return;
        var (translate, scale) = EnsureTransform(control);

        // 与 nya-pulse 共存时只做位移：脉冲由计时器持续写缩放值，
        // 若缩放也挂 Transitions 会让每帧写入都被平滑成迟滞的波形。
        var animateScale = !PulseTimers.TryGetValue(control, out _);

        // M3 short4：悬浮微交互标准时长 200ms，emphasized 标准曲线。
        // Transitions 自动以当前值为起点，快速进出时天然续播、不跳变。
        var transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = MaterialMotion.EmphasizedEasing
            }
        };
        if (animateScale)
        {
            transitions.Add(new DoubleTransition
            {
                Property = ScaleTransform.ScaleXProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = MaterialMotion.EmphasizedEasing
            });
            transitions.Add(new DoubleTransition
            {
                Property = ScaleTransform.ScaleYProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = MaterialMotion.EmphasizedEasing
            });
        }

        translate.Transitions = transitions;
        // 悬浮抬升量收敛：-6px + 1.02（原 -10px + 1.03 在大量卡片场景下过于夸张），
        // 位移轻盈、缩放若有若无，才是 M3 想要的「呼吸感」。
        translate.Y = up ? -6 : 0;
        if (animateScale)
            scale.ScaleX = scale.ScaleY = up ? 1.02 : 1;

        // 播完后摘除 Transitions，把 transform 还给可能随后启动的 pulse
        _ = ClearLiftTransitionsAsync(translate, scale, animateScale);
    }

    private static async System.Threading.Tasks.Task ClearLiftTransitionsAsync(
        TranslateTransform translate, ScaleTransform scale, bool animateScale)
    {
        await System.Threading.Tasks.Task.Delay(200);
        translate.Transitions = null;
        if (animateScale)
            scale.Transitions = null;
    }

    #endregion

    #region FadeInOnLoad 实现（Transitions 渲染线程驱动）

    private static void OnFadeInChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (FadeAttached.TryGetValue(control, out _)) return;
        FadeAttached.Add(control, new object());

        WhenAttached(control, () =>
        {
            // 动画总开关关闭时不播淡入，控件保持默认可见
            if (!AnimationGate.Enabled)
                return;

            control.Transitions = null;
            control.Opacity = 0;
            var translate = new TranslateTransform(0, 18);
            control.RenderTransform = translate;
            _ = RunFadeInAsync(control, translate);
        });
    }

    private static async System.Threading.Tasks.Task RunFadeInAsync(Control control, TranslateTransform translate)
    {
        const int durationMs = MaterialMotion.MediumTransitionMs;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        translate.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Easing = MaterialMotion.EmphasizedDecelerateEasing
            }
        };
        control.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs * MaterialMotion.FadeEndFraction),
                Easing = MaterialMotion.LinearEasing
            }
        };
        control.Opacity = 1;
        translate.Y = 0;

        await System.Threading.Tasks.Task.Delay(durationMs);
        control.Transitions = null;
        control.RenderTransform = null;
    }

    #endregion

    #region Spin 实现

    private static readonly ConditionalWeakTable<Control, DispatcherTimer> SpinTimers = new();

    private static void OnSpinChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            WhenAttached(control, () => StartSpin(control));
            control.DetachedFromVisualTree += StopSpinHandler;
        }
        else
        {
            StopSpin(control);
        }
    }

    private static void StopSpinHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        if (s is Control c)
        {
            c.DetachedFromVisualTree -= StopSpinHandler;
            StopSpin(c);
        }
    }

    private static void StartSpin(Control control)
    {
        if (SpinTimers.TryGetValue(control, out _)) return;
        var rotate = new RotateTransform(0);
        control.RenderTransform = rotate;
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        var durationMs = Math.Max(200, GetSpinDurationMs(control));
        var last = DateTime.Now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // 动画总开关关闭时暂停旋转（角度冻结无视觉副作用）
            if (!AnimationGate.Enabled) return;
            var now = DateTime.Now;
            var dt = (now - last).TotalMilliseconds;
            last = now;
            // 控件不可见时停走角度（避免遮罩隐藏后空转 CPU），恢复可见时平滑续转
            if (!control.IsVisible) return;
            rotate.Angle = (rotate.Angle + dt * 360.0 / durationMs) % 360.0;
        };
        SpinTimers.Add(control, timer);
        timer.Start();
    }

    private static void StopSpin(Control control)
    {
        if (SpinTimers.TryGetValue(control, out var timer))
        {
            timer.Stop();
            SpinTimers.Remove(control);
        }
        if (control.RenderTransform is RotateTransform)
            control.RenderTransform = null;
    }

    #endregion

    #region Pulse 实现

    private static readonly ConditionalWeakTable<Control, DispatcherTimer> PulseTimers = new();

    private static void OnPulseChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            WhenAttached(control, () => StartPulse(control));
            control.DetachedFromVisualTree += StopPulseHandler;
        }
        else
        {
            StopPulse(control);
        }
    }

    private static void StopPulseHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        if (s is Control c)
        {
            c.DetachedFromVisualTree -= StopPulseHandler;
            StopPulse(c);
        }
    }

    private static void StartPulse(Control control)
    {
        if (PulseTimers.TryGetValue(control, out _)) return;
        // 复用 EnsureTransform 的共享 TransformGroup：与 nya-lift 共存时不会互相顶掉 RenderTransform
        var (_, scale) = EnsureTransform(control);
        // 脉冲由计时器持续写缩放值，接管前摘除残留的 lift 缩放过渡
        scale.Transitions = null;
        var peak = Math.Max(1.01, GetPulseScale(control));
        var durationMs = Math.Max(400, GetPulseDurationMs(control));
        var last = DateTime.Now;
        var elapsed = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // 动画总开关关闭时暂停脉冲并归位缩放，避免元素卡在半大
            if (!AnimationGate.Enabled)
            {
                scale.ScaleX = scale.ScaleY = 1;
                return;
            }
            var now = DateTime.Now;
            var dt = (now - last).TotalMilliseconds;
            last = now;
            elapsed += dt;
            var phase = (elapsed % durationMs) / durationMs; // 0..1
            var s = 1 + (peak - 1) * Math.Sin(phase * Math.PI);
            scale.ScaleX = scale.ScaleY = s;
        };
        PulseTimers.Add(control, timer);
        timer.Start();
    }

    private static void StopPulse(Control control)
    {
        if (PulseTimers.TryGetValue(control, out var timer))
        {
            timer.Stop();
            PulseTimers.Remove(control);
        }
        if (control.RenderTransform is ScaleTransform st &&
            Math.Abs(st.ScaleX - 1) < 0.001 && Math.Abs(st.ScaleY - 1) < 0.001)
        {
            control.RenderTransform = null;
        }
    }

    #endregion

    #region Marquee 实现

    private sealed class MarqueeState
    {
        public TranslateTransform Translate { get; } = new();
        public double FullWidth;
        public double VisibleWidth;
        public double Distance;
        public double Speed;
    }

    private static readonly ConditionalWeakTable<Control, DispatcherTimer> MarqueeTimers = new();

    private static void OnMarqueeChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            WhenAttached(control, () => StartMarquee(control));
            control.DetachedFromVisualTree += StopMarqueeHandler;
        }
        else
        {
            StopMarquee(control);
        }
    }

    private static void StopMarqueeHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        if (s is Control c)
        {
            c.DetachedFromVisualTree -= StopMarqueeHandler;
            StopMarquee(c);
        }
    }

    private static void StartMarquee(Control control)
    {
        if (MarqueeTimers.TryGetValue(control, out _)) return;
        if (control is not TextBlock textBlock) return;

        var state = new MarqueeState();
        control.RenderTransform = state.Translate;
        control.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        control.ClipToBounds = true;

        void Measure()
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            state.FullWidth = textBlock.DesiredSize.Width;
            // 可见视口宽度：TextBlock 自身布局宽（受限容器里已被压到列宽）；
            // 若处于 StackPanel 等不约束子元素宽度的容器里，自身 Bounds 等于文本全宽，
            // 此时以父容器宽度为视口，否则 Distance 恒为 0、长文本永不滚动。
            var own = control.Bounds.Width;
            var parent = control.GetVisualParent() as Control;
            var viewW = parent is not null && parent.Bounds.Width > 0
                ? Math.Min(own, parent.Bounds.Width)
                : own;
            state.VisibleWidth = viewW;
            state.Distance = Math.Max(0, state.FullWidth - viewW);
            state.Speed = Math.Max(8, GetMarqueeSpeed(control));
        }

        Measure();
        textBlock.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty) Measure();
        };
        control.SizeChanged += (_, _) => Measure();

        var last = DateTime.Now;
        var offset = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // 动画总开关关闭时暂停跑马灯并归位
            if (!AnimationGate.Enabled)
            {
                state.Translate.X = 0;
                return;
            }
            if (state.Distance <= 1)
            {
                state.Translate.X = 0;
                return;
            }
            var now = DateTime.Now;
            var dt = (now - last).TotalMilliseconds;
            last = now;
            offset += dt * state.Speed / 1000.0;
            if (offset > state.Distance + state.VisibleWidth) offset = 0;
            state.Translate.X = -Math.Min(offset, state.Distance);
        };
        MarqueeTimers.Add(control, timer);
        timer.Start();
    }

    private static void StopMarquee(Control control)
    {
        if (MarqueeTimers.TryGetValue(control, out var timer))
        {
            timer.Stop();
            MarqueeTimers.Remove(control);
        }
        if (control.RenderTransform is TranslateTransform)
            control.RenderTransform = null;
    }

    #endregion

    #region 共享：RenderTransform 管理

    private static (TranslateTransform, ScaleTransform) EnsureTransform(Control control)
    {
        if (control.RenderTransform is TransformGroup group)
        {
            var tt = Find<TranslateTransform>(group);
            var st = Find<ScaleTransform>(group);
            if (tt is null)
            {
                tt = new TranslateTransform(0, 0);
                group.Children.Add(tt);
            }
            if (st is null)
            {
                st = new ScaleTransform(1, 1);
                group.Children.Add(st);
            }
            return (tt, st);
        }

        var translate = new TranslateTransform(0, 0);
        var scale = new ScaleTransform(1, 1);
        control.RenderTransform = new TransformGroup { Children = { translate, scale } };
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        return (translate, scale);
    }

    private static T? Find<T>(TransformGroup group) where T : Transform
    {
        foreach (var child in group.Children)
            if (child is T t) return t;
        return null;
    }

    #endregion
}
