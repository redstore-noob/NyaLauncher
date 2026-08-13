using System;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Describes a command exposed by a feature area.
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
    /// Preferred component footprint in device-independent pixels.
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
