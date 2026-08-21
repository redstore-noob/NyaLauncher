using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

public static class RippleBehavior
{
    private static readonly Color RippleColor = Color.FromArgb(55, 255, 255, 255);
    public static Canvas? GlobalRippleLayer { get; set; }

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
        var cx = control.Bounds.Width / 2;
        var cy = control.Bounds.Height / 2;
        var origin = control.TranslatePoint(new Point(cx, cy), layer);
        if (origin == null) return;

        var maxRadius = Math.Sqrt(control.Bounds.Width * control.Bounds.Width + control.Bounds.Height * control.Bounds.Height) * 0.9;
        if (maxRadius < 4) return;

        var ripple = new Border
        {
            Width = 0,
            Height = 0,
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(RippleColor),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(ripple, origin.Value.X);
        Canvas.SetTop(ripple, origin.Value.Y);
        layer.Children.Add(ripple);

        try
        {
            const int frames = 15;
            for (int i = 1; i <= frames; i++)
            {
                var t = i / (double)frames;
                var eased = 1 - Math.Pow(1 - t, 3);
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
        }
        finally
        {
            layer.Children.Remove(ripple);
        }
    }
}
