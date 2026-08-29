using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 垂直换页过渡：旧页向上划走（淡出 + 上移 24px），新页从下方划入（淡入 + 上移 24px），
/// 用于设置页等标签页切换（「上面一个划走，下面一个出现」）。
/// 全部逻辑只在本模块，主工程只调用 <see cref="SwapVertical"/>。
/// </summary>
public static class SwapTransition
{
    /// <summary>每个控件当前正在跑的过渡（用于快速连点标签时取消旧动画，防止打架）。</summary>
    private static readonly ConcurrentDictionary<Control, CancellationTokenSource> Active = new();

    /// <summary>每个控件的「最终可见性」标记：每次切换只改 new=true / old=false，
    /// 动画无论正常完成还是被取消，finally 都按最新标记复位，避免快速连点导致两个页同时可见（重叠）。</summary>
    private static readonly ConcurrentDictionary<Control, bool> FinalVisible = new();

    /// <summary>
    /// 垂直换页：newControl 先置为可见并从下方 24px 淡入上移；oldControl（非空且与 newControl 不同）同步
    /// 向上 24px 淡出划走，播完隐藏（IsVisible=false）并复位。若 oldControl 为空（首次进入），只播新页淡入。
    /// </summary>
    public static void SwapVertical(Control? newControl, Control? oldControl, int durationMs = 280)
    {
        if (newControl is null) return;
        FinalVisible[newControl] = true;
        newControl.IsVisible = true;

        // 动画总开关关闭时不做过渡，直接完成换页（旧页隐藏、新页复位为默认可见）
        if (!AnimationGate.Enabled)
        {
            newControl.Opacity = 1;
            newControl.RenderTransform = null;
            if (oldControl is not null && !ReferenceEquals(oldControl, newControl))
            {
                FinalVisible[oldControl] = false;
                oldControl.IsVisible = false;
                oldControl.Opacity = 1;
                oldControl.RenderTransform = null;
            }
            return;
        }

        if (oldControl is null || ReferenceEquals(oldControl, newControl))
        {
            _ = RunAsync(newControl, isSlideIn: true, durationMs);
            return;
        }

        FinalVisible[oldControl] = false;
        _ = RunAsync(newControl, isSlideIn: true, durationMs);
        _ = RunAsync(oldControl, isSlideIn: false, durationMs);
    }

    private static async Task RunAsync(Control control, bool isSlideIn, int durationMs)
    {
        // 取消同一控件上正在跑的旧过渡
        if (Active.TryGetValue(control, out var prev))
        {
            prev.Cancel();
            Active.TryRemove(control, out _);
        }
        var cts = new CancellationTokenSource();
        Active[control] = cts;
        var token = cts.Token;

        var translate = new TranslateTransform(0, isSlideIn ? 24 : 0);
        control.RenderTransform = translate;
        control.Opacity = isSlideIn ? 0 : 1;

        try
        {
            var frames = Math.Max(1, durationMs / 16);
            for (var i = 1; i <= frames; i++)
            {
                token.ThrowIfCancellationRequested();
                var t = i / (double)frames;
                var eased = isSlideIn ? 1 - Math.Pow(1 - t, 3) : t * t; // 滑入 cubicOut / 滑出 ease-in
                translate.Y = isSlideIn ? 24 * (1 - eased) : -24 * eased;
                control.Opacity = isSlideIn ? eased : 1 - eased;
                await Task.Delay(16, token);
            }
            control.Opacity = isSlideIn ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            // 被新过渡取代：不强行复位，由新过渡接管
        }
        finally
        {
            // 按「最终可见性」标记复位：若本次滑出已被取消且该控件又成为新页（标记已改回 true），
            // 就不能隐藏它；若已不再需要显示（标记 false）则隐藏。没有标记的控件保持可见。
            control.IsVisible = FinalVisible.TryGetValue(control, out var visible) ? visible : true;
            control.Opacity = 1;
            control.RenderTransform = null;
            if (Active.TryGetValue(control, out var cur) && ReferenceEquals(cur, cts))
                Active.TryRemove(control, out _);
            cts.Dispose();
        }
    }
}
