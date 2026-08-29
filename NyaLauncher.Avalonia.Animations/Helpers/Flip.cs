using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 卡片 3D 翻转：hover 时卡片绕「竖轴」翻转（用 ScaleX 沿 cos(phase·π) 从 1→-1 模拟绕 Y 轴旋转，
/// 2D 投影下即水平翻转），过 90° 时切换正/背面可见性，背面内容用预置 ScaleTransform(-1,1) 补偿成正向。
/// 约定：宿主 Border 的 Child 是 Panel（通常 Grid），其中「最后一个子元素」为背面，其余为正面。
/// class="nya-flip"。全部逻辑只在本模块。
/// </summary>
public static class Flip
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Flip), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>单次翻转毫秒数，默认 380。</summary>
    public static readonly AttachedProperty<double> DurationMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("DurationMs", typeof(Flip), 380.0);

    public static void SetDurationMs(AvaloniaObject element, double value) =>
        element.SetValue(DurationMsProperty, value);

    public static double GetDurationMs(AvaloniaObject element) =>
        element.GetValue(DurationMsProperty);

    /// <summary>
    /// 把面板里的某个子元素标记为「背面」。推荐显式标记（而不是"最后一个子"），
    /// 因为卡片若同时挂了 nya-shimmer，OverlayHost 会包一层 wrapper、把 shimmer 层塞成最后一个子。
    /// </summary>
    public static readonly AttachedProperty<bool> IsBackProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsBack", typeof(Flip), false);

    public static void SetIsBack(AvaloniaObject element, bool value) =>
        element.SetValue(IsBackProperty, value);

    public static bool GetIsBack(AvaloniaObject element) =>
        element.GetValue(IsBackProperty);

    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    /// <summary>每个卡片维护翻转版本号：新翻转触发时自增，旧翻转检测到版本过期即退出，防止进出竞态。</summary>
    private sealed class FlipState
    {
        public long Version;
    }

    private static readonly ConditionalWeakTable<Control, FlipState> States = new();

    static Flip()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (Attached.TryGetValue(control, out _)) return;
        Attached.Add(control, new object());

        control.PointerEntered += (_, _) => Animate(control, toBack: true);
        control.PointerExited += (_, _) => Animate(control, toBack: false);
    }

    private static (Control? front, Control? back) ResolveFaces(Control host)
    {
        if (host is Border { Child: Panel panel })
        {
            // 优先取显式标记 IsBack 的子元素（兼容 shimmer wrapper 干扰）；front 取第一个非背面子元素
            Control? markedBack = null;
            foreach (var child in panel.Children)
                if (GetIsBack(child)) { markedBack = child; break; }
            if (markedBack is not null)
            {
                Control? front = null;
                foreach (var child in panel.Children)
                    if (!ReferenceEquals(child, markedBack)) { front = child; break; }
                return (front, markedBack);
            }
            // 退回：第一个子为正面、最后一个子为背面
            if (panel.Children.Count >= 2)
                return (panel.Children[0], panel.Children[^1]);
        }
        return (null, null);
    }

    private static async void Animate(Control host, bool toBack)
    {
        // 动画总开关关闭时不翻转（保持正面显示）
        if (!AnimationGate.Enabled) return;

        var (front, back) = ResolveFaces(host);
        if (front is null || back is null) return;

        // 竞态防护：新翻转取代旧翻转，旧循环检测版本过期立即退出
        var state = States.GetValue(host, _ => new FlipState());
        var version = ++state.Version;

        // 背面预置水平镜像补偿：容器 ScaleX=-1 时背面内容恢复正向
        if (back.RenderTransform is not ScaleTransform backScale || backScale.ScaleX > 0)
            back.RenderTransform = new ScaleTransform(-1, 1);

        var duration = Math.Max(120, GetDurationMs(host));
        var frames = Math.Max(1, duration / 16);
        var flip = new ScaleTransform(toBack ? -1 : 1, 1);
        host.RenderTransform = flip;
        host.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        try
        {
            for (var i = 1; i <= frames; i++)
            {
                if (state.Version != version) return; // 已被新翻转取代
                var t = i / (double)frames;
                var eased = 1 - Math.Pow(1 - t, 3);
                var phase = toBack ? eased : 1 - eased; // 0→1 翻到背面，1→0 翻回
                flip.ScaleX = Math.Cos(phase * Math.PI); // 1 → 0 → -1
                var showingBack = flip.ScaleX < 0;
                if (front.IsVisible == showingBack)
                {
                    front.IsVisible = !showingBack;
                    back.IsVisible = showingBack;
                }
                await Task.Delay(16);
            }

            if (state.Version != version) return;

            var finalBack = toBack;
            front.IsVisible = !finalBack;
            back.IsVisible = finalBack;
            flip.ScaleX = finalBack ? -1 : 1;
        }
        catch (Exception)
        {
            // 动画失败也要保证正常显示正面
            front.IsVisible = true;
            back.IsVisible = false;
            host.RenderTransform = null;
        }
    }
}
