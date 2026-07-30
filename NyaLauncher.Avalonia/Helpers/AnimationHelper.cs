using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Helpers;

/// <summary>
/// 弹性动效辅助类 — 给交互元素带来 Q 弹手感
/// </summary>
public static class AnimationHelper
{
    /// <summary>
    /// 对目标元素执行 Q 弹缩放动画
    /// 从 1.0 → 1.12 → 0.96 → 1.0（带过冲的弹性效果）
    /// </summary>
    public static async Task BounceAsync(Control target, double scaleUp = 1.12, int durationMs = 350)
    {
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        var startScale = 1.0;
        var peakScale = scaleUp;           // 弹起
        var overshootScale = 0.96;         // 回弹过冲
        var settleScale = 1.0;             // 归位

        // 分段弹性曲线
        await AnimateScaleAsync(st, startScale, peakScale, durationMs / 3, EasingType.CubicOut);
        await AnimateScaleAsync(st, peakScale, overshootScale, durationMs / 3, EasingType.CubicIn);
        await AnimateScaleAsync(st, overshootScale, settleScale, durationMs / 3, EasingType.CubicOut);
    }

    /// <summary>
    /// 按下时的缩放反馈 (1.0 → 0.92)
    /// </summary>
    public static async Task PressAsync(Control target, int durationMs = 120)
    {
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        await AnimateScaleAsync(st, 1.0, 0.92, durationMs, EasingType.CubicIn);
    }

    /// <summary>
    /// 释放时的回弹动画 (0.92 → 1.0 带一点过冲)
    /// </summary>
    public static async Task ReleaseAsync(Control target, int durationMs = 300)
    {
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        await AnimateScaleAsync(st, 0.92, 1.05, durationMs / 2, EasingType.CubicOut);
        await AnimateScaleAsync(st, 1.05, 1.0, durationMs / 2, EasingType.CubicOut);
    }

    /// <summary>
    /// 鼠标悬停进入放大
    /// </summary>
    public static async Task HoverInAsync(Control target, int durationMs = 200)
    {
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        await AnimateScaleAsync(st, 1.0, 1.05, durationMs, EasingType.CubicOut);
    }

    /// <summary>
    /// 鼠标悬停离开恢复
    /// </summary>
    public static async Task HoverOutAsync(Control target, int durationMs = 200)
    {
        if (target.RenderTransform is not ScaleTransform st) return;
        await AnimateScaleAsync(st, 1.05, 1.0, durationMs, EasingType.CubicOut);
    }

    /// <summary>
    /// 页面内容渐入
    /// </summary>
    public static async Task FadeInAsync(Visual target, int durationMs = 300)
    {
        target.Opacity = 0;
        // 等待一帧确保 Opacity = 0 已生效
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var frames = Math.Max(1, durationMs / 16);
        for (int i = 1; i <= frames; i++)
        {
            target.Opacity = i / (double)frames;
            // 使用平滑步进
            target.Opacity = SmoothStep(0, 1, i / (double)frames);
            // 16ms ≈ 60fps
            await Task.Delay(16);
        }
        target.Opacity = 1;
    }

    // ===== 内部工具 =====

    /// <summary>
    /// 跟踪每个控件上正在运行的缩放动画，用于取消旧动画防止抽搐
    /// </summary>
    private static readonly ConcurrentDictionary<Control, CancellationTokenSource> _activeScales = new();

    internal static async Task AnimateScaleAsync(
        ScaleTransform st, double from, double to,
        int durationMs, EasingType easing,
        Control? owner = null)
    {
        CancellationToken ct = default;
        CancellationTokenSource? cts = null;

        if (owner != null)
        {
            // 取消该控件上正在运行的旧动画
            if (_activeScales.TryRemove(owner, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            cts = new CancellationTokenSource();
            _activeScales[owner] = cts;
            ct = cts.Token;
        }

        if (durationMs <= 0) { st.ScaleX = st.ScaleY = to; return; }

        try
        {
            var frames = Math.Max(1, durationMs / 16);
            for (int i = 1; i <= frames; i++)
            {
                ct.ThrowIfCancellationRequested();
                var t = i / (double)frames;
                var eased = ApplyEasing(t, easing);
                var val = from + (to - from) * eased;
                st.ScaleX = st.ScaleY = val;
                await Task.Delay(16, ct);
            }
            st.ScaleX = st.ScaleY = to;
        }
        catch (OperationCanceledException)
        {
            // 被新动画取消，静默退出
        }
        finally
        {
            // ★ 关键修复：只有字典中仍然指向我们的 cts 才清理，
            //   避免旧 animation 的 finally 误删新 animation 的 cts
            if (owner != null && cts != null)
            {
                if (_activeScales.TryGetValue(owner, out var current) && current == cts)
                    _activeScales.TryRemove(owner, out _);
            }
        }
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    internal enum EasingType { CubicIn, CubicOut }

    internal static double ApplyEasing(double t, EasingType easing) => easing switch
    {
        EasingType.CubicIn => t * t * t,
        EasingType.CubicOut => 1 - Math.Pow(1 - t, 3),
        _ => t
    };
}

/// <summary>
/// 为 Control 附加 Pointer 事件，自动触发弹性动画
/// </summary>
public class BounceBehavior
{
    /// <summary>
    /// 为按钮附加弹跳效果（PointerEntered → 放大, PointerExited → 恢复, Click → 按压回弹）
    /// </summary>
    public static void AttachBounce(Button button)
    {
        button.PointerEntered += async (_, _) =>
        {
            await AnimationHelper.HoverInAsync(button);
        };
        button.PointerExited += async (_, _) =>
        {
            await AnimationHelper.HoverOutAsync(button);
        };
        button.Click += async (_, _) =>
        {
            await AnimationHelper.PressAsync(button);
            await AnimationHelper.ReleaseAsync(button);
            await AnimationHelper.BounceAsync(button);
        };
    }

    /// <summary>
    /// 为任意控件附加悬停缩放
    /// </summary>
    public static void AttachHoverScale(Control control, double hoverScale = 1.05)
    {
        control.PointerEntered += async (_, _) =>
        {
            await AnimationHelper.HoverInAsync(control);
        };
        control.PointerExited += async (_, _) =>
        {
            await AnimationHelper.HoverOutAsync(control);
        };
    }

    /// <summary>
    /// 为任意控件附加点击弹跳
    /// </summary>
    public static void AttachClickBounce(Control control)
    {
        control.PointerPressed += async (_, _) =>
        {
            await AnimationHelper.PressAsync(control);
        };
        control.PointerReleased += async (_, _) =>
        {
            await AnimationHelper.ReleaseAsync(control);
        };
    }

    /// <summary>
    /// 为 ComboBox 附加下拉弹出动效（淡入 + 纵向缩放展开）
    /// 核心技巧：利用 DropDownOpened 同步设初始态（渲染前生效），
    /// 然后用 DispatcherTimer 逐帧弹出。
    /// </summary>
    public static void AttachDropDownAnimation(ComboBox comboBox)
    {
        comboBox.DropDownOpened += (_, _) =>
        {
            // 找到内部 Popup 的内容控件
            var popup = FindComboBoxPopup(comboBox);
            if (popup?.Child is not Control child) return;

            // ★ 同步设置隐藏态（在渲染管线刷新前生效，不会闪烁）
            child.Opacity = 0;
            var scaleTransform = new ScaleTransform(1, 0.75);
            child.RenderTransform = scaleTransform;
            child.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);

            // DispatcherTimer 逐帧弹出
            var frame = 0;
            const int totalFrames = 24;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                frame++;
                var t = frame / (double)totalFrames;
                if (t >= 1.0)
                {
                    timer.Stop();
                    child.Opacity = 1;
                    child.RenderTransform = null;
                    return;
                }
                var eased = 1 - Math.Pow(1 - t, 3); // CubicOut
                scaleTransform.ScaleY = 0.75 + 0.25 * eased;
                child.Opacity = t * t * (3 - 2 * t); // SmoothStep
            };
            timer.Start();
        };

        comboBox.DropDownClosed += (_, _) =>
        {
            // 关闭后无需额外操作，下次打开会重新设初始态
        };
    }

    /// <summary>
    /// 递归找 ComboBox 内部模板里的 Popup
    /// </summary>
    private static Popup? FindComboBoxPopup(ComboBox comboBox)
    {
        foreach (var desc in comboBox.GetVisualDescendants())
        {
            if (desc is Popup popup)
                return popup;
        }
        return null;
    }

    /// <summary>
    /// 为 ItemsControl 的子项附加悬停缩放 + 点击回弹 + 水波纹
    /// 每次 ItemsSource 更新后调用，等容器生成后自动附加动效
    /// </summary>
    public static async System.Threading.Tasks.Task AttachListItemEffectsAsync(
        ItemsControl itemsControl,
        double hoverScale = 1.03,
        Canvas? rippleLayer = null)
    {
        await EnsureItemContainersReadyAsync(itemsControl);

        var items = new List<Control>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            items.Clear();
            CollectItemContainers(itemsControl, items);
            if (items.Count > 0)
                break;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await System.Threading.Tasks.Task.Delay(50);
        }

        foreach (var item in items)
        {
            // ★ 局部变量避免闭包捕获问题
            var captured = item;

            // 悬停：非线性微微放大（PointerEntered/Exited）
            captured.PointerEntered += async (_, _) =>
            {
                if (captured.RenderTransform is not ScaleTransform st)
                {
                    st = new ScaleTransform(1, 1);
                    captured.RenderTransform = st;
                    captured.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                }
                await AnimationHelper.AnimateScaleAsync(st, 1.0, hoverScale, 180, AnimationHelper.EasingType.CubicOut, captured);
            };
            captured.PointerExited += async (_, _) =>
            {
                if (captured.RenderTransform is ScaleTransform st)
                {
                    await AnimationHelper.AnimateScaleAsync(st, hoverScale, 1.0, 180, AnimationHelper.EasingType.CubicOut, captured);
                }
            };

            // 单击：按压回弹（Press → Release → Bounce）
            captured.PointerPressed += async (_, e) =>
            {
                if (e.GetCurrentPoint(captured).Properties.IsLeftButtonPressed)
                {
                    if (captured.RenderTransform is not ScaleTransform st)
                    {
                        st = new ScaleTransform(1, 1);
                        captured.RenderTransform = st;
                        captured.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                    }
                    await AnimationHelper.AnimateScaleAsync(st, hoverScale, 0.92, 100, AnimationHelper.EasingType.CubicIn, captured);
                }
            };
            captured.PointerReleased += async (_, _) =>
            {
                if (captured.RenderTransform is ScaleTransform st)
                {
                    // 回弹：0.92 → 1.05 → 1.0
                    await AnimationHelper.AnimateScaleAsync(st, 0.92, 1.05, 120, AnimationHelper.EasingType.CubicOut, captured);
                    await AnimationHelper.AnimateScaleAsync(st, 1.05, 1.0, 100, AnimationHelper.EasingType.CubicOut, captured);
                }
            };

            // 水波纹
            if (rippleLayer != null)
            {
                RippleBehavior.AttachRipple(captured, rippleLayer);
            }
        }
    }

    /// <summary>
    /// 收集 ItemsControl 的列表项容器（DataTemplate 根元素）。
    /// 只找 ContentPresenter 内部的 Border/Panel，避免误抓模板级外层 Border。
    /// </summary>
    private static void CollectItemContainers(Visual parent, List<Control> results)
    {
        foreach (var desc in parent.GetVisualDescendants())
        {
            if (desc is ContentPresenter cp)
            {
                if (TryFindItemRoot(cp, out var itemRoot))
                {
                    results.Add(itemRoot);
                }
            }
        }
    }

    private static bool TryFindItemRoot(Visual visual, out Control result)
    {
        foreach (var child in visual.GetVisualChildren())
        {
            if (child is Border || child is Panel)
            {
                result = (Control)child;
                return true;
            }

            if (TryFindItemRoot(child, out result))
                return true;
        }

        result = default!;
        return false;
    }

    private static async System.Threading.Tasks.Task EnsureItemContainersReadyAsync(ItemsControl itemsControl)
    {
        var attempts = 0;
        while (attempts < 10)
        {
            if (!itemsControl.IsAttachedToVisualTree())
            {
                var tcs = new TaskCompletionSource<bool>();
                void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
                {
                    tcs.TrySetResult(true);
                    itemsControl.AttachedToVisualTree -= OnAttached;
                }

                itemsControl.AttachedToVisualTree += OnAttached;
                if (!itemsControl.IsAttachedToVisualTree())
                    await tcs.Task;
            }

            var containers = itemsControl.GetVisualDescendants().OfType<ContentPresenter>().ToList();
            var itemRoots = containers
                .SelectMany(cp => cp.GetVisualChildren())
                .Where(child => child is Border || child is Panel)
                .Cast<Control>()
                .ToList();

            if (itemRoots.Count > 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await System.Threading.Tasks.Task.Delay(50);
            attempts++;
        }
    }
}
