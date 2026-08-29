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
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// A two-dimensional docking workspace. Feature areas can be docked to any
/// edge of another area, while the border seams resize adjacent layout groups.
/// </summary>
public partial class DockWorkspace : UserControl
{
    private static IBrush CardBackground => ThemeBrushes.CardBackground;
    private static IBrush HeaderBackground => ThemeBrushes.HeaderBackground;
    private static IBrush CardBorder => ThemeBrushes.CardBorder;
    private static IBrush Accent => ThemeBrushes.Accent;
    private static IBrush Muted => ThemeBrushes.Muted;
    private static IBrush SeamIdle => ThemeBrushes.SeamIdle;

    private const double MinimumAreaWidth = 180;
    private const double MinimumAreaHeight = 150;
    private const double SeamThickness = 1;
    private const double SeamHitSize = 9;
    private const double DockDraggedStartScale = 0.965;
    private readonly Dictionary<string, AreaVisual> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GroupVisual> _groupVisuals = [];

    private FeatureAreaRegistry? _registry;
    private LayoutNode? _layoutRoot;
    private string? _draggedAreaId;
    private string? _targetAreaId;
    private DropSide? _dropSide;
    private bool _registrySubscribed;
    // 布局重建计数：过期的过渡动画通过它自我中止，避免在新布局上播放旧轨迹
    private int _dockLayoutGeneration;
    private DispatcherTimer? _dockMoveTimer;

    public event EventHandler? LayoutChanged;

    /// <summary>组件动作反馈消息（成功提示或失败原因），转发给宿主状态栏。</summary>
    public event EventHandler<string>? ComponentFeedback;

    public DockWorkspace()
    {
        InitializeComponent();
        _polygonComponentInstancePool = new PolygonComponentInstancePool(
            OnPolygonComponentDisposalCompleted);
        WorkspaceRoot.PointerMoved += OnRestoredResizePointerMoved;
        WorkspaceRoot.PointerReleased += OnRestoredResizePointerReleased;
        WireDiscardBin();
        Action themeChangedHandler = () =>
        {
            if (IsLoaded)
                Rebuild();
        };
        ThemeManager.ThemeChanged += themeChangedHandler;
        DetachedFromVisualTree += (_, _) =>
        {
            // 退订静态事件，防止旧实例被永久钉住
            ThemeManager.ThemeChanged -= themeChangedHandler;
            if (_registry is not null && _registrySubscribed)
            {
                _registry.Changed -= OnRegistryChanged;
                _registrySubscribed = false;
            }

            _polygonComponentInstancePool.ReleaseAll();
            _refreshPolygonInstancesOnAttach = true;
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (_polygonComponentInstancePool.IsShuttingDown)
                return;

            if (_registry is not null && !_registrySubscribed)
            {
                _registry.Changed += OnRegistryChanged;
                _registrySubscribed = true;
            }

            if (!_refreshPolygonInstancesOnAttach)
                return;

            _refreshPolygonInstancesOnAttach = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_refreshPolygonInstancesOnAttach &&
                    !_polygonComponentInstancePool.IsShuttingDown)
                    SynchronizeWithRegistry();
            }, DispatcherPriority.Loaded);
        };
    }

    public void UseRegistry(FeatureAreaRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_polygonComponentInstancePool.IsShuttingDown)
            return;

        var registryChanged = _registry is not null && !ReferenceEquals(_registry, registry);
        if (_registry is not null && _registrySubscribed)
            _registry.Changed -= OnRegistryChanged;
        if (registryChanged)
            _polygonComponentInstancePool.ReleaseAll();

        _registry = registry;
        _registrySubscribed = false;
        if (_refreshPolygonInstancesOnAttach)
            return;

        _registry.Changed += OnRegistryChanged;
        _registrySubscribed = true;
        SynchronizeWithRegistry();
    }

    public DockLayoutProfile? ExportLayout()
    {
        CaptureLayoutRatios();
        return _layoutRoot is null ? null : ExportNode(_layoutRoot);
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
        if (_refreshPolygonInstancesOnAttach ||
            _polygonComponentInstancePool.IsShuttingDown)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            SynchronizeWithRegistry();
        else
            Dispatcher.UIThread.Post(() =>
            {
                if (!_refreshPolygonInstancesOnAttach &&
                    !_polygonComponentInstancePool.IsShuttingDown)
                    SynchronizeWithRegistry();
            });
    }

    private void SynchronizeWithRegistry()
    {
        if (_registry is null || _polygonComponentInstancePool.IsShuttingDown)
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
        _dockLayoutGeneration++;
        StopDockMoveAnimation();
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
        ReleaseUnplacedPolygonComponentInstances();
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
            Background = ThemeBrushes.IconBoxBg,
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
                    Foreground = ThemeBrushes.Accent
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
            Background = ThemeBrushes.DragHandleBg,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 21,
                Foreground = ThemeBrushes.DragHandleGlyph,
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
                    Foreground = ThemeBrushes.Accent
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


    private void BeginDrag(string areaId, Border handle, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        _draggedAreaId = areaId;
        _targetAreaId = null;
        _dropSide = null;
        handle.Background = ThemeBrushes.DragHandleActive;
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
        var previousBounds = CaptureAreaBounds();
        var remaining = RemoveArea(_layoutRoot, draggedId);
        if (remaining is null || !ContainsArea(remaining, targetId))
            return;

        _layoutRoot = InsertArea(remaining, targetId, draggedNode, side);
        Rebuild();
        AnimateDockRelayout(previousBounds, draggedId, _dockLayoutGeneration);
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
            visual.Handle.Background = ThemeBrushes.DragHandleBg;
        }
    }

    // —— 停靠过渡动画（FLIP，遵循 Material Design 3 motion）——
    // 记录旧位置、布局重建后反向偏移再缓动归零。曲线与时长取自 M3 令牌：
    // 邻居位移用 emphasized（cubic-bezier(0.2, 0, 0, 1)），被拖拽卡片以
    // emphasized-decelerate（0.05, 0.7, 0.1, 1）落座，透明度在前 40% 时长内完成。
    // 布局代数计数保证动画可中断：二次重排会立即接管，符合 M3 的可交互性要求。

    private Dictionary<string, Rect> CaptureAreaBounds()
    {
        var bounds = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _visuals)
            bounds[pair.Key] = GetWorkspaceBounds(pair.Value.Card);
        return bounds;
    }

    private void AnimateDockRelayout(IReadOnlyDictionary<string, Rect> previousBounds, string draggedId, int generation)
    {
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            AreaGrid.LayoutUpdated -= handler;
            if (_dockLayoutGeneration != generation)
                return;
            PlayDockMoveAnimation(previousBounds, draggedId, generation);
        };
        AreaGrid.LayoutUpdated += handler;
    }

    private void PlayDockMoveAnimation(
        IReadOnlyDictionary<string, Rect> previousBounds,
        string draggedId,
        int generation)
    {
        StopDockMoveAnimation();
        if (!AnimationGate.Enabled)
            return;

        var moves = new List<(Border Card, TranslateTransform Translate, ScaleTransform? Scale, double StartX, double StartY)>();
        foreach (var pair in _visuals)
        {
            if (!previousBounds.TryGetValue(pair.Key, out var before))
                continue;

            var origin = pair.Value.Card.TranslatePoint(new Point(0, 0), AreaGrid);
            if (origin is null)
                continue;

            var offsetX = before.X - origin.Value.X;
            var offsetY = before.Y - origin.Value.Y;
            if (Math.Abs(offsetX) < 0.5 && Math.Abs(offsetY) < 0.5)
                continue;

            var translate = new TranslateTransform(offsetX, offsetY);
            ScaleTransform? scale = null;
            if (string.Equals(pair.Key, draggedId, StringComparison.OrdinalIgnoreCase))
            {
                scale = new ScaleTransform(DockDraggedStartScale, DockDraggedStartScale);
                pair.Value.Card.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                pair.Value.Card.RenderTransform = new TransformGroup { Children = { translate, scale } };
                // 拖拽结束时透明度被重置为 1，这里重新拉低，随滑动一起淡入
                pair.Value.Card.Opacity = 0.7;
            }
            else
            {
                pair.Value.Card.RenderTransform = translate;
            }

            moves.Add((pair.Value.Card, translate, scale, offsetX, offsetY));
        }

        if (moves.Count == 0)
            return;

        var frameCount = Math.Max(1, (int)Math.Ceiling(MaterialMotion.LargeTransitionMs / 16d));
        var frame = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            frame++;
            var progress = Math.Min(frame / (double)frameCount, 1d);
            if (_dockLayoutGeneration != generation || progress >= 1d)
            {
                StopDockMoveAnimation();
                foreach (var move in moves)
                {
                    move.Card.RenderTransform = null;
                    move.Card.Opacity = 1;
                }
                return;
            }

            foreach (var move in moves)
            {
                // M3：位移中的容器用 emphasized 缓动；落座的被拖拽卡片用
                // emphasized-decelerate，前段更快、结尾更缓
                var eased = move.Scale is not null
                    ? MaterialMotion.EmphasizedDecelerate(progress)
                    : MaterialMotion.Emphasized(progress);
                move.Translate.X = move.StartX * (1 - eased);
                move.Translate.Y = move.StartY * (1 - eased);
                if (move.Scale is not null)
                {
                    move.Scale.ScaleX = move.Scale.ScaleY =
                        DockDraggedStartScale + (1 - DockDraggedStartScale) * eased;

                    // M3：进入元素的不透明度在前 40% 时长内匀速完成，
                    // 避免位移结束时还残留一块半透明卡片
                    var fade = Math.Min(1d, progress / MaterialMotion.FadeEndFraction);
                    move.Card.Opacity = 0.7 + 0.3 * fade;
                }
            }
        };

        _dockMoveTimer = timer;
        timer.Start();
    }

    private void StopDockMoveAnimation()
    {
        if (_dockMoveTimer is null)
            return;

        _dockMoveTimer.Stop();
        _dockMoveTimer = null;
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
    private sealed record GroupVisual(
        GroupNode Node,
        IReadOnlyList<Control> ChildViews,
        Grid Grid);
}
