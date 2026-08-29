using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 环形下载进度控件：track 圆环 + 按 <see cref="Value"/>（0~100）填充的进度弧（主题渐变、圆头端帽）+
/// 弧末端高光点。替代普通 ProgressBar 用于下载进度。全部逻辑只在本模块。
/// </summary>
public class RingProgressControl : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RingProgressControl, double>("Value", 0);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<RingProgressControl, double>("Thickness", 6);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RingProgressControl, IBrush?>("TrackBrush", null);

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<RingProgressControl, IBrush?>("ProgressBrush", null);

    static RingProgressControl()
    {
        AffectsRender<RingProgressControl>(ValueProperty, ThicknessProperty, TrackBrushProperty, ProgressBrushProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var thickness = Math.Clamp(Thickness, 2, Math.Min(w, h) / 2 - 1);
        var rect = new Rect(thickness / 2, thickness / 2, w - thickness, h - thickness);
        var center = rect.Center;
        var radius = rect.Width / 2;
        if (radius <= 0) return;

        // 底环
        context.DrawEllipse(null, new Pen(TrackBrush ?? TrackDefault(), thickness), center, radius, radius);

        var pct = Math.Clamp(Value, 0, 100);
        if (pct <= 0) return;

        // 进度弧：从 12 点方向顺时针
        var startAngle = -Math.PI / 2;
        var endAngle = startAngle + pct / 100.0 * 2 * Math.PI;
        var start = new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle));
        var end = new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, pct > 50, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        var progressPen = new Pen(ProgressBrush ?? ProgressDefault(), thickness) { LineCap = PenLineCap.Round };
        context.DrawGeometry(null, progressPen, geo);

        // 弧末端高光点（有"流光"感）
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), null, end, thickness * 0.9, thickness * 0.9);
    }

    private static IBrush TrackDefault() => new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));

    private static IBrush ProgressDefault()
    {
        var accent = ResolveAccent();
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(accent, 0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromRgb(
                (byte)Math.Min(255, accent.R + 70),
                (byte)Math.Min(255, accent.G + 70),
                (byte)Math.Min(255, accent.B + 70)), 1));
        return brush;
    }

    private static Color ResolveAccent()
    {
        if (Application.Current?.TryGetResource("SystemAccentColor", null, out var acc) == true && acc is Color c)
            return c;
        return Color.FromRgb(110, 170, 255);
    }
}
