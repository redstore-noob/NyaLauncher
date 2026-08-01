using System;

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
    /// Preferred component footprint in device-independent pixels. Plugins can
    /// override it without changing the workspace layout contract.
    /// </summary>
    public double BaseWidth { get; init; } = 220;

    public double BaseHeight { get; init; } = 82;
}
