using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// A two-dimensional docking workspace. Feature areas can be docked to any
/// edge of another area, while the border seams resize adjacent layout groups.
/// </summary>
public partial class DockWorkspace : UserControl
{
    private static readonly IBrush CardBackground = Brush.Parse("#171B2B");
    private static readonly IBrush HeaderBackground = Brush.Parse("#20263A");
    private static readonly IBrush CardBorder = Brush.Parse("#30374D");
    private static readonly IBrush Accent = Brush.Parse("#7C8CFF");
    private static readonly IBrush Muted = Brush.Parse("#8F98B3");
    private static readonly IBrush SeamIdle = Brush.Parse("#343C52");

    private const double MinimumAreaWidth = 180;
    private const double MinimumAreaHeight = 150;
    private const double SeamThickness = 1;
    private const double SeamHitSize = 9;
    private const double SidebarSeamHitSize = 16;
    private const double SidebarAnimationDurationMilliseconds = 180;
    private const double CollapseWidthThreshold = 230;
    private const double CollapseHeightThreshold = 180;
    private const double CollapsedRailSize = 42;
    private const double ComponentDesktopPadding = 12;

    private readonly Dictionary<string, AreaVisual> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GroupVisual> _groupVisuals = [];
    private readonly Dictionary<DockEdge, SidebarState> _sidebars = [];
    private readonly List<Control> _sidebarHosts = [];
    private readonly Dictionary<string, ComponentPlacementProfile> _componentPlacements =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DesktopComponentVisual> _componentVisuals =
        new(StringComparer.OrdinalIgnoreCase);

    private FeatureAreaRegistry? _registry;
    private LayoutNode? _layoutRoot;
    private string? _draggedAreaId;
    private string? _targetAreaId;
    private DropSide? _dropSide;
    private DockEdge? _draggedSidebarEdge;
    private DockEdge? _sidebarDropEdge;
    private RestoredResizeSession? _restoredResizeSession;
    private ComponentDragPreview? _componentDragPreview;
    private DispatcherTimer? _componentDragPreviewHideTimer;
    private string? _hoveredComponentKey;
    private Viewbox? _componentHoverOverlay;
    private double _globalComponentScale = 1;

    public event EventHandler? LayoutChanged;
    public event EventHandler<ComponentDropRequestedEventArgs>? ComponentDropRequested;

    public double GlobalComponentScale => _globalComponentScale;

    private ColumnDefinition LeftSidebarColumn => WorkspaceRoot.ColumnDefinitions[0];
    private ColumnDefinition RightSidebarColumn => WorkspaceRoot.ColumnDefinitions[2];
    private RowDefinition TopSidebarRow => WorkspaceRoot.RowDefinitions[0];
    private RowDefinition BottomSidebarRow => WorkspaceRoot.RowDefinitions[2];

    public DockWorkspace()
    {
        InitializeComponent();
        WorkspaceRoot.PointerMoved += OnRestoredResizePointerMoved;
        WorkspaceRoot.PointerReleased += OnRestoredResizePointerReleased;
    }

    public void UseRegistry(FeatureAreaRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (_registry is not null)
            _registry.Changed -= OnRegistryChanged;

        _registry = registry;
        _registry.Changed += OnRegistryChanged;
        SynchronizeWithRegistry();
    }

    public DockLayoutProfile? ExportLayout()
    {
        CaptureLayoutRatios();
        return _layoutRoot is null ? null : ExportNode(_layoutRoot);
    }

    public IReadOnlyList<SidebarProfile> ExportSidebars()
    {
        return _sidebars.Values.Select(sidebar => new SidebarProfile
        {
            AreaId = sidebar.Definition.Id,
            Edge = sidebar.Edge,
            RevealSize = sidebar.RevealSize
        }).ToArray();
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
        return _componentPlacements.Remove(ComponentPlacementKey(areaId, componentId));
    }

    public void ImportLayout(
        DockLayoutProfile? profile,
        IEnumerable<SidebarProfile>? sidebars = null,
        IEnumerable<ComponentPlacementProfile>? componentPlacements = null,
        double globalComponentScale = 1)
    {
        if (_registry is null)
            return;

        var definitions = _registry.Areas.ToDictionary(
            area => area.Id,
            StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _globalComponentScale = Math.Clamp(
            double.IsFinite(globalComponentScale) ? globalComponentScale : 1,
            FeatureAreaRegistry.MinimumComponentScale,
            FeatureAreaRegistry.MaximumComponentScale);
        _componentPlacements.Clear();
        foreach (var placement in componentPlacements ?? [])
        {
            if (string.IsNullOrWhiteSpace(placement.AreaId) ||
                string.IsNullOrWhiteSpace(placement.ComponentId))
            {
                continue;
            }

            var copy = ClonePlacement(placement);
            copy.RelativeX = Math.Clamp(copy.RelativeX, 0, 1);
            copy.RelativeY = Math.Clamp(copy.RelativeY, 0, 1);
            copy.ZIndex = Math.Max(0, copy.ZIndex);
            _componentPlacements[ComponentPlacementKey(copy.AreaId, copy.ComponentId)] = copy;
        }

        _sidebars.Clear();
        foreach (var sidebar in sidebars ?? [])
        {
            if (_sidebars.ContainsKey(sidebar.Edge) ||
                !definitions.TryGetValue(sidebar.AreaId, out var definition) ||
                !usedIds.Add(sidebar.AreaId))
            {
                continue;
            }

            _sidebars[sidebar.Edge] = new SidebarState(
                definition,
                sidebar.Edge,
                sidebar.RevealSize > 0
                    ? sidebar.RevealSize
                    : DefaultRevealSize(sidebar.Edge));
        }

        var imported = profile is null ? null : ImportNode(profile, definitions, usedIds);

        foreach (var definition in _registry.Areas)
        {
            if (usedIds.Add(definition.Id))
                imported = AppendArea(imported, new AreaNode(definition));
        }

        _layoutRoot = imported;
        EnsureAllComponentPlacements();
        PruneComponentPlacements();
        Rebuild();
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        SynchronizeWithRegistry();
    }

    private void SynchronizeWithRegistry()
    {
        if (_registry is null)
            return;

        CaptureLayoutRatios();

        var registeredIds = _registry.Areas
            .Select(area => area.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in _sidebars
                     .Where(pair => !registeredIds.Contains(pair.Value.Definition.Id))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _sidebars.Remove(edge);
        }

        if (_layoutRoot is not null)
        {
            var updatedRoot = _layoutRoot;
            foreach (var existingId in EnumerateAreas(updatedRoot)
                         .Select(area => area.Definition.Id)
                         .Where(id => !registeredIds.Contains(id))
                         .ToArray())
            {
                if (updatedRoot is null)
                    break;

                updatedRoot = RemoveArea(updatedRoot, existingId);
            }

            _layoutRoot = updatedRoot;
        }

        var currentIds = _layoutRoot is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : EnumerateAreas(_layoutRoot)
                .Select(area => area.Definition.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        currentIds.UnionWith(_sidebars.Values.Select(sidebar => sidebar.Definition.Id));

        foreach (var definition in _registry.Areas)
        {
            if (!currentIds.Contains(definition.Id))
            {
                _layoutRoot = AppendArea(_layoutRoot, new AreaNode(definition));
                currentIds.Add(definition.Id);
            }
        }

        var currentDefinitions = _registry.Areas.ToDictionary(
            area => area.Id,
            StringComparer.OrdinalIgnoreCase);
        if (_layoutRoot is not null)
        {
            foreach (var area in EnumerateAreas(_layoutRoot))
            {
                if (currentDefinitions.TryGetValue(area.Definition.Id, out var current))
                    area.Definition = current;
            }
        }

        foreach (var sidebar in _sidebars.Values)
        {
            if (currentDefinitions.TryGetValue(sidebar.Definition.Id, out var current))
                sidebar.Definition = current;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        HideComponentDragPreview();
        ClearHoveredComponent();
        AreaGrid.Children.Clear();
        AreaGrid.ColumnDefinitions.Clear();
        AreaGrid.RowDefinitions.Clear();
        _visuals.Clear();
        _groupVisuals.Clear();
        _componentVisuals.Clear();
        foreach (var host in _sidebarHosts)
            WorkspaceRoot.Children.Remove(host);
        _sidebarHosts.Clear();
        ResetSidebarTracks();
        UpdateMainWorkspacePlacement();

        if (_layoutRoot is null)
        {
            AreaGrid.Children.Add(CreateEmptyState());
        }
        else
        {
            AreaGrid.Children.Add(BuildNode(_layoutRoot));
        }

        BuildSidebarVisuals();
        InvalidateWorkspaceLayout();
    }

    private void UpdateMainWorkspacePlacement()
    {
        var hasLeft = _sidebars.ContainsKey(DockEdge.Left);
        var hasRight = _sidebars.ContainsKey(DockEdge.Right);
        var hasTop = _sidebars.ContainsKey(DockEdge.Top);
        var hasBottom = _sidebars.ContainsKey(DockEdge.Bottom);

        // Span vacated edge tracks instead of relying solely on a GridLength
        // transition to zero.  Avalonia can retain the previous arrange result
        // for the center cell during the same rebuild frame; spanning the track
        // makes the restored area occupy the old sidebar position immediately.
        Grid.SetColumn(MainWorkspaceCell, hasLeft ? 1 : 0);
        Grid.SetColumnSpan(
            MainWorkspaceCell,
            3 - (hasLeft ? 1 : 0) - (hasRight ? 1 : 0));
        Grid.SetRow(MainWorkspaceCell, hasTop ? 1 : 0);
        Grid.SetRowSpan(
            MainWorkspaceCell,
            3 - (hasTop ? 1 : 0) - (hasBottom ? 1 : 0));
    }

    private void InvalidateWorkspaceLayout()
    {
        AreaGrid.InvalidateMeasure();
        AreaGrid.InvalidateArrange();
        MainWorkspaceCell.InvalidateMeasure();
        MainWorkspaceCell.InvalidateArrange();
        WorkspaceRoot.InvalidateMeasure();
        WorkspaceRoot.InvalidateArrange();
    }

    private Control BuildNode(LayoutNode node)
    {
        if (node is AreaNode area)
        {
            var visual = CreateAreaVisual(area.Definition);
            _visuals.Add(area.Definition.Id, visual);
            return visual.Card;
        }

        var group = (GroupNode)node;
        var grid = new Grid();
        var childViews = new List<Control>(group.Children.Count);

        for (var index = 0; index < group.Children.Count; index++)
        {
            var weight = Math.Max(0.01, group.Weights[index]);
            var child = BuildNode(group.Children[index]);
            childViews.Add(child);

            if (group.Orientation == Orientation.Horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star))
                {
                    MinWidth = MinimumAreaWidth
                });
                Grid.SetColumn(child, index * 2);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(weight, GridUnitType.Star))
                {
                    MinHeight = MinimumAreaHeight
                });
                Grid.SetRow(child, index * 2);
            }

            grid.Children.Add(child);

            if (index >= group.Children.Count - 1)
                continue;

            if (group.Orientation == Orientation.Horizontal)
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SeamThickness)));
            else
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(SeamThickness)));

            AddResizeSeam(grid, group.Orientation, index * 2 + 1);
        }

        _groupVisuals.Add(new GroupVisual(group, childViews, grid));
        return grid;
    }

    private void AddResizeSeam(Grid grid, Orientation orientation, int gridIndex)
    {
        var line = new Border
        {
            Background = SeamIdle,
            IsHitTestVisible = false
        };

        var splitter = new GridSplitter
        {
            Background = Brushes.Transparent,
            ResizeDirection = orientation == Orientation.Horizontal
                ? GridResizeDirection.Columns
                : GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Cursor = new Cursor(orientation == Orientation.Horizontal
                ? StandardCursorType.SizeWestEast
                : StandardCursorType.SizeNorthSouth)
        };

        if (orientation == Orientation.Horizontal)
        {
            splitter.Width = SeamHitSize;
            splitter.Margin = new Thickness(-(SeamHitSize - SeamThickness) / 2, 0);
            splitter.HorizontalAlignment = HorizontalAlignment.Center;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(line, gridIndex);
            Grid.SetColumn(splitter, gridIndex);
        }
        else
        {
            splitter.Height = SeamHitSize;
            splitter.Margin = new Thickness(0, -(SeamHitSize - SeamThickness) / 2);
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(line, gridIndex);
            Grid.SetRow(splitter, gridIndex);
        }

        splitter.PointerEntered += (_, _) => line.Background = Accent;
        splitter.PointerExited += (_, _) => line.Background = SeamIdle;
        splitter.DragCompleted += (_, _) =>
        {
            CaptureLayoutRatios();
            Dispatcher.UIThread.Post(() =>
            {
                if (!TryAutoCollapse())
                    LayoutChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Background);
        };
        ToolTip.SetTip(splitter, orientation == Orientation.Horizontal
            ? "拖动接缝调整左右区域宽度"
            : "拖动接缝调整上下区域高度");

        splitter.ZIndex = 20;
        grid.Children.Add(line);
        grid.Children.Add(splitter);
    }

    private AreaVisual CreateAreaVisual(FeatureAreaDefinition definition, bool wireAreaDrag = true)
    {
        var card = new Border
        {
            Margin = new Thickness(0),
            // Components use emergency uniform scaling when their desktop is
            // smaller than their preferred footprint. They must therefore not
            // raise the area's layout minimum, otherwise DPI rounding can keep
            // the splitter just outside the sidebar-collapse threshold.
            MinWidth = MinimumAreaWidth,
            MinHeight = MinimumAreaHeight,
            Background = CardBackground,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        card.Child = layout;

        var header = new Border
        {
            Background = HeaderBackground,
            Padding = new Thickness(17, 15),
            CornerRadius = new CornerRadius(0)
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var glyphBox = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(13),
            Background = Brush.Parse("#303958"),
            ClipToBounds = true,
            Child = FeatureIconFactory.Create(definition.Glyph, definition.IconPath)
        };

        var titles = new StackPanel
        {
            Margin = new Thickness(12, 0),
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = definition.Title,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                },
                new TextBlock
                {
                    Text = definition.Subtitle,
                    FontSize = 11,
                    Foreground = Muted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        Grid.SetColumn(titles, 1);

        var dragHandle = new Border
        {
            Name = $"DragHandle_{definition.Id}",
            Width = 48,
            Height = 34,
            CornerRadius = new CornerRadius(11),
            Background = Brush.Parse("#2B3248"),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 21,
                Foreground = Brush.Parse("#AAB2CC"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -3, 0, 0)
            }
        };
        if (wireAreaDrag)
        {
            ToolTip.SetTip(dragHandle, "拖动到其他区域的任意一侧进行吸附");
            dragHandle.PointerPressed += (_, args) => BeginDrag(definition.Id, dragHandle, args);
            dragHandle.PointerMoved += (_, args) => ContinueDrag(args);
            dragHandle.PointerReleased += (_, args) => EndDrag(args);
            dragHandle.PointerCaptureLost += (_, _) => CancelDrag();
        }
        Grid.SetColumn(dragHandle, 2);

        headerGrid.Children.Add(glyphBox);
        headerGrid.Children.Add(titles);
        headerGrid.Children.Add(dragHandle);
        header.Child = headerGrid;
        layout.Children.Add(header);

        Canvas? desktop = null;
        Control content;
        if (definition.Actions.Count > 0 || definition.ContentFactory is null)
        {
            desktop = CreateActionContent(definition);
            content = desktop;
        }
        else
        {
            content = definition.ContentFactory.Invoke();
        }
        Grid.SetRow(content, 1);
        layout.Children.Add(content);

        WireComponentDropTarget(card, desktop, definition.Id);

        return new AreaVisual(card, dragHandle, desktop);
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
                ? Brush.Parse("#6C7BFF")
                : Brush.Parse("#22283A");
            var normalBorderBrush = action.IsPrimary
                ? Brush.Parse("#8D98FF")
                : Brush.Parse("#31394F");
            var hoverBackground = action.IsPrimary
                ? Brush.Parse("#7886FF")
                : Brush.Parse("#2D354D");
            var button = new Button
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
                Background = action.IsPrimary ? Brush.Parse("#29FFFFFF") : Brush.Parse("#2D354D"),
                Child = new TextBlock
                {
                    Text = action.Glyph,
                    FontSize = 17,
                    Foreground = Brushes.White,
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
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = action.Description,
                        FontSize = 11,
                        Foreground = action.IsPrimary ? Brush.Parse("#E0E4FF") : Muted,
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
            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Child = button
            };
            ComponentDragSource.Attach(viewbox, action.Id, definition.Id);

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
            GetComponentBounds(current).Contains(pointerPosition))
        {
            // Once a partially covered component has been reached through its
            // visible portion, keep it selected while the pointer moves through
            // the overlap. This prevents the old top component stealing hover.
            hovered = current;
        }

        hovered ??= _componentVisuals.Values
            .Where(candidate =>
                ReferenceEquals(candidate.Desktop, desktop) &&
                GetComponentBounds(candidate).Contains(pointerPosition))
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
        visual.Button.Background = visual.HoverBackground;
        visual.Button.BorderBrush = Accent;
        visual.Button.BorderThickness = new Thickness(2);
        AutomationProperties.SetItemStatus(visual.Button, "悬停置顶");
        _componentHoverOverlay = CreateComponentHoverOverlay(visual.Action);
        visual.Desktop.Children.Add(_componentHoverOverlay);
        UpdateComponentHoverOverlayBounds(visual);
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
        visual.Button.Background = visual.NormalBackground;
        visual.Button.BorderBrush = visual.NormalBorderBrush;
        visual.Button.BorderThickness = new Thickness(1);
        AutomationProperties.SetItemStatus(visual.Button, string.Empty);
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
            Background = action.IsPrimary ? Brush.Parse("#35FFFFFF") : Brush.Parse("#38425F"),
            Child = new TextBlock
            {
                Text = action.Glyph,
                FontSize = 17,
                Foreground = Brushes.White,
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
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = action.Description,
                    FontSize = 11,
                    Foreground = action.IsPrimary ? Brush.Parse("#E8EBFF") : Muted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        Grid.SetColumn(copy, 1);
        var arrow = new TextBlock
        {
            Text = "›",
            FontSize = 22,
            Foreground = Brushes.White,
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
            Background = action.IsPrimary ? Brush.Parse("#7886FF") : Brush.Parse("#2D354D"),
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

    private Size GetEffectiveComponentSize(FeatureAreaAction action, Size desktopSize)
    {
        var preferredWidth = Math.Max(1, action.BaseWidth * _globalComponentScale);
        var preferredHeight = Math.Max(1, action.BaseHeight * _globalComponentScale);
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
        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = Brush.Parse("#45527A"),
            Child = new TextBlock
            {
                Text = action.Glyph,
                FontSize = 17,
                Foreground = Brushes.White,
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
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = "释放后放置于此",
                    FontSize = 11,
                    Foreground = Brush.Parse("#DDE1FF")
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
            Background = Brush.Parse("#D9343D62"),
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
            _componentPlacements.Remove(key);
        }
    }

    private static string ComponentPlacementKey(string areaId, string componentId) =>
        $"{areaId}\0{componentId}";

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

    private static Control CreateEmptyState()
    {
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "＋",
                    FontSize = 32,
                    Foreground = Accent,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "还没有注册功能区",
                    FontSize = 15,
                    Foreground = Brushes.White
                },
                new TextBlock
                {
                    Text = "通过 FeatureAreaRegistry 添加第一个区域",
                    FontSize = 12,
                    Foreground = Muted
                }
            }
        };
    }

    private void BuildSidebarVisuals()
    {
        foreach (var sidebar in _sidebars.Values)
        {
            var host = new Border
            {
                Background = CardBackground,
                BorderBrush = Brush.Parse("#46506A"),
                BorderThickness = SidebarBorderThickness(sidebar.Edge),
                ZIndex = sidebar.Edge is DockEdge.Top or DockEdge.Bottom ? 52 : 51,
                ClipToBounds = true
            };

            if (sidebar.Edge is DockEdge.Left or DockEdge.Right)
            {
                host.HorizontalAlignment = HorizontalAlignment.Stretch;
                host.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRow(host, 1);
                Grid.SetRowSpan(host, 1);
                Grid.SetColumn(host, sidebar.Edge == DockEdge.Left ? 0 : 2);
            }
            else
            {
                host.HorizontalAlignment = HorizontalAlignment.Stretch;
                host.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(host, 0);
                Grid.SetColumnSpan(host, 3);
                Grid.SetRow(host, sidebar.Edge == DockEdge.Top ? 0 : 2);
            }

            sidebar.Host = host;
            host.Child = CreateCollapsedRail(sidebar);
            WireSidebarComponentDropTarget(host, sidebar);
            host.PointerEntered += (_, _) => RevealSidebar(sidebar);
            host.PointerExited += (_, _) =>
            {
                if (_draggedSidebarEdge != sidebar.Edge)
                    HideSidebar(sidebar);
            };
            WorkspaceRoot.Children.Add(host);
            _sidebarHosts.Add(host);
        }
    }

    private void WireSidebarComponentDropTarget(Border host, SidebarState sidebar)
    {
        DragDrop.SetAllowDrop(host, true);

        host.AddHandler(
            DragDrop.DragEnterEvent,
            (_, args) => UpdateSidebarComponentDropTarget(host, sidebar, args),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        host.AddHandler(
            DragDrop.DragOverEvent,
            (_, args) => UpdateSidebarComponentDropTarget(host, sidebar, args),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        host.AddHandler(
            DragDrop.DragLeaveEvent,
            (_, _) =>
            {
                ScheduleComponentDragPreviewHide(sidebar.Desktop);
                ScheduleSidebarHideAfterComponentDrag(host, sidebar);
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        host.AddHandler(
            DragDrop.DropEvent,
            (_, args) =>
            {
                CancelSidebarComponentDragHide(sidebar);
                host.BorderBrush = Brush.Parse("#46506A");
                HideComponentDragPreview(sidebar.Desktop);

                // The revealed area card is already a drop target. If it handled
                // this routed event, only keep the sidebar open and avoid adding
                // the component for a second time at the host level.
                if (args.Handled)
                    return;

                if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
                    payload is null)
                {
                    args.DragEffects = DragDropEffects.None;
                    return;
                }

                args.DragEffects = payload.IsFromLibrary
                    ? DragDropEffects.Copy
                    : DragDropEffects.Move;
                args.Handled = true;
                var position = sidebar.Desktop is null
                    ? new Point(0, 0)
                    : args.GetPosition(sidebar.Desktop);
                var relativePosition = CalculateRelativeDropPosition(
                    sidebar.Definition.Id,
                    payload.ComponentId,
                    sidebar.Desktop,
                    position);
                HideComponentDragPreview(sidebar.Desktop);
                ComponentDropRequested?.Invoke(
                    this,
                    new ComponentDropRequestedEventArgs(
                        payload.ComponentId,
                        sidebar.Definition.Id,
                        payload.SourceAreaId,
                        relativePosition.X,
                        relativePosition.Y));
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void UpdateSidebarComponentDropTarget(
        Border host,
        SidebarState sidebar,
        DragEventArgs args)
    {
        if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
            payload is null)
        {
            HideComponentDragPreview(sidebar.Desktop);
            args.DragEffects = DragDropEffects.None;
            return;
        }

        CancelSidebarComponentDragHide(sidebar);
        RevealSidebar(sidebar);
        host.BorderBrush = Accent;
        args.DragEffects = payload.IsFromLibrary
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        args.Handled = true;
        ShowComponentDragPreview(
            sidebar.Desktop,
            sidebar.Definition.Id,
            payload.ComponentId,
            sidebar.Desktop is null ? new Point(0, 0) : args.GetPosition(sidebar.Desktop));
    }

    private void ScheduleSidebarHideAfterComponentDrag(Border host, SidebarState sidebar)
    {
        CancelSidebarComponentDragHide(sidebar);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        sidebar.ComponentDragLeaveTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(sidebar.ComponentDragLeaveTimer, timer))
                return;

            sidebar.ComponentDragLeaveTimer = null;
            host.BorderBrush = Brush.Parse("#46506A");
            if (!host.IsPointerOver && _draggedSidebarEdge != sidebar.Edge)
                HideSidebar(sidebar);
        };
        timer.Start();
    }

    private static void CancelSidebarComponentDragHide(SidebarState sidebar)
    {
        sidebar.ComponentDragLeaveTimer?.Stop();
        sidebar.ComponentDragLeaveTimer = null;
    }

    private static Control CreateCollapsedRail(SidebarState sidebar)
    {
        var iconBox = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
            Background = Brush.Parse("#303958"),
            ClipToBounds = true,
            Child = FeatureIconFactory.Create(
                sidebar.Definition.Glyph,
                sidebar.Definition.IconPath,
                15)
        };

        var rail = new Grid
        {
            Background = CardBackground
        };

        if (sidebar.Edge is DockEdge.Left or DockEdge.Right)
        {
            rail.RowDefinitions.Add(new RowDefinition(new GridLength(72)));
            rail.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            rail.Children.Add(new Border
            {
                Background = HeaderBackground,
                IsHitTestVisible = false
            });
            Grid.SetRowSpan(iconBox, 2);
        }

        rail.Children.Add(iconBox);
        iconBox.HorizontalAlignment = HorizontalAlignment.Center;
        iconBox.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(rail, $"悬停展开 · {sidebar.Definition.Title}");
        return rail;
    }

    private void RevealSidebar(SidebarState sidebar)
    {
        if (sidebar.IsRevealed || sidebar.Host is null)
            return;

        sidebar.IsRevealed = true;

        var visual = CreateAreaVisual(sidebar.Definition, wireAreaDrag: false);
        sidebar.Desktop = visual.Desktop;
        ToolTip.SetTip(visual.Handle, "拖动侧边栏到窗口的任意边缘；已有侧边栏时会交换位置");
        visual.Handle.PointerPressed += (_, args) => BeginSidebarDrag(sidebar, visual.Handle, args);
        visual.Handle.PointerMoved += (_, args) => ContinueSidebarDrag(args);
        visual.Handle.PointerReleased += (_, args) => EndSidebarDrag(args);
        visual.Handle.PointerCaptureLost += (_, _) => CancelSidebarDrag();
        sidebar.Host.Child = CreateSidebarShell(sidebar, visual.Card);
        AnimateSidebarTrack(sidebar, sidebar.RevealSize);
    }

    private void HideSidebar(SidebarState sidebar)
    {
        if (!sidebar.IsRevealed || sidebar.Host is null)
            return;

        sidebar.IsRevealed = false;
        AnimateSidebarTrack(sidebar, CollapsedRailSize, () =>
        {
            if (!sidebar.IsRevealed && sidebar.Host is not null)
            {
                sidebar.Host.Child = CreateCollapsedRail(sidebar);
                sidebar.Desktop = null;
            }
        });
    }

    private Control CreateSidebarShell(SidebarState sidebar, Control content)
    {
        var shell = new Grid();
        shell.Children.Add(content);

        var grip = new Border
        {
            Background = Brushes.Transparent,
            ZIndex = 80,
            Cursor = new Cursor(sidebar.Edge is DockEdge.Left or DockEdge.Right
                ? StandardCursorType.SizeWestEast
                : StandardCursorType.SizeNorthSouth)
        };

        if (sidebar.Edge is DockEdge.Left or DockEdge.Right)
        {
            grip.Width = SidebarSeamHitSize;
            grip.HorizontalAlignment = sidebar.Edge == DockEdge.Left
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
            grip.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            grip.Height = SidebarSeamHitSize;
            grip.HorizontalAlignment = HorizontalAlignment.Stretch;
            grip.VerticalAlignment = sidebar.Edge == DockEdge.Top
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
        }

        ToolTip.SetTip(grip, "拖动此边框会立即恢复为普通功能区");
        grip.PointerEntered += (_, _) => grip.Background = Accent;
        grip.PointerExited += (_, _) => grip.Background = Brushes.Transparent;
        grip.PointerPressed += (_, args) => RestoreSidebarOnResizeAttempt(sidebar, grip, args);
        shell.Children.Add(grip);
        return shell;
    }

    private void ResetSidebarTracks()
    {
        foreach (var sidebar in _sidebars.Values)
        {
            sidebar.TrackAnimation?.Stop();
            sidebar.TrackAnimation = null;
            CancelSidebarComponentDragHide(sidebar);
            sidebar.IsRevealed = false;
            sidebar.Host = null;
            sidebar.Desktop = null;
        }

        LeftSidebarColumn.Width = new GridLength(
            _sidebars.ContainsKey(DockEdge.Left) ? CollapsedRailSize : 0);
        RightSidebarColumn.Width = new GridLength(
            _sidebars.ContainsKey(DockEdge.Right) ? CollapsedRailSize : 0);
        TopSidebarRow.Height = new GridLength(
            _sidebars.ContainsKey(DockEdge.Top) ? CollapsedRailSize : 0);
        BottomSidebarRow.Height = new GridLength(
            _sidebars.ContainsKey(DockEdge.Bottom) ? CollapsedRailSize : 0);
    }

    private void SetSidebarTrackSize(DockEdge edge, double size)
    {
        var length = new GridLength(Math.Max(CollapsedRailSize, size));
        switch (edge)
        {
            case DockEdge.Left:
                LeftSidebarColumn.Width = length;
                break;
            case DockEdge.Right:
                RightSidebarColumn.Width = length;
                break;
            case DockEdge.Top:
                TopSidebarRow.Height = length;
                break;
            case DockEdge.Bottom:
                BottomSidebarRow.Height = length;
                break;
        }
    }

    private double GetSidebarTrackSize(DockEdge edge)
    {
        return edge switch
        {
            DockEdge.Left => LeftSidebarColumn.Width.Value,
            DockEdge.Right => RightSidebarColumn.Width.Value,
            DockEdge.Top => TopSidebarRow.Height.Value,
            DockEdge.Bottom => BottomSidebarRow.Height.Value,
            _ => CollapsedRailSize
        };
    }

    private void AnimateSidebarTrack(
        SidebarState sidebar,
        double targetSize,
        Action? completed = null)
    {
        sidebar.TrackAnimation?.Stop();

        var startSize = GetSidebarTrackSize(sidebar.Edge);
        var target = Math.Max(CollapsedRailSize, targetSize);
        if (Math.Abs(startSize - target) < 0.5)
        {
            SetSidebarTrackSize(sidebar.Edge, target);
            completed?.Invoke();
            return;
        }

        var startedAt = DateTime.UtcNow;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        sidebar.TrackAnimation = timer;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(sidebar.TrackAnimation, timer))
            {
                timer.Stop();
                return;
            }

            var progress = Math.Clamp(
                (DateTime.UtcNow - startedAt).TotalMilliseconds /
                SidebarAnimationDurationMilliseconds,
                0,
                1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            SetSidebarTrackSize(
                sidebar.Edge,
                startSize + ((target - startSize) * eased));

            if (progress < 1)
                return;

            timer.Stop();
            if (ReferenceEquals(sidebar.TrackAnimation, timer))
                sidebar.TrackAnimation = null;
            completed?.Invoke();
        };
        timer.Start();
    }

    private void RestoreSidebarOnResizeAttempt(
        SidebarState sidebar,
        Border grip,
        PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;

        var session = new RestoredResizeSession(
            sidebar.Definition.Id,
            sidebar.Edge,
            sidebar.RevealSize);
        _restoredResizeSession = session;

        // Capture to the stable root before rebuilding.  Removing the pressed
        // sidebar grip can raise PointerCaptureLost; treating that bubbled
        // event as the end of this gesture immediately collapsed the restored
        // area again and left what looked like a black sidebar remnant.
        e.Pointer.Capture(WorkspaceRoot);
        RestoreSidebar(sidebar, notifyLayoutChanged: false);
        TryResizeRestoredArea(session, e);
        e.Handled = true;
    }

    private void RestoreSidebar(SidebarState sidebar, bool notifyLayoutChanged = true)
    {
        sidebar.TrackAnimation?.Stop();
        sidebar.TrackAnimation = null;
        CaptureLayoutRatios();
        var restoredEdge = sidebar.Edge;

        // Remove by area identity as well as by edge.  The sidebar may have
        // changed edges during a previous drag, so relying on its last edge
        // alone can leave the old state and its grid track behind.
        var entriesToRemove = _sidebars
            .Where(pair => ReferenceEquals(pair.Value, sidebar) ||
                           string.Equals(
                               pair.Value.Definition.Id,
                               sidebar.Definition.Id,
                               StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var entry in entriesToRemove)
        {
            entry.Value.TrackAnimation?.Stop();
            entry.Value.TrackAnimation = null;
            if (entry.Value.Host is not null)
            {
                entry.Value.Host.Child = null;
                WorkspaceRoot.Children.Remove(entry.Value.Host);
                _sidebarHosts.Remove(entry.Value.Host);
                entry.Value.Host = null;
            }

            _sidebars.Remove(entry.Key);
        }

        // Release the occupied edge before rebuilding the regular-area tree,
        // so there is never a frame in which the restored area and its former
        // sidebar slot coexist.
        ClearVacatedSidebarTrack(restoredEdge);

        _layoutRoot = InsertAtWorkspaceEdge(
            _layoutRoot,
            new AreaNode(sidebar.Definition),
            restoredEdge,
            sidebar.RevealSize);
        Rebuild();
        ClearVacatedSidebarTrack(restoredEdge);

        // A stopped DispatcherTimer can already have queued its final tick.
        // Scrub the vacated edge once more after that queue has drained.
        Dispatcher.UIThread.Post(() => ClearVacatedSidebarTrack(restoredEdge));
        if (notifyLayoutChanged)
            LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearVacatedSidebarTrack(DockEdge edge)
    {
        if (_sidebars.ContainsKey(edge))
            return;

        switch (edge)
        {
            case DockEdge.Left:
                LeftSidebarColumn.Width = new GridLength(0);
                break;
            case DockEdge.Right:
                RightSidebarColumn.Width = new GridLength(0);
                break;
            case DockEdge.Top:
                TopSidebarRow.Height = new GridLength(0);
                break;
            case DockEdge.Bottom:
                BottomSidebarRow.Height = new GridLength(0);
                break;
        }

        UpdateMainWorkspacePlacement();
        InvalidateWorkspaceLayout();
    }

    private void OnRestoredResizePointerMoved(object? sender, PointerEventArgs e)
    {
        var session = _restoredResizeSession;
        if (session is null)
            return;

        if (!e.GetCurrentPoint(WorkspaceRoot).Properties.IsLeftButtonPressed)
        {
            CompleteRestoredResize();
            e.Pointer.Capture(null);
            return;
        }

        if (TryResizeRestoredArea(session, e))
            e.Handled = true;
    }

    private void OnRestoredResizePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_restoredResizeSession is null)
            return;

        CompleteRestoredResize();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private bool TryResizeRestoredArea(
        RestoredResizeSession session,
        PointerEventArgs e)
    {
        if (_layoutRoot is not GroupNode rootGroup)
            return false;

        var expectedOrientation = session.Edge is DockEdge.Left or DockEdge.Right
            ? Orientation.Horizontal
            : Orientation.Vertical;
        if (rootGroup.Orientation != expectedOrientation)
            return false;

        var areaIndex = rootGroup.Children.FindIndex(child =>
            child is AreaNode area &&
            string.Equals(
                area.Definition.Id,
                session.AreaId,
                StringComparison.OrdinalIgnoreCase));
        if (areaIndex < 0)
            return false;

        var visual = _groupVisuals.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Node, rootGroup));
        if (visual is null)
            return false;

        var position = e.GetPosition(visual.Grid);
        var totalLength = expectedOrientation == Orientation.Horizontal
            ? visual.Grid.Bounds.Width
            : visual.Grid.Bounds.Height;
        if (totalLength <= 0)
            return false;

        var requested = session.Edge switch
        {
            DockEdge.Left => position.X,
            DockEdge.Right => totalLength - position.X,
            DockEdge.Top => position.Y,
            DockEdge.Bottom => totalLength - position.Y,
            _ => session.LastSize
        };
        var minimum = expectedOrientation == Orientation.Horizontal
            ? MinimumAreaWidth
            : MinimumAreaHeight;
        var usableLength = Math.Max(
            minimum,
            totalLength - (SeamThickness * (rootGroup.Children.Count - 1)));
        var maximum = Math.Max(
            minimum,
            usableLength - (minimum * (rootGroup.Children.Count - 1)));
        var desired = Math.Clamp(requested, minimum, maximum);
        var otherWeight = rootGroup.Weights
            .Where((_, index) => index != areaIndex)
            .Sum(weight => Math.Max(0.01, weight));
        var weight = otherWeight <= 0 || usableLength - desired <= 0.01
            ? desired
            : (desired * otherWeight) / (usableLength - desired);

        rootGroup.Weights[areaIndex] = Math.Max(0.01, weight);
        if (expectedOrientation == Orientation.Horizontal)
        {
            visual.Grid.ColumnDefinitions[areaIndex * 2].Width =
                new GridLength(rootGroup.Weights[areaIndex], GridUnitType.Star);
        }
        else
        {
            visual.Grid.RowDefinitions[areaIndex * 2].Height =
                new GridLength(rootGroup.Weights[areaIndex], GridUnitType.Star);
        }

        session.LastSize = desired;
        visual.Grid.InvalidateMeasure();
        return true;
    }

    private void CompleteRestoredResize()
    {
        var session = _restoredResizeSession;
        if (session is null)
            return;

        _restoredResizeSession = null;
        CaptureLayoutRatios();

        var collapseThreshold = session.Edge is DockEdge.Left or DockEdge.Right
            ? CollapseWidthThreshold
            : CollapseHeightThreshold;
        if (session.LastSize <= collapseThreshold &&
            CollapseRestoredArea(session))
        {
            return;
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CollapseRestoredArea(RestoredResizeSession session)
    {
        if (_layoutRoot is null || _sidebars.ContainsKey(session.Edge))
            return false;

        var area = EnumerateAreas(_layoutRoot).FirstOrDefault(node =>
            string.Equals(
                node.Definition.Id,
                session.AreaId,
                StringComparison.OrdinalIgnoreCase));
        if (area is null)
            return false;

        _layoutRoot = RemoveArea(_layoutRoot, session.AreaId);
        _sidebars[session.Edge] = new SidebarState(
            area.Definition,
            session.Edge,
            Math.Max(CollapsedRailSize, session.LastSize));
        Rebuild();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static LayoutNode InsertAtWorkspaceEdge(
        LayoutNode? root,
        AreaNode area,
        DockEdge edge,
        double areaWeight)
    {
        if (root is null)
            return area;

        LayoutNode existingRoot = root;

        var orientation = edge is DockEdge.Left or DockEdge.Right
            ? Orientation.Horizontal
            : Orientation.Vertical;
        var insertFirst = edge is DockEdge.Left or DockEdge.Top;
        var normalizedWeight = Math.Max(1, areaWeight);

        if (existingRoot is GroupNode group && group.Orientation == orientation)
        {
            var index = insertFirst ? 0 : group.Children.Count;
            group.Children.Insert(index, area);
            group.Weights.Insert(index, normalizedWeight);
            return group;
        }

        var existingWeight = orientation == Orientation.Horizontal
            ? 640
            : 420;
        return insertFirst
            ? new GroupNode(orientation, [area, existingRoot], [normalizedWeight, existingWeight])
            : new GroupNode(orientation, [existingRoot, area], [existingWeight, normalizedWeight]);
    }

    private void BeginSidebarDrag(
        SidebarState sidebar,
        Border handle,
        PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        _draggedSidebarEdge = sidebar.Edge;
        _sidebarDropEdge = sidebar.Edge;
        handle.Background = Brush.Parse("#445078");
        if (sidebar.Host is not null)
            sidebar.Host.Opacity = 0.72;
        e.Pointer.Capture(handle);
        DockHint.IsVisible = true;
        DockHintText.Text = "拖动到窗口任意边缘；目标已有侧边栏时交换位置";
        UpdateSidebarDropPreview(sidebar.Edge);
        e.Handled = true;
    }

    private void ContinueSidebarDrag(PointerEventArgs e)
    {
        if (_draggedSidebarEdge is null)
            return;

        if (!e.GetCurrentPoint(WorkspaceRoot).Properties.IsLeftButtonPressed)
        {
            CancelSidebarDrag();
            e.Pointer.Capture(null);
            return;
        }

        var edge = GetNearestWorkspaceEdge(e.GetPosition(WorkspaceRoot));
        _sidebarDropEdge = edge;
        UpdateSidebarDropPreview(edge);
        DockHintText.Text = _sidebars.ContainsKey(edge) && edge != _draggedSidebarEdge
            ? $"释放后与{GetEdgeName(edge)}侧边栏交换位置"
            : $"释放后吸附到{GetEdgeName(edge)}边";
        e.Handled = true;
    }

    private void EndSidebarDrag(PointerReleasedEventArgs e)
    {
        if (_draggedSidebarEdge is null)
            return;

        var source = _draggedSidebarEdge.Value;
        var target = _sidebarDropEdge ?? source;
        e.Pointer.Capture(null);
        ResetSidebarDragState();

        if (source != target)
            MoveOrSwapSidebar(source, target);

        e.Handled = true;
    }

    private void MoveOrSwapSidebar(DockEdge source, DockEdge target)
    {
        if (!_sidebars.TryGetValue(source, out var moved))
            return;

        _sidebars.Remove(source);
        if (_sidebars.TryGetValue(target, out var displaced))
        {
            _sidebars.Remove(target);
            displaced.RevealSize = AdaptSidebarSize(displaced.RevealSize, target, source);
            displaced.Edge = source;
            _sidebars[source] = displaced;
        }

        moved.RevealSize = AdaptSidebarSize(moved.RevealSize, source, target);
        moved.Edge = target;
        _sidebars[target] = moved;
        Rebuild();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double AdaptSidebarSize(double size, DockEdge oldEdge, DockEdge newEdge)
    {
        var changedOrientation = (oldEdge is DockEdge.Left or DockEdge.Right) !=
                                 (newEdge is DockEdge.Left or DockEdge.Right);
        return changedOrientation ? DefaultRevealSize(newEdge) : size;
    }

    private DockEdge GetNearestWorkspaceEdge(Point position)
    {
        var distances = new[]
        {
            (Edge: DockEdge.Left, Distance: Math.Abs(position.X)),
            (Edge: DockEdge.Right, Distance: Math.Abs(WorkspaceRoot.Bounds.Width - position.X)),
            (Edge: DockEdge.Top, Distance: Math.Abs(position.Y)),
            (Edge: DockEdge.Bottom, Distance: Math.Abs(WorkspaceRoot.Bounds.Height - position.Y))
        };
        return distances.OrderBy(item => item.Distance).First().Edge;
    }

    private void UpdateSidebarDropPreview(DockEdge edge)
    {
        SidebarDropPreview.IsVisible = true;
        SidebarDropPreview.Margin = new Thickness(0);
        SidebarDropPreview.Width = double.NaN;
        SidebarDropPreview.Height = double.NaN;
        SidebarDropPreview.HorizontalAlignment = HorizontalAlignment.Stretch;
        SidebarDropPreview.VerticalAlignment = VerticalAlignment.Stretch;

        if (edge is DockEdge.Left or DockEdge.Right)
        {
            SidebarDropPreview.Width = CollapsedRailSize;
            SidebarDropPreview.HorizontalAlignment = edge == DockEdge.Left
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        }
        else
        {
            SidebarDropPreview.Height = CollapsedRailSize;
            SidebarDropPreview.VerticalAlignment = edge == DockEdge.Top
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
        }
    }

    private void CancelSidebarDrag()
    {
        if (_draggedSidebarEdge is not null)
            ResetSidebarDragState();
    }

    private void ResetSidebarDragState()
    {
        foreach (var sidebar in _sidebars.Values)
        {
            if (sidebar.Host is not null)
                sidebar.Host.Opacity = 1;
        }

        _draggedSidebarEdge = null;
        _sidebarDropEdge = null;
        SidebarDropPreview.IsVisible = false;
        DockHint.IsVisible = false;
    }

    private static string GetEdgeName(DockEdge edge)
    {
        return edge switch
        {
            DockEdge.Left => "左",
            DockEdge.Right => "右",
            DockEdge.Top => "上",
            DockEdge.Bottom => "下",
            _ => string.Empty
        };
    }

    private bool TryAutoCollapse()
    {
        if (_layoutRoot is null || EnumerateAreas(_layoutRoot).Count() <= 1)
            return false;

        const double outerTolerance = 9;
        var workspace = new Rect(AreaGrid.Bounds.Size);
        var candidates = new List<CollapseCandidate>();

        foreach (var pair in _visuals)
        {
            var bounds = GetWorkspaceBounds(pair.Value.Card);
            var touchesLeft = Math.Abs(bounds.Left - workspace.Left) <= outerTolerance;
            var touchesRight = Math.Abs(bounds.Right - workspace.Right) <= outerTolerance;
            var touchesTop = Math.Abs(bounds.Top - workspace.Top) <= outerTolerance;
            var touchesBottom = Math.Abs(bounds.Bottom - workspace.Bottom) <= outerTolerance;

            if (bounds.Width <= CollapseWidthThreshold)
            {
                if (touchesLeft && !_sidebars.ContainsKey(DockEdge.Left))
                    candidates.Add(new CollapseCandidate(pair.Key, DockEdge.Left, bounds.Width,
                        bounds.Width / CollapseWidthThreshold));
                if (touchesRight && !_sidebars.ContainsKey(DockEdge.Right))
                    candidates.Add(new CollapseCandidate(pair.Key, DockEdge.Right, bounds.Width,
                        bounds.Width / CollapseWidthThreshold));
            }

            if (bounds.Height <= CollapseHeightThreshold)
            {
                if (touchesTop && !_sidebars.ContainsKey(DockEdge.Top))
                    candidates.Add(new CollapseCandidate(pair.Key, DockEdge.Top, bounds.Height,
                        bounds.Height / CollapseHeightThreshold));
                if (touchesBottom && !_sidebars.ContainsKey(DockEdge.Bottom))
                    candidates.Add(new CollapseCandidate(pair.Key, DockEdge.Bottom, bounds.Height,
                        bounds.Height / CollapseHeightThreshold));
            }
        }

        var candidate = candidates.OrderBy(item => item.SizeRatio).FirstOrDefault();
        if (candidate is null)
            return false;

        var area = EnumerateAreas(_layoutRoot).FirstOrDefault(node =>
            string.Equals(node.Definition.Id, candidate.AreaId, StringComparison.OrdinalIgnoreCase));
        if (area is null)
            return false;

        CaptureLayoutRatios();
        _layoutRoot = RemoveArea(_layoutRoot, candidate.AreaId);
        _sidebars[candidate.Edge] = new SidebarState(area.Definition, candidate.Edge, candidate.RevealSize);
        Rebuild();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static Thickness SidebarBorderThickness(DockEdge edge)
    {
        return edge switch
        {
            DockEdge.Left => new Thickness(0, 0, 1, 0),
            DockEdge.Right => new Thickness(1, 0, 0, 0),
            DockEdge.Top => new Thickness(0, 0, 0, 1),
            DockEdge.Bottom => new Thickness(0, 1, 0, 0),
            _ => new Thickness(1)
        };
    }

    private static double DefaultRevealSize(DockEdge edge)
    {
        return edge is DockEdge.Left or DockEdge.Right ? 260 : 210;
    }

    private void BeginDrag(string areaId, Border handle, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        _draggedAreaId = areaId;
        _targetAreaId = null;
        _dropSide = null;
        handle.Background = Brush.Parse("#445078");
        e.Pointer.Capture(handle);

        if (_visuals.TryGetValue(areaId, out var visual))
            visual.Card.Opacity = 0.62;

        DockHint.IsVisible = true;
        DockHintText.Text = "移动到目标区域的上、下、左、右侧";
        e.Handled = true;
    }

    private void ContinueDrag(PointerEventArgs e)
    {
        if (_draggedAreaId is null)
            return;

        if (!e.GetCurrentPoint(AreaGrid).Properties.IsLeftButtonPressed)
        {
            CancelDrag();
            e.Pointer.Capture(null);
            return;
        }

        var position = e.GetPosition(AreaGrid);
        var target = FindTargetArea(position);
        if (target is null || string.Equals(target.Value.Id, _draggedAreaId, StringComparison.OrdinalIgnoreCase))
        {
            ClearDropTarget();
            return;
        }

        _targetAreaId = target.Value.Id;
        _dropSide = GetDropSide(position, target.Value.Bounds);
        UpdateDropPreview(target.Value.Bounds, _dropSide.Value);
        e.Handled = true;
    }

    private void EndDrag(PointerReleasedEventArgs e)
    {
        if (_draggedAreaId is null)
            return;

        var draggedId = _draggedAreaId;
        var targetId = _targetAreaId;
        var side = _dropSide;

        if (targetId is not null && side is not null &&
            !string.Equals(draggedId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            DockArea(draggedId, targetId, side.Value);
        }

        ResetDragState();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void DockArea(string draggedId, string targetId, DropSide side)
    {
        if (_layoutRoot is null)
            return;

        var draggedNode = EnumerateAreas(_layoutRoot).FirstOrDefault(area =>
            string.Equals(area.Definition.Id, draggedId, StringComparison.OrdinalIgnoreCase));
        if (draggedNode is null)
            return;

        CaptureLayoutRatios();
        var remaining = RemoveArea(_layoutRoot, draggedId);
        if (remaining is null || !ContainsArea(remaining, targetId))
            return;

        _layoutRoot = InsertArea(remaining, targetId, draggedNode, side);
        Rebuild();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private (string Id, Rect Bounds)? FindTargetArea(Point position)
    {
        (string Id, Rect Bounds)? nearest = null;
        var nearestDistance = double.MaxValue;

        foreach (var pair in _visuals)
        {
            var bounds = GetWorkspaceBounds(pair.Value.Card);
            if (bounds.Contains(position))
                return (pair.Key, bounds);

            var center = bounds.Center;
            var distance = Math.Pow(position.X - center.X, 2) + Math.Pow(position.Y - center.Y, 2);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = (pair.Key, bounds);
            }
        }

        return nearest;
    }

    private Rect GetWorkspaceBounds(Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), AreaGrid) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    private static DropSide GetDropSide(Point position, Rect targetBounds)
    {
        var horizontal = (position.X - targetBounds.Center.X) / Math.Max(1, targetBounds.Width);
        var vertical = (position.Y - targetBounds.Center.Y) / Math.Max(1, targetBounds.Height);

        if (Math.Abs(horizontal) > Math.Abs(vertical))
            return horizontal < 0 ? DropSide.Left : DropSide.Right;

        return vertical < 0 ? DropSide.Top : DropSide.Bottom;
    }

    private void UpdateDropPreview(Rect targetBounds, DropSide side)
    {
        foreach (var visual in _visuals.Values)
        {
            visual.Card.BorderBrush = CardBorder;
            visual.Card.BorderThickness = new Thickness(0);
        }

        if (_targetAreaId is not null && _visuals.TryGetValue(_targetAreaId, out var targetVisual))
        {
            targetVisual.Card.BorderBrush = Accent;
            targetVisual.Card.BorderThickness = new Thickness(2);
        }

        var preview = side switch
        {
            DropSide.Left => new Rect(targetBounds.X, targetBounds.Y, targetBounds.Width / 2, targetBounds.Height),
            DropSide.Right => new Rect(targetBounds.Center.X, targetBounds.Y, targetBounds.Width / 2, targetBounds.Height),
            DropSide.Top => new Rect(targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height / 2),
            DropSide.Bottom => new Rect(targetBounds.X, targetBounds.Center.Y, targetBounds.Width, targetBounds.Height / 2),
            _ => targetBounds
        };

        Canvas.SetLeft(DropPreview, preview.X);
        Canvas.SetTop(DropPreview, preview.Y);
        DropPreview.Width = preview.Width;
        DropPreview.Height = preview.Height;
        DropPreview.IsVisible = true;

        DockHintText.Text = side switch
        {
            DropSide.Left => "释放后吸附到目标左侧",
            DropSide.Right => "释放后吸附到目标右侧",
            DropSide.Top => "释放后吸附到目标上方",
            DropSide.Bottom => "释放后吸附到目标下方",
            _ => "释放以吸附"
        };
    }

    private void ClearDropTarget()
    {
        _targetAreaId = null;
        _dropSide = null;
        DropPreview.IsVisible = false;
        DockHintText.Text = "移动到目标区域的上、下、左、右侧";

        foreach (var visual in _visuals.Values)
        {
            visual.Card.BorderBrush = CardBorder;
            visual.Card.BorderThickness = new Thickness(0);
        }
    }

    private void CancelDrag()
    {
        if (_draggedAreaId is null)
            return;

        ResetDragState();
    }

    private void ResetDragState()
    {
        _draggedAreaId = null;
        _targetAreaId = null;
        _dropSide = null;
        DropPreview.IsVisible = false;
        DockHint.IsVisible = false;

        foreach (var visual in _visuals.Values)
        {
            visual.Card.Opacity = 1;
            visual.Card.BorderBrush = CardBorder;
            visual.Card.BorderThickness = new Thickness(0);
            visual.Handle.Background = Brush.Parse("#2B3248");
        }
    }

    private void CaptureLayoutRatios()
    {
        foreach (var visual in _groupVisuals)
        {
            for (var index = 0; index < visual.ChildViews.Count; index++)
            {
                var size = visual.Node.Orientation == Orientation.Horizontal
                    ? visual.ChildViews[index].Bounds.Width
                    : visual.ChildViews[index].Bounds.Height;

                if (size > 0)
                    visual.Node.Weights[index] = size;
            }
        }
    }

    private static LayoutNode AppendArea(LayoutNode? root, AreaNode area)
    {
        if (root is null)
            return area;

        if (root is GroupNode { Orientation: Orientation.Horizontal } group)
        {
            var averageWeight = group.Weights.Count == 0 ? 1 : group.Weights.Average();
            group.Children.Add(area);
            group.Weights.Add(averageWeight);
            return group;
        }

        return new GroupNode(Orientation.Horizontal, [root, area], [1, 1]);
    }

    private static LayoutNode? RemoveArea(LayoutNode node, string id)
    {
        if (node is AreaNode area)
        {
            return string.Equals(area.Definition.Id, id, StringComparison.OrdinalIgnoreCase)
                ? null
                : area;
        }

        var group = (GroupNode)node;
        for (var index = group.Children.Count - 1; index >= 0; index--)
        {
            var updated = RemoveArea(group.Children[index], id);
            if (updated is null)
            {
                group.Children.RemoveAt(index);
                group.Weights.RemoveAt(index);
            }
            else
            {
                group.Children[index] = updated;
            }
        }

        return group.Children.Count switch
        {
            0 => null,
            1 => group.Children[0],
            _ => group
        };
    }

    private static LayoutNode InsertArea(LayoutNode node, string targetId, AreaNode dragged, DropSide side)
    {
        var requiredOrientation = side is DropSide.Left or DropSide.Right
            ? Orientation.Horizontal
            : Orientation.Vertical;
        var insertBefore = side is DropSide.Left or DropSide.Top;

        if (node is AreaNode target)
        {
            if (!string.Equals(target.Definition.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return target;

            return insertBefore
                ? new GroupNode(requiredOrientation, [dragged, target], [1, 1])
                : new GroupNode(requiredOrientation, [target, dragged], [1, 1]);
        }

        var group = (GroupNode)node;
        var directTargetIndex = group.Children.FindIndex(child => child is AreaNode area &&
            string.Equals(area.Definition.Id, targetId, StringComparison.OrdinalIgnoreCase));

        if (group.Orientation == requiredOrientation && directTargetIndex >= 0)
        {
            var targetWeight = Math.Max(0.02, group.Weights[directTargetIndex]);
            group.Weights[directTargetIndex] = targetWeight / 2;
            var insertIndex = insertBefore ? directTargetIndex : directTargetIndex + 1;
            group.Children.Insert(insertIndex, dragged);
            group.Weights.Insert(insertIndex, targetWeight / 2);
            return group;
        }

        for (var index = 0; index < group.Children.Count; index++)
        {
            if (ContainsArea(group.Children[index], targetId))
            {
                group.Children[index] = InsertArea(group.Children[index], targetId, dragged, side);
                break;
            }
        }

        return group;
    }

    private static bool ContainsArea(LayoutNode node, string id)
    {
        return node switch
        {
            AreaNode area => string.Equals(area.Definition.Id, id, StringComparison.OrdinalIgnoreCase),
            GroupNode group => group.Children.Any(child => ContainsArea(child, id)),
            _ => false
        };
    }

    private static IEnumerable<AreaNode> EnumerateAreas(LayoutNode node)
    {
        if (node is AreaNode area)
        {
            yield return area;
            yield break;
        }

        foreach (var child in ((GroupNode)node).Children)
        {
            foreach (var descendant in EnumerateAreas(child))
                yield return descendant;
        }
    }

    private static DockLayoutProfile ExportNode(LayoutNode node)
    {
        if (node is AreaNode area)
        {
            return new DockLayoutProfile
            {
                AreaId = area.Definition.Id
            };
        }

        var group = (GroupNode)node;
        return new DockLayoutProfile
        {
            Direction = group.Orientation == Orientation.Horizontal
                ? DockSplitDirection.Horizontal
                : DockSplitDirection.Vertical,
            Children = group.Children.Select(ExportNode).ToList(),
            Weights = [.. group.Weights]
        };
    }

    private static LayoutNode? ImportNode(
        DockLayoutProfile profile,
        IReadOnlyDictionary<string, FeatureAreaDefinition> definitions,
        ISet<string> usedIds)
    {
        if (!string.IsNullOrWhiteSpace(profile.AreaId))
        {
            if (!definitions.TryGetValue(profile.AreaId, out var definition) || !usedIds.Add(profile.AreaId))
                return null;

            return new AreaNode(definition);
        }

        if (profile.Direction is null)
            return null;

        var children = profile.Children
            .Select(child => ImportNode(child, definitions, usedIds))
            .OfType<LayoutNode>()
            .ToList();

        if (children.Count == 0)
            return null;
        if (children.Count == 1)
            return children[0];

        var weights = Enumerable.Range(0, children.Count)
            .Select(index => index < profile.Weights.Count && profile.Weights[index] > 0
                ? profile.Weights[index]
                : 1)
            .ToList();

        return new GroupNode(
            profile.Direction == DockSplitDirection.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical,
            children,
            weights);
    }

    private abstract class LayoutNode;

    private sealed class AreaNode(FeatureAreaDefinition definition) : LayoutNode
    {
        public FeatureAreaDefinition Definition { get; set; } = definition;
    }

    private sealed class GroupNode(
        Orientation orientation,
        List<LayoutNode> children,
        List<double> weights) : LayoutNode
    {
        public Orientation Orientation { get; } = orientation;
        public List<LayoutNode> Children { get; } = children;
        public List<double> Weights { get; } = weights;
    }

    private enum DropSide
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private sealed record AreaVisual(Border Card, Border Handle, Canvas? Desktop);
    private sealed record DesktopComponentVisual(
        string AreaId,
        FeatureAreaAction Action,
        Canvas Desktop,
        Viewbox View,
        Button Button,
        IBrush NormalBackground,
        IBrush NormalBorderBrush,
        IBrush HoverBackground,
        ComponentPlacementProfile Placement);
    private sealed record ComponentDragPreview(
        string ComponentId,
        Canvas Desktop,
        Viewbox View);
    private sealed record GroupVisual(
        GroupNode Node,
        IReadOnlyList<Control> ChildViews,
        Grid Grid);
    private sealed record CollapseCandidate(
        string AreaId,
        DockEdge Edge,
        double RevealSize,
        double SizeRatio);

    private sealed class RestoredResizeSession(
        string areaId,
        DockEdge edge,
        double initialSize)
    {
        public string AreaId { get; } = areaId;
        public DockEdge Edge { get; } = edge;
        public double LastSize { get; set; } = initialSize;
    }

    private sealed class SidebarState(
        FeatureAreaDefinition definition,
        DockEdge edge,
        double revealSize)
    {
        public FeatureAreaDefinition Definition { get; set; } = definition;
        public DockEdge Edge { get; set; } = edge;
        public double RevealSize { get; set; } = revealSize;
        public Border? Host { get; set; }
        public Canvas? Desktop { get; set; }
        public bool IsRevealed { get; set; }
        public DispatcherTimer? TrackAnimation { get; set; }
        public DispatcherTimer? ComponentDragLeaveTimer { get; set; }
    }
}

public sealed record ComponentDropRequestedEventArgs(
    string ComponentId,
    string TargetAreaId,
    string? SourceAreaId,
    double RelativeX,
    double RelativeY);
