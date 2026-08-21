using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using NyaLauncher.Core.Content;

namespace NyaLauncher.Avalonia.Controls;

public sealed class ContentEntryItem : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> ModFileChangedEvent =
        RoutedEvent.Register<ContentEntryItem, RoutedEventArgs>(
            nameof(ModFileChanged),
            RoutingStrategies.Bubble);

    private readonly TextBlock _glyph;
    private readonly TextBlock _title;
    private readonly TextBlock _metadata;
    private readonly TextBlock _description;
    private readonly Button _toggle;

    public ContentEntryItem()
    {
        _glyph = new TextBlock
        {
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _title = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold };
        _metadata = new TextBlock { FontSize = 10, Foreground = Brush.Parse("#A5AEC7") };
        _description = new TextBlock
        {
            FontSize = 10,
            Foreground = Brush.Parse("#7E88A4"),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2
        };
        _toggle = new Button
        {
            Padding = new Thickness(12, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        _toggle.Click += OnToggleClick;

        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(_title);
        text.Children.Add(_metadata);
        text.Children.Add(_description);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("42,*,Auto"),
            ColumnSpacing = 10
        };
        grid.Children.Add(_glyph);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(_toggle, 2);
        grid.Children.Add(_toggle);

        Content = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(9),
            Background = Brush.Parse("#20283D"),
            BorderBrush = Brush.Parse("#38435F"),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        DataContextChanged += (_, _) => Refresh();
    }

    public event EventHandler<RoutedEventArgs> ModFileChanged
    {
        add => AddHandler(ModFileChangedEvent, value);
        remove => RemoveHandler(ModFileChangedEvent, value);
    }

    private void Refresh()
    {
        if (DataContext is not GameContentEntry entry)
            return;

        _glyph.Text = string.IsNullOrWhiteSpace(entry.FallbackGlyph) ? "◇" : entry.FallbackGlyph;
        _title.Text = entry.Name;
        _metadata.Text = entry.MetadataLine;
        _description.Text = entry.Description;
        var canToggle = entry.SourcePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                        entry.SourcePath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
        _toggle.IsVisible = canToggle;
        _toggle.Content = entry.IsDisabled ? "启用" : "禁用";
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry)
            return;

        try
        {
            var target = entry.IsDisabled
                ? entry.SourcePath[..^".disabled".Length]
                : $"{entry.SourcePath}.disabled";
            File.Move(entry.SourcePath, target);
            DataContext = entry with { SourcePath = target, IsDisabled = !entry.IsDisabled };
            RaiseEvent(new RoutedEventArgs(ModFileChangedEvent));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _description.Text = $"切换失败：{exception.Message}";
        }
    }
}
