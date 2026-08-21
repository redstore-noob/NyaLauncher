using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Pages;

namespace NyaLauncher.Avalonia.Controls;

public sealed class InstanceListItem : UserControl
{
    private readonly TextBlock _glyph;
    private readonly TextBlock _name;
    private readonly TextBlock _mode;

    public InstanceListItem()
    {
        _glyph = new TextBlock
        {
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _name = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _mode = new TextBlock
        {
            FontSize = 9,
            Foreground = Brush.Parse("#7E88A4"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(_name);
        labels.Children.Add(_mode);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*") };
        grid.Children.Add(_glyph);
        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);
        Content = new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Child = grid
        };

        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) => Invoke(page => page.RequestRename(Current.VersionId));
        var delete = new MenuItem { Header = "删除" };
        delete.Click += (_, _) => Invoke(page => page.RequestDelete(Current.VersionId));
        ContextMenu = new ContextMenu { ItemsSource = new[] { rename, delete } };
        DataContextChanged += (_, _) => Refresh();
    }

    private VersionListItem Current => DataContext as VersionListItem ??
        new VersionListItem(string.Empty, string.Empty, string.Empty, null, "◇");

    private void Refresh()
    {
        var item = Current;
        _glyph.Text = string.IsNullOrWhiteSpace(item.IconGlyph) ? "◇" : item.IconGlyph;
        _name.Text = string.IsNullOrWhiteSpace(item.Name) ? item.VersionId : item.Name;
        _mode.Text = item.DirectoryMode;
    }

    private void Invoke(Action<VersionManagerPage> action)
    {
        var page = this.GetVisualAncestors().OfType<VersionManagerPage>().FirstOrDefault();
        if (page is not null && !string.IsNullOrWhiteSpace(Current.VersionId))
            action(page);
    }
}
