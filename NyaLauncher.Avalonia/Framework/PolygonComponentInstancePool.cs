using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Owns the runtime instances associated with placed polygon components.
/// Visuals may be rebuilt independently; an instance remains alive until its
/// placement disappears, its registry changes, or the workspace shuts down.
/// </summary>
internal sealed class PolygonComponentInstancePool(
    Action<string, string> disposalCompleted)
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly ComponentInstanceKeyComparer KeyComparer = new();

    private readonly Dictionary<ComponentInstanceKey, InstanceEntry> _instances =
        new(KeyComparer);
    private readonly Dictionary<ComponentInstanceKey, Task> _disposals = new(KeyComparer);
    private readonly Action<string, string> _disposalCompleted = disposalCompleted ??
        throw new ArgumentNullException(nameof(disposalCompleted));

    public bool IsShuttingDown { get; private set; }

    public IPolygonComponentInstance? GetOrCreate(
        string areaId,
        string componentId,
        PolygonComponentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (IsShuttingDown)
            return null;

        var key = new ComponentInstanceKey(areaId, componentId);
        if (_instances.TryGetValue(key, out var existing))
        {
            if (ReferenceEquals(existing.Registration, registration) &&
                ReferenceEquals(existing.Factory, registration.Factory))
            {
                return existing.Instance;
            }

            // A refreshed registration may reuse the same area/component key
            // while supplying a new factory. Never reuse its previous instance.
            Release(key);
        }

        if (registration.Factory is null)
            return null;
        if (_disposals.TryGetValue(key, out var pendingDisposal))
        {
            if (!pendingDisposal.IsCompleted)
                return null;

            _disposals.Remove(key);
        }

        try
        {
            var componentInstance = registration.Factory.Create(
                new ComponentInstanceContext(componentId, areaId));
            if (componentInstance is null)
                return null;

            var instance = new PolygonComponentInstanceHost(
                componentInstance,
                registration.Definition);
            _instances[key] = new InstanceEntry(
                instance,
                registration,
                registration.Factory);
            return instance;
        }
        catch
        {
            // A third-party runtime must not prevent the remaining workspace
            // components from being created. Its declarative view stays visible
            // with actions disabled when no runtime instance can be created.
            return null;
        }
    }

    public void Release(string areaId, string componentId) =>
        Release(new ComponentInstanceKey(areaId, componentId));

    public void ReleaseUnreferenced(IEnumerable<ComponentInstanceContext> liveInstances)
    {
        ArgumentNullException.ThrowIfNull(liveInstances);
        var liveKeys = liveInstances
            .Select(context => new ComponentInstanceKey(context.AreaId, context.ComponentId))
            .ToHashSet(KeyComparer);

        foreach (var key in _instances.Keys
                     .Where(key => !liveKeys.Contains(key))
                     .ToArray())
        {
            Release(key);
        }
    }

    public void ReleaseAll()
    {
        var instances = _instances.ToArray();
        _instances.Clear();
        foreach (var (key, entry) in instances)
            TrackDisposal(key, entry.Instance);
    }

    public async Task ReleaseAndWaitAsync(
        IEnumerable<ComponentInstanceContext> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var keys = instances
            .Select(context => new ComponentInstanceKey(context.AreaId, context.ComponentId))
            .Distinct(KeyComparer)
            .ToArray();
        foreach (var key in keys)
            Release(key);

        var pending = keys
            .Select(key => _disposals.GetValueOrDefault(key))
            .Where(task => task is not null)
            .Distinct()
            .Cast<Task>()
            .ToArray();
        if (pending.Length == 0)
            return;

        var completion = Task.WhenAll(pending);
        var timeout = Task.Delay(ShutdownTimeout, cancellationToken);
        if (await Task.WhenAny(completion, timeout) != completion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("插件组件未能在时限内释放。");
        }

        try
        {
            await completion;
        }
        catch (Exception)
        {
            // 已完成的失败任务不会再执行插件代码；观察器仍会记录并清理。
        }
    }

    public async Task ShutdownAsync()
    {
        if (!IsShuttingDown)
        {
            IsShuttingDown = true;
            ReleaseAll();
        }

        await AwaitPendingDisposalsAsync();
    }

    private void Release(ComponentInstanceKey key)
    {
        if (!_instances.Remove(key, out var entry))
            return;

        TrackDisposal(key, entry.Instance);
    }

    private void TrackDisposal(
        ComponentInstanceKey key,
        IPolygonComponentInstance instance)
    {
        Task disposalTask;
        try
        {
            var disposal = instance.DisposeAsync();
            if (disposal.IsCompletedSuccessfully)
            {
                disposal.GetAwaiter().GetResult();
                return;
            }

            disposalTask = disposal.AsTask();
        }
        catch
        {
            // Plugin cleanup is best-effort during removal and application exit.
            return;
        }

        _disposals[key] = disposalTask;
        _ = ObserveDisposalAsync(key, disposalTask);
    }

    private async Task AwaitPendingDisposalsAsync()
    {
        var pending = _disposals.Values
            .Distinct()
            .ToArray();
        if (pending.Length == 0)
            return;

        using var timeoutCancellation = new CancellationTokenSource();
        var timeout = Task.Delay(ShutdownTimeout, timeoutCancellation.Token);
        foreach (var disposal in pending)
        {
            if (await Task.WhenAny(disposal, timeout) != disposal)
                return;

            try
            {
                await disposal;
            }
            catch
            {
                // Cleanup errors are isolated from the host shutdown path.
                // Every task is also observed by its disposal observer.
            }
        }

        await timeoutCancellation.CancelAsync();
    }

    private async Task ObserveDisposalAsync(
        ComponentInstanceKey key,
        Task disposalTask)
    {
        try
        {
            await disposalTask.ConfigureAwait(false);
        }
        catch
        {
            // Plugin cleanup is best-effort during removal and application exit.
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposals.TryGetValue(key, out var current) ||
                !ReferenceEquals(current, disposalTask))
            {
                return;
            }

            _disposals.Remove(key);
            _disposalCompleted(key.AreaId, key.ComponentId);
        });
    }

    private readonly record struct ComponentInstanceKey(
        string AreaId,
        string ComponentId);

    private sealed record InstanceEntry(
        IPolygonComponentInstance Instance,
        PolygonComponentRegistration Registration,
        IPolygonComponentFactory Factory);

    private sealed class ComponentInstanceKeyComparer : IEqualityComparer<ComponentInstanceKey>
    {
        public bool Equals(ComponentInstanceKey first, ComponentInstanceKey second) =>
            string.Equals(first.AreaId, second.AreaId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.ComponentId, second.ComponentId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ComponentInstanceKey key)
        {
            var hash = new HashCode();
            hash.Add(key.AreaId, StringComparer.OrdinalIgnoreCase);
            hash.Add(key.ComponentId, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
