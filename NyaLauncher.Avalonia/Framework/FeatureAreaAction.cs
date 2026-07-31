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
    bool IsPrimary = false);
