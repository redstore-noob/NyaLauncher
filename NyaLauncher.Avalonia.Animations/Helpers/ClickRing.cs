using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 点击圆环：在宿主容器（Grid）注入一个全 Stretch 的 Canvas，
/// 每次左键点击从点击点冒出一圈扩散、淡出的圆环（450ms，随机配色）。
/// 开关只控制生成时机（静态布尔守卫），层注入后常驻，切换开关即时生效。
/// class 无需声明，由主工程挂 animations:ClickRing.Enabled="True" 启用。全部逻辑只在本模块。
/// </summary>
public static class ClickRing
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ClickRing), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>手动启用（供主工程兜底调用；已启用时幂等）。</summary>
    public static void Enable(Control host)
    {
        if (host is not null) SetEnabled(host, true);
    }

    /// <summary>全局「点击圆环」开关（默认开），由设置页切换并持久化；关闭后点击不再冒圈。</summary>
    public static bool ClickRingEnabled { get; set; } = true;

    // 圆环参数：基准直径 40px，扩散到 2.2 倍，450ms 淡出
    private const int RingMs = 450;
    private const double RingBaseSize = 40;
    private const double RingMaxScale = 2.2;

    private sealed class RingState
    {
        public Canvas Layer { get; } = new()
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
        };
        public bool Subscribed; // 防止重复订阅 PointerPressed
    }

    private static readonly ConcurrentDictionary<Control, RingState> States = new();

    static ClickRing()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            WhenAttached(control, () => Attach(control));
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

    private static void Attach(Control control)
    {
        if (control is not Grid grid) return;
        if (!States.TryGetValue(control, out var state))
        {
            state = new RingState();
            States[control] = state;
        }

        // 层注入后常驻（随宿主一起挂载/卸载），开关只守卫生成时机
        if (state.Layer.Parent is Grid current && !ReferenceEquals(current, grid))
            current.Children.Remove(state.Layer);
        if (state.Layer.Parent is null)
        {
            Grid.SetRow(state.Layer, 0);
            Grid.SetColumn(state.Layer, 0);
            if (grid.RowDefinitions.Count > 0) Grid.SetRowSpan(state.Layer, grid.RowDefinitions.Count);
            if (grid.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(state.Layer, grid.ColumnDefinitions.Count);
            state.Layer.ZIndex = 220;
            grid.Children.Add(state.Layer);
        }

        // 冒泡订阅：点击宿主任意子元素都会冒泡到这里；只订阅一次
        if (!state.Subscribed)
        {
            control.PointerPressed += (_, ev) => OnPointerPressed(state, ev);
            state.Subscribed = true;
        }
    }

    /// <summary>左键点击：从点击点冒出一圈扩散消散的圆环（淡出走 Transitions，缩放走轻量定时推进）。</summary>
    private static void OnPointerPressed(RingState state, PointerPressedEventArgs ev)
    {
        if (!ClickRingEnabled || !AnimationGate.Enabled) return;
        if (state.Layer.Bounds.Width <= 0 || state.Layer.Bounds.Height <= 0) return;
        var props = ev.GetCurrentPoint(state.Layer);
        if (!props.Properties.IsLeftButtonPressed) return;

        var pos = ev.GetPosition(state.Layer);

        var ring = new Ellipse
        {
            Width = RingBaseSize,
            Height = RingBaseSize,
            StrokeThickness = 2,
            Stroke = new SolidColorBrush(RandomColor()),
            IsHitTestVisible = false,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(0.3, 0.3),
            Opacity = 0.9,
        };
        // 透明度淡出走 Transitions（渲染线程驱动，一次性）
        ring.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(RingMs),
            },
        };

        Canvas.SetLeft(ring, pos.X - ring.Width / 2);
        Canvas.SetTop(ring, pos.Y - ring.Height / 2);
        state.Layer.Children.Add(ring);
        Dispatcher.UIThread.Post(() => ring.Opacity = 0);

        // 缩放推进：ScaleTransform 非 Animatable，用 450ms 生命周期的轻量定时器，
        // 结束即回收（非长驻帧循环）
        var scale = (ScaleTransform)ring.RenderTransform!;
        var elapsed = 0.0;
        var last = DateTime.Now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            try
            {
                var now = DateTime.Now;
                elapsed += (now - last).TotalMilliseconds;
                last = now;
                if (elapsed >= RingMs)
                {
                    timer.Stop();
                    state.Layer.Children.Remove(ring);
                    return;
                }

                // 与淡出同步的缓出曲线：快扩散、缓收尾
                var t = elapsed / RingMs;
                var eased = 1 - Math.Pow(1 - t, 3);
                var s = 0.3 + (RingMaxScale - 0.3) * eased;
                scale.ScaleX = scale.ScaleY = s;
            }
            catch
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    private static readonly Random Rand = new();

    private static double Rng() => Rand.NextDouble();

    private static Color RandomColor()
    {
        var baseColor = ResolveAccent();
        return Rng() switch
        {
            < 0.45 => baseColor,
            < 0.7 => Colors.White,
            < 0.85 => Color.FromRgb(255, 215, 130), // 暖金
            _ => Color.FromRgb(255, 190, 230),      // 粉
        };
    }

    private static Color ResolveAccent()
    {
        if (Application.Current?.TryGetResource("SystemAccentColor", null, out var acc) == true && acc is Color c)
            return c;
        return Color.FromRgb(150, 110, 230);
    }
}
