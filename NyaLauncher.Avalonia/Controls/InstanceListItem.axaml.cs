using Avalonia.Controls;
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
}
