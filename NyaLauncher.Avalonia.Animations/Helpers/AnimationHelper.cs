using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

public static class AnimationHelper
{
    public static async Task BounceAsync(Control target, double scaleUp = 1.12, int durationMs = 350)
    {
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        await AnimateScaleAsync(st, 1.0, scaleUp, durationMs / 3, EasingType.CubicOut);
        await AnimateScaleAsync(st, scaleUp, 0.96, durationMs / 3, EasingType.CubicIn);
        await AnimateScaleAsync(st, 0.96, 1.0, durationMs / 3, EasingType.CubicOut);
    }

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

    public static async Task HoverInAsync(Control target, int durationMs = 200, double hoverScale = 1.05)
    {
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        await AnimateScaleAsync(st, st.ScaleX, hoverScale, durationMs, EasingType.CubicOut, target);
    }

    public static async Task HoverOutAsync(Control target, int durationMs = 200)
    {
        if (target.RenderTransform is not ScaleTransform st) return;
        await AnimateScaleAsync(st, st.ScaleX, 1.0, durationMs, EasingType.CubicOut, target);
    }

    public static async Task FadeInAsync(Visual target, int durationMs = 300)
    {
        target.Opacity = 0;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var frames = Math.Max(1, durationMs / 16);
        for (int i = 1; i <= frames; i++)
        {
            target.Opacity = SmoothStep(0, 1, i / (double)frames);
            await Task.Delay(16);
        }
        target.Opacity = 1;
    }

    private static readonly ConcurrentDictionary<Control, CancellationTokenSource> _activeScales = new();

    internal static async Task AnimateScaleAsync(ScaleTransform st, double from, double to, int durationMs, EasingType easing, Control? owner = null)
    {
        CancellationToken ct = default;
        CancellationTokenSource? cts = null;
        if (owner != null)
        {
            if (_activeScales.TryRemove(owner, out var oldCts))
            {
                oldCts.Cancel();
            }
            cts = new CancellationTokenSource();
            _activeScales[owner] = cts;
            ct = cts.Token;
        }

        try
        {
            if (durationMs <= 0)
            {
                st.ScaleX = st.ScaleY = to;
                return;
            }

            // An interrupted interaction must continue from the value currently on
            // screen. Restarting from a fixed endpoint makes rapid enter/exit events
            // visibly jump between scales.
            from = st.ScaleX;
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
        catch (OperationCanceledException) { }
        finally
        {
            if (owner != null && cts != null)
            {
                if (_activeScales.TryGetValue(owner, out var current) && current == cts)
                    _activeScales.TryRemove(owner, out _);
                cts.Dispose();
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

public class BounceBehavior
{
    private static readonly HashSet<Control> _attachedInteractiveControls = new();
    private static readonly object _attachedInteractiveControlsLock = new();

    public static void AttachBounce(Button button)
    {
        button.PointerEntered += async (_, _) => await AnimationHelper.HoverInAsync(button);
        button.PointerExited += async (_, _) => await AnimationHelper.HoverOutAsync(button);
        button.Click += async (_, _) =>
        {
            await AnimationHelper.PressAsync(button);
            await AnimationHelper.ReleaseAsync(button);
            await AnimationHelper.BounceAsync(button);
        };
    }

    public static void AttachHoverScale(Control control, double hoverScale = 1.05)
    {
        control.PointerEntered += async (_, _) => await AnimationHelper.HoverInAsync(control, hoverScale: hoverScale);
        control.PointerExited += async (_, _) => await AnimationHelper.HoverOutAsync(control);
    }

    public static void AttachClickBounce(Control control)
    {
        control.PointerPressed += async (_, _) => await AnimationHelper.PressAsync(control);
        control.PointerReleased += async (_, _) => await AnimationHelper.ReleaseAsync(control);
    }

    public static void AttachDropDownAnimation(ComboBox comboBox)
    {
        comboBox.DropDownOpened += (_, _) =>
        {
            var popup = FindComboBoxPopup(comboBox);
            if (popup?.Child is not Control child) return;
            child.Opacity = 0;
            var scaleTransform = new ScaleTransform(1, 0.75);
            child.RenderTransform = scaleTransform;
            child.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
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
                var eased = 1 - Math.Pow(1 - t, 3);
                scaleTransform.ScaleY = 0.75 + 0.25 * eased;
                child.Opacity = t * t * (3 - 2 * t);
            };
            timer.Start();
        };
    }

    private static Popup? FindComboBoxPopup(ComboBox comboBox)
    {
        foreach (var desc in comboBox.GetVisualDescendants())
        {
            if (desc is Popup popup)
                return popup;
        }
        return null;
    }

    public static async System.Threading.Tasks.Task AttachListItemEffectsAsync(ItemsControl itemsControl, double hoverScale = 1.03, Canvas? rippleLayer = null)
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
            await Task.Delay(50);
        }

        foreach (var item in items)
        {
            lock (_attachedInteractiveControlsLock)
            {
                if (!_attachedInteractiveControls.Add(item))
                    continue;
            }

            var captured = item;
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
                    await AnimationHelper.AnimateScaleAsync(st, hoverScale, 1.0, 180, AnimationHelper.EasingType.CubicOut, captured);
            };
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
                    await AnimationHelper.AnimateScaleAsync(st, 0.92, 1.05, 120, AnimationHelper.EasingType.CubicOut, captured);
                    await AnimationHelper.AnimateScaleAsync(st, 1.05, 1.0, 100, AnimationHelper.EasingType.CubicOut, captured);
                }
            };
            if (rippleLayer != null)
                RippleBehavior.AttachRipple(captured, rippleLayer);
        }
    }

    private static void CollectItemContainers(Visual parent, List<Control> results)
    {
        foreach (var desc in parent.GetVisualDescendants())
        {
            if (desc is ContentPresenter cp && TryFindItemRoot(cp, out var itemRoot))
                results.Add(itemRoot);
        }
    }

    private static bool TryFindItemRoot(Visual visual, out Control result)
    {
        foreach (var child in visual.GetVisualChildren())
        {
            if (child is Border or Panel)
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

    private static async Task EnsureItemContainersReadyAsync(ItemsControl itemsControl)
    {
        if (!itemsControl.IsAttachedToVisualTree())
        {
            var attachedTcs = new TaskCompletionSource<bool>();
            void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
            {
                itemsControl.AttachedToVisualTree -= OnAttached;
                attachedTcs.TrySetResult(true);
            }
            itemsControl.AttachedToVisualTree += OnAttached;
            if (!itemsControl.IsAttachedToVisualTree())
                await attachedTcs.Task;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var containers = itemsControl.GetVisualDescendants().OfType<ContentPresenter>().ToList();
            var itemRoots = containers.SelectMany(cp => cp.GetVisualChildren()).Where(child => child is Border || child is Panel).Cast<Control>().ToList();
            if (itemRoots.Count > 0)
                return;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(16);
        }
    }
}
