using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 按钮水波纹：在控件之上注入一个 Canvas 覆盖层，左键按下时从"点击点"扩散出一圈水波纹。
/// 自带覆盖层，不依赖任何全局 Canvas，所以主工程只要给控件加 class="nya-ripple" 即可。
/// 全局 Button 启用时，会跳过 ScrollBar / Popup 内部的按钮（滚动条箭头、下拉项），避免破坏模板布局。
/// 全部逻辑只在本模块。
/// </summary>
public static class Ripple
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Ripple), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>涟漪颜色（ARGB，uint 形式），默认半透明白 0x59FFFFFF（alpha≈35%，深浅主题都可见；可用 Ripple.Color 覆盖）。</summary>
    public static readonly AttachedProperty<uint> ColorProperty =
        AvaloniaProperty.RegisterAttached<Control, uint>("Color", typeof(Ripple), 0x59FFFFFF);

    public static void SetColor(AvaloniaObject element, uint value) =>
        element.SetValue(ColorProperty, value);

    public static uint GetColor(AvaloniaObject element) =>
        element.GetValue(ColorProperty);

    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    static Ripple()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (Attached.TryGetValue(control, out _)) return;
        // 跳过滚动条箭头、下拉弹出项等模板内部按钮，避免破坏其布局。
        if (IsInsideScrollOrPopup(control)) return;
        Attached.Add(control, new object());

        var wrapper = OverlayHost.GetOrCreateOverlay(control);
        if (wrapper == null) return;

        var layer = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 100,
        };
        wrapper.Children.Add(layer);

        var color = GetColor(control);
        control.PointerPressed += (_, ev) =>
        {
            if (!ev.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
            _ = SpawnAsync(layer, ev, color);
        };
    }

    private static async Task SpawnAsync(Canvas layer, PointerPressedEventArgs ev, uint colorArgb)
    {
        var local = ev.GetPosition(layer);
        var w = layer.Bounds.Width;
        var h = layer.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var maxR = Math.Sqrt(w * w + h * h);
        var color = ToColor(colorArgb);
        var dot = new Border
        {
            Width = 0,
            Height = 0,
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(color),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, local.X);
        Canvas.SetTop(dot, local.Y);
        layer.Children.Add(dot);

        const int frames = 22;
        try
        {
            for (int i = 1; i <= frames; i++)
            {
                var t = i / (double)frames;
                var eased = 1 - Math.Pow(1 - t, 3);
                var r = eased * maxR;
                dot.Width = r * 2;
                dot.Height = r * 2;
                dot.CornerRadius = new CornerRadius(r);
                dot.Opacity = (1 - eased) * 0.6;
                Canvas.SetLeft(dot, local.X - r);
                Canvas.SetTop(dot, local.Y - r);
                await Task.Delay(16);
            }
        }
        finally
        {
            layer.Children.Remove(dot);
        }
    }

    private static Color ToColor(uint argb)
    {
        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        return Color.FromArgb(a, r, g, b);
    }

    private static bool IsInsideScrollOrPopup(Control control)
    {
        for (var p = control.GetVisualParent(); p != null; p = p.GetVisualParent())
        {
            if (p is ScrollBar or Popup)
                return true;
        }
        return false;
    }
}
