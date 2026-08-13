using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Owns one placed component's runtime lifetime. Visuals may be rebuilt many
/// times; cancellation and action re-entry therefore belong here, not to a
/// transient Avalonia control.
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

    public ComponentStateSnapshot CurrentState => _inner.CurrentState;

    public event EventHandler<ComponentStateChangedEventArgs>? StateChanged
    {
        add => _inner.StateChanged += value;
        remove => _inner.StateChanged -= value;
    }

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
            return await Task.Run(async () =>
                    await _inner.InvokeAsync(invocation, linkedCancellation.Token)
                        .ConfigureAwait(false))
                .ConfigureAwait(false);
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
            // Cancellation callbacks belong to plugin code. Disposal must still
            // wait for actions and release the underlying instance.
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
