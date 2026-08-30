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

    /// <summary>为 Minecraft 皮肤贴图：加载后自动合成为双层头像（脸层 + 帽层）。</summary>
    public bool IsSkinHead { get; init; }
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

    /// <summary>
    /// When <see cref="IconSource"/> is a Minecraft skin texture, only show the
    /// face avatar region (top-left 1/8 of the sheet) instead of the whole skin.
    /// </summary>
    public bool IsSkinHead { get; init; }

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

    /// <summary>触发按钮内容右对齐（如整卡下拉框场景中 chevron 靠右）。</summary>
    public bool AlignRight { get; init; } = false;
}

public sealed record ComponentActionDefinition
{
    public required string Id { get; init; }

    public bool AllowReentry { get; init; }
}

/// <summary>组件卡片视觉变体：具体颜色一律由宿主主题资源决定，组件不再自带颜色。</summary>
public enum ComponentThemeVariant
{
    /// <summary>常规卡片：表面/边框/文字均绑定主题的中性色。</summary>
    Default,

    /// <summary>强调卡片（如启动按钮卡）：整体使用主题强调色填充，文字用强调前景色。</summary>
    Launch
}

/// <summary>
/// 组件卡片主题设置：语义变体 + 数值参数。
/// 颜色完全由宿主主题资源决定（ComponentBgBrush / PrimaryTextBrush / AccentBrush 等），
/// 主题切换时自动跟随，无需重建组件。
/// 旧版（testplug 时代）的颜色字符串属性保留为兼容占位：仅为让按旧 API 编译的
/// 插件程序集能正常加载（否则运行时抛 MissingMethodException），宿主渲染时忽略这些值。
/// </summary>
public sealed record PolygonComponentTheme
{
    /// <summary>
    /// 兼容占位：所有颜色槽位委托给宿主语义资源的主题（与新默认行为一致）。
    /// </summary>
    public static PolygonComponentTheme InheritHost { get; } = new()
    {
        Surface = string.Empty,
        SurfaceHover = string.Empty,
        Border = string.Empty,
        BorderHover = string.Empty,
        TextPrimary = string.Empty,
        TextSecondary = string.Empty,
        Accent = string.Empty,
        AccentForeground = string.Empty,
        ProgressTrack = string.Empty
    };

    public ComponentThemeVariant Variant { get; init; } = ComponentThemeVariant.Default;

    /// <summary>兼容占位：宿主忽略，表面色由主题资源决定。</summary>
    public string Surface { get; init; } = "#22283A";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string SurfaceHover { get; init; } = "#2D354D";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string Border { get; init; } = "#3A4563";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string BorderHover { get; init; } = "#7C8CFF";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string TextPrimary { get; init; } = "#F6F7FF";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string TextSecondary { get; init; } = "#A5AEC7";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string Accent { get; init; } = "#6C7BFF";

    /// <summary>兼容占位：宿主忽略。</summary>
    public string AccentForeground { get; init; } = "#FFFFFF";

    /// <summary>兼容占位：宿主忽略。</summary>
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
