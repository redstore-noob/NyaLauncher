using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Avalonia.Framework;

using NyaLauncher.Avalonia.Animations.Helpers;

namespace NyaLauncher.Avalonia.Dialogs;

public partial class SkinModelDialog : Window
{
    public SkinModelDialog()
    {
        InitializeComponent();
    }

    private void OnClassicClick(object? sender, RoutedEventArgs e) =>
        OverlayEffects.PopOut(this, () => Close(MinecraftSkinModel.Classic));

    private void OnSlimClick(object? sender, RoutedEventArgs e) =>
        OverlayEffects.PopOut(this, () => Close(MinecraftSkinModel.Slim));

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        OverlayEffects.PopOut(this, () => Close(null));
}
