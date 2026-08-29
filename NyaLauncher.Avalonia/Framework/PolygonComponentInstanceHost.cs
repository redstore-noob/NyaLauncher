using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 一个已摆放组件的运行时宿主，负责它的生命周期、动作防重入与取消。
/// <para>
/// 之所以需要这一层：组件的<b>视觉</b>（<c>PolygonComponentView</c>）会在缩放、
/// 布局重建、主题热重载时被反复销毁重建，但业务实例不该跟着一起死。
/// 因此取消令牌与重入计数放在这里，而不是放在那个随时被替换的 Avalonia 控件上——
/// 缩放或重建只会换视图，不会中断该位置正在执行的动作。
/// </para>
/// </summary>
internal sealed class PolygonComponentInstanceHost : IPolygonComponentInstance
{
    private readonly IPolygonComponentInstance _inner;
    private readonly Dictionary<string, ComponentActionDefinition> _actions;
    private readonly HashSet<string> _runningActions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _gate = new();
    private int _activeInvocations;
    private TaskCompletionSource<bool>? _idleCompletion;
    private Task? _disposalTask;
    private bool _disposed;

    /// <summary>为一个被摆放的组件实例套上宿主壳。</summary>
    /// <param name="inner">插件提供的真实实例。</param>
    /// <param name="definition">
    /// 已通过校验的组件定义；宿主据此建立动作表，用于校验动作 Id 与判断是否允许重入。
    /// </param>
    /// <exception cref="ArgumentNullException">任一参数为 <c>null</c>。</exception>
    public PolygonComponentInstanceHost(
        IPolygonComponentInstance inner,
        PolygonComponentDefinition definition)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(definition);
        _actions = definition.Actions.ToDictionary(
            action => action.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>组件当前的状态快照，直接透传内部实例。</summary>
    public ComponentStateSnapshot CurrentState => _inner.CurrentState;

    /// <summary>
    /// 状态变更事件，直接转发内部实例的事件。
    /// 宿主（视图）在组件进入可视树时订阅、离开时取消订阅。
    /// </summary>
    public event EventHandler<ComponentStateChangedEventArgs>? StateChanged
    {
        add => _inner.StateChanged += value;
        remove => _inner.StateChanged -= value;
    }

    /// <summary>
    /// 调用组件动作。会依次做三件事：校验动作是否已声明、按
    /// <c>AllowReentry</c> 决定是否防重入、把生命周期令牌与调用方令牌链接后传给插件。
    /// </summary>
    /// <param name="invocation">包含动作 Id 与参数的调用请求。</param>
    /// <param name="cancellationToken">调用方的取消令牌；组件被释放时宿主也会自行取消。</param>
    /// <returns>
    /// 插件返回的执行结果；下列情况直接返回失败而不进入插件代码：
    /// 动作未声明、实例已释放、动作不允许重入且上一次仍在执行。
    /// </returns>
    public async ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!_actions.TryGetValue(invocation.ActionId, out var action))
            return ComponentActionResult.Failed($"未声明的组件动作：{invocation.ActionId}");

        CancellationTokenSource linkedCancellation;
        lock (_gate)
        {
            if (_disposed)
                return ComponentActionResult.Failed("组件实例已释放。");
            if (!action.AllowReentry && !_runningActions.Add(action.Id))
                return ComponentActionResult.Failed("该组件动作正在执行。");

            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken);
            _activeInvocations++;
        }

        try
        {
            // 动作在调用方（UI 线程）上直接执行：组件如需后台工作应自行 Task.Run，
            // 而 UI 操作（弹窗、导航）无需再手动封送回 UI 线程。
            return await _inner.InvokeAsync(invocation, linkedCancellation.Token)
                .ConfigureAwait(true);
        }
        finally
        {
            linkedCancellation.Dispose();
            lock (_gate)
            {
                if (!action.AllowReentry)
                    _runningActions.Remove(action.Id);

                _activeInvocations--;
                if (_activeInvocations == 0)
                    _idleCompletion?.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// 释放组件实例：先取消生命周期令牌，<b>等待所有进行中的动作退出</b>，再释放插件实例。
    /// <para>
    /// 多次调用返回同一个任务；即使插件的取消回调抛异常，也会继续等待动作并释放实例，
    /// 不让异常卡住清理流程。
    /// </para>
    /// </summary>
    /// <returns>清理完成的异步任务；插件 <c>DisposeAsync</c> 抛出的异常会经此任务重新抛出。</returns>
    public ValueTask DisposeAsync()
    {
        Task idleTask;
        TaskCompletionSource<bool> completion;
        lock (_gate)
        {
            if (_disposalTask is not null)
                return new ValueTask(_disposalTask);

            _disposed = true;
            idleTask = _activeInvocations == 0
                ? Task.CompletedTask
                : (_idleCompletion ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposalTask = completion.Task;
        }

        _ = Task.Run(() => CompleteDisposalAsync(idleTask, completion));
        return new ValueTask(completion.Task);
    }

    /// <summary>
    /// 实际清理流程：取消令牌 → 等待动作空闲 → 释放插件实例 → 释放令牌源。
    /// 每一步的异常都会被收集，最终经 <paramref name="completion"/> 抛出。
    /// </summary>
    /// <param name="idleTask">动作全部退出后完成的任务。</param>
    /// <param name="completion">对外暴露的释放结果。</param>
    private async Task CompleteDisposalAsync(
        Task idleTask,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            await _lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // 取消回调属于插件代码，可能抛异常。
            // 释放流程仍必须继续等待动作退出并释放底层实例，不能在这里中断。
        }

        Exception? error = null;
        try
        {
            await idleTask.ConfigureAwait(false);
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            try
            {
                _lifetimeCancellation.Dispose();
            }
            catch (Exception exception)
            {
                error ??= exception;
            }
        }

        if (error is null)
            completion.TrySetResult(true);
        else
            completion.TrySetException(error);
    }
}
