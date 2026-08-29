using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 主窗口出入场 + 状态切换动效：
/// 创建时「飞出来」（内容自下方上移 + 轻微放大 + 淡入），关闭时「飞走」（缩小 + 上飘 + 淡出）；
/// 最小化时内容向任务栏方向收缩淡出，最大化/还原时做轻微缩放确认动画，从任务栏恢复时放大淡入。
/// 动画作用于 <see cref="Window.Content"/>（视觉根），同时用 Window.Opacity 做整窗淡入淡出；
/// 全部基于 Avalonia Transitions（渲染线程驱动），缓动与时长取自 <see cref="MaterialMotion"/>（M3 令牌）。
/// 全部逻辑只在本模块，主工程只调用公开方法。
/// </summary>
public static class WindowEffects
{
    /// <summary>窗口状态动画进行中标记：防止最小化/最大化/恢复动画互相叠加冲突。</summary>
    private static bool _transitioning;

    /// <summary>等一次低优先级布局，确保初始状态先被渲染一帧。</summary>
    private static async Task FlushAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    /// <summary>整窗淡入/淡出的不透明度过渡（匀速，M3 fade 语义）。</summary>
    private static DoubleTransition WindowFade(int durationMs, double fadeFraction) => new()
    {
        Property = Visual.OpacityProperty,
        Duration = TimeSpan.FromMilliseconds(durationMs * fadeFraction),
        Easing = MaterialMotion.LinearEasing
    };

    /// <summary>
    /// 入场：主窗口飞出来（M3 大型转场规格 400ms）。在 <see cref="Window.Opened"/>（或首次显示后）调用。
    /// 编排：整窗先快速淡入（前 40%），内容从下方 28px + 96% 缩放落座到原位（emphasized-decelerate），
    /// 窗框先现、内容后坐，形成层次感；播完复位 RenderTransform 与 Transitions。
    /// 动画总开关关闭时直接返回，窗口保持默认可见。
    /// </summary>
    /// <param name="window">要播放入场动效的窗口。</param>
    /// <param name="durationMs">内容落座时长；默认 <see cref="MaterialMotion.LargeTransitionMs"/>。</param>
    public static async void Enter(Window window, int durationMs = MaterialMotion.LargeTransitionMs)
    {
        // 动画总开关关闭时不播入场，窗口保持默认可见
        if (!AnimationGate.Enabled)
            return;

        if (window.Content is not Control content)
            return;

        try
        {
            content.Transitions = null;
            window.Transitions = null;
            content.Opacity = 0;
            window.Opacity = 0;
            content.RenderTransform = TransformOperations.Parse("translateY(28px) scale(0.96)");
            content.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            await FlushAsync();

            content.Transitions = new Transitions
            {
                WindowFade(durationMs, MaterialMotion.FadeEndFraction),
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    Easing = MaterialMotion.EmphasizedDecelerateEasing
                }
            };
            window.Transitions = new Transitions { WindowFade(durationMs, MaterialMotion.FadeEndFraction) };
            content.Opacity = 1;
            window.Opacity = 1;
            content.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");

            await Task.Delay(durationMs);
        }
        catch (Exception)
        {
            // 动画失败也要保证窗口正常可见
        }
        finally
        {
            content.Opacity = 1;
            window.Opacity = 1;
            content.Transitions = null;
            window.Transitions = null;
            content.RenderTransform = null;
        }
    }

    /// <summary>
    /// 退场：主窗口飞走。在真正关闭前调用，播完回调 onCompleted（通常是 Close()）。
    /// 内容轻微收缩 1→0.96 + 上飘 16px（emphasized-accelerate，240ms），整窗同步淡出（前 30%），
    /// 「轻轻收起再离开」而不是生硬消失。动画总开关关闭时跳过动画、直接回调。
    /// </summary>
    /// <param name="window">要关闭的窗口。</param>
    /// <param name="onCompleted">动效播完后的回调，通常是真正执行 <c>Close()</c>。</param>
    public static async void Exit(Window window, Action? onCompleted = null)
    {
        // 动画总开关关闭时，跳过动画直接完成关闭（与遮罩层 PopOut 一致）
        if (!AnimationGate.Enabled)
        {
            onCompleted?.Invoke();
            return;
        }

        if (window.Content is not Control content)
        {
            onCompleted?.Invoke();
            return;
        }

        const int durationMs = 240;
        try
        {
            content.Transitions = null;
            window.Transitions = null;
            content.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
            content.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            await FlushAsync();

            content.Transitions = new Transitions
            {
                WindowFade(durationMs, MaterialMotion.FadeEndFractionExit),
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    Easing = MaterialMotion.EmphasizedAccelerateEasing
                }
            };
            window.Transitions = new Transitions { WindowFade(durationMs, MaterialMotion.FadeEndFractionExit) };
            content.Opacity = 0;
            window.Opacity = 0;
            content.RenderTransform = TransformOperations.Parse("translateY(-16px) scale(0.96)");

            await Task.Delay(durationMs);
        }
        catch (Exception)
        {
            // 动画失败也要保证最终能关闭
        }
        finally
        {
            content.Transitions = null;
            window.Transitions = null;
            content.Opacity = 0;
            window.Opacity = 0;
            onCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 最小化：内容向任务栏方向收缩飞去（下移 48px + 缩小到 88%）+ 淡出（240ms），
    /// 播完回调 onCompleted（调用方设置 Minimized）。
    /// 窗口隐藏期间动画状态由 <see cref="Restore"/> 负责复位。
    /// 总开关关闭或已有状态动画在播时，跳过动画直接回调。
    /// </summary>
    /// <param name="window">要最小化的窗口。</param>
    /// <param name="onCompleted">动效播完后的回调，由调用方设置 <c>WindowState.Minimized</c>。</param>
    public static async void Minimize(Window window, Action? onCompleted = null)
    {
        if (!AnimationGate.Enabled || _transitioning)
        {
            onCompleted?.Invoke();
            return;
        }
        if (window.Content is not Control content)
        {
            onCompleted?.Invoke();
            return;
        }

        _transitioning = true;
        const int durationMs = 240;
        try
        {
            content.Transitions = null;
            window.Transitions = null;
            content.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
            content.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            await FlushAsync();

            content.Transitions = new Transitions
            {
                WindowFade(durationMs, MaterialMotion.FadeEndFractionExit),
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    Easing = MaterialMotion.EmphasizedAccelerateEasing
                }
            };
            window.Transitions = new Transitions { WindowFade(durationMs, MaterialMotion.FadeEndFractionExit) };
            content.Opacity = 0;
            window.Opacity = 0;
            content.RenderTransform = TransformOperations.Parse("translateY(48px) scale(0.88)");

            await Task.Delay(durationMs);
            onCompleted?.Invoke();
        }
        catch (Exception)
        {
            onCompleted?.Invoke();
        }
        finally
        {
            content.Transitions = null;
            window.Transitions = null;
            _transitioning = false;
        }
    }

    /// <summary>最大化确认动效：内容从下方 8px + 96% 缩放「落座」弹开（emphasized-decelerate）。</summary>
    /// <param name="window">要播放动效的窗口。</param>
    public static void Maximize(Window window) =>
        PlayConfirmScale(window, fromScale: 0.96, fromOpacity: 0, durationMs: 240, fromTranslateY: 8);

    /// <summary>
    /// 还原确认动效。
    /// <paramref name="fromMinimized"/> 为 false（从最大化恢复）：内容轻微收缩回正（102%→1），
    /// 像松手后稳稳落回。
    /// <paramref name="fromMinimized"/> 为 true（从任务栏恢复）：先复位整窗透明度，再从下方 20px +
    /// 94% 缩放放大淡入（280ms）——与最小化时「向下飞向任务栏」形成反向承接，动线连贯。
    /// </summary>
    /// <param name="window">要还原的窗口。</param>
    /// <param name="fromMinimized">
    /// <c>true</c> 表示从任务栏（最小化）恢复，会额外复位整窗透明度；
    /// <c>false</c> 表示从最大化恢复。
    /// </param>
    public static void Restore(Window window, bool fromMinimized = false)
    {
        if (fromMinimized)
            window.Opacity = 1; // 最小化时整窗透明度被置 0，恢复前先复位避免"隐形窗口"
        PlayConfirmScale(window,
            fromScale: fromMinimized ? 0.94 : 1.02,
            fromOpacity: fromMinimized ? 0 : 0.6,
            durationMs: fromMinimized ? 280 : 180,
            fromTranslateY: fromMinimized ? 20 : 0);
    }

    /// <summary>
    /// 通用"缩放落座"动画：从 fromTranslateY/fromScale/fromOpacity 平滑过渡到 0/1/1
    /// （位移用 emphasized-decelerate，透明度匀速），播完复位 RenderTransform 与 Transitions。
    /// 动画进行中（_transitioning）直接跳过，避免叠加冲突。
    /// </summary>
    private static async void PlayConfirmScale(
        Window window, double fromScale, double fromOpacity, int durationMs, double fromTranslateY = 0)
    {
        if (!AnimationGate.Enabled || _transitioning)
            return;
        if (window.Content is not Control content)
            return;

        _transitioning = true;
        try
        {
            content.Transitions = null;
            content.Opacity = fromOpacity;
            content.RenderTransform = TransformOperations.Parse(
                fromTranslateY != 0
                    ? $"translateY({fromTranslateY}px) scale({fromScale})"
                    : $"scale({fromScale})");
            content.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            await FlushAsync();

            content.Transitions = new Transitions
            {
                WindowFade(durationMs, MaterialMotion.FadeEndFraction),
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    Easing = MaterialMotion.EmphasizedDecelerateEasing
                }
            };
            content.Opacity = 1;
            content.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");

            await Task.Delay(durationMs);
        }
        catch (Exception)
        {
            // 动画失败也保证最终可见
        }
        finally
        {
            content.Opacity = 1;
            content.Transitions = null;
            content.RenderTransform = null;
            _transitioning = false;
        }
    }
}
