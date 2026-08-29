using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 磁性弹簧按钮（class="nya-magnetic"）：hover 时控件朝鼠标方向被「吸」过去一点（最大 <see cref="MaxOffsetProperty"/>），
/// 离开时平滑回弹。位移走共享 TransformGroup 的 Translate（与 nya-pulse 等共用 RenderTransform，不会互相顶掉）。
/// 全部逻辑只在本模块。
/// </summary>
public static class Magnetic
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Magnetic), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>最大偏移（像素），默认 8。</summary>
    public static readonly AttachedProperty<double> MaxOffsetProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("MaxOffset", typeof(Magnetic), 8.0);

    public static void SetMaxOffset(AvaloniaObject element, double value) =>
        element.SetValue(MaxOffsetProperty, value);

    public static double GetMaxOffset(AvaloniaObject element) =>
        element.GetValue(MaxOffsetProperty);

    /// <summary>吸附强度（越大越跟手），默认 0.12（配合平滑跟随）。</summary>
    public static readonly AttachedProperty<double> StrengthProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Strength", typeof(Magnetic), 0.12);

    public static void SetStrength(AvaloniaObject element, double value) =>
        element.SetValue(StrengthProperty, value);

    public static double GetStrength(AvaloniaObject element) =>
        element.GetValue(StrengthProperty);

    private sealed class MagneticState
    {
        public TranslateTransform Translate { get; } = new();
        public double TargetX, TargetY;
        public DispatcherTimer? Timer;
    }

    private static readonly ConditionalWeakTable<Control, MagneticState> States = new();

    static Magnetic()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (States.TryGetValue(control, out _)) return;

        var state = new MagneticState();
        States.Add(control, state);

        // 把 translate 放进共享 TransformGroup（保留已有 scale，如 Pulse）
        EnsureTranslate(control, state.Translate);

        control.PointerEntered += (_, _) => EnsureTimer(control, state);
        control.PointerExited += (_, _) => { state.TargetX = 0; state.TargetY = 0; };
        control.PointerMoved += (_, ev) => UpdateTarget(control, state, ev);
    }

    private static void UpdateTarget(Control control, MagneticState state, PointerEventArgs ev)
    {
        var pos = ev.GetPosition(control);
        var c = control.Bounds;
        if (c.Width <= 0 || c.Height <= 0) return;
        var max = Math.Max(0.5, GetMaxOffset(control));
        var strength = Math.Clamp(GetStrength(control), 0.01, 1.0);
        var cx = c.X + c.Width / 2;
        var cy = c.Y + c.Height / 2;
        state.TargetX = Math.Clamp((pos.X - cx) * strength, -max, max);
        state.TargetY = Math.Clamp((pos.Y - cy) * strength, -max, max);
    }

    private static void EnsureTimer(Control control, MagneticState state)
    {
        if (state.Timer is not null) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // 动画总开关关闭时归位并停止吸附
            if (!AnimationGate.Enabled)
            {
                state.Translate.X = 0;
                state.Translate.Y = 0;
                state.TargetX = 0;
                state.TargetY = 0;
                if (timer == state.Timer)
                {
                    timer.Stop();
                    state.Timer = null;
                }
                return;
            }

            // 平滑跟随 + 回弹（lerp）
            state.Translate.X += (state.TargetX - state.Translate.X) * 0.28;
            state.Translate.Y += (state.TargetY - state.Translate.Y) * 0.28;
            if (Math.Abs(state.TargetX) < 0.01 && Math.Abs(state.TargetY) < 0.01 &&
                Math.Abs(state.Translate.X) < 0.05 && Math.Abs(state.Translate.Y) < 0.05)
            {
                state.Translate.X = 0;
                state.Translate.Y = 0;
                timer.Stop();
                state.Timer = null;
                return;
            }
            if (!control.IsPointerOver)
            {
                state.TargetX = 0;
                state.TargetY = 0;
            }
        };
        state.Timer = timer;
        timer.Start();
    }

    private static void EnsureTranslate(Control control, TranslateTransform translate)
    {
        if (control.RenderTransform is TransformGroup group)
        {
            // 已有 TransformGroup（如 Pulse 创建的）：把我们的 translate 加进去，保留现有 scale
            foreach (var child in group.Children)
                if (ReferenceEquals(child, translate))
                    return;
            group.Children.Add(translate);
            return;
        }

        // 无 TransformGroup：保留已有变换（如单独 ScaleTransform），一起放进新组
        var existingTransform = control.RenderTransform;
        var newGroup = new TransformGroup();
        if (existingTransform is Transform existing)
            newGroup.Children.Add(existing);
        newGroup.Children.Add(translate);
        control.RenderTransform = newGroup;
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
    }
}
