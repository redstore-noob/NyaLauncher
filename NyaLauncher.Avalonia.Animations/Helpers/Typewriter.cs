using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 打字机效果：TextBlock 挂载后逐字打出（class="nya-typewriter"），适合标语/副标题/状态文案。
/// 只播一次；动画逻辑全部只在本模块。
/// </summary>
public static class Typewriter
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Typewriter), false);

    public static void SetEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AvaloniaObject element) =>
        element.GetValue(EnabledProperty);

    /// <summary>每个字符间隔毫秒数，默认 40。</summary>
    public static readonly AttachedProperty<double> DelayMsProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("DelayMs", typeof(Typewriter), 40.0);

    public static void SetDelayMs(AvaloniaObject element, double value) =>
        element.SetValue(DelayMsProperty, value);

    public static double GetDelayMs(AvaloniaObject element) =>
        element.GetValue(DelayMsProperty);

    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    static Typewriter()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (Attached.TryGetValue(control, out _)) return;
        if (control is not TextBlock textBlock) return;
        Attached.Add(control, new object());

        WhenAttached(textBlock, () => Start(textBlock));
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

    private static void Start(TextBlock textBlock)
    {
        // 动画总开关关闭时不播打字机（保留完整文本，避免文字被清空）
        if (!AnimationGate.Enabled) return;

        var full = textBlock.Text ?? string.Empty;
        if (full.Length == 0) return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(10, GetDelayMs(textBlock)));
        var index = 0;
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) =>
        {
            index++;
            if (index >= full.Length)
            {
                textBlock.Text = full;
                timer.Stop();
                return;
            }
            textBlock.Text = full[..index];
        };
        textBlock.Text = string.Empty;
        timer.Start();
    }
}
