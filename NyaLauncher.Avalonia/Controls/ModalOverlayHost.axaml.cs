using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using NyaLauncher.Avalonia.Animations.Helpers;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 内容视图接入宿主的约定接口：宿主 <see cref="ModalOverlayHost.Show"/> 时自动注入自身，
/// 内容视图在确认/取消时调用 <c>Host?.Close(result)</c> 把结果交回并关闭。
/// </summary>
public interface IModalHostAware
{
    ModalOverlayHost? Host { get; set; }
}

/// <summary>
/// 通用模态遮罩宿主：遮罩层骨架（半透明背景 + 居中卡片 + 弹入/退出动画）只在这里写一次。
/// 任何内容视图（普通 UserControl）塞进来即可弹窗：
/// <code>
/// host.Show(view);                                   // 无返回值展示
/// var r = await host.ShowAsync&lt;TResult&gt;(view);      // 等待内容视图 Close(result)
/// </code>
/// 内容视图自带卡片外观（Width/背景/圆角），宿主只负责遮罩、动画与生命周期。
/// </summary>
public partial class ModalOverlayHost : UserControl
{
    public static readonly StyledProperty<Control?> DialogContentProperty =
        AvaloniaProperty.Register<ModalOverlayHost, Control?>(nameof(DialogContent));

    /// <summary>当前展示的内容视图；null 表示宿主隐藏。</summary>
    public Control? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    private readonly object _gate = new();
    private IDeferredResult? _pending;
    private bool _closing;

    public ModalOverlayHost()
    {
        InitializeComponent();
    }

    /// <summary>展示一个内容视图（无返回值）。挂起结果的清理由 ShowAsync 负责。</summary>
    public void Show(Control view)
    {
        AttachHost(view);
        DialogContent = view;
        // 兜底：清除上次关闭可能残留的动画状态（Opacity/RenderTransform），
        // 确保动画中断后第二次打开不会半透明/不可见
        view.Opacity = 1;
        view.RenderTransform = null;
        IsVisible = true;
    }

    /// <summary>
    /// 展示内容视图并等待其结果：内容视图调用 <see cref="Close{TResult}"/> 后返回其值；
    /// 取消/无结果时返回 default。宿主隐藏时挂起任务立即完成（防泄漏）。
    /// </summary>
    public Task<TResult?> ShowAsync<TResult>(Control view)
    {
        var deferred = new DeferredResult<TResult>();
        lock (_gate)
        {
            _pending?.Complete(null);
            _pending = deferred;
        }
        Show(view);
        return deferred.Task;
    }

    /// <summary>无结果关闭（取消等场景）。</summary>
    public void Close() => BeginClose(null);

    /// <summary>携带结果关闭：完成对应 <see cref="ShowAsync{TResult}"/> 的等待。</summary>
    public void Close<TResult>(TResult? result) => BeginClose(result);

    private void BeginClose(object? result)
    {
        if (_closing) return;
        _closing = true;

        lock (_gate)
        {
            _pending?.Complete(result);
            _pending = null;
        }

        OverlayEffects.PopOut(this, () =>
        {
            IsVisible = false;
            DialogContent = null;
            _closing = false;
        });
    }

    private void AttachHost(Control view)
    {
        if (view is IModalHostAware aware)
            aware.Host = this;
    }

    /// <summary>挂起结果的统一抽象，避免泛型字段。</summary>
    private interface IDeferredResult
    {
        void Complete(object? result);
    }

    private sealed class DeferredResult<T> : IDeferredResult
    {
        private readonly TaskCompletionSource<T?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T?> Task => _tcs.Task;

        public void Complete(object? result)
        {
            if (result is null)
                _tcs.TrySetResult(default);
            else if (result is T typed)
                _tcs.TrySetResult(typed);
        }
    }
}
