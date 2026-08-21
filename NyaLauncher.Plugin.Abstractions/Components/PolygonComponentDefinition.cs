namespace NyaLauncher.Plugin.Abstractions.Components;

public enum ComponentTextRole
{
    Title,
    Body,
    Caption,
    Emphasis
}

public abstract record ComponentElementDefinition
{
    public required string Id { get; init; }

    public required ComponentRect Bounds { get; init; }

    public int ZIndex { get; init; }

    public bool IsVisible { get; init; } = true;

    public string? AutomationName { get; init; }
}

public sealed record TextElementDefinition : ComponentElementDefinition
{
    public string Text { get; init; } = string.Empty;

    public ComponentTextRole Role { get; init; } = ComponentTextRole.Body;

    public double FontSize { get; init; } = 12;

    public bool Wrap { get; init; } = true;
}

public sealed record ProgressElementDefinition : ComponentElementDefinition
{
    public string Label { get; init; } = string.Empty;

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 100;

    public double Value { get; init; }

    public bool ShowPercentage { get; init; } = true;

    public bool IsIndeterminate { get; init; }
}

/// <summary>
/// A launcher-rendered text editor. Press Enter to submit a single-line input;
/// multiline inputs use Ctrl+Enter so Enter remains available for new lines.
/// </summary>
public sealed record TextInputElementDefinition : ComponentElementDefinition
{
    public string Value { get; init; } = string.Empty;

    public string Placeholder { get; init; } = string.Empty;

    public int MaximumLength { get; init; } = 256;

    public bool IsMultiline { get; init; }

    public required string ActionId { get; init; }
}

/// <summary>A launcher-rendered boolean switch.</summary>
public sealed record ToggleElementDefinition : ComponentElementDefinition
{
    public string Label { get; init; } = string.Empty;

    public bool IsChecked { get; init; }

    public required string ActionId { get; init; }
}

/// <summary>A bounded numeric input rendered by the launcher.</summary>
public sealed record SliderElementDefinition : ComponentElementDefinition
{
    public string Label { get; init; } = string.Empty;

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 100;

    public double Value { get; init; }

    public double Step { get; init; } = 1;

    public required string ActionId { get; init; }
}

public enum ComponentImageStretch
{
    None,
    Fill,
    Uniform,
    UniformToFill
}

/// <summary>
/// Displays an image without exposing a UI-framework-specific bitmap type.
/// Sources may be local paths or absolute HTTPS URLs. SourceRect uses normalized
/// image coordinates, while SourcePixelRect selects an exact pixel region. At
/// most one crop rectangle may be specified.
/// </summary>
public sealed record ImageElementDefinition : ComponentElementDefinition
{
    public string Source { get; init; } = string.Empty;

    public ComponentRect? SourceRect { get; init; }

    public ComponentPixelRect? SourcePixelRect { get; init; }

    public ComponentImageStretch Stretch { get; init; } = ComponentImageStretch.UniformToFill;

    public string FallbackText { get; init; } = "?";

    public double CornerRadius { get; init; }

    public bool Pixelated { get; init; }
}

public sealed record ButtonElementDefinition : ComponentElementDefinition
{
    public required string Text { get; init; }

    public string Glyph { get; init; } = string.Empty;

    public required string ActionId { get; init; }

    public bool IsPrimary { get; init; }
}

/// <summary>
/// One command row shown by a dropdown element. Definitions can pin rows to
/// the top of the menu while runtime state contributes additional rows.
/// </summary>
public sealed record ComponentMenuItem
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public string SecondaryText { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    /// <summary>
    /// Optional absolute local path or HTTPS image shown before the labels.
    /// Hosts fall back to <see cref="Glyph"/> when it is unavailable.
    /// </summary>
    public string? IconSource { get; init; }

    public required string ActionId { get; init; }

    public IReadOnlyDictionary<string, string> Arguments { get; init; } =
        new Dictionary<string, string>();

    public bool IsEnabled { get; init; } = true;

    public bool IsSelected { get; init; }

    public bool SeparatorAfter { get; init; }
}

/// <summary>
/// A compact button that opens a command menu. Pinned items always remain at
/// the top; state-provided menu items are appended below them.
/// </summary>
public sealed record DropdownElementDefinition : ComponentElementDefinition
{
    public string Glyph { get; init; } = "⌄";

    public IReadOnlyList<ComponentMenuItem> PinnedItems { get; init; } = [];
}

public sealed record ComponentActionDefinition
{
    public required string Id { get; init; }

    public bool AllowReentry { get; init; }
}

/// <summary>
/// Theme fallbacks are expressed as #AARRGGBB/#RRGGBB strings so the public
/// contract does not expose a specific UI framework. The host may replace them
/// with semantic theme colors.
/// </summary>
public sealed record PolygonComponentTheme
{
    public string Surface { get; init; } = "#22283A";

    public string SurfaceHover { get; init; } = "#2D354D";

    public string Border { get; init; } = "#3A4563";

    public string BorderHover { get; init; } = "#7C8CFF";

    public string TextPrimary { get; init; } = "#F6F7FF";

    public string TextSecondary { get; init; } = "#A5AEC7";

    public string Accent { get; init; } = "#6C7BFF";

    public string AccentForeground { get; init; } = "#FFFFFF";

    public string ProgressTrack { get; init; } = "#30384F";

    public double BorderThickness { get; init; } = 1.5;
}

/// <summary>An immutable declaration shared by all visual instances.</summary>
public sealed class PolygonComponentDefinition
{
    public const int CurrentContractVersion = 1;

    public int ContractVersion { get; init; } = CurrentContractVersion;

    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Glyph { get; init; } = "⬡";

    public ComponentSize PreferredSize { get; init; } = new(300, 170);

    public ComponentSize MinimumSize { get; init; } = new(160, 90);

    public ComponentSize MaximumSize { get; init; } = new(900, 600);

    public PolygonShapeDefinition Shape { get; init; } = PolygonShapeDefinition.Rectangle();

    public ComponentRect DragHandleBounds { get; init; } = new(0.44, 0.035, 0.12, 0.13);

    public PolygonComponentTheme Theme { get; init; } = new();

    public IReadOnlyList<ComponentElementDefinition> Elements { get; init; } = [];

    public IReadOnlyList<ComponentActionDefinition> Actions { get; init; } = [];

    public string? SurfaceActionId { get; init; }
}
