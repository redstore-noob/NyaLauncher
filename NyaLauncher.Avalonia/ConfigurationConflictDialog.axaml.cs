using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia;

public partial class ConfigurationConflictDialog : Window
{
    public ConfigurationConflictDialog()
    {
        InitializeComponent();
    }

    public ConfigurationConflictDialog(
        string previousDirectory,
        StorageDirectoryInspection target) : this()
    {
        PreviousDirectoryText.Text = previousDirectory;
        TargetDirectoryText.Text = target.Directory;
        ExistingFilesText.Text =
            $"目标目录已有：{string.Join("、", target.ExistingFileNames)}";
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) =>
        Close(ConfigurationConflictChoice.DeletePrevious);

    private void OnBackupClick(object? sender, RoutedEventArgs e) =>
        Close(ConfigurationConflictChoice.BackupPrevious);

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Close(ConfigurationConflictChoice.Cancel);
}

public enum ConfigurationConflictChoice
{
    Cancel,
    DeletePrevious,
    BackupPrevious
}
