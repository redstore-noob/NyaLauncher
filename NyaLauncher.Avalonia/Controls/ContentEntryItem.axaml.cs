using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Core.Content;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 通用内容条目（模组/资源包/光影/存档），显示图标、名称、元数据与描述。
/// 由 DataTemplate 驱动，绑定 <see cref="Core.Content.GameContentEntry"/> 作为 DataContext。
/// </summary>
public partial class ContentEntryItem : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> ModFileChangedEvent =
        RoutedEvent.Register<ContentEntryItem, RoutedEventArgs>(
            nameof(ModFileChanged),
            RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? ModFileChanged
    {
        add => AddHandler(ModFileChangedEvent, value);
        remove => RemoveHandler(ModFileChangedEvent, value);
    }

    public ContentEntryItem()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry)
            return;
        ToggleDisableMenuItem.Header = entry.IsDisabled ? "启用该mod" : "禁用该mod";
    }

    private void OnToggleDisableClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry)
            return;
        try
        {
            if (entry.IsDisabled)
            {
                var newPath = entry.SourcePath[..^".disabled".Length];
                File.Move(entry.SourcePath, newPath);
            }
            else
            {
                File.Move(entry.SourcePath, entry.SourcePath + ".disabled");
            }
            RaiseEvent(new RoutedEventArgs(ModFileChangedEvent));
        }
        catch (Exception)
        {
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry)
            return;
        try
        {
            File.Delete(entry.SourcePath);
            RaiseEvent(new RoutedEventArgs(ModFileChangedEvent));
        }
        catch (Exception)
        {
        }
    }
}
