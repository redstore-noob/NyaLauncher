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
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Sidebar construction, reveal animation, edge drag/drop, and restored-area
/// resize/collapse behavior for the docking workspace.
/// </summary>
public partial class DockWorkspace
{
    private const double SidebarSeamHitSize = 16;
    private const double SidebarAnimationDurationMilliseconds = 180;
    private const double CollapseWidthThreshold = 230;
    private const double CollapseHeightThreshold = 180;
    private const double CollapsedRailSize = 42;

    private readonly Dictionary<DockEdge, SidebarState> _sidebars = [];
    private readonly List<Control> _sidebarHosts = [];

    private DockEdge? _draggedSidebarEdge;
    private DockEdge? _sidebarDropEdge;
    private RestoredResizeSession? _restoredResizeSession;

    private ColumnDefinition LeftSidebarColumn => WorkspaceRoot.ColumnDefinitions[0];
    private ColumnDefinition RightSidebarColumn => WorkspaceRoot.ColumnDefinitions[2];
    private RowDefinition TopSidebarRow => WorkspaceRoot.RowDefinitions[0];
    private RowDefinition BottomSidebarRow => WorkspaceRoot.RowDefinitions[2];

    public IReadOnlyList<SidebarProfile> ExportSidebars()
    {
        return _sidebars.Values.Select(sidebar => new SidebarProfile
        {
            AreaId = sidebar.Definition.Id,
            Edge = sidebar.Edge,
            RevealSize = sidebar.RevealSize
        }).ToArray();
    }

    private void BuildSidebarVisuals()
    {
        foreach (var sidebar in _sidebars.Values)
        {
            var host = new Border
            {
                Background = CardBackground,
                BorderBrush = ThemeBrushes.SidebarBorder,
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
                    host.BorderBrush = ThemeBrushes.SidebarBorder;
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
            host.BorderBrush = ThemeBrushes.SidebarBorder;
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
            Background = ThemeBrushes.IconBoxBg,
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
        handle.Background = ThemeBrushes.DragHandleActive;
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
