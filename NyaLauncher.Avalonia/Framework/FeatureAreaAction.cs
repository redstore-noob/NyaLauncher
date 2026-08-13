using System;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Describes a command exposed by a feature area.
/// Plugins can use this lightweight model when they do not need a custom view.
/// </summary>
public sealed record FeatureAreaAction(
    string Id,
    string Title,
    string Description,
    string Glyph,
    Action? Execute = null,
    bool IsPrimary = false)
{
    /// <summary>
    /// Stable owner used by the registry to suspend and hot-replace every
    /// contribution from one plugin without confusing that with user removal.
    /// Built-in launcher components leave this value null.
    /// </summary>
    public string? OwnerPluginId { get; init; }

    /// <summary>
    /// A dormant action is a launcher-owned placeholder for a plugin component
    /// that is currently unavailable.  It deliberately keeps the original id
    /// and footprint so workspace membership and placement survive disabling.
    /// </summary>
    public bool IsDormant { get; init; }

    /// <summary>
    /// Preferred component footprint in device-independent pixels. Plugins can
    /// override it without changing the workspace layout contract.
    /// </summary>
    public double BaseWidth { get; init; } = 220;

    public double BaseHeight { get; init; } = 82;

    /// <summary>
    /// Optional declarative polygon component. Existing actions keep using the
    /// legacy rectangular button renderer when this property is null.
    /// </summary>
    public PolygonComponentRegistration? PolygonComponent { get; init; }

    public double EffectiveBaseWidth =>
        PolygonComponent?.Definition.PreferredSize.Width ?? BaseWidth;

    public double EffectiveBaseHeight =>
        PolygonComponent?.Definition.PreferredSize.Height ?? BaseHeight;
}
