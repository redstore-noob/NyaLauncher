using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 组件库内容视图：列出所有可用组件卡片，支持拖出到功能区。
/// 可被右侧抽屉（MainWindow）或独立窗口承载。
/// </summary>
public partial class ComponentLibraryView : UserControl
{
    private static readonly IBrush Muted = ThemePolygonHelper.Muted;
    private FeatureAreaRegistry? _registry;

    /// <summary>
    /// 任一组件卡即将启动拖拽时触发。宿主可用于提前播放收起动画
    /// （在 DoDragDropAsync 阻塞前发出，Transitions 由渲染线程继续播放）。
    /// </summary>
    public event EventHandler? DragStarting;

    public ComponentLibraryView()
    {
        InitializeComponent();
    }

    public void AttachRegistry(FeatureAreaRegistry registry)
    {
        if (ReferenceEquals(_registry, registry))
            return;
        if (_registry is not null)
            _registry.Changed -= OnRegistryChanged;
        _registry = registry;
        _registry.Changed += OnRegistryChanged;
        BuildComponentList();
    }

    public void DetachRegistry()
    {
        if (_registry is not null)
            _registry.Changed -= OnRegistryChanged;
        _registry = null;
    }

    /// <summary>宿主可见性变化后调用：不可见时预隐藏，可见时播错峰入场。</summary>
    public void PlayStagger()
    {
        if (!AnimationGate.Enabled)
            return;
        if (!IsEffectivelyVisible)
        {
            foreach (var child in ComponentList.Children)
                ((Control)child).Opacity = 0;
            return;
        }
        _ = AnimationHelper.StaggerInAsync(ComponentList.Children.OfType<Control>());
    }

    /// <summary>沿视觉树检查实际可见性（父级 IsVisible=false 时子级依然为 true）。</summary>
    private bool IsEffectivelyVisible
    {
        get
        {
            for (var v = (Visual?)this; v is not null; v = v.GetVisualParent())
            {
                if (v is Control c && !c.IsVisible)
                    return false;
            }
            return true;
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            BuildComponentList();
        else
            Dispatcher.UIThread.Post(BuildComponentList);
    }

    private void BuildComponentList()
    {
        ComponentList.Children.Clear();

        foreach (var component in _registry?.AvailableActions ?? [])
        {
            if (component.PolygonComponent is not null)
            {
                var polygonCard = CreatePolygonComponentCard(component);
                ComponentDragSource.Attach(
                    polygonCard,
                    component.Id,
                    sourceAreaId: null,
                    onDragStarting: () => DragStarting?.Invoke(this, EventArgs.Empty));
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
                // 字形渲染统一走 FeatureIconFactory："material:Kind" 显示为 Material 图标，其余回退文字
                Child = FeatureIconFactory.CreateGlyph(component.Glyph, 17, ThemePolygonHelper.Muted)
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

            ToolTip.SetTip(card, $"拖动“{component.Title}”到目标功能区");
            ComponentDragSource.Attach(
                card,
                component.Id,
                sourceAreaId: null,
                onDragStarting: () => DragStarting?.Invoke(this, EventArgs.Empty));
            // 拖回的组件可放到任意卡片上（包括其它卡片），保证整个列表都是合法落点
            DragDrop.SetAllowDrop(card, true);
            ComponentList.Children.Add(card);
        }

        PlayStagger();
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
}
