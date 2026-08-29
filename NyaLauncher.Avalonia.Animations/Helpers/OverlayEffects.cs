using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 遮罩层弹入动效（附加属性）。
/// 在遮罩层根元素（UserControl / Panel）上设置 <c>OverlayEffects.PopIn="True"</c>，
/// 根元素显示（IsVisible 变 true）时，自动对其第一个子元素（居中的对话框卡片）
/// 播放「缩放 0.96→1 + 淡入」动画；隐藏时立即复位，避免残留透明状态。动画失败不影响显示。
/// 全部基于 Avalonia Transitions（渲染线程驱动），缓动与时长取自 <see cref="MaterialMotion"/>（M3 令牌）。
/// </summary>
public static class OverlayEffects
{
    public static readonly AttachedProperty<bool> PopInProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "PopIn", typeof(OverlayEffects), false);

    private static readonly ConditionalWeakTable<Control, object> Playing = new();

    static OverlayEffects()
    {
        PopInProperty.Changed.AddClassHandler<Control>(OnPopInChanged);
        // 监听根元素的 IsVisible：遮罩 ShowFor/Hide 都会切换它
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>(OnIsVisibleChanged);
    }

    public static void SetPopIn(AvaloniaObject element, bool value) =>
        element.SetValue(PopInProperty, value);

    public static bool GetPopIn(AvaloniaObject element) =>
        element.GetValue(PopInProperty);

    private static void OnPopInChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // 已在可见状态挂载 PopIn 时立即播放一次
        if (e.NewValue is true && control.IsVisible)
            PlayPopIn(control);
    }

    private static void OnIsVisibleChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control.GetValue(PopInProperty) != true)
            return;
        if (e.NewValue is true)
            PlayPopIn(control);
        else
            ResetPopIn(control);
    }

    /// <summary>
    /// 取对话框卡片：递归跳过全屏容器（Panel / ContentControl），
    /// 最终落到居中卡片（通常是 Border）。找不到时退回根元素自身。
    /// </summary>
    private static Control? FindDialogSurface(Control root)
    {
        if (root is Panel { Children.Count: > 0 } panel && panel.Children[0] is Control first)
            return FindDialogSurface(first);
        if (root is ContentControl { Content: Control content })
            return FindDialogSurface(content);
        return root;
    }

    /// <summary>等一次低优先级布局，确保初始状态先被渲染一帧。</summary>
    private static async Task FlushAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static async void PlayPopIn(Control root)
    {
        // 动画总开关关闭时不播弹入，直接保持控件默认可见
        if (!AnimationGate.Enabled)
            return;

        var dialog = FindDialogSurface(root);
        if (dialog is null || Playing.TryGetValue(dialog, out _))
            return;
        Playing.Add(dialog, new object());

        try
        {
            dialog.Transitions = null;
            dialog.RenderTransform = TransformOperations.Parse("scale(0.96)");
            dialog.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            dialog.Opacity = 0;
            await FlushAsync();

            const int durationMs = MaterialMotion.MediumTransitionMs;
            dialog.Transitions = new Transitions
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
            dialog.Opacity = 1;
            dialog.RenderTransform = TransformOperations.Parse("scale(1)");

            await Task.Delay(durationMs);
            if (!root.IsVisible)
                return; // 动画中途被隐藏：由 ResetPopIn 复位
            dialog.Transitions = null;
            dialog.RenderTransform = null;
        }
        catch (Exception)
        {
            // 动画失败不影响显示：保证控件最终可见
            dialog.Transitions = null;
            dialog.Opacity = 1;
            dialog.RenderTransform = null;
        }
        finally
        {
            // 兜底复位：动画中途被隐藏时 Opacity 可能停在中间值，
            // 不复位会导致下次打开时遮罩半透明/不可见（第二次弹不出来）
            if (!root.IsVisible)
                ResetPopIn(root);
            Playing.Remove(dialog);
        }
    }

    private static void ResetPopIn(Control root)
    {
        if (FindDialogSurface(root) is { } dialog)
        {
            dialog.Transitions = null;
            dialog.Opacity = 1;
            dialog.RenderTransform = null;
        }
    }

    /// <summary>
    /// 遮罩层退出动效（与 <see cref="PopInProperty"/> 镜像）。在 host 之上播放「缩放 1→0.94 + 淡出」，
    /// 当 host 与对话框卡片不是同一元素时整层（或整窗）同步淡出，播完回调 onCompleted（真正隐藏/关闭窗口）。
    /// 动画逻辑全部在本模块；主工程只调用本方法，或经 ModalOverlayBase.CloseAnimated 封装。
    /// host 传 UserControl 遮罩层本身，或 Window 弹窗实例均可。
    /// </summary>
    private static readonly ConditionalWeakTable<Control, object> Closing = new();

    public static void PopOut(Control host, Action? onCompleted = null)
    {
        // 动画总开关关闭时，跳过动画直接完成关闭
        if (!AnimationGate.Enabled)
        {
            onCompleted?.Invoke();
            return;
        }

        // Window 的可见内容是其 Content；动画应作用于真正的对话框表面。
        var target = host is Window win && win.Content is Control content ? content : host;
        var dialog = FindDialogSurface(target) ?? target;

        // 正在播放弹入时直接完成关闭，避免两段动画抢同一元素造成闪烁
        if (Playing.TryGetValue(dialog, out _))
        {
            onCompleted?.Invoke();
            return;
        }
        // 防止关闭动画重入（连续点击关闭）
        if (Closing.TryGetValue(dialog, out _))
        {
            onCompleted?.Invoke();
            return;
        }
        Closing.Add(dialog, new object());

        _ = AnimateOut(host, dialog, () =>
        {
            Closing.Remove(dialog);
            onCompleted?.Invoke();
        });
    }

    private static async Task AnimateOut(Control host, Control dialog, Action done)
    {
        var isSameElement = ReferenceEquals(host, dialog);
        try
        {
            host.Transitions = null;
            dialog.Transitions = null;
            dialog.RenderTransform = TransformOperations.Parse("scale(1)");
            dialog.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            dialog.Opacity = 1;
            host.Opacity = 1;
            await FlushAsync();

            // M3 退出：缩放用 emphasized-accelerate，不透明度匀速在前 30% 消失。
            const int durationMs = 200;
            dialog.Transitions = new Transitions
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
            if (!isSameElement)
            {
                host.Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = Visual.OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(durationMs),
                        Easing = MaterialMotion.LinearEasing
                    }
                };
            }
            dialog.Opacity = 0;
            dialog.RenderTransform = TransformOperations.Parse("scale(0.94)");
            if (!isSameElement)
                host.Opacity = 0; // 整层/整窗同步淡出

            await Task.Delay(durationMs);
            dialog.Transitions = null;
            host.Transitions = null;
            dialog.Opacity = 0;
            if (!isSameElement)
                host.Opacity = 0;
        }
        catch (Exception)
        {
            // 动画失败也要保证最终关闭
            dialog.Transitions = null;
            host.Transitions = null;
            dialog.Opacity = 0;
            if (!ReferenceEquals(host, dialog))
                host.Opacity = 0;
        }
        finally
        {
            done();
        }
    }
}
