using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 通用动效工具。全部基于 Avalonia Transitions（渲染线程驱动，替代 UI 线程逐帧循环），
/// 缓动曲线与时长统一取自 <see cref="MaterialMotion"/>（M3 令牌）。
/// 通过代数计数器防止快速连续触发时旧动画复位新动画的视觉状态。
/// </summary>
public static class AnimationHelper
{
    #region 代数计数器：仅最新一代动画允许清理视觉状态

    private static readonly ConditionalWeakTable<Control, Box> Generations = new();

    private sealed class Box
    {
        public int Value;
    }

    private static int NextGeneration(Control control)
    {
        var box = Generations.GetOrCreateValue(control);
        return ++box.Value;
    }

    private static bool IsStale(Control control, int generation) =>
        !Generations.TryGetValue(control, out var box) || box.Value != generation;

    /// <summary>等一次低优先级布局，确保初始隐藏/位移状态先被渲染一帧。</summary>
    private static async Task FlushAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    /// <summary>M3 入场过渡：不透明度匀速在前 40% 完成，位移用 emphasized-decelerate。</summary>
    private static Transitions CreateEnterTransitions(int durationMs) => new()
    {
        new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs * MaterialMotion.FadeEndFraction),
            Easing = MaterialMotion.LinearEasing
        },
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = MaterialMotion.EmphasizedDecelerateEasing
        }
    };

    /// <summary>M3 退出过渡：不透明度匀速在前 30% 消失，位移用 emphasized-accelerate。</summary>
    private static Transitions CreateExitTransitions(int durationMs) => new()
    {
        new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs * MaterialMotion.FadeEndFractionExit),
            Easing = MaterialMotion.LinearEasing
        },
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = MaterialMotion.EmphasizedAccelerateEasing
        }
    };

    #endregion

    #region 缩放微交互（按压 / 悬浮 / 弹跳）

    private static ScaleTransform EnsureScaleTransform(Control target)
    {
        if (target.RenderTransform is ScaleTransform st)
            return st;

        st = new ScaleTransform(1, 1);
        target.RenderTransform = st;
        target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        return st;
    }

    /// <summary>轻弹反馈：默认 1.06（原 1.12 过于夸张），三段式起-收-落。</summary>
    public static async Task BounceAsync(Control target, double scaleUp = 1.06, int durationMs = 300)
    {
        if (!AnimationGate.Enabled) return;
        var st = EnsureScaleTransform(target);
        await AnimateScaleAsync(st, 1.0, scaleUp, durationMs / 3, EasingType.CubicOut);
        await AnimateScaleAsync(st, scaleUp, 0.98, durationMs / 3, EasingType.CubicIn);
        await AnimateScaleAsync(st, 0.98, 1.0, durationMs / 3, EasingType.CubicOut);
    }

    /// <summary>按压反馈：轻压到 0.97（幅度克制才有质感，重压显卡通）。</summary>
    public static async Task PressAsync(Control target, int durationMs = 120)
    {
        if (!AnimationGate.Enabled) return;
        var st = EnsureScaleTransform(target);
        await AnimateScaleAsync(st, 1.0, 0.97, durationMs, EasingType.CubicIn);
    }

    /// <summary>松开回位：从当前值先微过冲 1.01 再落回 1.0，像松手后稳稳弹回。</summary>
    public static async Task ReleaseAsync(Control target, int durationMs = 240)
    {
        if (!AnimationGate.Enabled) return;
        var st = EnsureScaleTransform(target);
        await AnimateScaleAsync(st, st.ScaleX, 1.01, durationMs / 2, EasingType.CubicOut, target);
        await AnimateScaleAsync(st, 1.01, 1.0, durationMs / 2, EasingType.CubicOut, target);
    }

    /// <summary>悬浮放大：默认 1.02，轻盈的「呼吸感」而非夸张弹起。</summary>
    public static async Task HoverInAsync(Control target, int durationMs = 200, double hoverScale = 1.02)
    {
        if (!AnimationGate.Enabled) return;
        var st = EnsureScaleTransform(target);
        await AnimateScaleAsync(st, st.ScaleX, hoverScale, durationMs, EasingType.CubicOut, target);
    }

    public static async Task HoverOutAsync(Control target, int durationMs = 200)
    {
        if (!AnimationGate.Enabled) return;
        if (target.RenderTransform is not ScaleTransform st) return;
        await AnimateScaleAsync(st, st.ScaleX, 1.0, durationMs, EasingType.CubicOut, target);
    }

    internal static async Task AnimateScaleAsync(
        ScaleTransform st, double from, double to, int durationMs, EasingType easing, Control? owner = null)
    {
        if (!AnimationGate.Enabled) return;

        if (durationMs <= 0)
        {
            st.ScaleX = st.ScaleY = to;
            return;
        }

        // owner 存在时记录代数：新一轮缩放开始后，旧一轮不得清理 Transitions。
        // Transitions 自动以当前值为起点，快速连续触发时天然续播、不跳变。
        var generation = owner is null ? 0 : NextGeneration(owner);

        var transition = new DoubleTransition
        {
            Property = ScaleTransform.ScaleXProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = EasingFor(easing)
        };
        var transitionY = new DoubleTransition
        {
            Property = ScaleTransform.ScaleYProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = EasingFor(easing)
        };
        st.Transitions = new Transitions { transition, transitionY };
        st.ScaleX = st.ScaleY = to;

        await Task.Delay(durationMs);
        if (owner is not null && IsStale(owner, generation))
            return;
        st.Transitions = null;
    }

    private static Easing EasingFor(EasingType easing) => easing switch
    {
        // CubicIn/Out 语义分别映射到 M3 退出/进入曲线：收缩加速、回弹减速。
        EasingType.CubicIn => MaterialMotion.EmphasizedAccelerateEasing,
        EasingType.CubicOut => MaterialMotion.EmphasizedDecelerateEasing,
        _ => MaterialMotion.EmphasizedEasing
    };

    internal enum EasingType { CubicIn, CubicOut }

    #endregion

    #region 透明度 / 页面切换 / 列表错峰入场

    public static async Task FadeInAsync(Visual target, int durationMs = 300)
    {
        if (!AnimationGate.Enabled || target is not Control control) return;
        var generation = NextGeneration(control);

        control.Transitions = null;
        control.Opacity = 0;
        await FlushAsync();
        if (IsStale(control, generation)) return;

        control.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Easing = MaterialMotion.LinearEasing
            }
        };
        control.Opacity = 1;

        await Task.Delay(durationMs);
        if (IsStale(control, generation)) return;
        control.Transitions = null;
    }

    /// <summary>
    /// 页面切换动效：不透明度匀速淡入（前 40% 完成）+ 自下方上浮（emphasized-decelerate），
    /// 结束后清除 Transitions 与 RenderTransform。
    /// </summary>
    public static async Task SlideFadeInAsync(Visual target, int durationMs = 300, double slideOffset = 24)
    {
        if (!AnimationGate.Enabled || target is not Control control) return;
        var generation = NextGeneration(control);

        control.Transitions = null;
        control.IsHitTestVisible = true;
        control.Opacity = 0;
        control.RenderTransform = TransformOperations.Parse($"translateY({slideOffset}px)");
        await FlushAsync();
        if (IsStale(control, generation)) return;

        control.Transitions = CreateEnterTransitions(durationMs);
        control.Opacity = 1;
        control.RenderTransform = TransformOperations.Parse("translateY(0px)");

        await Task.Delay(durationMs);
        if (IsStale(control, generation)) return;
        control.Transitions = null;
        control.RenderTransform = null;
    }

    /// <summary>
    /// 页面退出动效：不透明度匀速淡出（前 30% 消失）+ 向下沉落（emphasized-accelerate），
    /// 与入场方向对称。Transitions 自动从当前值续播，快速连续切换不会跳变；
    /// 结束后保留 Opacity=0，由调用方在元素脱离视觉树后复位（缓存页面复用前必须恢复 Opacity=1）。
    /// </summary>
    public static async Task SlideFadeOutAsync(Visual target, int durationMs = 180, double slideOffset = 16)
    {
        if (!AnimationGate.Enabled || target is not Control control) return;
        var generation = NextGeneration(control);
        control.IsHitTestVisible = false;

        var startY = control.RenderTransform is TranslateTransform existing ? existing.Y : 0d;
        control.Transitions = null;
        control.RenderTransform = TransformOperations.Parse($"translateY({startY}px)");
        await FlushAsync();
        if (IsStale(control, generation)) return;

        control.Transitions = CreateExitTransitions(durationMs);
        control.Opacity = 0;
        control.RenderTransform = TransformOperations.Parse($"translateY({slideOffset}px)");

        await Task.Delay(durationMs);
        if (IsStale(control, generation)) return;
        control.Transitions = null;
        // 保留 Opacity=0 与下沉位移：由下一次入场或调用方复位
    }

    /// <summary>
    /// 列表错峰入场（M3 stagger）：逐项延迟播放入场动效。
    /// 总延迟受 <see cref="MaterialMotion.MaxStaggerTotalDelayMs"/> 封顶：项数很多时
    /// 自动压缩逐项间隔，保证整段编排仍在数百毫秒内完成，尾项不会迟迟不出现。
    /// </summary>
    public static async Task StaggerInAsync(
        IEnumerable<Control> items, int perItemDelayMs = 45, int durationMs = 300, double slideOffset = 18)
    {
        if (!AnimationGate.Enabled) return;
        var list = items.ToList();
        if (list.Count == 0) return;

        var perItem = list.Count > 1
            ? Math.Min(perItemDelayMs, MaterialMotion.MaxStaggerTotalDelayMs / (list.Count - 1))
            : perItemDelayMs;

        foreach (var item in list)
        {
            item.Transitions = null;
            item.Opacity = 0;
            item.RenderTransform = TransformOperations.Parse($"translateY({slideOffset}px)");
        }

        var tasks = new List<Task>(list.Count);
        for (var i = 0; i < list.Count; i++)
            tasks.Add(DelayedSlideFadeIn(list[i], i * perItem, durationMs, slideOffset));
        await Task.WhenAll(tasks);
    }

    private static async Task DelayedSlideFadeIn(
        Control control, int delayMs, int durationMs, double slideOffset)
    {
        if (delayMs > 0)
            await Task.Delay(delayMs);

        var generation = NextGeneration(control);
        await FlushAsync();
        if (IsStale(control, generation)) return;

        control.Transitions = CreateEnterTransitions(durationMs);
        control.Opacity = 1;
        control.RenderTransform = TransformOperations.Parse("translateY(0px)");

        await Task.Delay(durationMs);
        if (IsStale(control, generation)) return;
        control.Transitions = null;
        control.RenderTransform = null;
    }

    /// <summary>
    /// 下拉框弹出面板的入场动效：淡入 + 自 0.75 缩放展开（M3 medium 时长 + emphasized-decelerate）。
    /// </summary>
    internal static async Task AnimateDropDownInAsync(Control child)
    {
        child.Transitions = null;
        child.Opacity = 0;
        child.RenderTransform = TransformOperations.Parse("scaleY(0.75)");
        child.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);

        var generation = NextGeneration(child);
        await FlushAsync();
        if (IsStale(child, generation)) return;

        child.Transitions = CreateEnterTransitions(MaterialMotion.MediumTransitionMs);
        child.Opacity = 1;
        child.RenderTransform = TransformOperations.Parse("scaleY(1)");

        await Task.Delay(MaterialMotion.MediumTransitionMs);
        if (IsStale(child, generation)) return;
        child.Transitions = null;
        child.RenderTransform = null;
    }

    #endregion
}

public class BounceBehavior
{
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

    public static void AttachHoverScale(Control control, double hoverScale = 1.02)
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
            if (!AnimationGate.Enabled) return;
            var popup = FindComboBoxPopup(comboBox);
            if (popup?.Child is not Control child) return;
            _ = AnimationHelper.AnimateDropDownInAsync(child);
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
}
