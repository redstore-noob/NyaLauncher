using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 星尘跟随鼠标：在宿主容器顶层注入一个全 Stretch 的 Canvas，隧道捕获鼠标移动（节流），
/// 从指针位置飘出小星星（Path 四角星，随机大小/色相/初速），上飘 + 微漂 + 渐隐消散。
/// class="nya-sparkles"。全部逻辑只在本模块。
/// </summary>
public static class SparkleTrail
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(SparkleTrail), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>手动启用（供主工程兜底调用；class 已启用时幂等，不会重复注入）。</summary>
    public static void Enable(Control host)
    {
        if (host is not null) SetEnabled(host, true);
    }

    private const int MaxParticles = 48;
    private const int SpawnIntervalMs = 28;
    private const double LifeMs = 1000;

    private const string StarPath =
        "M10,0 L12.4,7.6 L20,10 L12.4,12.4 L10,20 L7.6,12.4 L0,10 L7.6,7.6 Z";

    private sealed class Particle
    {
        public Path Shape { get; } = new()
        {
            Data = Geometry.Parse(StarPath),
            IsHitTestVisible = false,
        };
        public double X, Y, Vx, Vy, Age;
    }

    private sealed class TrailState
    {
        public Canvas Layer { get; } = new()
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
        };
        public List<Particle> Particles { get; } = new();
        public DispatcherTimer? Timer;
        public DateTime LastSpawn = DateTime.MinValue;
        public bool Subscribed; // 防止 Stop 后重新 Start 时重复订阅 PointerMoved
    }

    /// <summary>
    /// 全局「星尘特效」开关（默认开），由设置页主题卡片切换并持久化。
    /// 关闭时移除已注入的星星层；打开时重新注入。
    /// </summary>
    public static bool SparkleTrailEnabled { get; set; } = true;

    private static readonly ConcurrentDictionary<Control, TrailState> States = new();

    /// <summary>挂载过 class（Enabled=true）的宿主集合：层被停止/开关关闭后仍保留，供重新打开时找回重注入。</summary>
    private static readonly ConditionalWeakTable<Control, object> EnabledHosts = new();

    static SparkleTrail()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            EnabledHosts.Remove(control);
            EnabledHosts.Add(control, new object());
            WhenAttached(control, () => Start(control));
            control.DetachedFromVisualTree += StopHandler;
        }
        else
        {
            EnabledHosts.Remove(control);
            Stop(control);
        }
    }

    private static void StopHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        if (s is Control c)
        {
            c.DetachedFromVisualTree -= StopHandler;
            EnabledHosts.Remove(c);
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
        if (control is not Panel panel) return;
        // 全局开关或动画总开关关闭时不注入
        if (!SparkleTrailEnabled || !AnimationGate.Enabled) return;
        // 已在运行则不重复启动
        if (States.TryGetValue(control, out var existing) && existing.Timer is not null) return;

        var state = existing ?? new TrailState();
        if (panel is Grid grid)
        {
            Grid.SetRow(state.Layer, 0);
            Grid.SetColumn(state.Layer, 0);
            if (grid.RowDefinitions.Count > 0) Grid.SetRowSpan(state.Layer, grid.RowDefinitions.Count);
            if (grid.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(state.Layer, grid.ColumnDefinitions.Count);
        }
        // 添加到末尾 = 顶层绘制（星尘浮在内容之上）；IsHitTestVisible=false 不挡交互
        panel.Children.Add(state.Layer);

        // 冒泡订阅：鼠标在宿主任意子元素上移动都会冒泡到这里（无需手动转发）。
        // Stop 后重新 Start 时不重复订阅。
        if (!state.Subscribed)
        {
            control.PointerMoved += (_, ev) => OnPointerMoved(state, ev);
            state.Subscribed = true;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => Tick(state);
        state.Timer = timer;
        timer.Start();

        States[control] = state;
    }

    private static void Stop(Control control)
    {
        if (States.TryGetValue(control, out var state))
        {
            state.Timer?.Stop();
            state.Timer = null;
            state.Particles.Clear();
            if (control is Panel panel) panel.Children.Remove(state.Layer);
            // 保留注册（供 RefreshGlobal 在开关重新打开时找回宿主重注入）
        }
    }

    /// <summary>
    /// 全局开关变化后刷新：关闭时移除已注入的星星层；打开时对所有启用过 class 的宿主重新注入。
    /// 由设置页切换「星尘特效」开关或动画总开关变化时调用（主工程只调用，不写动画逻辑）。
    /// </summary>
    public static void RefreshGlobal()
    {
        foreach (var host in EnabledHosts)
        {
            if (host.Key is not Control control || !control.IsAttachedToVisualTree())
                continue;
            if (SparkleTrailEnabled && AnimationGate.Enabled)
                Start(control);
            else
                Stop(control);
        }
    }

    private static void OnPointerMoved(TrailState state, PointerEventArgs ev)
    {
        var now = DateTime.Now;
        if ((now - state.LastSpawn).TotalMilliseconds < SpawnIntervalMs) return;
        state.LastSpawn = now;
        if (state.Particles.Count >= MaxParticles) return;

        var pos = ev.GetPosition(state.Layer);
        if (state.Layer.Bounds.Width <= 0 || state.Layer.Bounds.Height <= 0) return;

        var p = new Particle();
        p.X = pos.X;
        p.Y = pos.Y;
        var angle = RandomAngle();
        var speed = 0.5 + Rng() * 0.9;
        p.Vx = Math.Cos(angle) * speed;
        p.Vy = Math.Sin(angle) * speed - 0.55; // 整体偏上飘
        var size = 8 + Rng() * 10;
        p.Shape.Width = size;
        p.Shape.Height = size;
        p.Shape.Fill = new SolidColorBrush(RandomColor());
        p.Shape.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        p.Shape.RenderTransform = new RotateTransform(Rng() * 360);
        Canvas.SetLeft(p.Shape, p.X - size / 2);
        Canvas.SetTop(p.Shape, p.Y - size / 2);
        state.Layer.Children.Add(p.Shape);
        state.Particles.Add(p);
    }

    private static void Tick(TrailState state)
    {
        var dt = 16.0;
        for (var i = state.Particles.Count - 1; i >= 0; i--)
        {
            var p = state.Particles[i];
            p.Age += dt;
            var t = p.Age / LifeMs;
            if (t >= 1)
            {
                state.Layer.Children.Remove(p.Shape);
                state.Particles.RemoveAt(i);
                continue;
            }
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            Canvas.SetLeft(p.Shape, p.X - p.Shape.Width / 2);
            Canvas.SetTop(p.Shape, p.Y - p.Shape.Height / 2);
            p.Shape.Opacity = 1 - t; // 从全亮渐隐到消失
            // 星星自转 + 略微缩小，更有"魔法光点"感
            if (p.Shape.RenderTransform is RotateTransform rot)
                rot.Angle = (rot.Angle + dt * 0.18) % 360;
            if (p.Shape.RenderTransform is not ScaleTransform st)
                p.Shape.RenderTransform = new ScaleTransform(1, 1);
            else
            {
                var s = 1 - 0.4 * t;
                st.ScaleX = st.ScaleY = s;
            }
        }
    }

    private static readonly Random Rand = new();

    private static double Rng() => Rand.NextDouble();

    private static double RandomAngle() => Rng() * Math.PI * 2;

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
