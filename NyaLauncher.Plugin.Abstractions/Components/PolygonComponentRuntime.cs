using System.Collections.ObjectModel;

namespace NyaLauncher.Plugin.Abstractions.Components;

public sealed record PolygonComponentRegistration
{
    public required PolygonComponentDefinition Definition { get; init; }

    public IPolygonComponentFactory? Factory { get; init; }
}

public interface IPolygonComponentProvider
{
    IReadOnlyList<PolygonComponentRegistration> GetPolygonComponents();
}

public interface IPolygonComponentFactory
{
    IPolygonComponentInstance Create(ComponentInstanceContext context);
}

public sealed record ComponentInstanceContext(
    string ComponentId,
    string AreaId);

public interface IPolygonComponentInstance : IAsyncDisposable
{
    ComponentStateSnapshot CurrentState { get; }

    event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

    ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken);
}

public sealed record ComponentActionInvocation(
    string ActionId,
    IReadOnlyDictionary<string, string>? Arguments = null);

public sealed record ComponentActionResult(
    bool Success,
    string? Message = null)
{
    public static ComponentActionResult Completed(string? message = null) => new(true, message);

    public static ComponentActionResult Failed(string message) => new(false, message);
}

public sealed class ComponentStateChangedEventArgs(ComponentStateSnapshot state) : EventArgs
{
    public ComponentStateSnapshot State { get; } = state;
}

/// <summary>
/// Complete visual state for one component revision. Missing elements and null
/// override fields resolve to their declaration defaults; snapshots are not
/// incremental patches and must not be mutated after publication.
/// </summary>
public sealed record ComponentStateSnapshot
{
    public long Revision { get; init; }

    public IReadOnlyDictionary<string, ComponentElementState> Elements { get; init; } =
        new ReadOnlyDictionary<string, ComponentElementState>(
            new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase));

    public static ComponentStateSnapshot Empty { get; } = new();
}

/// <summary>Optional overrides for one declared element in a full snapshot.</summary>
public sealed record ComponentElementState
{
    public string? Text { get; init; }

    /// <summary>
    /// Overrides the current text-input value, or a slider value formatted as
    /// an invariant-culture number. Null resolves to the declaration default.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>Overrides a toggle's checked state.</summary>
    public bool? IsChecked { get; init; }

    /// <summary>Overrides an image element's local path or absolute HTTPS URL.</summary>
    public string? ImageSource { get; init; }

    /// <summary>
    /// Optional token that forces an image to reload even when
    /// <see cref="ImageSource"/> is unchanged. Increment it to manually
    /// refresh remote avatars (e.g. after a skin change).
    /// </summary>
    public long? ImageRefreshToken { get; init; }

    public double? ProgressValue { get; init; }

    public bool? IsEnabled { get; init; }

    public bool? IsVisible { get; init; }

    public bool? IsIndeterminate { get; init; }

    /// <summary>
    /// Runtime rows appended after a dropdown element's pinned rows. A null
    /// value contributes no additional rows for this full state snapshot.
    /// </summary>
    public IReadOnlyList<ComponentMenuItem>? MenuItems { get; init; }
}

public sealed class DelegatePolygonComponentFactory(
    Func<ComponentInstanceContext, IPolygonComponentInstance> factory)
    : IPolygonComponentFactory
{
    public IPolygonComponentInstance Create(ComponentInstanceContext context) => factory(context);
}
