using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// 星尘跟随鼠标：在宿主容器顶层注入一个全 Stretch 的 Canvas，
/// 星尘从指针位置坠下：固定横向初速随阻尼衰减，形成「先弯后直」的固定弧线轨迹。
/// （点击圆环特效已独立为 ClickRing。）
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

    private const int MaxParticles = 36;
    private const int SpawnIntervalMs = 56;
    private const double LifeMs = 1500; // 坠落需要足够的划过时间

    private const string StarPath =
        "M10,0 L12.4,7.6 L20,10 L12.4,12.4 L10,20 L7.6,12.4 L0,10 L7.6,7.6 Z";

    /// <summary>单颗星尘：位置、速度与自转全部随帧积分；运动 = 纯重力直落。</summary>
    private sealed class Particle
    {
        public Path Shape { get; } = new()
        {
            Data = Geometry.Parse(StarPath),
            IsHitTestVisible = false,
        };

        public Particle()
        {
            // 旋转 + 缩放共存于 TransformGroup：避免自转被缩放变换覆盖
            Shape.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            Shape.RenderTransform = new TransformGroup { Children = { Rotate, Scale } };
        }

        public readonly RotateTransform Rotate = new();
        public readonly ScaleTransform Scale = new();

        public double X, Y;          // 当前位置（px）
        public double Vx, Vy;        // 速度（px/ms）：Vx 是固定的横向初速，随阻尼衰减出弧线
        public double Gravity;       // 重力加速度（px/ms²），每颗星随机，决定终端坠落速度
        public double Angle;         // 自转角（°）
        public double Spin;          // 自转角速（°/ms），随阻尼衰减
        public double Age;           // 存活时间（ms）
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
        public DateTime LastTick = DateTime.MinValue;   // 真实帧间隔（可变步长积分）
        public Point? LastPointerPos;                    // 最近指针位置（星星生成点）
        public DateTime LastPointerMove = DateTime.MinValue;
        public DateTime PointerActiveUntil = DateTime.MinValue; // 指针活跃窗口：期间由定时器均匀补生成
        public bool Subscribed; // 防止 Stop 后重新 Start 时重复订阅
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

        // 冒泡订阅：指针在宿主任意子元素上移动都会冒泡到这里（无需手动转发）。
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
            state.LastTick = DateTime.MinValue;
            state.LastPointerPos = null;
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

    /// <summary>指针停止移动后仍继续生成星星的时间窗口（ms）。</summary>
    private const double PointerActiveWindowMs = 150;

    private static void OnPointerMoved(TrailState state, PointerEventArgs ev)
    {
        var now = DateTime.Now;
        var pos = ev.GetPosition(state.Layer);
        if (state.Layer.Bounds.Width <= 0 || state.Layer.Bounds.Height <= 0)
        {
            state.LastPointerPos = pos;
            state.LastPointerMove = now;
            return;
        }

        state.LastPointerPos = pos;
        state.LastPointerMove = now;
        state.PointerActiveUntil = now.AddMilliseconds(PointerActiveWindowMs);
    }

    /// <summary>
    /// 按固定间隔尝试生成一颗星：只在指针活跃窗口内生效。
    /// 由 Tick 定时器调用，保证生成节奏与鼠标事件密度无关、严格均匀。
    /// </summary>
    private static void TrySpawn(TrailState state, DateTime now)
    {
        if (now >= state.PointerActiveUntil) return;
        if ((now - state.LastSpawn).TotalMilliseconds < SpawnIntervalMs) return;
        if (state.LastPointerPos is not { } pos) return;
        if (state.Particles.Count >= MaxParticles) return;
        state.LastSpawn = now;
        SpawnParticle(state, pos);
    }

    private static void SpawnParticle(TrailState state, Point pos)
    {
        var p = new Particle();
        // 出生点在指针附近微散开，避免同点叠星
        p.X = pos.X + (Rng() - 0.5) * 7;
        p.Y = pos.Y + (Rng() - 0.5) * 7;
        // 固定弧线：随机方向的横向初速（不再持续摆动），随阻尼衰减 → 先弯后直
        p.Vx = (Rng() < 0.5 ? -1 : 1) * (0.02 + Rng() * 0.04);
        p.Vy = 0.06 + Rng() * 0.06;
        p.Gravity = 0.00022 + Rng() * 0.00012;         // 重力：与阻尼平衡出终端坠落速度
        p.Angle = Rng() * 360;
        p.Spin = (Rng() < 0.5 ? -1 : 1) * (0.02 + Rng() * 0.07); // 随机正反自转（不影响轨迹形状）
        var size = 8 + Rng() * 10;
        p.Shape.Width = size;
        p.Shape.Height = size;
        p.Shape.Fill = new SolidColorBrush(RandomColor());
        Canvas.SetLeft(p.Shape, p.X - size / 2);
        Canvas.SetTop(p.Shape, p.Y - size / 2);
        state.Layer.Children.Add(p.Shape);
        state.Particles.Add(p);
    }

    private static void Tick(TrailState state)
    {
        var now = DateTime.Now;
        var dt = state.LastTick == DateTime.MinValue
            ? 16.0
            : Math.Clamp((now - state.LastTick).TotalMilliseconds, 1, 40); // 可变步长，防卡顿大跳
        state.LastTick = now;

        // 生成由定时器驱动：节奏与指针事件密度无关，均匀稳定
        TrySpawn(state, now);

        var damping = Math.Pow(0.999, dt); // 空气阻尼：速度与自转按比例衰减（约 0.984/帧）

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

            // 力积分：重力直落；横向速度无外力、按更强的阻尼衰减 → 固定的「先弯后直」弧线
            var lateralDamping = Math.Pow(0.992, dt); // 横向衰减快于纵向：弯曲集中在前半程
            p.Vx = p.Vx * lateralDamping;
            p.Vy = p.Vy * damping + p.Gravity * dt;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            Canvas.SetLeft(p.Shape, p.X - p.Shape.Width / 2);
            Canvas.SetTop(p.Shape, p.Y - p.Shape.Height / 2);

            // 自转随阻尼减缓；缩放：先轻盈弹出再缓慢收小
            p.Angle = (p.Angle + p.Spin * dt) % 360;
            p.Spin *= damping;
            p.Rotate.Angle = p.Angle;

            var grow = EaseOutCubic(Math.Min(t / 0.18, 1));
            var shrink = 1 - 0.42 * Math.Pow(t, 1.5);
            var s = (0.55 + 0.45 * grow) * shrink;
            p.Scale.ScaleX = p.Scale.ScaleY = s;

            // 不透明度：快速亮起（前 12% 寿命）再缓淡出，避免突现突消
            var fadeIn = Math.Min(t / 0.12, 1);
            p.Shape.Opacity = fadeIn * Math.Pow(1 - t, 1.4);
        }
    }

    private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);

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
