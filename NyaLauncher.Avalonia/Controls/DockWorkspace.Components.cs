using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Material.Icons;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Avalonia.Windows;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Component placement, visuals, drag/drop interaction, and polygon runtime
/// lifetime for the docking workspace.
/// </summary>
public partial class DockWorkspace
{
    private const double ComponentDesktopPadding = 12;
    private readonly Dictionary<string, ComponentPlacementProfile> _componentPlacements =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DesktopComponentVisual> _componentVisuals =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly PolygonComponentInstancePool _polygonComponentInstancePool;

    private ComponentDragPreview? _componentDragPreview;
    private DispatcherTimer? _componentDragPreviewHideTimer;
    private string? _hoveredComponentKey;
    private Viewbox? _componentHoverOverlay;
    private double _globalComponentScale = 1;
    private bool _refreshPolygonInstancesOnAttach;

    public event EventHandler<ComponentDropRequestedEventArgs>? ComponentDropRequested;

    /// <summary>组件被拖到垃圾桶松手丢弃；复用组件库移除参数。</summary>
    public event EventHandler<ComponentRemovalRequestedEventArgs>? ComponentDiscardRequested;

    private static readonly IBrush DiscardBinIdleBg = new ImmutableSolidColorBrush(Color.Parse("#26E53935"));
    private static readonly IBrush DiscardBinHotBg = new ImmutableSolidColorBrush(Color.Parse("#40E53935"));

    public double GlobalComponentScale => _globalComponentScale;

    /// <summary>
    /// Stops creating polygon runtimes and gives all current runtimes a bounded
    /// opportunity to finish asynchronous cleanup before the owning window exits.
    /// </summary>
    public async Task ShutdownPolygonComponentsAsync()
    {
        if (_polygonComponentInstancePool.IsShuttingDown)
        {
            await _polygonComponentInstancePool.ShutdownAsync();
            return;
        }

        if (_registry is not null && _registrySubscribed)
        {
            _registry.Changed -= OnRegistryChanged;
            _registrySubscribed = false;
        }

        await _polygonComponentInstancePool.ShutdownAsync();
    }

    public IReadOnlyList<ComponentPlacementProfile> ExportComponentPlacements()
    {
        EnsureAllComponentPlacements();
        PruneComponentPlacements();
        return _componentPlacements.Values
            .OrderBy(placement => placement.AreaId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(placement => placement.ZIndex)
            .Select(ClonePlacement)
            .ToArray();
    }

    public void SetGlobalComponentScale(double scale)
    {
        var normalized = Math.Clamp(
            double.IsFinite(scale) ? scale : 1,
            FeatureAreaRegistry.MinimumComponentScale,
            FeatureAreaRegistry.MaximumComponentScale);
        if (Math.Abs(_globalComponentScale - normalized) < 0.001)
            return;

        _globalComponentScale = normalized;
        Rebuild();
    }

    public bool SetComponentPlacement(
        string componentId,
        string targetAreaId,
        string? sourceAreaId,
        double relativeX,
        double relativeY)
    {
        if (string.IsNullOrWhiteSpace(componentId) ||
            string.IsNullOrWhiteSpace(targetAreaId))
        {
            return false;
        }

        var changed = false;
        if (!string.IsNullOrWhiteSpace(sourceAreaId) &&
            !string.Equals(sourceAreaId, targetAreaId, StringComparison.OrdinalIgnoreCase))
        {
            changed |= _componentPlacements.Remove(
                ComponentPlacementKey(sourceAreaId, componentId));
            _polygonComponentInstancePool.Release(sourceAreaId, componentId);
        }

        var key = ComponentPlacementKey(targetAreaId, componentId);
        var x = Math.Clamp(double.IsFinite(relativeX) ? relativeX : 0.5, 0, 1);
        var y = Math.Clamp(double.IsFinite(relativeY) ? relativeY : 0.5, 0, 1);
        var nextZIndex = _componentPlacements.Values
            .Where(placement => string.Equals(
                placement.AreaId,
                targetAreaId,
                StringComparison.OrdinalIgnoreCase))
            .Select(placement => placement.ZIndex)
            .DefaultIfEmpty(0)
            .Max() + 1;

        if (!_componentPlacements.TryGetValue(key, out var placement))
        {
            placement = new ComponentPlacementProfile
            {
                AreaId = targetAreaId,
                ComponentId = componentId
            };
            _componentPlacements[key] = placement;
            changed = true;
        }

        changed |= Math.Abs(placement.RelativeX - x) > 0.0001 ||
                   Math.Abs(placement.RelativeY - y) > 0.0001 ||
                   placement.ZIndex != nextZIndex;
        placement.RelativeX = x;
        placement.RelativeY = y;
        placement.ZIndex = nextZIndex;

        if (_componentVisuals.TryGetValue(key, out var visual))
            ArrangeDesktopComponent(visual);

        return changed;
    }

    public bool RemoveComponentPlacement(string componentId, string areaId)
    {
        var removed = _componentPlacements.Remove(ComponentPlacementKey(areaId, componentId));
        _polygonComponentInstancePool.Release(areaId, componentId);
        return removed;
    }

    private Canvas CreateActionContent(FeatureAreaDefinition definition)
    {
        var desktop = new Canvas
        {
            Margin = new Thickness(ComponentDesktopPadding),
            Background = Brushes.Transparent,
            ClipToBounds = true
        };
        desktop.SizeChanged += (_, _) => ArrangeDesktopComponents(definition.Id, desktop);
        desktop.AddHandler(
            InputElement.PointerMovedEvent,
            (_, args) => UpdateHoveredComponent(desktop, args.GetPosition(desktop)),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        desktop.PointerExited += (_, _) => ClearHoveredComponent(desktop);

        for (var index = 0; index < definition.Actions.Count; index++)
        {
            var action = definition.Actions[index];
            var normalBackground = action.IsPrimary
                ? ThemeBrushes.ComponentPrimaryBg
                : ThemeBrushes.ComponentBg;
            var normalBorderBrush = action.IsPrimary
                ? ThemeBrushes.ComponentPrimaryBorder
                : ThemeBrushes.ComponentBorder;
            var hoverBackground = action.IsPrimary
                ? ThemeBrushes.ComponentPrimaryHoverBg
                : ThemeBrushes.ComponentHoverBg;
            Button? button = null;
            PolygonComponentView? polygonView = null;
            Control componentSurface;
            if (action.PolygonComponent is { } registration)
            {
                var instance = _polygonComponentInstancePool.GetOrCreate(
                    definition.Id,
                    action.Id,
                    registration);
                polygonView = new PolygonComponentView(
                    registration,
                    instance,
                    PolygonComponentVisualState.Normal,
                    interactive: true);
                polygonView.ActionFeedback += (_, message) =>
                    ComponentFeedback?.Invoke(this, message);
                ToolTip.SetTip(
                    polygonView,
                    "短按使用组件；长按组件任意位置后拖动可自由摆放");
                componentSurface = polygonView;
            }
            else
            {
                button = new Button
                {
                    Width = action.BaseWidth,
                    Height = action.BaseHeight,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(14, 13),
                    CornerRadius = new CornerRadius(14),
                    Background = normalBackground,
                    BorderBrush = normalBorderBrush,
                    BorderThickness = new Thickness(1),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                AutomationProperties.SetAutomationId(button, $"Component_{action.Id}");
                AutomationProperties.SetName(button, action.Title);

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                var icon = new Border
                {
                    Width = 38,
                    Height = 38,
                    CornerRadius = new CornerRadius(11),
                    Background = action.IsPrimary ? ThemeBrushes.ComponentPrimaryBg : ThemeBrushes.ComponentHoverBg,
                    // 字形渲染统一走 FeatureIconFactory："material:Kind" 显示为 Material 图标，其余回退文字
                    Child = FeatureIconFactory.CreateGlyph(
                        action.Glyph,
                        17,
                        action.IsPrimary ? Brushes.White : ThemeBrushes.Muted)
                };

                var copy = new StackPanel
                {
                    Margin = new Thickness(12, 0),
                    Spacing = 3,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = action.Title,
                            FontSize = 13,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = action.IsPrimary ? Brushes.White : ThemeBrushes.Accent,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = action.Description,
                            FontSize = 11,
                            Foreground = action.IsPrimary ? ThemePolygonHelper.TertiaryText : Muted,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                };
                Grid.SetColumn(copy, 1);

                var arrow = new TextBlock
                {
                    Text = "›",
                    FontSize = 22,
                    Foreground = action.IsPrimary ? Brushes.White : Muted,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(arrow, 2);

                row.Children.Add(icon);
                row.Children.Add(copy);
                row.Children.Add(arrow);
                button.Content = row;

                if (action.Execute is not null)
                    button.Click += (_, _) => action.Execute();

                ToolTip.SetTip(button, "单击打开；按住并拖动可在功能区桌面中自由摆放");
                componentSurface = button;
            }

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Child = componentSurface
            };
            ComponentDragSource.Attach(
                viewbox,
                action.Id,
                definition.Id);

            var placement = EnsureComponentPlacement(
                definition.Id,
                action.Id,
                index,
                definition.Actions.Count);
            var visual = new DesktopComponentVisual(
                definition.Id,
                action,
                desktop,
                viewbox,
                button,
                polygonView,
                normalBackground,
                normalBorderBrush,
                hoverBackground,
                placement);
            _componentVisuals[ComponentPlacementKey(definition.Id, action.Id)] = visual;
            desktop.Children.Add(viewbox);
        }

        Dispatcher.UIThread.Post(() => ArrangeDesktopComponents(definition.Id, desktop));
        return desktop;
    }

    private void ArrangeDesktopComponents(string areaId, Canvas desktop)
    {
        foreach (var visual in _componentVisuals.Values.Where(candidate =>
                     string.Equals(candidate.AreaId, areaId, StringComparison.OrdinalIgnoreCase) &&
                     ReferenceEquals(candidate.Desktop, desktop)))
        {
            ArrangeDesktopComponent(visual);
        }
    }

    private void ArrangeDesktopComponent(DesktopComponentVisual visual)
    {
        var size = GetEffectiveComponentSize(visual.Action, visual.Desktop.Bounds.Size);
        visual.View.Width = size.Width;
        visual.View.Height = size.Height;

        var travelX = Math.Max(0, visual.Desktop.Bounds.Width - size.Width);
        var travelY = Math.Max(0, visual.Desktop.Bounds.Height - size.Height);
        Canvas.SetLeft(visual.View, Math.Clamp(visual.Placement.RelativeX, 0, 1) * travelX);
        Canvas.SetTop(visual.View, Math.Clamp(visual.Placement.RelativeY, 0, 1) * travelY);
        if (!string.Equals(
                _hoveredComponentKey,
                ComponentPlacementKey(visual.AreaId, visual.Action.Id),
                StringComparison.OrdinalIgnoreCase))
        {
            visual.View.ZIndex = visual.Placement.ZIndex;
        }
        else
        {
            UpdateComponentHoverOverlayBounds(visual);
        }
    }

    private void UpdateHoveredComponent(Canvas desktop, Point pointerPosition)
    {
        DesktopComponentVisual? hovered = null;
        if (_hoveredComponentKey is not null &&
            _componentVisuals.TryGetValue(_hoveredComponentKey, out var current) &&
            ReferenceEquals(current.Desktop, desktop) &&
            ContainsComponentPoint(current, pointerPosition))
        {
            // Once a partially covered component has been reached through its
            // visible portion, keep it selected while the pointer moves through
            // the overlap. This prevents the old top component stealing hover.
            hovered = current;
        }

        hovered ??= _componentVisuals.Values
            .Where(candidate =>
                ReferenceEquals(candidate.Desktop, desktop) &&
                ContainsComponentPoint(candidate, pointerPosition))
            .OrderByDescending(candidate => candidate.Placement.ZIndex)
            .FirstOrDefault();

        SetHoveredComponent(hovered);
    }

    private void SetHoveredComponent(DesktopComponentVisual? visual)
    {
        var nextKey = visual is null
            ? null
            : ComponentPlacementKey(visual.AreaId, visual.Action.Id);
        if (string.Equals(_hoveredComponentKey, nextKey, StringComparison.OrdinalIgnoreCase))
            return;

        ClearHoveredComponent();
        if (visual is null)
            return;

        var highestPermanentZIndex = _componentVisuals.Values
            .Where(candidate => string.Equals(
                candidate.AreaId,
                visual.AreaId,
                StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Placement.ZIndex)
            .DefaultIfEmpty(visual.Placement.ZIndex)
            .Max();
        visual.View.ZIndex = highestPermanentZIndex + 1;
        if (visual.PolygonView is not null)
        {
            visual.PolygonView.SetVisualState(PolygonComponentVisualState.Hovered);
            AutomationProperties.SetItemStatus(visual.PolygonView, "悬停置顶");
        }
        else if (visual.Button is not null)
        {
            visual.Button.Background = visual.HoverBackground;
            visual.Button.BorderBrush = Accent;
            visual.Button.BorderThickness = new Thickness(2);
            AutomationProperties.SetItemStatus(visual.Button, "悬停置顶");
            _componentHoverOverlay = CreateComponentHoverOverlay(visual.Action);
            visual.Desktop.Children.Add(_componentHoverOverlay);
            UpdateComponentHoverOverlayBounds(visual);
        }
        _hoveredComponentKey = nextKey;
    }

    private void ClearHoveredComponent(Canvas? expectedDesktop = null)
    {
        var key = _hoveredComponentKey;
        if (key is null)
            return;
        if (!_componentVisuals.TryGetValue(key, out var visual))
        {
            _hoveredComponentKey = null;
            _componentHoverOverlay = null;
            return;
        }
        if (expectedDesktop is not null && !ReferenceEquals(visual.Desktop, expectedDesktop))
            return;

        visual.View.ZIndex = visual.Placement.ZIndex;
        if (visual.PolygonView is not null)
        {
            visual.PolygonView.SetVisualState(PolygonComponentVisualState.Normal);
            AutomationProperties.SetItemStatus(visual.PolygonView, string.Empty);
        }
        else if (visual.Button is not null)
        {
            visual.Button.Background = visual.NormalBackground;
            visual.Button.BorderBrush = visual.NormalBorderBrush;
            visual.Button.BorderThickness = new Thickness(1);
            AutomationProperties.SetItemStatus(visual.Button, string.Empty);
        }
        if (_componentHoverOverlay is not null)
            visual.Desktop.Children.Remove(_componentHoverOverlay);
        _hoveredComponentKey = null;
        _componentHoverOverlay = null;
    }

    private void UpdateComponentHoverOverlayBounds(DesktopComponentVisual visual)
    {
        if (_componentHoverOverlay is null)
            return;

        _componentHoverOverlay.Width = visual.View.Width;
        _componentHoverOverlay.Height = visual.View.Height;
        Canvas.SetLeft(_componentHoverOverlay, Canvas.GetLeft(visual.View));
        Canvas.SetTop(_componentHoverOverlay, Canvas.GetTop(visual.View));
    }

    private static Viewbox CreateComponentHoverOverlay(FeatureAreaAction action)
    {
        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = action.IsPrimary ? ThemeBrushes.ComponentPrimaryBg : ThemeBrushes.ComponentHoverBg,
            Child = new TextBlock
            {
                Text = action.Glyph,
                FontSize = 17,
                Foreground = action.IsPrimary ? Brushes.White : ThemeBrushes.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var copy = new StackPanel
        {
            Margin = new Thickness(12, 0),
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = action.Title,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = action.IsPrimary ? Brushes.White : ThemeBrushes.Accent,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = action.Description,
                    FontSize = 11,
                    Foreground = action.IsPrimary ? ThemePolygonHelper.TertiaryText : Muted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        Grid.SetColumn(copy, 1);
        var arrow = new TextBlock
        {
            Text = "›",
            FontSize = 22,
            Foreground = action.IsPrimary ? Brushes.White : Muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };
        row.Children.Add(icon);
        row.Children.Add(copy);
        row.Children.Add(arrow);
        var surface = new Border
        {
            Width = action.BaseWidth,
            Height = action.BaseHeight,
            Padding = new Thickness(14, 13),
            CornerRadius = new CornerRadius(14),
            Background = action.IsPrimary ? ThemeBrushes.ComponentPrimaryHoverBg : ThemeBrushes.ComponentHoverBg,
            BorderBrush = Accent,
            BorderThickness = new Thickness(2),
            Child = row
        };
        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            IsHitTestVisible = false,
            ZIndex = 20000,
            Child = surface
        };
    }

    private static Rect GetComponentBounds(DesktopComponentVisual visual)
    {
        var left = Canvas.GetLeft(visual.View);
        var top = Canvas.GetTop(visual.View);
        if (!double.IsFinite(left))
            left = 0;
        if (!double.IsFinite(top))
            top = 0;

        var width = visual.View.Bounds.Width > 0
            ? visual.View.Bounds.Width
            : visual.View.Width;
        var height = visual.View.Bounds.Height > 0
            ? visual.View.Bounds.Height
            : visual.View.Height;
        return new Rect(left, top, Math.Max(0, width), Math.Max(0, height));
    }

    private static bool ContainsComponentPoint(
        DesktopComponentVisual visual,
        Point desktopPoint)
    {
        var bounds = GetComponentBounds(visual);
        if (!bounds.Contains(desktopPoint))
            return false;
        if (visual.PolygonView is null || bounds.Width <= 0 || bounds.Height <= 0)
            return true;

        var polygonWidth = visual.PolygonView.Bounds.Width > 0
            ? visual.PolygonView.Bounds.Width
            : visual.PolygonView.Width;
        var polygonHeight = visual.PolygonView.Bounds.Height > 0
            ? visual.PolygonView.Bounds.Height
            : visual.PolygonView.Height;
        return visual.PolygonView.ContainsPoint(new Point(
            (desktopPoint.X - bounds.X) / bounds.Width * polygonWidth,
            (desktopPoint.Y - bounds.Y) / bounds.Height * polygonHeight));
    }

    private Size GetEffectiveComponentSize(FeatureAreaAction action, Size desktopSize)
    {
        var componentScale = _globalComponentScale;
        if (action.PolygonComponent?.Definition is { } definition)
        {
            var minimumScale = Math.Max(
                definition.MinimumSize.Width / definition.PreferredSize.Width,
                definition.MinimumSize.Height / definition.PreferredSize.Height);
            var maximumScale = Math.Min(
                definition.MaximumSize.Width / definition.PreferredSize.Width,
                definition.MaximumSize.Height / definition.PreferredSize.Height);
            componentScale = Math.Clamp(componentScale, minimumScale, maximumScale);
        }

        var preferredWidth = Math.Max(1, action.EffectiveBaseWidth * componentScale);
        var preferredHeight = Math.Max(1, action.EffectiveBaseHeight * componentScale);

        if (desktopSize.Width <= 0 || desktopSize.Height <= 0)
            return new Size(preferredWidth, preferredHeight);

        // An area is normally constrained by its component footprint. During
        // the final step into sidebar mode it may briefly become smaller, so use
        // a uniform emergency fit to keep the border inside the desktop.
        var fit = Math.Min(
            1,
            Math.Min(desktopSize.Width / preferredWidth, desktopSize.Height / preferredHeight));
        return new Size(preferredWidth * fit, preferredHeight * fit);
    }

    private Point CalculateRelativeDropPosition(
        string areaId,
        string componentId,
        Canvas? desktop,
        Point pointerPosition)
    {
        if (desktop is null || desktop.Bounds.Width <= 0 || desktop.Bounds.Height <= 0)
            return new Point(0.5, 0.5);

        var action = _registry?.AvailableActions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, componentId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
            return new Point(0.5, 0.5);

        var size = GetEffectiveComponentSize(action, desktop.Bounds.Size);
        var travelX = Math.Max(0, desktop.Bounds.Width - size.Width);
        var travelY = Math.Max(0, desktop.Bounds.Height - size.Height);
        var left = Math.Clamp(pointerPosition.X - (size.Width / 2), 0, travelX);
        var top = Math.Clamp(pointerPosition.Y - (size.Height / 2), 0, travelY);
        return new Point(
            travelX <= 0 ? 0.5 : left / travelX,
            travelY <= 0 ? 0.5 : top / travelY);
    }

    private void ShowComponentDragPreview(
        Canvas? desktop,
        string areaId,
        string componentId,
        Point pointerPosition)
    {
        CancelComponentDragPreviewHide();

        if (desktop is null)
        {
            HideComponentDragPreview();
            return;
        }

        var action = _registry?.AvailableActions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, componentId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            HideComponentDragPreview();
            return;
        }

        if (_componentDragPreview is null ||
            !ReferenceEquals(_componentDragPreview.Desktop, desktop) ||
            !string.Equals(
                _componentDragPreview.ComponentId,
                componentId,
                StringComparison.OrdinalIgnoreCase))
        {
            HideComponentDragPreview();
            var view = CreateComponentDragPreview(action);
            desktop.Children.Add(view);
            _componentDragPreview = new ComponentDragPreview(componentId, desktop, view);
        }

        var preview = _componentDragPreview;
        var size = GetEffectiveComponentSize(action, desktop.Bounds.Size);
        var relativePosition = CalculateRelativeDropPosition(
            areaId,
            componentId,
            desktop,
            pointerPosition);
        var travelX = Math.Max(0, desktop.Bounds.Width - size.Width);
        var travelY = Math.Max(0, desktop.Bounds.Height - size.Height);
        preview.View.Width = size.Width;
        preview.View.Height = size.Height;
        Canvas.SetLeft(preview.View, relativePosition.X * travelX);
        Canvas.SetTop(preview.View, relativePosition.Y * travelY);
    }

    private static Viewbox CreateComponentDragPreview(FeatureAreaAction action)
    {
        if (action.PolygonComponent is { } registration)
        {
            var polygon = new PolygonComponentView(
                registration,
                instance: null,
                PolygonComponentVisualState.DragPreview,
                interactive: false)
            {
                IsHitTestVisible = false
            };
            var polygonPreview = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                IsHitTestVisible = false,
                ZIndex = 10000,
                Child = polygon
            };
            AutomationProperties.SetAutomationId(polygonPreview, "ComponentDragPreview");
            AutomationProperties.SetName(polygonPreview, $"组件放置预览：{action.Title}");
            return polygonPreview;
        }

        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = ThemeBrushes.IconBoxBg,
            // 字形渲染统一走 FeatureIconFactory："material:Kind" 显示为 Material 图标，其余回退文字
            Child = FeatureIconFactory.CreateGlyph(action.Glyph, 17, ThemeBrushes.Muted)
        };

        var copy = new StackPanel
        {
            Margin = new Thickness(12, 0),
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = action.Title,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemeBrushes.Accent,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = "释放后放置于此",
                    FontSize = 11,
                    Foreground = ThemePolygonHelper.TertiaryText
                }
            }
        };
        Grid.SetColumn(copy, 1);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(14, 0)
        };
        content.Children.Add(icon);
        content.Children.Add(copy);

        var card = new Border
        {
            Width = action.BaseWidth,
            Height = action.BaseHeight,
            CornerRadius = new CornerRadius(14),
            Background = ThemePolygonHelper.DragPreviewBg,
            BorderBrush = Accent,
            BorderThickness = new Thickness(2),
            Child = content
        };

        var preview = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Opacity = 0.68,
            IsHitTestVisible = false,
            ZIndex = 10000,
            Child = card
        };
        AutomationProperties.SetAutomationId(preview, "ComponentDragPreview");
        AutomationProperties.SetName(preview, $"组件放置预览：{action.Title}");
        return preview;
    }

    private void HideComponentDragPreview(Canvas? expectedDesktop = null)
    {
        CancelComponentDragPreviewHide();

        var preview = _componentDragPreview;
        if (preview is null ||
            (expectedDesktop is not null && !ReferenceEquals(preview.Desktop, expectedDesktop)))
        {
            return;
        }

        preview.Desktop.Children.Remove(preview.View);
        _componentDragPreview = null;
    }

    private void ScheduleComponentDragPreviewHide(Canvas? expectedDesktop)
    {
        CancelComponentDragPreviewHide();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _componentDragPreviewHideTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_componentDragPreviewHideTimer, timer))
                return;

            _componentDragPreviewHideTimer = null;
            HideComponentDragPreview(expectedDesktop);
        };
        timer.Start();
    }

    private void CancelComponentDragPreviewHide()
    {
        _componentDragPreviewHideTimer?.Stop();
        _componentDragPreviewHideTimer = null;
    }

    private ComponentPlacementProfile EnsureComponentPlacement(
        string areaId,
        string componentId,
        int index,
        int componentCount)
    {
        var key = ComponentPlacementKey(areaId, componentId);
        if (_componentPlacements.TryGetValue(key, out var existing))
            return existing;

        var initialPosition = componentCount <= 1
            ? 0.5
            : index / (double)(componentCount - 1);
        var placement = new ComponentPlacementProfile
        {
            AreaId = areaId,
            ComponentId = componentId,
            // Diagonal defaults remain separated when a left/right sidebar has
            // no horizontal travel, or a top/bottom sidebar has no vertical
            // travel. Users can freely rearrange or overlap them afterwards.
            RelativeX = initialPosition,
            RelativeY = initialPosition,
            ZIndex = index + 1
        };
        _componentPlacements[key] = placement;
        return placement;
    }

    private void EnsureAllComponentPlacements()
    {
        if (_registry is null)
            return;

        foreach (var area in _registry.Areas)
        {
            for (var index = 0; index < area.Actions.Count; index++)
            {
                EnsureComponentPlacement(
                    area.Id,
                    area.Actions[index].Id,
                    index,
                    area.Actions.Count);
            }
        }
    }

    private void PruneComponentPlacements()
    {
        if (_registry is null)
            return;

        var validKeys = _registry.Areas
            .SelectMany(area => area.Actions.Select(action =>
                ComponentPlacementKey(area.Id, action.Id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _componentPlacements.Keys
                     .Where(key => !validKeys.Contains(key))
                     .ToArray())
        {
            var placement = _componentPlacements[key];
            _componentPlacements.Remove(key);
            _polygonComponentInstancePool.Release(
                placement.AreaId,
                placement.ComponentId);
        }
    }

    private void ReleaseUnplacedPolygonComponentInstances()
    {
        var liveInstances = _registry?.Areas
            .SelectMany(area => area.Actions
                .Where(action => action.PolygonComponent is not null)
                .Where(action => _componentPlacements.ContainsKey(
                    ComponentPlacementKey(area.Id, action.Id)))
                .Select(action => new ComponentInstanceContext(action.Id, area.Id))) ?? [];

        _polygonComponentInstancePool.ReleaseUnreferenced(liveInstances);
    }

    private void OnPolygonComponentDisposalCompleted(string areaId, string componentId)
    {
        if (!_refreshPolygonInstancesOnAttach &&
            !_polygonComponentInstancePool.IsShuttingDown &&
            IsLivePolygonComponent(areaId, componentId))
        {
            Rebuild();
        }
    }

    private bool IsLivePolygonComponent(string areaId, string componentId) =>
        _componentPlacements.ContainsKey(ComponentPlacementKey(areaId, componentId)) &&
        (_registry?.Areas.Any(area => area.Actions.Any(action =>
            action.PolygonComponent is not null &&
            string.Equals(area.Id, areaId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Id, componentId, StringComparison.OrdinalIgnoreCase))) ?? false);

    private static string ComponentPlacementKey(string areaId, string componentId) =>
        $"{areaId.Length}:{areaId}{componentId}";

    #region 拖拽丢弃（垃圾桶）

    private bool _discardBinHot;
    private bool _discardAnimationActive;
    private DispatcherTimer? _discardBinHideTimer;

    /// <summary>
    /// 组件拖拽期间在底部显示垃圾桶：指针命中 60x60 红圈时切换开盖图标，松手丢弃组件。
    /// DragDrop 路由事件只有 Bubble 策略，且卡片会把 DragOver 标记 Handled，
    /// 因此挂在 MainWorkspaceCell 上并开启 handledEventsToo；
    /// 同时给 MainWorkspaceCell 开放 AllowDrop，保证空白区域也有连续 DragOver，
    /// Esc/空白处松手结束后由延迟隐藏兜底，避免垃圾桶残留。
    /// </summary>
    private void WireDiscardBin()
    {
        DragDrop.SetAllowDrop(MainWorkspaceCell, true);
        DragDrop.SetAllowDrop(DiscardBinCircle, true);

        // 卡片/垃圾桶会把 DragOver 标记 Handled，必须开启 handledEventsToo 才能在
        // MainWorkspaceCell 上持续感知
        MainWorkspaceCell.AddHandler(
            DragDrop.DragEnterEvent,
            OnDiscardBinRootDragOver,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        MainWorkspaceCell.AddHandler(
            DragDrop.DragOverEvent,
            OnDiscardBinRootDragOver,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        MainWorkspaceCell.AddHandler(
            DragDrop.DragLeaveEvent,
            OnDiscardBinRootDragLeave,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        MainWorkspaceCell.AddHandler(
            DragDrop.DropEvent,
            (_, _) =>
            {
                // 丢弃动画播放期间延迟收起垃圾桶，让回弹与幽灵飞入可见
                if (_discardAnimationActive)
                    ScheduleDiscardBinHide(420);
                else
                    HideDiscardBin();
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        DragDrop.AddDragOverHandler(DiscardBinCircle, (_, args) =>
        {
            if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
                payload is null)
            {
                args.DragEffects = DragDropEffects.None;
                return;
            }

            // 仅工作区上的组件可丢弃；组件库拖入的在新区域松手放置
            args.DragEffects = payload.IsFromLibrary
                ? DragDropEffects.None
                : DragDropEffects.Move;
            args.Handled = true;
        });
        DragDrop.AddDropHandler(DiscardBinCircle, (_, args) =>
        {
            args.Handled = true;
            if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
                payload is null ||
                payload.IsFromLibrary)
            {
                args.DragEffects = DragDropEffects.None;
                return;
            }

            args.DragEffects = DragDropEffects.Move;
            _discardAnimationActive = true;
            PlayDiscardAnimation(payload.ComponentId, args.GetPosition(MainWorkspaceCell));
            ComponentDiscardRequested?.Invoke(
                this,
                new ComponentRemovalRequestedEventArgs(
                    payload.ComponentId,
                    payload.SourceAreaId!));
        });
    }

    private void OnDiscardBinRootDragOver(object? sender, DragEventArgs args)
    {
        if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
            payload is null)
        {
            return;
        }

        CancelDiscardBinHide();
        DiscardBin.IsVisible = true;
        var origin = DiscardBinCircle.TranslatePoint(new Point(0, 0), MainWorkspaceCell);
        var hot = origin.HasValue && new Rect(
            origin.Value,
            DiscardBinCircle.Bounds.Size).Contains(args.GetPosition(MainWorkspaceCell));
        if (hot == _discardBinHot)
            return;

        _discardBinHot = hot;
        DiscardBinIcon.Kind = hot ? MaterialIconKind.DeleteEmpty : MaterialIconKind.Delete;
        DiscardBinCircle.Background = hot ? DiscardBinHotBg : DiscardBinIdleBg;
        DiscardBinText.Text = hot ? "松手删除组件" : "松手删除";
    }

    private void OnDiscardBinRootDragLeave(object? sender, DragEventArgs args)
    {
        // 在子级卡片之间移动也会冒泡 DragLeave：指针仍在工作区内时只是短暂离开
        // 某个卡片，用延迟隐藏兜底；一旦后续 DragOver 到来即取消。
        var position = args.GetPosition(MainWorkspaceCell);
        if (new Rect(default, MainWorkspaceCell.Bounds.Size).Contains(position))
        {
            ScheduleDiscardBinHide();
            return;
        }

        HideDiscardBin();
    }

    private void ScheduleDiscardBinHide(int delayMs = 150)
    {
        CancelDiscardBinHide();
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        };
        _discardBinHideTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_discardBinHideTimer, timer))
            {
                _discardBinHideTimer = null;
                HideDiscardBin();
            }
        };
        timer.Start();
    }

    private void CancelDiscardBinHide()
    {
        _discardBinHideTimer?.Stop();
        _discardBinHideTimer = null;
    }

    private void HideDiscardBin()
    {
        CancelDiscardBinHide();
        DiscardBin.IsVisible = false;
        _discardBinHot = false;
        DiscardBinIcon.Kind = MaterialIconKind.Delete;
        DiscardBinCircle.Background = DiscardBinIdleBg;
        DiscardBinText.Text = "松手删除";
    }

    /// <summary>
    /// 丢弃吸附动画：被丢弃的组件以幽灵形态飞向垃圾桶并缩小消失（M3 加速曲线），
    /// 同时垃圾桶缩放回弹表达「吃掉」反馈。
    /// </summary>
    private void PlayDiscardAnimation(string componentId, Point dropPosition)
    {
        if (!AnimationGate.Enabled)
            return;

        var action = _registry?.AvailableActions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, componentId, StringComparison.OrdinalIgnoreCase));
        if (action is not null)
        {
            var ghost = CreateComponentDragPreview(action);
            var size = GetEffectiveComponentSize(action, MainWorkspaceCell.Bounds.Size);
            _ = AnimateDiscardGhostAsync(ghost, size, dropPosition);
        }

        _ = AnimateDiscardBinBumpAsync();
    }

    private async Task AnimateDiscardGhostAsync(Viewbox ghost, Size size, Point dropPosition)
    {
        try
        {
            var layer = DropPreviewLayer;
            ghost.Width = size.Width;
            ghost.Height = size.Height;
            var startX = Math.Clamp(
                dropPosition.X - size.Width / 2,
                0,
                Math.Max(0, layer.Bounds.Width - size.Width));
            var startY = Math.Clamp(
                dropPosition.Y - size.Height / 2,
                0,
                Math.Max(0, layer.Bounds.Height - size.Height));
            Canvas.SetLeft(ghost, startX);
            Canvas.SetTop(ghost, startY);
            ghost.Opacity = 1;
            ghost.RenderTransform = TransformOperations.Parse("translate(0px, 0px) scale(1)");
            layer.Children.Add(ghost);

            // 等一帧让初始布局生效，Transitions 才能从正确的起点插值
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var binOrigin = DiscardBinCircle.TranslatePoint(new Point(0, 0), MainWorkspaceCell);
            if (!binOrigin.HasValue)
                return;

            var binCenter = new Point(
                binOrigin.Value.X + DiscardBinCircle.Bounds.Width / 2,
                binOrigin.Value.Y + DiscardBinCircle.Bounds.Height / 2);
            var dx = binCenter.X - (startX + size.Width / 2);
            var dy = binCenter.Y - (startY + size.Height / 2);

            ghost.Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(MaterialMotion.MediumTransitionMs),
                    Easing = MaterialMotion.EmphasizedAccelerateEasing
                },
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(
                        MaterialMotion.MediumTransitionMs * MaterialMotion.FadeEndFractionExit),
                    Easing = MaterialMotion.LinearEasing
                }
            };
            ghost.RenderTransform = TransformOperations.Parse(
                $"translate({dx:F1}px, {dy:F1}px) scale(0.12)");
            ghost.Opacity = 0;

            await Task.Delay(MaterialMotion.MediumTransitionMs + 40);
            _discardAnimationActive = false;
            HideDiscardBin();
        }
        finally
        {
            ((Canvas)ghost.Parent)?.Children.Remove(ghost);
        }
    }

    private async Task AnimateDiscardBinBumpAsync()
    {
        DiscardBinCircle.RenderTransformOrigin =
            new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        DiscardBinCircle.RenderTransform = TransformOperations.Parse("scale(1)");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        // 吞下：150ms 快速放大（减速曲线）
        DiscardBinCircle.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(150),
                Easing = MaterialMotion.EmphasizedDecelerateEasing
            }
        };
        DiscardBinCircle.RenderTransform = TransformOperations.Parse("scale(1.22)");
        await Task.Delay(170);

        // 回弹：200ms 归位（标准强调曲线）
        DiscardBinCircle.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = MaterialMotion.EmphasizedEasing
            }
        };
        DiscardBinCircle.RenderTransform = TransformOperations.Parse("scale(1)");
        await Task.Delay(220);
        DiscardBinCircle.Transitions = null;
        DiscardBinCircle.RenderTransform = null;
    }

    #endregion

    private static ComponentPlacementProfile ClonePlacement(ComponentPlacementProfile placement)
    {
        return new ComponentPlacementProfile
        {
            AreaId = placement.AreaId,
            ComponentId = placement.ComponentId,
            RelativeX = placement.RelativeX,
            RelativeY = placement.RelativeY,
            ZIndex = placement.ZIndex
        };
    }

    private void WireComponentDropTarget(
        Border card,
        Canvas? desktop,
        string targetAreaId)
    {
        DragDrop.SetAllowDrop(card, true);

        DragDrop.AddDragEnterHandler(card, (_, args) =>
            UpdateComponentDropTarget(card, desktop, targetAreaId, args));
        DragDrop.AddDragOverHandler(card, (_, args) =>
            UpdateComponentDropTarget(card, desktop, targetAreaId, args));
        DragDrop.AddDragLeaveHandler(card, (_, _) =>
        {
            ResetComponentDropTarget(card);
            // Adding the preview changes the visual tree under the pointer and
            // can briefly raise DragLeave even though the pointer never left
            // the area. A short grace period keeps the preview continuous;
            // the next DragOver cancels it immediately.
            ScheduleComponentDragPreviewHide(desktop);
        });
        DragDrop.AddDropHandler(card, (_, args) =>
        {
            ResetComponentDropTarget(card);
            if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
                payload is null)
            {
                HideComponentDragPreview(desktop);
                args.DragEffects = DragDropEffects.None;
                return;
            }

            args.DragEffects = payload.IsFromLibrary
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
            args.Handled = true;
            var position = desktop is null
                ? new Point(0, 0)
                : args.GetPosition(desktop);
            var relativePosition = CalculateRelativeDropPosition(
                targetAreaId,
                payload.ComponentId,
                desktop,
                position);
            HideComponentDragPreview(desktop);
            ComponentDropRequested?.Invoke(
                this,
                new ComponentDropRequestedEventArgs(
                    payload.ComponentId,
                    targetAreaId,
                    payload.SourceAreaId,
                    relativePosition.X,
                    relativePosition.Y));
        });
    }

    private void UpdateComponentDropTarget(
        Border card,
        Canvas? desktop,
        string targetAreaId,
        DragEventArgs args)
    {
        if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
            payload is null)
        {
            HideComponentDragPreview(desktop);
            args.DragEffects = DragDropEffects.None;
            return;
        }

        args.DragEffects = payload.IsFromLibrary
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        args.Handled = true;
        card.BorderBrush = Accent;
        card.BorderThickness = new Thickness(3);
        ShowComponentDragPreview(
            desktop,
            targetAreaId,
            payload.ComponentId,
            desktop is null ? new Point(0, 0) : args.GetPosition(desktop));
    }

    private static void ResetComponentDropTarget(Border card)
    {
        card.BorderBrush = CardBorder;
        card.BorderThickness = new Thickness(0);
    }


    private sealed record DesktopComponentVisual(
        string AreaId,
        FeatureAreaAction Action,
        Canvas Desktop,
        Viewbox View,
        Button? Button,
        PolygonComponentView? PolygonView,
        IBrush NormalBackground,
        IBrush NormalBorderBrush,
        IBrush HoverBackground,
        ComponentPlacementProfile Placement);
    private sealed record ComponentDragPreview(
        string ComponentId,
        Canvas Desktop,
        Viewbox View);
}

public sealed record ComponentDropRequestedEventArgs(
    string ComponentId,
    string TargetAreaId,
    string? SourceAreaId,
    double RelativeX,
    double RelativeY);
