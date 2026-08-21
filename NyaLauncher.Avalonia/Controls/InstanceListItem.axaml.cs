using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Pages;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 实例版本列表中的单个条目，显示图标、名称与目录模式。
/// 由 DataTemplate 驱动，绑定 <see cref="Pages.VersionListItem"/> 作为 DataContext。
/// </summary>
public partial class InstanceListItem : UserControl
{
    public InstanceListItem()
    {
        InitializeComponent();
    }

    private VersionListItem? Item => DataContext as VersionListItem;

    private VersionManagerPage? FindParentPage() =>
        this.FindAncestorOfType<VersionManagerPage>();

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        FindParentPage()?.RequestRename(Item.VersionId);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        FindParentPage()?.RequestDelete(Item.VersionId);
    }
}
