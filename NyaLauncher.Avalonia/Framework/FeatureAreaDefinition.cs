using System;
using System.Collections.Generic;
using Avalonia.Controls;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// A self-contained area that can be placed in the launcher workspace.
/// </summary>
public sealed class FeatureAreaDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public string Glyph { get; init; } = "◇";

    public string? IconPath { get; init; }

    /// <summary>
    /// Creates arbitrary area content for launcher-owned feature providers.
    /// When omitted, <see cref="Actions"/> are rendered by the built-in action view.
    /// </summary>
    public Func<Control>? ContentFactory { get; init; }

    public IReadOnlyList<FeatureAreaAction> Actions { get; init; } = [];

    /// <summary>
    /// Declarative components adapted into the same global catalog used by actions.
    /// </summary>
    public IReadOnlyList<PolygonComponentRegistration> PolygonComponents { get; init; } = [];
}
