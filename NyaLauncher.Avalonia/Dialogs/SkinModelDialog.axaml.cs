using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Dialogs;

public partial class SkinModelDialog : Window
{
    public SkinModelDialog()
    {
        InitializeComponent();
    }

    private void OnClassicClick(object? sender, RoutedEventArgs e) =>
        Close(MinecraftSkinModel.Classic);

    private void OnSlimClick(object? sender, RoutedEventArgs e) =>
        Close(MinecraftSkinModel.Slim);

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Close(null);
}
