using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 底部警示滑条宿主：嵌入 MainWindow（ZIndex 940），警示从左侧滑入、停留数秒后自动收回。
/// 动画基于 Avalonia Transitions（渲染线程驱动），M3 令牌 + 尊重 AnimationGate。
/// 调用入口是 <see cref="NyaAlert"/> 静态门面；展示中再次触发则就地换文案并重置倒计时。
/// </summary>
public partial class NyaAlertHost : UserControl
{
    /// <summary>M3 位移过渡时长（进入 emphasized-decelerate / 退出 emphasized-accelerate）。</summary>
    private const int SlideMs = MaterialMotion.MediumTransitionMs;

    private readonly DispatcherTimer _autoHide;
    private int _generation;
    private bool _hiding;

    /// <summary>
    /// 初始化宿主并把它注册到 <see cref="NyaAlert"/> 门面。
    /// 一个窗口内应只存在一个实例：后注册的宿主会顶掉先前的注册。
    /// </summary>
    public NyaAlertHost()
    {
        InitializeComponent();
        NyaAlert.Register(this);
        _autoHide = new DispatcherTimer();
        _autoHide.Tick += (_, _) => HideNow();
    }

    /// <summary>
    /// 展示一条警示（必须在 UI 线程调用；<see cref="NyaAlert"/> 门面已负责封送）。
    /// 若当前已有警示在展示，则只替换文案与配色并重置倒计时，<b>不重播</b>滑入动画。
    /// </summary>
    /// <param name="message">提示文字。</param>
    /// <param name="severity">严重级别，决定图标与主题色。</param>
    /// <param name="duration">停留时长，到点后自动收回。</param>
    public void Show(string message, NyaNoticeSeverity severity, TimeSpan duration)
    {
        _autoHide.Stop();
        _generation++;
        _hiding = false;

        AlertText.Text = message;
        var (kind, brushKey) = NyaNoticeSeverities.Map(severity);
        AlertIcon.Kind = kind;
        AlertIcon[!MaterialIcon.ForegroundProperty] = new DynamicResourceExtension(brushKey);
        AccentStrip[!Border.BackgroundProperty] = new DynamicResourceExtension(brushKey);

        RestartAutoHide(duration);
        if (IsVisible)
            return; // 展示中：就地换文案，不重播动画

        IsVisible = true;
        if (AnimationGate.Enabled)
            _ = AnimateInAsync();
    }

    /// <summary>立即收回（点关闭或倒计时到点）。</summary>
    public void HideNow()
    {
        _autoHide.Stop();
        if (!IsVisible || _hiding)
            return;

        if (!AnimationGate.Enabled)
        {
            FinishHide();
            return;
        }

        _hiding = true;
        _ = AnimateOutAsync(_generation);
    }

    private async Task AnimateInAsync()
    {
        try
        {
            // 进入：淡入（前 40% 匀速完成）+ 从左滑入（emphasized-decelerate）
            AlertCard.Transitions = null;
            AlertCard.Opacity = 0;
            AlertCard.RenderTransform = TransformOperations.Parse("translateX(-48px)");
            await FlushAsync();

            AlertCard.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(SlideMs * MaterialMotion.FadeEndFraction),
                    Easing = MaterialMotion.LinearEasing,
                },
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(SlideMs),
                    Easing = MaterialMotion.EmphasizedDecelerateEasing,
                },
            };
            AlertCard.Opacity = 1;
            AlertCard.RenderTransform = TransformOperations.Parse("translateX(0px)");
        }
        catch (Exception)
        {
            // 动画失败不影响展示
            ResetCardState();
        }
    }

    private async Task AnimateOutAsync(int generation)
    {
        try
        {
            // 退出：淡出（前 30% 匀速消失）+ 向左滑出（emphasized-accelerate）
            AlertCard.Transitions = null;
            AlertCard.Opacity = 1;
            AlertCard.RenderTransform = TransformOperations.Parse("translateX(0px)");
            await FlushAsync();

            AlertCard.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200 * MaterialMotion.FadeEndFractionExit),
                    Easing = MaterialMotion.LinearEasing,
                },
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                    Easing = MaterialMotion.EmphasizedAccelerateEasing,
                },
            };
            AlertCard.Opacity = 0;
            AlertCard.RenderTransform = TransformOperations.Parse("translateX(-48px)");
            await Task.Delay(200);
        }
        finally
        {
            // 期间有新警示进来（generation 变化）则放弃收场，由新的进入动画接管
            if (generation == _generation)
                FinishHide();
        }
    }

    private void FinishHide()
    {
        ResetCardState();
        IsVisible = false;
        _hiding = false;
    }

    private void ResetCardState()
    {
        AlertCard.Transitions = null;
        AlertCard.Opacity = 1;
        AlertCard.RenderTransform = null;
    }

    private void RestartAutoHide(TimeSpan duration)
    {
        _autoHide.Interval = duration;
        _autoHide.Stop();
        _autoHide.Start();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => HideNow();

    /// <summary>等一次低优先级布局，确保初始状态先被渲染一帧（同 OverlayEffects）。</summary>
    private static async Task FlushAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
}
