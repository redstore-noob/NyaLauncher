namespace NyaLauncher.Plugin.Abstractions.Components;

/// <summary>Convenience builder for plugin authors; Build always validates.</summary>
public sealed class PolygonComponentBuilder
{
    private readonly string _id;
    private readonly string _title;
    private readonly List<ComponentElementDefinition> _elements = [];
    private readonly List<ComponentActionDefinition> _actions = [];
    private string _description = string.Empty;
    private string _glyph = "⬡";
    private ComponentSize _preferredSize = new(300, 170);
    private ComponentSize? _minimumSize;
    private ComponentSize? _maximumSize;
    private PolygonShapeDefinition _shape = PolygonShapeDefinition.Rectangle();
    private ComponentRect _dragHandleBounds = new(0.44, 0.035, 0.12, 0.13);
    private PolygonComponentTheme _theme = new();
    private string? _surfaceActionId;

    public PolygonComponentBuilder(string id, string title)
    {
        _id = id;
        _title = title;
    }

    public PolygonComponentBuilder WithDescription(string description)
    {
        _description = description ?? string.Empty;
        return this;
    }

    public PolygonComponentBuilder WithGlyph(string glyph)
    {
        _glyph = string.IsNullOrWhiteSpace(glyph) ? "⬡" : glyph;
        return this;
    }

    public PolygonComponentBuilder WithSize(double width, double height)
    {
        _preferredSize = new ComponentSize(width, height);
        return this;
    }

    public PolygonComponentBuilder WithSizeLimits(
        double minimumWidth,
        double minimumHeight,
        double maximumWidth,
        double maximumHeight)
    {
        _minimumSize = new ComponentSize(minimumWidth, minimumHeight);
        _maximumSize = new ComponentSize(maximumWidth, maximumHeight);
        return this;
    }

    public PolygonComponentBuilder WithShape(PolygonShapeDefinition shape)
    {
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));
        return this;
    }

    public PolygonComponentBuilder WithDragHandle(ComponentRect bounds)
    {
        _dragHandleBounds = bounds;
        return this;
    }

    public PolygonComponentBuilder WithTheme(PolygonComponentTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        return this;
    }

    public PolygonComponentBuilder AddAction(string id, bool allowReentry = false)
    {
        _actions.Add(new ComponentActionDefinition { Id = id, AllowReentry = allowReentry });
        return this;
    }

    public PolygonComponentBuilder UseSurfaceAction(string actionId)
    {
        _surfaceActionId = actionId;
        return this;
    }

    public PolygonComponentBuilder AddText(
        string id,
        ComponentRect bounds,
        string text,
        ComponentTextRole role = ComponentTextRole.Body,
        double fontSize = 12)
    {
        _elements.Add(new TextElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Text = text,
            Role = role,
            FontSize = fontSize
        });
        return this;
    }

    public PolygonComponentBuilder AddProgress(
        string id,
        ComponentRect bounds,
        string label,
        double value = 0,
        double minimum = 0,
        double maximum = 100)
    {
        _elements.Add(new ProgressElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Label = label,
            Minimum = minimum,
            Maximum = maximum,
            Value = value
        });
        return this;
    }

    public PolygonComponentBuilder AddTextInput(
        string id,
        ComponentRect bounds,
        string actionId,
        string value = "",
        string placeholder = "",
        int maximumLength = 256,
        bool isMultiline = false)
    {
        _elements.Add(new TextInputElementDefinition
        {
            Id = id,
            Bounds = bounds,
            ActionId = actionId,
            Value = value ?? string.Empty,
            Placeholder = placeholder ?? string.Empty,
            MaximumLength = maximumLength,
            IsMultiline = isMultiline
        });
        return this;
    }

    public PolygonComponentBuilder AddToggle(
        string id,
        ComponentRect bounds,
        string label,
        string actionId,
        bool isChecked = false)
    {
        _elements.Add(new ToggleElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Label = label ?? string.Empty,
            ActionId = actionId,
            IsChecked = isChecked
        });
        return this;
    }

    public PolygonComponentBuilder AddSlider(
        string id,
        ComponentRect bounds,
        string label,
        string actionId,
        double minimum = 0,
        double maximum = 100,
        double value = 0,
        double step = 1)
    {
        _elements.Add(new SliderElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Label = label ?? string.Empty,
            ActionId = actionId,
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Step = step
        });
        return this;
    }

    public PolygonComponentBuilder AddImage(
        string id,
        ComponentRect bounds,
        string source = "",
        ComponentRect? sourceRect = null,
        ComponentImageStretch stretch = ComponentImageStretch.UniformToFill,
        string fallbackText = "?",
        double cornerRadius = 0,
        bool pixelated = false,
        ComponentPixelRect? sourcePixelRect = null,
        bool isSkinHead = false)
    {
        _elements.Add(new ImageElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Source = source ?? string.Empty,
            SourceRect = sourceRect,
            SourcePixelRect = sourcePixelRect,
            Stretch = stretch,
            FallbackText = fallbackText ?? string.Empty,
            CornerRadius = cornerRadius,
            Pixelated = pixelated,
            IsSkinHead = isSkinHead
        });
        return this;
    }

    public PolygonComponentBuilder AddButton(
        string id,
        ComponentRect bounds,
        string text,
        string actionId,
        string glyph = "",
        bool isPrimary = false)
    {
        _elements.Add(new ButtonElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Text = text,
            ActionId = actionId,
            Glyph = glyph,
            IsPrimary = isPrimary
        });
        return this;
    }

    public PolygonComponentBuilder AddDropdown(
        string id,
        ComponentRect bounds,
        string glyph = "⌄",
        IEnumerable<ComponentMenuItem>? pinnedItems = null,
        bool alignRight = false)
    {
        _elements.Add(new DropdownElementDefinition
        {
            Id = id,
            Bounds = bounds,
            Glyph = glyph ?? "⌄",
            PinnedItems = pinnedItems?.ToArray() ?? [],
            AlignRight = alignRight
        });
        return this;
    }

    public PolygonComponentDefinition Build()
    {
        var definition = new PolygonComponentDefinition
        {
            Id = _id,
            Title = _title,
            Description = _description,
            Glyph = _glyph,
            PreferredSize = _preferredSize,
            MinimumSize = _minimumSize ?? new ComponentSize(
                Math.Min(160, _preferredSize.Width),
                Math.Min(90, _preferredSize.Height)),
            MaximumSize = _maximumSize ?? new ComponentSize(
                Math.Max(900, _preferredSize.Width),
                Math.Max(600, _preferredSize.Height)),
            Shape = _shape,
            DragHandleBounds = _dragHandleBounds,
            Theme = _theme,
            Elements = _elements.ToArray(),
            Actions = _actions.ToArray(),
            SurfaceActionId = _surfaceActionId
        };
        return PolygonComponentValidator.ValidateAndSnapshot(definition);
    }
}
