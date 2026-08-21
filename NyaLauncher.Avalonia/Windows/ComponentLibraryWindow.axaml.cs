using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Windows;

public partial class ComponentLibraryWindow : Window
{
    private static readonly IBrush Muted = ThemePolygonHelper.Muted;
    private FeatureAreaRegistry? _registry;
    private bool _isClosed;

    public event System.EventHandler<ComponentRemovalRequestedEventArgs>? ComponentRemovalRequested;

    public ComponentLibraryWindow()
    {
        InitializeComponent();
    }

    public ComponentLibraryWindow(FeatureAreaRegistry registry) : this()
    {
        _registry = registry;
        _registry.Changed += OnRegistryChanged;
        Closed += (_, _) =>
        {
            _isClosed = true;
            _registry.Changed -= OnRegistryChanged;
        };
        WireRemovalDropTarget();
        BuildComponentList();
    }

    private void OnRegistryChanged(object? sender, System.EventArgs e)
    {
        if (_isClosed)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            BuildComponentList();
        else
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isClosed)
                    BuildComponentList();
            });
    }

    private void BuildComponentList()
    {
        ComponentList.Children.Clear();

        foreach (var component in _registry?.AvailableActions ?? [])
        {
            if (component.PolygonComponent is not null)
            {
                var polygonCard = CreatePolygonComponentCard(component);
                ComponentDragSource.Attach(polygonCard, component.Id, sourceAreaId: null);
                DragDrop.SetAllowDrop(polygonCard, true);
                ComponentList.Children.Add(polygonCard);
                continue;
            }

            var card = new Border
            {
                Padding = new Thickness(13, 11),
                Background = ThemePolygonHelper.CardBackground,
                BorderBrush = ThemePolygonHelper.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var icon = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(11),
                Background = ThemePolygonHelper.IconBoxBg,
                Child = new TextBlock
                {
                    Text = component.Glyph,
                    FontSize = 17,
                    Foreground = ThemePolygonHelper.Muted,
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
                        Text = component.Title,
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ThemePolygonHelper.Muted
                    },
                    new TextBlock
                    {
                        Text = component.Description,
                        FontSize = 10,
                        Foreground = Muted,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
            Grid.SetColumn(copy, 1);

            var dragGlyph = new TextBlock
            {
                Text = "⠿",
                FontSize = 20,
                Foreground = ThemePolygonHelper.DragGlyph,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dragGlyph, 2);

            row.Children.Add(icon);
            row.Children.Add(copy);
            row.Children.Add(dragGlyph);
            card.Child = row;

            ToolTip.SetTip(
                card,
                $"拖动“{component.Title}”到目标功能区");
            ComponentDragSource.Attach(card, component.Id, sourceAreaId: null);
            // The native drag target is resolved from the control directly under
            // the pointer. Mark every component card as a valid target as well so
            // an item dragged back from the workspace can be dropped anywhere in
            // the library, including on top of another component card.
            DragDrop.SetAllowDrop(card, true);
            ComponentList.Children.Add(card);
        }
    }

    private static Border CreatePolygonComponentCard(FeatureAreaAction component)
    {
        var registration = component.PolygonComponent!;
        var preview = new PolygonComponentView(
            registration,
            instance: null,
            visualState: PolygonComponentVisualState.LibraryPreview,
            interactive: false);

        var previewHost = new Viewbox
        {
            MaxWidth = 340,
            MaxHeight = 190,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Child = preview
        };

        var title = new TextBlock
        {
            Text = component.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemePolygonHelper.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var description = new TextBlock
        {
            Text = component.Description,
            FontSize = 10,
            Foreground = Muted,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var copy = new StackPanel
        {
            Spacing = 3,
            Children = { title, description }
        };

        var dragGlyph = new TextBlock
        {
            Text = "⠿",
            FontSize = 20,
            Foreground = ThemePolygonHelper.DragGlyph,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dragGlyph, 1);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { copy, dragGlyph }
        };

        var card = new Border
        {
            Padding = new Thickness(13, 11),
            Background = ThemePolygonHelper.CardBackground,
            BorderBrush = ThemePolygonHelper.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { header, previewHost }
            }
        };
        ToolTip.SetTip(card, $"拖动“{component.Title}”到目标功能区");
        return card;
    }

    private void WireRemovalDropTarget()
    {
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(LibraryRoot, true);
        DragDrop.SetAllowDrop(ComponentList, true);

        LibraryRoot.AddHandler(
            DragDrop.DragEnterEvent,
            (_, args) => UpdateRemovalDrop(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        LibraryRoot.AddHandler(
            DragDrop.DragOverEvent,
            (_, args) => UpdateRemovalDrop(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        LibraryRoot.AddHandler(
            DragDrop.DragLeaveEvent,
            (_, _) => RemoveDropHint.IsVisible = false,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        LibraryRoot.AddHandler(
            DragDrop.DropEvent,
            (_, args) =>
        {
            RemoveDropHint.IsVisible = false;
            if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
                payload is null ||
                payload.IsFromLibrary ||
                string.IsNullOrWhiteSpace(payload.SourceAreaId))
            {
                args.DragEffects = DragDropEffects.None;
                return;
            }

            args.DragEffects = DragDropEffects.Move;
            args.Handled = true;
            ComponentRemovalRequested?.Invoke(
                this,
                new ComponentRemovalRequestedEventArgs(
                    payload.ComponentId,
                    payload.SourceAreaId));
        },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void UpdateRemovalDrop(DragEventArgs args)
    {
        if (!ComponentDragPayload.TryParse(args.DataTransfer, out var payload) ||
            payload is null ||
            payload.IsFromLibrary)
        {
            RemoveDropHint.IsVisible = false;
            args.DragEffects = DragDropEffects.None;
            return;
        }

        RemoveDropHint.IsVisible = true;
        args.DragEffects = DragDropEffects.Move;
        args.Handled = true;
    }
}

public sealed record ComponentRemovalRequestedEventArgs(
    string ComponentId,
    string SourceAreaId);
