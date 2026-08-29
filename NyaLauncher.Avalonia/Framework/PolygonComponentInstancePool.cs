using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 多边形组件运行时实例池：管理「已摆放组件」对应的业务实例。
/// <para>
/// 实例按 <b>（区域 Id + 组件 Id）</b> 建键。只要这个摆放位置还在，实例就一直存活，
/// 期间视图可以被反复重建（缩放、布局变化、主题热重载）而不影响它。
/// 位置消失、注册信息变化或工作区关闭时，实例才会被释放。
/// </para>
/// <para>
/// 插件异常不会拖垮宿主：创建失败时返回 <c>null</c>（组件仍以声明式形态显示，只是动作不可用）；
/// 清理失败也不会阻塞工作区关闭——最多等 <see cref="ShutdownTimeout"/>。
/// </para>
/// </summary>
/// <param name="disposalCompleted">
/// 某个实例清理完成后的回调（在 UI 线程上触发），参数为区域 Id 与组件 Id。
/// </param>
internal sealed class PolygonComponentInstancePool(
    Action<string, string> disposalCompleted)
{
    /// <summary>关闭时等待插件异步清理的最长时间；超过后不再等待，直接允许应用退出。</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private static readonly ComponentInstanceKeyComparer KeyComparer = new();

    private readonly Dictionary<ComponentInstanceKey, InstanceEntry> _instances =
        new(KeyComparer);
    private readonly Dictionary<ComponentInstanceKey, Task> _disposals = new(KeyComparer);
    private readonly Action<string, string> _disposalCompleted = disposalCompleted ??
        throw new ArgumentNullException(nameof(disposalCompleted));

    /// <summary>是否已进入关闭流程；为 <c>true</c> 后不再创建新实例。</summary>
    public bool IsShuttingDown { get; private set; }

    /// <summary>
    /// 取回该摆放位置的实例，不存在则创建。
    /// <para>
    /// 注册项发生变化（换了工厂）时会先释放旧实例再新建，
    /// 绝不复用上一个注册项留下的实例。
    /// </para>
    /// </summary>
    /// <param name="areaId">区域 Id。</param>
    /// <param name="componentId">组件 Id。</param>
    /// <param name="registration">注册项（定义 + 工厂）。</param>
    /// <returns>
    /// 可用的实例宿主；以下情况返回 <c>null</c>：正在关闭、工厂为空、
    /// 上一次释放尚未完成、插件工厂返回 <c>null</c> 或创建时抛异常。
    /// </returns>
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

            // 刷新后的注册项可能复用同一组（区域, 组件）键却换了工厂，
            // 因此绝不复用上一个注册项留下的实例，先释放再重建。
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
            // 第三方运行时出错不能阻止工作区其余组件继续创建。
            // 拿不到实例时组件仍以声明式形态显示，只是动作不可用。
            return null;
        }
    }

    /// <summary>释放指定摆放位置的实例（组件被移除或跨区移动时调用）。</summary>
    /// <param name="areaId">区域 Id。</param>
    /// <param name="componentId">组件 Id。</param>
    public void Release(string areaId, string componentId) =>
        Release(new ComponentInstanceKey(areaId, componentId));

    /// <summary>
    /// 释放所有不再被引用的实例：传入当前仍然存活的摆放位置，
    /// 池中不在其中的键都会被释放。
    /// </summary>
    /// <param name="liveInstances">当前工作区里仍然存在的实例上下文。</param>
    /// <exception cref="ArgumentNullException"><paramref name="liveInstances"/> 为 <c>null</c>。</exception>
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

    /// <summary>释放池中所有实例（工作区重建或即将关闭时调用），不等待清理完成。</summary>
    public void ReleaseAll()
    {
        var instances = _instances.ToArray();
        _instances.Clear();
        foreach (var (key, entry) in instances)
            TrackDisposal(key, entry.Instance);
    }

    /// <summary>
    /// 释放指定的一组实例并等待它们的异步清理完成（插件热卸载时按所有者精准等待）。
    /// 最多等待 <see cref="ShutdownTimeout"/>；超时或插件清理异常都不会抛出。
    /// </summary>
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

    /// <summary>
    /// 关闭实例池：先释放全部实例，再等待所有异步清理完成。
    /// <para>
    /// 最多等待 <see cref="ShutdownTimeout"/>（5 秒）——失控的第三方实现
    /// 不能永久阻止启动器退出。可重复调用。
    /// </para>
    /// </summary>
    /// <returns>清理完成或超时的异步任务；本方法不抛出插件的清理异常。</returns>
    public async Task ShutdownAsync()
    {
        if (!IsShuttingDown)
        {
            IsShuttingDown = true;
            ReleaseAll();
        }

        await AwaitPendingDisposalsAsync();
    }

    /// <summary>从池中摘除指定键并触发其释放。</summary>
    /// <param name="key">实例键。</param>
    private void Release(ComponentInstanceKey key)
    {
        if (!_instances.Remove(key, out var entry))
            return;

        TrackDisposal(key, entry.Instance);
    }

    /// <summary>
    /// 启动释放并登记它的任务，便于关闭时统一等待。
    /// 同步完成的清理直接结束，不登记。
    /// </summary>
    /// <param name="key">实例键。</param>
    /// <param name="instance">要释放的实例。</param>
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
            // 移除组件与退出应用时，插件的清理属于「尽力而为」，失败不影响宿主流程
            return;
        }

        _disposals[key] = disposalTask;
        _ = ObserveDisposalAsync(key, disposalTask);
    }

    /// <summary>
    /// 等待所有未完成的释放任务，整体受 <see cref="ShutdownTimeout"/> 限制。
    /// 超时后立即返回，剩下的任务由各自的观察者继续收尾。
    /// </summary>
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
                // 清理异常与宿主的关闭路径隔离；
                // 每个任务另有自己的观察者负责收尾（也会观察到同一个异常）。
            }
        }

        await timeoutCancellation.CancelAsync();
    }

    /// <summary>
    /// 观察一个释放任务：吞掉插件的清理异常，完成后在 UI 线程上回调
    /// <c>disposalCompleted</c>，并从待释放表中移除。
    /// </summary>
    /// <param name="key">实例键。</param>
    /// <param name="disposalTask">释放任务。</param>
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
            // 移除组件与退出应用时，插件的清理属于「尽力而为」，失败不影响宿主流程
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

    /// <summary>实例在池中的键：区域 Id + 组件 Id，两个字段均按忽略大小写比较。</summary>
    /// <param name="AreaId">区域 Id。</param>
    /// <param name="ComponentId">组件 Id。</param>
    private readonly record struct ComponentInstanceKey(
        string AreaId,
        string ComponentId);

    /// <summary>池中登记的实例条目，同时记住它的注册项与工厂，用于判断注册是否已刷新。</summary>
    /// <param name="Instance">实例宿主。</param>
    /// <param name="Registration">创建它时使用的注册项。</param>
    /// <param name="Factory">创建它时使用的工厂。</param>
    private sealed record InstanceEntry(
        IPolygonComponentInstance Instance,
        PolygonComponentRegistration Registration,
        IPolygonComponentFactory Factory);

    /// <summary><see cref="ComponentInstanceKey"/> 的比较器：区域 Id 与组件 Id 均按忽略大小写比较。</summary>
    private sealed class ComponentInstanceKeyComparer : IEqualityComparer<ComponentInstanceKey>
    {
        /// <summary>判断两个键是否表示同一个摆放位置。</summary>
        /// <param name="first">第一个键。</param>
        /// <param name="second">第二个键。</param>
        /// <returns>区域 Id 与组件 Id 都相同（忽略大小写）时返回 <c>true</c>。</returns>
        public bool Equals(ComponentInstanceKey first, ComponentInstanceKey second) =>
            string.Equals(first.AreaId, second.AreaId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.ComponentId, second.ComponentId, StringComparison.OrdinalIgnoreCase);

        /// <summary>计算与 <see cref="Equals(ComponentInstanceKey, ComponentInstanceKey)"/> 一致的哈希值。</summary>
        /// <param name="key">实例键。</param>
        /// <returns>忽略大小写的组合哈希。</returns>
        public int GetHashCode(ComponentInstanceKey key)
        {
            var hash = new HashCode();
            hash.Add(key.AreaId, StringComparer.OrdinalIgnoreCase);
            hash.Add(key.ComponentId, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
