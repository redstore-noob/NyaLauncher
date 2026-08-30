using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 主界面自定义背景图：在宿主容器（Grid）注入一个全 Stretch 的图片层
/// （ZIndex 210：盖在工作区内容之上、抽屉遮罩 240 / 导航页面 300 之下），
/// 不透明度可调。路径与不透明度由设置页「个性化」卡片驱动并持久化；
/// 未设置路径或文件不存在时层保持隐藏。全部逻辑只在本模块。
/// </summary>
public static class CustomBackgroundImage
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(CustomBackgroundImage), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>手动启用（供主工程兜底调用；class 已启用时幂等）。</summary>
    public static void Enable(Control host)
    {
        if (host is not null) SetEnabled(host, true);
    }

    /// <summary>当前背景图路径（null/空/文件不存在 → 隐藏层）。</summary>
    public static string? ImagePath { get; private set; }

    /// <summary>背景图不透明度（0.05–0.85），默认 0.35。</summary>
    public static double Opacity { get; private set; } = 0.35;

    /// <summary>背景图高斯模糊半径（0–30，0 = 关闭），默认 0。</summary>
    public static double BlurRadius { get; private set; } = 0;

    private sealed class LayerState
    {
        public LayerState()
        {
            Layer.Child = Image;
            // 显示/隐藏时 300ms 淡入淡出（M3 medium 转场，渲染线程驱动）
            Layer.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(MaterialMotion.MediumTransitionMs),
                    Easing = MaterialMotion.EmphasizedEasing,
                }
            };
        }

        public Image Image { get; } = new()
        {
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false,
        };

        public Border Layer { get; } = new()
        {
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0,
        };

        public string? AppliedPath;
    }

    private static readonly ConcurrentDictionary<Control, LayerState> States = new();

    /// <summary>挂载过 Enabled 的宿主集合：层被移除后仍保留引用，供 SetImage/Refresh 找回。</summary>
    private static readonly ConditionalWeakTable<Control, object> EnabledHosts = new();

    /// <summary>图片解码代际：快速连续换图时丢弃过期解码结果。</summary>
    private static int _loadGeneration;

    static CustomBackgroundImage()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            EnabledHosts.Remove(control);
            EnabledHosts.Add(control, new object());
            WhenAttached(control, () => Attach(control));
        }
        else
        {
            EnabledHosts.Remove(control);
            Detach(control);
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

    private static void Attach(Control control)
    {
        if (control is not Grid grid) return;
        if (!States.TryGetValue(control, out var state))
        {
            state = new LayerState();
            States[control] = state;
        }
        if (state.Layer.Parent is Grid current && !ReferenceEquals(current, grid))
            current.Children.Remove(state.Layer);
        if (state.Layer.Parent is null)
        {
            Grid.SetRow(state.Layer, 0);
            Grid.SetColumn(state.Layer, 0);
            if (grid.RowDefinitions.Count > 0) Grid.SetRowSpan(state.Layer, grid.RowDefinitions.Count);
            if (grid.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(state.Layer, grid.ColumnDefinitions.Count);
            state.Layer.ZIndex = 210;
            grid.Children.Add(state.Layer);
        }
        Apply(state);
    }

    private static void Detach(Control control)
    {
        if (States.TryRemove(control, out var state))
        {
            (state.Layer.Parent as Panel)?.Children.Remove(state.Layer);
        }
    }

    /// <summary>按当前静态状态应用层（显隐 / 不透明度 / 解码图片）。</summary>
    private static void Apply(LayerState state)
    {
        var path = ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _loadGeneration++;
            state.AppliedPath = null;
            state.Image.Source = null;
            state.Layer.IsVisible = false;
            state.Layer.Opacity = 0;
            return;
        }

        state.Layer.IsVisible = true;
        state.Layer.Opacity = Opacity;
        state.Image.Effect = BlurRadius > 0 ? new BlurEffect { Radius = BlurRadius } : null;
        if (string.Equals(state.AppliedPath, path, StringComparison.OrdinalIgnoreCase)) return;
        state.AppliedPath = path;

        var generation = ++_loadGeneration;
        Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
            // 大图解码到 1600 宽即可，避免 4K 原图占显存
            return Bitmap.DecodeToWidth(stream, 1600);
        }).ContinueWith(t =>
        {
            if (!t.IsFaulted && t.Result is { } bitmap)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation != _loadGeneration)
                    {
                        bitmap.Dispose();
                        return;
                    }
                    state.Image.Source = bitmap;
                });
            }
        });
    }

    /// <summary>设置背景图路径（null/空 = 关闭），对所有已注入宿主立即生效。</summary>
    public static void SetImage(string? path)
    {
        ImagePath = string.IsNullOrWhiteSpace(path) ? null : path;
        foreach (var host in EnabledHosts)
        {
            if (host.Key is Control control && control.IsAttachedToVisualTree() &&
                States.TryGetValue(control, out var state))
                Apply(state);
        }
    }

    /// <summary>设置背景图不透明度（0.05–0.85），立即生效。</summary>
    public static void SetOpacity(double value)
    {
        Opacity = Math.Clamp(value, 0.05, 0.85);
        foreach (var host in EnabledHosts)
        {
            if (host.Key is Control control && control.IsAttachedToVisualTree() &&
                States.TryGetValue(control, out var state) && state.Layer.IsVisible)
                state.Layer.Opacity = Opacity;
        }
    }

    /// <summary>设置背景图高斯模糊半径（0–30，0 = 关闭），立即生效（毛玻璃效果，提升文字可读性）。</summary>
    public static void SetBlur(double radius)
    {
        BlurRadius = Math.Clamp(radius, 0, 30);
        foreach (var host in EnabledHosts)
        {
            if (host.Key is Control control && control.IsAttachedToVisualTree() &&
                States.TryGetValue(control, out var state))
                state.Image.Effect = BlurRadius > 0 ? new BlurEffect { Radius = BlurRadius } : null;
        }
    }
}
