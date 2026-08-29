using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 背景流光渐变：在宿主容器（Grid/Panel）最底层注入一个全 Stretch 的半透明渐变层，
/// 缓慢旋转渐变方向（画笔 RelativeTransform = RotateTransform，绕 (0.5,0.5) 不会露边界），
/// 营造背景色彩缓慢流动的氛围感。class="nya-ambient"。全部逻辑只在本模块。
/// </summary>
public static class AmbientGradient
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(AmbientGradient), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>手动启用（供主工程兜底调用；class 已启用时幂等，不会重复注入）。</summary>
    public static void Enable(Control host)
    {
        if (host is not null) SetEnabled(host, true);
    }

    /// <summary>渐变旋转一圈毫秒数，默认 7000（氛围流动要肉眼可见，太慢等于没做）。</summary>
    public static readonly AttachedProperty<double> DurationMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("DurationMs", typeof(AmbientGradient), 7000.0);

    public static void SetDurationMs(AvaloniaObject element, double value) =>
        element.SetValue(DurationMsProperty, value);

    public static double GetDurationMs(AvaloniaObject element) =>
        element.GetValue(DurationMsProperty);

    /// <summary>
    /// 全局「彩虹背景」开关（默认开），由设置页主题卡片切换并持久化。
    /// 关闭时停止并移除所有已注入的渐变层；打开时重新注入。
    /// </summary>
    public static bool AmbientGradientEnabled { get; set; } = true;

    private static readonly ConcurrentDictionary<Control, DispatcherTimer> Timers = new();
    private static readonly ConcurrentDictionary<Control, Border> Layers = new();

    /// <summary>挂载过 class（Enabled=true）的宿主集合：层被停止/开关关闭后仍保留，供重新打开时找回重注入。</summary>
    private static readonly ConditionalWeakTable<Control, object> EnabledHosts = new();

    static AmbientGradient()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // 登记表全程保留：宿主 detach（如主题热重载的重挂载）后，
            // 重新 attach 时仍能凭登记恢复流光层。
            EnabledHosts.AddOrUpdate(control, new object());
            control.AttachedToVisualTree += AttachHandler;
            control.DetachedFromVisualTree += DetachHandler;
            WhenAttached(control, () => TryStart(control));
        }
        else
        {
            control.AttachedToVisualTree -= AttachHandler;
            control.DetachedFromVisualTree -= DetachHandler;
            EnabledHosts.Remove(control);
            Stop(control);
        }
    }

    private static void AttachHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        // 重挂载（theme remount 等）后的恢复入口；Start 自带幂等保护
        if (s is Control c)
            TryStart(c);
    }

    private static void DetachHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        // 仅停层与计时器，不清登记、不退订，等待重新 attach 恢复
        if (s is Control c)
            Stop(c);
    }

    private static void TryStart(Control control)
    {
        // Start 自带全局开关判断与幂等保护，这里只补充"已挂载"校验
        if (!control.IsAttachedToVisualTree())
            return;
        Start(control);
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
        if (control is not Panel panel) return;
        // 全局开关或动画总开关关闭时不注入
        if (!AmbientGradientEnabled || !AnimationGate.Enabled) return;

        var baseColor = ResolveBaseColor();
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        // 滤镜式半透明渐变：盖在内容之上（低 alpha），既保证可见又不影响阅读/交互。
        // 用较鲜明对比（色相 ±60° + 亮度上浮）让流动肉眼可辨。
        gradient.GradientStops.Add(new GradientStop(WithAlpha(ShiftHue(baseColor, -60), 0.40), 0.00));
        gradient.GradientStops.Add(new GradientStop(WithAlpha(Lighten(baseColor, 0.18), 0.26), 0.50));
        gradient.GradientStops.Add(new GradientStop(WithAlpha(ShiftHue(baseColor, 60), 0.40), 1.00));

        var layer = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = gradient,
        };
        // 注意：底层（Insert(0)）会被内容的不透明背景完全盖住而不可见，
        // 因此作为「顶层氛围滤镜」Add 到末尾；若宿主是带行列的 Grid 则铺满整个网格。
        if (panel is Grid grid)
        {
            Grid.SetRow(layer, 0);
            Grid.SetColumn(layer, 0);
            if (grid.RowDefinitions.Count > 0) Grid.SetRowSpan(layer, grid.RowDefinitions.Count);
            if (grid.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(layer, grid.ColumnDefinitions.Count);
        }
        panel.Children.Add(layer);

        var duration = Math.Max(4000, GetDurationMs(control));
        var last = DateTime.Now;
        var angle = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            try
            {
                var now = DateTime.Now;
                var dt = (now - last).TotalMilliseconds;
                last = now;
                angle = (angle + dt * 360.0 / duration) % 360.0;
                // 画笔不支持 RelativeTransform（Avalonia 无此属性）：改为让 StartPoint/EndPoint
                // 绕 (0.5,0.5) 以半径 0.5 做圆周运动，等效于渐变方向缓慢旋转。
                var rad = angle * Math.PI / 180.0;
                gradient.StartPoint = new RelativePoint(0.5 - 0.5 * Math.Cos(rad), 0.5 - 0.5 * Math.Sin(rad), RelativeUnit.Relative);
                gradient.EndPoint = new RelativePoint(0.5 + 0.5 * Math.Cos(rad), 0.5 + 0.5 * Math.Sin(rad), RelativeUnit.Relative);
            }
            catch (Exception)
            {
                // 动画失败不中断界面
            }
        };
        Timers[control] = timer;
        Layers[control] = layer;
        timer.Start();
    }

    private static void Stop(Control control)
    {
        if (Timers.TryGetValue(control, out var timer))
        {
            timer.Stop();
            Timers.TryRemove(control, out _);
        }
        if (Layers.TryRemove(control, out var layer) &&
            control is Panel panel)
        {
            panel.Children.Remove(layer);
        }
    }

    /// <summary>
    /// 全局「彩虹背景」开关或动画总开关变化后刷新：
    /// 关闭时移除所有已注入的渐变层，打开时对启用过的宿主重新注入。
    /// </summary>
    public static void RefreshGlobal()
    {
        foreach (var host in EnabledHosts)
        {
            if (host.Key is not Control control || !control.IsAttachedToVisualTree())
                continue;
            if (AmbientGradientEnabled && AnimationGate.Enabled)
            {
                if (!Timers.ContainsKey(control))
                    Start(control);
            }
            else
            {
                Stop(control);
            }
        }
    }

    /// <summary>
    /// 主题切换后重建所有活跃流光层：渐变颜色在 Start 时一次性取自主题资源
    /// （SystemAccentColor），不重建就会一直停留在旧家族的配色上。
    /// 主题热重载入口调用（ThemeChanged 后）即可让氛围光效跟随新主题。
    /// </summary>
    public static void RecreateAll()
    {
        foreach (var host in EnabledHosts)
        {
            if (host.Key is not Control control ||
                !control.IsAttachedToVisualTree() ||
                !AmbientGradientEnabled ||
                !AnimationGate.Enabled)
            {
                continue;
            }

            Stop(control);
            Start(control);
        }
    }

    private static Color ResolveBaseColor()
    {
        if (Application.Current?.TryGetResource("SystemAccentColor", null, out var acc) == true && acc is Color c)
            return c;
        if (Application.Current?.TryGetResource("WindowBgBrush", null, out var wb) == true && wb is ISolidColorBrush sb)
            return sb.Color;
        return Color.FromRgb(120, 90, 220);
    }

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>按比例提亮（把颜色向白色方向偏移 amount）。</summary>
    private static Color Lighten(Color c, double amount)
    {
        byte Blend(byte v) => (byte)Math.Clamp(v + (255 - v) * amount, 0, 255);
        return Color.FromRgb(Blend(c.R), Blend(c.G), Blend(c.B));
    }

    /// <summary>把颜色的色相旋转 deg 度（RGB→HSL→HSL→RGB）。</summary>
    private static Color ShiftHue(Color c, double deg)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h, s, l = (max + min) / 2.0;
        double d = max - min;
        if (d == 0) h = 0;
        else if (max == r) h = 60.0 * (((g - b) / d) % 6);
        else if (max == g) h = 60.0 * ((b - r) / d + 2);
        else h = 60.0 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));

        h = (h + deg + 360) % 360;
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        byte R = (byte)Math.Round(HueToRgb(p, q, h / 360 + 1.0 / 3.0) * 255);
        byte G = (byte)Math.Round(HueToRgb(p, q, h / 360) * 255);
        byte B = (byte)Math.Round(HueToRgb(p, q, h / 360 - 1.0 / 3.0) * 255);
        return Color.FromRgb(R, G, B);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
