using System;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Plugin.Abstractions.Components;

/// <summary>
/// 内置/插件组件实例基类：统一封装 revision 递增、状态快照发布与释放检查，
/// 子类不再手写 Interlocked/Volatile/StateChanged 样板。
///
/// 子类约定：
/// - 构造函数中调用 <see cref="SetState"/> 发布初始状态；
/// - 状态变化时调用 <see cref="SetState"/>（传入的快照 Revision 为 0 时自动递增）；
/// - 重写 <see cref="DisposeAsync"/> 完成事件解绑等清理，并调用 base。
/// </summary>
public abstract class PolygonComponentInstanceBase : IPolygonComponentInstance
{
    private long _revision;
    private int _isDisposed;
    private ComponentStateSnapshot _currentState = ComponentStateSnapshot.Empty;

    /// <inheritdoc />
    public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

    /// <inheritdoc />
    public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

    /// <summary>实例是否已释放（释放后的状态发布与动作调用都应静默跳过）。</summary>
    protected bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    /// <summary>取下一个状态修订号（原子递增；到达 long.MaxValue 后饱和）。</summary>
    protected long NextRevision()
    {
        while (true)
        {
            var current = Volatile.Read(ref _revision);
            if (current == long.MaxValue)
                return current;
            if (Interlocked.CompareExchange(ref _revision, current + 1, current) == current)
                return current + 1;
        }
    }

    /// <summary>
    /// 发布新的状态快照：Revision 为 0 时自动分配修订号，已释放时静默忽略。
    /// </summary>
    protected void SetState(ComponentStateSnapshot next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (IsDisposed)
            return;

        next = next with { Revision = ReserveRevision(next.Revision) };
        while (true)
        {
            var current = Volatile.Read(ref _currentState);
            if (next.Revision <= current.Revision)
                return;
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _currentState, next, current),
                    current))
            {
                break;
            }
        }

        StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(next));
    }

    /// <summary>
    /// Keeps automatic revisions strictly above an explicitly supplied revision.
    /// Only zero requests automatic allocation; explicit stale revisions retain
    /// their identity and are ignored by SetState rather than reviving old data.
    /// </summary>
    private long ReserveRevision(long requested)
    {
        if (requested == 0)
            return NextRevision();

        while (true)
        {
            var current = Volatile.Read(ref _revision);
            if (requested <= current)
                return requested;
            if (Interlocked.CompareExchange(ref _revision, requested, current) == current)
                return requested;
        }
    }

    /// <summary>
    /// 标记实例为已释放并清空订阅者（幂等）。子类清理逻辑写在重写的
    /// <see cref="DisposeAsync"/> 中，最后调用 base。
    /// </summary>
    protected void MarkDisposed() => Interlocked.Exchange(ref _isDisposed, 1);

    /// <inheritdoc />
    public abstract ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        MarkDisposed();
        StateChanged = null;
        return ValueTask.CompletedTask;
    }
}
