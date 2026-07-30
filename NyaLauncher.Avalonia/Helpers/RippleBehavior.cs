using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Helpers;

/// <summary>
/// 水波纹特效 — 在 Canvas 层上创建圆形扩散动画
/// </summary>
public static class RippleBehavior
{
    private static readonly Color RippleColor = Color.FromArgb(55, 255, 255, 255);

    /// <summary>
    /// 全局波纹层 — 子页面可通过此引用附加水波纹
    /// </summary>
    public static Canvas? GlobalRippleLayer { get; set; }

    /// <summary>
    /// 为控件附加水波纹效果（点击时从中心扩散）
    /// </summary>
    public static void AttachRipple(Control control, Canvas layer)
    {
        control.PointerPressed += async (_, e) =>
        {
            if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                await ShowRippleAsync(control, layer);
        };
    }

    private static async Task ShowRippleAsync(Control control, Canvas layer)
    {
        // 获取控件中心相对于 Canvas 的位置
        var cx = control.Bounds.Width / 2;
        var cy = control.Bounds.Height / 2;
        var origin = control.TranslatePoint(new Point(cx, cy), layer);
        if (origin == null) return;

        // 计算波纹最大半径（取对角线的一半 * 1.8 确保覆盖）
        var maxRadius = Math.Sqrt(
            control.Bounds.Width * control.Bounds.Width +
            control.Bounds.Height * control.Bounds.Height
        ) * 0.9;

        if (maxRadius < 4) return;

        // 创建波纹圆
        var ripple = new Border
        {
            Width = 0,
            Height = 0,
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(RippleColor),
            IsHitTestVisible = false,
        };

        // 用 Canvas 定位到中心
        Canvas.SetLeft(ripple, origin.Value.X);
        Canvas.SetTop(ripple, origin.Value.Y);

        layer.Children.Add(ripple);

        // 动画：15 帧，约 250ms，EaseOutCubic
        const int frames = 15;
        for (int i = 1; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3); // CubicOut
            var r = eased * maxRadius;
            var d = r * 2;

            ripple.Width = d;
            ripple.Height = d;
            ripple.CornerRadius = new CornerRadius(r);
            ripple.Opacity = 0.45 * (1 - eased);
            Canvas.SetLeft(ripple, origin.Value.X - r);
            Canvas.SetTop(ripple, origin.Value.Y - r);

            await Task.Delay(16);
        }

        // 动画结束移除
        layer.Children.Remove(ripple);
    }
}
