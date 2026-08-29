using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 光影流转：在控件之上注入一层斜向掠光带，循环扫过，营造"流光溢彩"的质感。
/// 自带覆盖层（裁剪到控件范围），不依赖任何全局 Canvas；主工程只要加 class="nya-shimmer"。
/// 全部逻辑只在本模块。
/// </summary>
public static class Shimmer
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Shimmer), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>单次掠过周期（毫秒），默认 2600。</summary>
    public static readonly AttachedProperty<double> DurationMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("DurationMs", typeof(Shimmer), 2600.0);

    public static void SetDurationMs(AvaloniaObject element, double value) =>
        element.SetValue(DurationMsProperty, value);

    public static double GetDurationMs(AvaloniaObject element) =>
        element.GetValue(DurationMsProperty);

    /// <summary>掠光带最大不透明度，默认 0.35（0~1）。</summary>
    public static readonly AttachedProperty<double> IntensityProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Intensity", typeof(Shimmer), 0.35);

    public static void SetIntensity(AvaloniaObject element, double value) =>
        element.SetValue(IntensityProperty, value);

    public static double GetIntensity(AvaloniaObject element) =>
        element.GetValue(IntensityProperty);

    private static readonly ConditionalWeakTable<Control, DispatcherTimer> Timers = new();

    static Shimmer()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            WhenAttached(control, () => Start(control));
            control.DetachedFromVisualTree += StopHandler;
        }
        else
        {
            Stop(control);
        }
    }

    private static void StopHandler(object? s, VisualTreeAttachmentEventArgs ev)
    {
        if (s is Control c)
        {
            c.DetachedFromVisualTree -= StopHandler;
            Stop(c);
        }
    }

    private static void WhenAttached(Control control, Action run)
    {
        if (control.IsAttachedToVisualTree()) run();
        else
        {
            void Handler(object? s, VisualTreeAttachmentEventArgs ev)
            {
                control.AttachedToVisualTree -= Handler;
                run();
            }
            control.AttachedToVisualTree += Handler;
        }
    }

    private static void Start(Control control)
    {
        if (Timers.TryGetValue(control, out _)) return;
        // 动画总开关关闭时不注入流光层
        if (!AnimationGate.Enabled) return;
        var wrapper = OverlayHost.GetOrCreateOverlay(control);
        if (wrapper == null) return;

        var intensity = Math.Clamp(GetIntensity(control), 0.02, 1.0);
        var duration = Math.Max(600, GetDurationMs(control));

        var band = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = BuildBrush(intensity),
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new RotateTransform(18, 0.5, 0.5),
                    new TranslateTransform(),
                },
            },
        };

        var host = new Border
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = band,
        };
        wrapper.Children.Add(host);

        var last = DateTime.Now;
        var elapsed = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // 动画总开关关闭时隐藏流光带（避免留下一道静止的高光），打开时恢复
            if (!AnimationGate.Enabled)
            {
                band.Opacity = 0;
                return;
            }
            band.Opacity = 1;

            var w = wrapper.Bounds.Width;
            if (w <= 0) return;
            var bandW = w * 0.6;
            band.Width = bandW;
            band.Height = wrapper.Bounds.Height * 1.8;

            var now = DateTime.Now;
            var dt = (now - last).TotalMilliseconds;
            last = now;
            elapsed += dt;

            var t = (elapsed % duration) / duration; // 0..1 循环
            var x = -bandW * 0.6 + t * (w + bandW * 1.2);
            if (band.RenderTransform is TransformGroup tg && tg.Children[1] is TranslateTransform tr)
                tr.X = x;
        };
        Timers.Add(control, timer);
        timer.Start();
    }

    private static void Stop(Control control)
    {
        if (Timers.TryGetValue(control, out var timer))
        {
            timer.Stop();
            Timers.Remove(control);
        }
    }

    private static Brush BuildBrush(double intensity)
    {
        var a = (byte)Math.Round(intensity * 255);
        var g = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 0.5), 170, 225, 255), 0.40)); // 冷调
        g.GradientStops.Add(new GradientStop(Color.FromArgb(a, 255, 255, 255), 0.50));               // 高光
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 0.5), 255, 200, 240), 0.60)); // 暖调
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
        return g;
    }
}
