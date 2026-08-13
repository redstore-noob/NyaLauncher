using System.Collections.ObjectModel;

namespace NyaLauncher.Plugin.Abstractions.Components;

public sealed record ComponentValidationError(
    string Code,
    string Path,
    string Message);

public sealed class ComponentValidationResult(
    IReadOnlyList<ComponentValidationError> errors)
{
    public IReadOnlyList<ComponentValidationError> Errors { get; } =
        Array.AsReadOnly(errors?.ToArray() ??
            throw new ArgumentNullException(nameof(errors)));

    public bool IsValid => Errors.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new ComponentDefinitionException(Errors);
    }
}

public sealed class ComponentDefinitionException(
    IReadOnlyList<ComponentValidationError> errors)
    : ArgumentException(string.Join(Environment.NewLine, errors.Select(error =>
        $"[{error.Code}] {error.Path}: {error.Message}")))
{
    public IReadOnlyList<ComponentValidationError> Errors { get; } =
        Array.AsReadOnly(errors?.ToArray() ??
            throw new ArgumentNullException(nameof(errors)));
}

public static class PolygonComponentValidator
{
    private const double Epsilon = 0.000001;
    private const double MinimumComponentDimension = 16;
    private const double MaximumComponentDimension = 8192;
    private const double MinimumFontSize = 1;
    private const double MaximumFontSize = 512;
    private const double MaximumBorderThickness = 128;
    private const int MaximumElementCount = 256;
    private const int MaximumActionCount = 128;
    private const int MaximumMenuItemCount = 128;
    private const int MaximumMenuArgumentCount = 16;
    private const int MaximumMenuArgumentValueLength = 1024;
    private const int MaximumMenuTextLength = 256;
    private const int MaximumMenuSecondaryTextLength = 512;
    private const int MaximumMenuGlyphLength = 32;
    private const int MaximumImageSourceLength = 4096;
    private const int MaximumImageFallbackTextLength = 64;
    private const double MaximumImageCornerRadius = 512;
    private const int MaximumInputLength = 32768;
    private const int MaximumInputPlaceholderLength = 512;
    private const int MaximumInteractiveLabelLength = 256;

    /// <summary>
    /// Validates a declaration and returns a detached snapshot. Hosts should
    /// retain the returned value instead of collections owned by a plugin.
    /// </summary>
    public static PolygonComponentDefinition ValidateAndSnapshot(
        PolygonComponentDefinition definition)
    {
        Validate(definition).ThrowIfInvalid();

        return new PolygonComponentDefinition
        {
            ContractVersion = definition.ContractVersion,
            Id = definition.Id,
            Title = definition.Title,
            Description = definition.Description,
            Glyph = definition.Glyph,
            PreferredSize = definition.PreferredSize,
            MinimumSize = definition.MinimumSize,
            MaximumSize = definition.MaximumSize,
            Shape = new PolygonShapeDefinition
            {
                Points = Array.AsReadOnly(definition.Shape.Points.ToArray())
            },
            DragHandleBounds = definition.DragHandleBounds,
            Theme = definition.Theme with { },
            Elements = Array.AsReadOnly(
                definition.Elements.Select(SnapshotElement).ToArray()),
            Actions = Array.AsReadOnly(
                definition.Actions.Select(action => action with { }).ToArray()),
            SurfaceActionId = definition.SurfaceActionId
        };
    }

    public static ComponentValidationResult Validate(PolygonComponentDefinition? definition)
    {
        var errors = new List<ComponentValidationError>();
        if (definition is null)
        {
            errors.Add(new("definition.null", "$", "组件定义不能为空。"));
            return new ComponentValidationResult(errors);
        }

        ValidateId(definition.Id, "$.id", errors, requireNamespace: true);
        if (string.IsNullOrWhiteSpace(definition.Title))
            errors.Add(new("title.empty", "$.title", "组件标题不能为空。"));
        if (definition.ContractVersion != PolygonComponentDefinition.CurrentContractVersion)
        {
            errors.Add(new(
                "contract.unsupported",
                "$.contractVersion",
                $"不支持的契约版本 {definition.ContractVersion}。"));
        }

        ValidateSizes(definition, errors);
        ValidateRect(definition.DragHandleBounds, "$.dragHandleBounds", errors);
        ValidateShape(definition.Shape, errors);
        ValidateDragHandle(definition.DragHandleBounds, definition.Shape, errors);
        ValidateTheme(definition.Theme, errors);

        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.Actions is null)
        {
            errors.Add(new("actions.null", "$.actions", "动作集合不能为 null。"));
        }
        else
        {
            if (definition.Actions.Count > MaximumActionCount)
            {
                errors.Add(new(
                    "actions.count",
                    "$.actions",
                    $"动作数量不能超过 {MaximumActionCount}。"));
            }

            var actionsToValidate = Math.Min(
                definition.Actions.Count,
                MaximumActionCount);
            for (var index = 0; index < actionsToValidate; index++)
            {
                var action = definition.Actions[index];
                var path = $"$.actions[{index}]";
                if (action is null)
                {
                    errors.Add(new("action.null", path, "动作不能为 null。"));
                    continue;
                }

                ValidateId(action.Id, $"{path}.id", errors, requireNamespace: false);
                if (!string.IsNullOrWhiteSpace(action.Id) && !actionIds.Add(action.Id))
                    errors.Add(new("action.duplicate", $"{path}.id", $"动作 ID“{action.Id}”重复。"));
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.SurfaceActionId) &&
            !actionIds.Contains(definition.SurfaceActionId))
        {
            errors.Add(new(
                "surfaceAction.missing",
                "$.surfaceActionId",
                "表面动作必须引用已声明的动作 ID。"));
        }

        var elementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.Elements is null)
        {
            errors.Add(new("elements.null", "$.elements", "元素集合不能为 null。"));
        }
        else
        {
            if (definition.Elements.Count > MaximumElementCount)
            {
                errors.Add(new(
                    "elements.count",
                    "$.elements",
                    $"元素数量不能超过 {MaximumElementCount}。"));
            }

            var elementsToValidate = Math.Min(
                definition.Elements.Count,
                MaximumElementCount);
            for (var index = 0; index < elementsToValidate; index++)
            {
                var element = definition.Elements[index];
                var path = $"$.elements[{index}]";
                if (element is null)
                {
                    errors.Add(new("element.null", path, "元素不能为空。"));
                    continue;
                }

                ValidateId(element.Id, $"{path}.id", errors, requireNamespace: false);
                if (!string.IsNullOrWhiteSpace(element.Id) && !elementIds.Add(element.Id))
                    errors.Add(new("element.duplicate", $"{path}.id", $"元素 ID“{element.Id}”重复。"));
                ValidateRect(element.Bounds, $"{path}.bounds", errors);

                switch (element)
                {
                    case TextElementDefinition text when
                        !double.IsFinite(text.FontSize) || text.FontSize < MinimumFontSize ||
                        text.FontSize > MaximumFontSize:
                        errors.Add(new(
                            "text.fontSize",
                            $"{path}.fontSize",
                            $"字体大小必须是 [{MinimumFontSize}, {MaximumFontSize}] 范围内的有限数。"));
                        break;
                    case ProgressElementDefinition progress:
                        ValidateProgress(progress, path, errors);
                        break;
                    case TextInputElementDefinition input:
                        ValidateTextInput(input, path, actionIds, errors);
                        break;
                    case ToggleElementDefinition toggle:
                        ValidateInteractiveLabel(toggle.Label, path, "toggle", errors);
                        ValidateActionReference(toggle.ActionId, path, "toggle", actionIds, errors);
                        break;
                    case SliderElementDefinition slider:
                        ValidateSlider(slider, path, actionIds, errors);
                        break;
                    case ImageElementDefinition image:
                        ValidateImage(image, path, errors);
                        break;
                    case ButtonElementDefinition button:
                        if (string.IsNullOrWhiteSpace(button.Text))
                            errors.Add(new("button.text", $"{path}.text", "按钮文字不能为空。"));
                        if (string.IsNullOrWhiteSpace(button.ActionId) ||
                            !actionIds.Contains(button.ActionId))
                        {
                            errors.Add(new(
                                "button.actionMissing",
                                $"{path}.actionId",
                                $"按钮引用了未声明的动作“{button.ActionId}”。"));
                        }
                        break;
                    case DropdownElementDefinition dropdown:
                        ValidateMenuItems(
                            dropdown.PinnedItems,
                            $"{path}.pinnedItems",
                            actionIds,
                            errors);
                        break;
                    default:
                        if (element is not TextElementDefinition and
                            not ProgressElementDefinition and
                            not TextInputElementDefinition and
                            not ToggleElementDefinition and
                            not SliderElementDefinition and
                            not ImageElementDefinition and
                            not ButtonElementDefinition and
                            not DropdownElementDefinition)
                        {
                            errors.Add(new("element.unsupported", path, "宿主不支持该元素类型。"));
                        }
                        break;
                }
            }
        }

        return new ComponentValidationResult(errors);
    }

    private static void ValidateImage(
        ImageElementDefinition image,
        string path,
        ICollection<ComponentValidationError> errors)
    {
        if (image.Source?.Length > MaximumImageSourceLength)
        {
            errors.Add(new(
                "image.sourceLength",
                $"{path}.source",
                $"图片来源长度不能超过 {MaximumImageSourceLength}。"));
        }

        if (image.FallbackText?.Length > MaximumImageFallbackTextLength)
        {
            errors.Add(new(
                "image.fallbackTextLength",
                $"{path}.fallbackText",
                $"图片占位文字长度不能超过 {MaximumImageFallbackTextLength}。"));
        }

        if (!Enum.IsDefined(image.Stretch))
            errors.Add(new("image.stretch", $"{path}.stretch", "图片缩放模式无效。"));
        if (!double.IsFinite(image.CornerRadius) || image.CornerRadius < 0 ||
            image.CornerRadius > MaximumImageCornerRadius)
        {
            errors.Add(new(
                "image.cornerRadius",
                $"{path}.cornerRadius",
                $"图片圆角必须是 [0, {MaximumImageCornerRadius}] 范围内的有限数。"));
        }

        if (image.SourceRect is { } sourceRect)
            ValidateRect(sourceRect, $"{path}.sourceRect", errors);
        if (image.SourcePixelRect is { } sourcePixelRect)
        {
            if (sourcePixelRect.X < 0 || sourcePixelRect.Y < 0 ||
                sourcePixelRect.Width <= 0 || sourcePixelRect.Height <= 0)
            {
                errors.Add(new(
                    "image.sourcePixelRect",
                    $"{path}.sourcePixelRect",
                    "图片像素裁剪区域必须使用非负坐标和正宽高。"));
            }
        }

        if (image.SourceRect is not null && image.SourcePixelRect is not null)
        {
            errors.Add(new(
                "image.sourceRectConflict",
                path,
                "归一化裁剪区域与像素裁剪区域不能同时设置。"));
        }
    }

    private static void ValidateId(
        string? id,
        string path,
        ICollection<ComponentValidationError> errors,
        bool requireNamespace)
    {
        if (string.IsNullOrEmpty(id))
        {
            errors.Add(new("id.empty", path, "ID 不能为空。"));
            return;
        }

        if (id.Length > (requireNamespace ? 128 : 64))
        {
            errors.Add(new("id.length", path, "ID 长度超出宿主限制。"));
            return;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add(new("id.empty", path, "ID 不能为空。"));
            return;
        }

        if (id.Any(char.IsWhiteSpace))
            errors.Add(new("id.whitespace", path, "ID 不能包含空白字符。"));
        if (id.Any(character => char.IsControl(character) || char.IsSurrogate(character)))
            errors.Add(new("id.control", path, "ID 不能包含控制字符或代理字符。"));
        if (requireNamespace)
        {
            var separator = id.IndexOf('/');
            if (separator <= 0 || separator == id.Length - 1 ||
                separator != id.LastIndexOf('/'))
            {
                errors.Add(new("id.namespace", path, "第三方组件 ID 必须使用 publisher.plugin/name 格式。"));
            }
            else if (!IsIdSegment(id.AsSpan(0, separator)) ||
                     !IsIdSegment(id.AsSpan(separator + 1)))
            {
                errors.Add(new(
                    "id.characters",
                    path,
                    "组件 ID 只能使用字母、数字、点、下划线和连字符。"));
            }
        }
        else if (!IsIdSegment(id.AsSpan()))
        {
            errors.Add(new(
                "id.characters",
                path,
                "元素与动作 ID 只能使用字母、数字、点、下划线和连字符。"));
        }
    }

    private static void ValidateSizes(
        PolygonComponentDefinition definition,
        ICollection<ComponentValidationError> errors)
    {
        if (!IsPositive(definition.MinimumSize) ||
            !IsPositive(definition.PreferredSize) ||
            !IsPositive(definition.MaximumSize))
        {
            errors.Add(new("size.invalid", "$.preferredSize", "组件尺寸必须是正有限数。"));
            return;
        }

        if (definition.MinimumSize.Width > MaximumComponentDimension ||
            definition.MinimumSize.Height > MaximumComponentDimension ||
            definition.PreferredSize.Width > MaximumComponentDimension ||
            definition.PreferredSize.Height > MaximumComponentDimension ||
            definition.MaximumSize.Width > MaximumComponentDimension ||
            definition.MaximumSize.Height > MaximumComponentDimension)
        {
            errors.Add(new(
                "size.limit",
                "$.preferredSize",
                $"组件任一尺寸不能超过 {MaximumComponentDimension} DIP。"));
        }

        if (definition.MinimumSize.Width < MinimumComponentDimension ||
            definition.MinimumSize.Height < MinimumComponentDimension ||
            definition.PreferredSize.Width < MinimumComponentDimension ||
            definition.PreferredSize.Height < MinimumComponentDimension ||
            definition.MaximumSize.Width < MinimumComponentDimension ||
            definition.MaximumSize.Height < MinimumComponentDimension)
        {
            errors.Add(new(
                "size.limit",
                "$.preferredSize",
                $"组件任一尺寸不能小于 {MinimumComponentDimension} DIP。"));
        }

        if (definition.MinimumSize.Width > definition.PreferredSize.Width ||
            definition.MinimumSize.Height > definition.PreferredSize.Height ||
            definition.PreferredSize.Width > definition.MaximumSize.Width ||
            definition.PreferredSize.Height > definition.MaximumSize.Height)
        {
            errors.Add(new(
                "size.order",
                "$.preferredSize",
                "尺寸必须满足 Minimum ≤ Preferred ≤ Maximum。"));
        }
    }

    private static bool IsPositive(ComponentSize size) =>
        double.IsFinite(size.Width) && double.IsFinite(size.Height) &&
        size.Width > 0 && size.Height > 0;

    private static void ValidateRect(
        ComponentRect rect,
        string path,
        ICollection<ComponentValidationError> errors)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) ||
            rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
            rect.X + rect.Width > 1 + Epsilon || rect.Y + rect.Height > 1 + Epsilon)
        {
            errors.Add(new("bounds.invalid", path, "归一化边界必须完整位于 [0,1] 且宽高为正。"));
        }
    }

    private static void ValidateShape(
        PolygonShapeDefinition? shape,
        ICollection<ComponentValidationError> errors)
    {
        if (shape?.Points is null || shape.Points.Count is < 3 or > 64)
        {
            errors.Add(new("shape.count", "$.shape.points", "多边形顶点数必须介于 3 与 64 之间。"));
            return;
        }

        for (var index = 0; index < shape.Points.Count; index++)
        {
            var point = shape.Points[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                point.X < 0 || point.X > 1 || point.Y < 0 || point.Y > 1)
            {
                errors.Add(new(
                    "shape.point",
                    $"$.shape.points[{index}]",
                    "顶点必须是 [0,1] 范围内的有限坐标。"));
            }

            var next = shape.Points[(index + 1) % shape.Points.Count];
            if (DistanceSquared(point, next) <= Epsilon * Epsilon)
            {
                errors.Add(new(
                    "shape.duplicate",
                    $"$.shape.points[{index}]",
                    "相邻顶点不能重复。"));
            }
        }

        if (Math.Abs(SignedArea(shape.Points)) <= Epsilon)
            errors.Add(new("shape.area", "$.shape.points", "多边形面积过小或已退化。"));

        for (var first = 0; first < shape.Points.Count; first++)
        {
            var firstNext = (first + 1) % shape.Points.Count;
            for (var second = first + 1; second < shape.Points.Count; second++)
            {
                var secondNext = (second + 1) % shape.Points.Count;
                if (first == second || firstNext == second || secondNext == first)
                    continue;
                if (first == 0 && secondNext == 0)
                    continue;

                if (SegmentsIntersect(
                        shape.Points[first],
                        shape.Points[firstNext],
                        shape.Points[second],
                        shape.Points[secondNext]))
                {
                    errors.Add(new(
                        "shape.selfIntersection",
                        "$.shape.points",
                        "多边形边不能自相交。"));
                    return;
                }
            }
        }
    }

    private static void ValidateDragHandle(
        ComponentRect bounds,
        PolygonShapeDefinition? shape,
        ICollection<ComponentValidationError> errors)
    {
        if (shape?.Points is null || shape.Points.Count is < 3 or > 64 ||
            !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var center = new ComponentPoint(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);
        if (!shape.Contains(center))
        {
            errors.Add(new(
                "dragHandle.outside",
                "$.dragHandleBounds",
                "拖动把手的中心必须位于多边形轮廓内。"));
        }
    }

    private static void ValidateProgress(
        ProgressElementDefinition progress,
        string path,
        ICollection<ComponentValidationError> errors)
    {
        var span = progress.Maximum - progress.Minimum;
        if (!double.IsFinite(progress.Minimum) || !double.IsFinite(progress.Maximum) ||
            !double.IsFinite(progress.Value) || !double.IsFinite(span) || span <= 0)
        {
            errors.Add(new("progress.range", path, "进度范围必须是有效的递增有限区间。"));
        }
        else if (progress.Value < progress.Minimum || progress.Value > progress.Maximum)
        {
            errors.Add(new("progress.value", $"{path}.value", "进度初值必须位于范围内。"));
        }
    }

    private static void ValidateTextInput(
        TextInputElementDefinition input,
        string path,
        IReadOnlySet<string> actionIds,
        ICollection<ComponentValidationError> errors)
    {
        if (input.MaximumLength is < 1 or > MaximumInputLength)
        {
            errors.Add(new(
                "input.maximumLength",
                $"{path}.maximumLength",
                $"输入长度限制必须位于 [1, {MaximumInputLength}]。"));
        }
        if (input.Value is null || input.Value.Length > input.MaximumLength)
        {
            errors.Add(new(
                "input.value",
                $"{path}.value",
                "输入初值不能为空，并且不能超过长度限制。"));
        }
        if (input.Placeholder is null ||
            input.Placeholder.Length > MaximumInputPlaceholderLength)
        {
            errors.Add(new(
                "input.placeholder",
                $"{path}.placeholder",
                $"输入占位文字不能为 null，且长度不能超过 {MaximumInputPlaceholderLength}。"));
        }

        ValidateActionReference(input.ActionId, path, "input", actionIds, errors);
    }

    private static void ValidateSlider(
        SliderElementDefinition slider,
        string path,
        IReadOnlySet<string> actionIds,
        ICollection<ComponentValidationError> errors)
    {
        ValidateInteractiveLabel(slider.Label, path, "slider", errors);
        ValidateActionReference(slider.ActionId, path, "slider", actionIds, errors);

        var span = slider.Maximum - slider.Minimum;
        if (!double.IsFinite(slider.Minimum) || !double.IsFinite(slider.Maximum) ||
            !double.IsFinite(slider.Value) || !double.IsFinite(slider.Step) ||
            !double.IsFinite(span) || span <= 0)
        {
            errors.Add(new("slider.range", path, "滑块范围、初值与步长必须是有效有限数。"));
            return;
        }
        if (slider.Value < slider.Minimum || slider.Value > slider.Maximum)
            errors.Add(new("slider.value", $"{path}.value", "滑块初值必须位于范围内。"));
        if (slider.Step <= 0 || slider.Step > span)
            errors.Add(new("slider.step", $"{path}.step", "滑块步长必须大于零且不超过范围跨度。"));
    }

    private static void ValidateInteractiveLabel(
        string? label,
        string path,
        string codePrefix,
        ICollection<ComponentValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > MaximumInteractiveLabelLength)
        {
            errors.Add(new(
                $"{codePrefix}.label",
                $"{path}.label",
                $"标签不能为空且长度不能超过 {MaximumInteractiveLabelLength}。"));
        }
    }

    private static void ValidateActionReference(
        string? actionId,
        string path,
        string codePrefix,
        IReadOnlySet<string> actionIds,
        ICollection<ComponentValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(actionId) || !actionIds.Contains(actionId))
        {
            errors.Add(new(
                $"{codePrefix}.actionMissing",
                $"{path}.actionId",
                $"交互元素引用了未声明的动作“{actionId}”。"));
        }
    }

    private static void ValidateTheme(
        PolygonComponentTheme theme,
        ICollection<ComponentValidationError> errors)
    {
        if (theme is null)
        {
            errors.Add(new("theme.null", "$.theme", "主题不能为空。"));
            return;
        }

        if (!double.IsFinite(theme.BorderThickness) || theme.BorderThickness < 0 ||
            theme.BorderThickness > MaximumBorderThickness)
        {
            errors.Add(new(
                "theme.border",
                "$.theme.borderThickness",
                $"边框宽度必须是 [0, {MaximumBorderThickness}] 范围内的有限数。"));
        }
    }

    private static void ValidateMenuItems(
        IReadOnlyList<ComponentMenuItem>? items,
        string path,
        IReadOnlySet<string> actionIds,
        ICollection<ComponentValidationError> errors)
    {
        if (items is null)
        {
            errors.Add(new("menu.itemsNull", path, "下拉菜单固定项集合不能为 null。"));
            return;
        }

        if (items.Count > MaximumMenuItemCount)
        {
            errors.Add(new(
                "menu.itemsCount",
                path,
                $"下拉菜单固定项不能超过 {MaximumMenuItemCount} 个。"));
        }

        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemsToValidate = Math.Min(items.Count, MaximumMenuItemCount);
        for (var index = 0; index < itemsToValidate; index++)
        {
            var item = items[index];
            var itemPath = $"{path}[{index}]";
            if (item is null)
            {
                errors.Add(new("menu.itemNull", itemPath, "下拉菜单项不能为空。"));
                continue;
            }

            ValidateId(item.Id, $"{itemPath}.id", errors, requireNamespace: false);
            if (!string.IsNullOrWhiteSpace(item.Id) && !knownIds.Add(item.Id))
            {
                errors.Add(new(
                    "menu.itemDuplicate",
                    $"{itemPath}.id",
                    $"下拉菜单项 ID“{item.Id}”重复。"));
            }

            if (string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > MaximumMenuTextLength)
            {
                errors.Add(new(
                    "menu.itemText",
                    $"{itemPath}.text",
                    $"下拉菜单项文字不能为空且长度不能超过 {MaximumMenuTextLength}。"));
            }
            if (item.SecondaryText?.Length > MaximumMenuSecondaryTextLength)
            {
                errors.Add(new(
                    "menu.itemSecondaryText",
                    $"{itemPath}.secondaryText",
                    $"下拉菜单项副标题长度不能超过 {MaximumMenuSecondaryTextLength}。"));
            }
            if (item.Glyph?.Length > MaximumMenuGlyphLength)
            {
                errors.Add(new(
                    "menu.itemGlyph",
                    $"{itemPath}.glyph",
                    $"下拉菜单项图标长度不能超过 {MaximumMenuGlyphLength}。"));
            }
            if (item.IconSource?.Length > MaximumImageSourceLength)
            {
                errors.Add(new(
                    "menu.itemIconSource",
                    $"{itemPath}.iconSource",
                    $"下拉菜单项图片来源长度不能超过 {MaximumImageSourceLength}。"));
            }
            if (string.IsNullOrWhiteSpace(item.ActionId) || !actionIds.Contains(item.ActionId))
            {
                errors.Add(new(
                    "menu.itemActionMissing",
                    $"{itemPath}.actionId",
                    $"下拉菜单项引用了未声明的动作“{item.ActionId}”。"));
            }

            ValidateMenuArguments(item.Arguments, $"{itemPath}.arguments", errors);
        }
    }

    private static void ValidateMenuArguments(
        IReadOnlyDictionary<string, string>? arguments,
        string path,
        ICollection<ComponentValidationError> errors)
    {
        if (arguments is null)
        {
            errors.Add(new("menu.argumentsNull", path, "下拉菜单参数集合不能为 null。"));
            return;
        }

        if (arguments.Count > MaximumMenuArgumentCount)
        {
            errors.Add(new(
                "menu.argumentsCount",
                path,
                $"下拉菜单参数不能超过 {MaximumMenuArgumentCount} 个。"));
        }

        var inspected = 0;
        foreach (var (key, value) in arguments)
        {
            if (++inspected > MaximumMenuArgumentCount)
                break;

            ValidateId(key, $"{path}.{key}", errors, requireNamespace: false);
            if (value is null || value.Length > MaximumMenuArgumentValueLength)
            {
                errors.Add(new(
                    "menu.argumentValue",
                    $"{path}.{key}",
                    $"下拉菜单参数值长度不能超过 {MaximumMenuArgumentValueLength}。"));
            }
        }
    }

    private static double SignedArea(IReadOnlyList<ComponentPoint> points)
    {
        var area = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            area += points[index].X * next.Y - next.X * points[index].Y;
        }
        return area / 2;
    }

    private static double DistanceSquared(ComponentPoint first, ComponentPoint second) =>
        Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2);

    private static bool SegmentsIntersect(
        ComponentPoint a,
        ComponentPoint b,
        ComponentPoint c,
        ComponentPoint d)
    {
        var d1 = Direction(c, d, a);
        var d2 = Direction(c, d, b);
        var d3 = Direction(a, b, c);
        var d4 = Direction(a, b, d);
        if (((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) &&
            ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon)))
        {
            return true;
        }

        return Math.Abs(d1) <= Epsilon && IsWithinSegment(a, c, d) ||
               Math.Abs(d2) <= Epsilon && IsWithinSegment(b, c, d) ||
               Math.Abs(d3) <= Epsilon && IsWithinSegment(c, a, b) ||
               Math.Abs(d4) <= Epsilon && IsWithinSegment(d, a, b);
    }

    private static double Direction(ComponentPoint a, ComponentPoint b, ComponentPoint c) =>
        (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

    private static bool IsWithinSegment(
        ComponentPoint point,
        ComponentPoint start,
        ComponentPoint end) =>
        point.X >= Math.Min(start.X, end.X) - Epsilon &&
        point.X <= Math.Max(start.X, end.X) + Epsilon &&
        point.Y >= Math.Min(start.Y, end.Y) - Epsilon &&
        point.Y <= Math.Max(start.Y, end.Y) + Epsilon;

    private static ComponentElementDefinition SnapshotElement(
        ComponentElementDefinition element) => element switch
        {
            TextElementDefinition text => text with { },
            ProgressElementDefinition progress => progress with { },
            TextInputElementDefinition input => input with { },
            ToggleElementDefinition toggle => toggle with { },
            SliderElementDefinition slider => slider with { },
            ImageElementDefinition image => image with { },
            ButtonElementDefinition button => button with { },
            DropdownElementDefinition dropdown => dropdown with
            {
                PinnedItems = Array.AsReadOnly(
                    dropdown.PinnedItems.Select(SnapshotMenuItem).ToArray())
            },
            _ => throw new NotSupportedException(
                $"不支持的多边形组件元素：{element.GetType().Name}")
        };

    private static ComponentMenuItem SnapshotMenuItem(ComponentMenuItem item) => item with
    {
        Arguments = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(item.Arguments, StringComparer.OrdinalIgnoreCase))
    };

    private static bool IsIdSegment(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('.' or '_' or '-'))
                return false;
        }

        return value.Length > 0;
    }
}
