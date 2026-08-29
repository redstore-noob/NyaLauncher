using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 组件拖拽载荷：在拖拽源与放置目标之间传递「拖的是哪个组件、来自哪个区域」。
/// <para>
/// 以纯文本形式放进 <see cref="DataTransfer"/>，因此跨窗口、跨进程的拖拽同样可用。
/// 文本格式：<c>nyalauncher-component-v1|{转义后的组件Id}|{转义后的源区域Id}</c>。
/// </para>
/// </summary>
/// <param name="ComponentId">被拖拽的组件（动作）Id。</param>
/// <param name="SourceAreaId">
/// 来源功能区 Id。为 <c>null</c> 或空白表示来自<b>组件库</b>（新建），
/// 否则表示来自<b>工作区</b>中的某个区域（移动）。
/// </param>
public sealed record ComponentDragPayload(string ComponentId, string? SourceAreaId)
{
    /// <summary>载荷文本前缀，同时充当版本号；解析时用它快速排除无关拖放数据。</summary>
    private const string Prefix = "nyalauncher-component-v1|";

    /// <summary>
    /// 是否来自组件库抽屉。为 <c>true</c> 时拖放效果是 <see cref="DragDropEffects.Copy"/>，
    /// 且按下即拖；为 <c>false</c> 时效果是 <see cref="DragDropEffects.Move"/>，需要长按才拖。
    /// </summary>
    public bool IsFromLibrary => string.IsNullOrWhiteSpace(SourceAreaId);

    /// <summary>序列化为可放进 <see cref="DataTransfer"/> 的文本；组件 Id 与区域 Id 均做 URI 转义。</summary>
    /// <returns>带前缀的载荷文本。</returns>
    public string Serialize()
    {
        return $"{Prefix}{Uri.EscapeDataString(ComponentId)}|" +
               Uri.EscapeDataString(SourceAreaId ?? string.Empty);
    }

    /// <summary>把本载荷包装成 <see cref="DataTransfer"/>，供 <c>DragDrop.DoDragDropAsync</c> 使用。</summary>
    /// <returns>只含一条文本项的传输对象。</returns>
    public DataTransfer CreateDataTransfer()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(Serialize()));
        return transfer;
    }

    /// <summary>尝试从拖放数据中解析出组件载荷。</summary>
    /// <param name="transfer">拖放传输对象。</param>
    /// <param name="payload">解析成功时的载荷；失败时为 <c>null</c>。</param>
    /// <returns>数据非空、前缀匹配且组件 Id 有效时返回 <c>true</c>。</returns>
    public static bool TryParse(IDataTransfer transfer, out ComponentDragPayload? payload)
    {
        payload = null;
        var text = transfer.TryGetText();
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = text[Prefix.Length..].Split('|', 2);
        if (parts.Length != 2)
            return false;

        var componentId = Uri.UnescapeDataString(parts[0]);
        var sourceAreaId = Uri.UnescapeDataString(parts[1]);
        if (string.IsNullOrWhiteSpace(componentId))
            return false;

        payload = new ComponentDragPayload(
            componentId,
            string.IsNullOrWhiteSpace(sourceAreaId) ? null : sourceAreaId);
        return true;
    }
}

/// <summary>
/// 组件拖拽手势源：把「按下 + 长按/移动」翻译成一次 Avalonia 拖拽。
/// <para>
/// 两种场景的手势规则不同：
/// <list type="bullet">
/// <item><description><b>来自组件库</b>（<see cref="ComponentDragPayload.IsFromLibrary"/> 为
/// <c>true</c>）：按下后指针移动超过阈值立即开始拖，不遮挡单击。</description></item>
/// <item><description><b>来自工作区</b>：需要<b>长按</b> 420ms 才开始拖，
/// 这样组件内部的按钮、滑块等元素仍能正常响应短按；
/// 未达长按就移动则取消拖拽，把事件让回给子元素。</description></item>
/// </list>
/// </para>
/// <para>
/// 事件全部以隧道（<see cref="RoutingStrategies.Tunnel"/>）+ <c>handledEventsToo</c>
/// 注册，因此即使子元素已把事件标记为已处理，拖拽手势依然能收到。
/// </para>
/// </summary>
public static class ComponentDragSource
{
    /// <summary>判定为「开始拖动」的指针位移阈值（设备无关像素）。</summary>
    private const double DragThreshold = 6;

    /// <summary>工作区组件需要长按多久才进入拖拽态。</summary>
    private static readonly TimeSpan LongPressDuration = TimeSpan.FromMilliseconds(420);

    /// <summary>
    /// 为一个控件挂载拖拽手势。
    /// </summary>
    /// <param name="control">手势宿主（组件卡片或组件库项）。</param>
    /// <param name="componentId">该控件代表的组件 Id。</param>
    /// <param name="sourceAreaId">
    /// 来源区域 Id；传 <c>null</c> 表示来自组件库，走「按下即拖」的宽松手势。
    /// </param>
    /// <param name="onDragStarting">
    /// 拖拽真正启动<b>前</b>的回调，可用于抽屉缩回等前置动作。
    /// 注意它发生在 <c>DoDragDropAsync</c> 之前，不要在其中做阻塞等待。
    /// </param>
    public static void Attach(
        Control control,
        string componentId,
        string? sourceAreaId,
        Action? onDragStarting = null)
    {
        PendingDrag? pending = null;

        void CancelPending()
        {
            var candidate = pending;
            pending = null;
            if (candidate is null)
                return;
            candidate.Cancellation.Cancel();
            candidate.Cancellation.Dispose();
        }

        async Task StartDragAsync(PendingDrag candidate)
        {
            try
            {
                if (!ReferenceEquals(pending, candidate))
                    return;

                pending = null;
                candidate.Cancellation.Dispose();
                // 拖拽启动前通知宿主：可用于抽屉缩回等前置动画（Transitions 由渲染线程驱动，不阻塞 DoDragDropAsync）
                onDragStarting?.Invoke();
                candidate.PressedEvent.Pointer.Capture(control);
                await DragDrop.DoDragDropAsync(
                    candidate.PressedEvent,
                    candidate.Payload.CreateDataTransfer(),
                    candidate.Payload.IsFromLibrary
                        ? DragDropEffects.Copy
                        : DragDropEffects.Move);
            }
            catch (OperationCanceledException)
            {
                // 拖拽被取消属正常流程
            }
            catch (Exception exception)
            {
                // 拖拽平台错误（控件被移除等）不能从 async void 逸出导致进程崩溃
                System.Diagnostics.Debug.WriteLine($"拖拽失败：{exception}");
            }
        }

        async Task StartAfterLongPressAsync(PendingDrag candidate)
        {
            try
            {
                await Task.Delay(LongPressDuration, candidate.Cancellation.Token);
                await StartDragAsync(candidate);
            }
            catch (OperationCanceledException)
            {
                // 未达长按就松手或移动：不启动拖拽，把交互原样让回给子元素的点击处理
            }
        }

        control.AddHandler(
            InputElement.PointerPressedEvent,
            (_, args) =>
            {
                if (!args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                    return;

                CancelPending();
                var candidate = new PendingDrag(
                    args,
                    args.GetPosition(control),
                    new ComponentDragPayload(componentId, sourceAreaId),
                    new CancellationTokenSource());
                pending = candidate;
                if (!candidate.Payload.IsFromLibrary)
                    _ = StartAfterLongPressAsync(candidate);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        control.AddHandler(
            InputElement.PointerMovedEvent,
            async (_, args) =>
            {
                var candidate = pending;
                if (candidate is null ||
                    !args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                var position = args.GetPosition(control);
                var delta = position - candidate.Origin;
                if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                    return;

                if (!candidate.Payload.IsFromLibrary)
                {
                    CancelPending();
                    return;
                }

                args.Handled = true;
                await StartDragAsync(candidate);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        control.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, _) => CancelPending(),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        control.PointerCaptureLost += (_, _) => CancelPending();
    }

    /// <summary>一次「按下但尚未确定要拖」的待定手势状态。</summary>
    /// <param name="PressedEvent">按下事件，拖拽启动时需要它来捕获指针与发起 DoDragDrop。</param>
    /// <param name="Origin">按下时相对控件的坐标，用于计算位移是否越过阈值。</param>
    /// <param name="Payload">待发起的拖拽载荷。</param>
    /// <param name="Cancellation">用于取消长按等待的令牌源。</param>
    private sealed record PendingDrag(
        PointerPressedEventArgs PressedEvent,
        Point Origin,
        ComponentDragPayload Payload,
        CancellationTokenSource Cancellation);
}

/// <summary>请求从某个功能区移除组件（垃圾桶丢弃 / 组件库收回共用）。</summary>
/// <param name="ComponentId">要移除的组件 Id。</param>
/// <param name="SourceAreaId">要从哪个区域移除。</param>
public sealed record ComponentRemovalRequestedEventArgs(
    string ComponentId,
    string SourceAreaId);
