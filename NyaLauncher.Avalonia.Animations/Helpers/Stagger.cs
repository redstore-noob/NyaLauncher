using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 列表级联入场：挂在 ItemsControl / ListBox 上（class="nya-stagger"），
/// 数据刷新后每个可见项容器依次错峰从右侧 24px 滑入 + 淡入（cubicOut），完成后复位。
/// 已播放过的容器不会重复播；整体替换（Reset）会重新级联。全部逻辑只在本模块。
/// </summary>
public static class Stagger
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Stagger), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>相邻两项的启动间隔（毫秒），默认 45。</summary>
    public static readonly AttachedProperty<double> DelayMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("DelayMs", typeof(Stagger), 45.0);

    public static void SetDelayMs(AvaloniaObject element, double value) =>
        element.SetValue(DelayMsProperty, value);

    public static double GetDelayMs(AvaloniaObject element) =>
        element.GetValue(DelayMsProperty);

    private sealed class StaggerState
    {
        public HashSet<Control> Played { get; } = new();
    }

    private static readonly ConditionalWeakTable<Control, StaggerState> States = new();

    static Stagger()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            WhenAttached(control, () => Attach(control));
            control.DetachedFromVisualTree += StopHandler;
        }
        else
        {
            States.Remove(control);
        }
    }

    private static void StopHandler(object? s, VisualTreeAttachmentEventArgs e)
    {
        if (s is Control c)
        {
            c.DetachedFromVisualTree -= StopHandler;
            States.Remove(c);
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

    /// <summary>
    /// 手动触发一次级联入场（供主工程在"列表已可见"的时机调用，如加载遮罩关闭后）。
    /// 与 class 挂载共用同一状态：已播过的容器不会重复播。
    /// </summary>
    public static void Play(ItemsControl host)
    {
        if (host is null || !host.IsAttachedToVisualTree()) return;
        if (!States.TryGetValue(host, out var state))
        {
            state = new StaggerState();
            States.Add(host, state);
        }
        QueuePlay(host, state);
    }

    private static void Attach(Control control)
    {
        if (control is not ItemsControl host) return;
        if (States.TryGetValue(host, out _)) return;

        var state = new StaggerState();
        States.Add(host, state);
        host.Items.CollectionChanged += (_, _) => QueuePlay(host, state);
        QueuePlay(host, state);
    }

    private static void QueuePlay(ItemsControl host, StaggerState state)
    {
        // 容器是异步生成的：等容器真正出现在可视树后再播，否则一次都拿不到容器（级联永远不触发）
        _ = QueuePlayCoreAsync(host, state);
    }

    private static async Task QueuePlayCoreAsync(ItemsControl host, StaggerState state)
    {
        if (!host.IsAttachedToVisualTree()) return;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (host.GetVisualDescendants().OfType<ContentPresenter>().Any())
            {
                PlayOnce(host, state);
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(16);
        }
    }

    private static void PlayOnce(ItemsControl host, StaggerState state)
    {
        // 动画总开关关闭时不播级联入场（容器保持默认可见）
        if (!AnimationGate.Enabled)
            return;

        var delayMs = Math.Max(10, GetDelayMs(host));
        // 容器 = 可视树中已生成的 ContentPresenter（与项目顺序一致），不依赖 ItemContainerGenerator API
        var containers = host.GetVisualDescendants().OfType<ContentPresenter>().ToList();
        for (var i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            if (state.Played.Contains(container))
                continue;
            state.Played.Add(container);
            _ = AnimateInAsync(container, (int)(delayMs * i));
        }
    }

    private static async Task AnimateInAsync(Control container, int delayMs)
    {
        await Task.Delay(delayMs);
        if (!container.IsAttachedToVisualTree()) return;

        var translate = new TranslateTransform(24, 0);
        container.RenderTransform = translate;
        container.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        container.Opacity = 0;

        const int durationMs = 300;
        var frames = Math.Max(1, durationMs / 16);
        try
        {
            for (var i = 1; i <= frames; i++)
            {
                var t = i / (double)frames;
                var eased = 1 - Math.Pow(1 - t, 3);
                translate.X = 24 * (1 - eased);
                container.Opacity = eased;
                await Task.Delay(16);
            }
        }
        catch (Exception)
        {
            // 动画失败也要保证容器最终可见
        }
        finally
        {
            container.Opacity = 1;
            container.RenderTransform = null;
        }
    }
}
