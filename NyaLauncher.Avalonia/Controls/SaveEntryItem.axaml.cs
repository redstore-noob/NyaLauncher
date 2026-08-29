using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Core.Content;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 存档条目：显示存档信息，右键支持 导出/备份/删除 存档。
/// 操作完成后抛出 <see cref="SaveChangedEvent"/> 供页面刷新列表。
/// </summary>
public partial class SaveEntryItem : UserControl
{
    /// <summary>存档被导出/删除/备份后触发，便于页面刷新列表。</summary>
    public static readonly RoutedEvent<RoutedEventArgs> SaveChangedEvent =
        RoutedEvent.Register<SaveEntryItem, RoutedEventArgs>(
            nameof(SaveChanged),
            RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? SaveChanged
    {
        add => AddHandler(SaveChangedEvent, value);
        remove => RemoveHandler(SaveChangedEvent, value);
    }

    private string? _pendingOperationStatus;

    /// <summary>最近一次操作的状态文本，供页面读取并显示。</summary>
    public string? PendingOperationStatus => _pendingOperationStatus;

    public SaveEntryItem()
    {
        InitializeComponent();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry || !entry.SourcePath.EndsWithSeparatorDirectory())
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"导出存档 {entry.Name}",
            SuggestedFileName = $"{entry.Name}.zip",
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("ZIP 存档") { Patterns = ["*.zip"] }
            ]
        });
        if (file?.TryGetLocalPath() is not { } destination)
            return;

        var result = await GameSaveService.ExportAsync(entry.SourcePath, destination);
        _pendingOperationStatus = result is null
            ? $"导出 {entry.Name} 失败。"
            : $"{entry.Name} 已导出到 {result}。";
        RaiseSaveChanged();
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry || !entry.SourcePath.EndsWithSeparatorDirectory())
            return;

        var result = await GameSaveService.BackupAsync(entry.SourcePath);
        _pendingOperationStatus = result is null
            ? $"备份 {entry.Name} 失败。"
            : $"{entry.Name} 备份完成：{Path.GetFileName(result)}";
        RaiseSaveChanged();
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameContentEntry entry || !entry.SourcePath.EndsWithSeparatorDirectory())
            return;

        var confirmed = await ShowDeleteDialogAsync(entry);
        if (!confirmed)
        {
            _pendingOperationStatus = "已取消删除存档。";
            return;
        }

        var ok = GameSaveService.Delete(entry.SourcePath);
        _pendingOperationStatus = ok
            ? $"{entry.Name} 已删除。"
            : $"删除 {entry.Name} 失败。";
        RaiseSaveChanged();
    }

    private void RaiseSaveChanged()
    {
        RaiseEvent(new RoutedEventArgs(SaveChangedEvent));
    }

    private async Task<bool> ShowDeleteDialogAsync(GameContentEntry entry)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dialog = new Window
        {
            Title = "删除存档",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = $"确定删除存档 {entry.Name}？",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "此操作将永久删除该存档目录及其所有文件，且无法恢复。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brushes.Gray
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "取消", Padding = new Thickness(18, 8) };
        cancel.Click += (_, _) => dialog.Close(false);
        var yes = new Button
        {
            Content = "删除",
            Padding = new Thickness(18, 8),
            Background = Brushes.Red,
            Foreground = Brushes.White
        };
        yes.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(yes);
        stack.Children.Add(buttons);
        dialog.Content = stack;
        return owner is null
            ? false
            : await dialog.ShowDialog<bool>(owner);
    }
}

internal static class SavePathExtensions
{
    /// <summary>存档 SourcePath 应为目录；该扩展用于防御非目录路径。</summary>
    public static bool EndsWithSeparatorDirectory(this string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}