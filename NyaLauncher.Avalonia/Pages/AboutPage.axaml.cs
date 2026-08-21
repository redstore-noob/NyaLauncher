using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Core;

namespace NyaLauncher.Avalonia.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        LauncherText.Text = NyaLauncherInfo.FormatVersionString();
    }
}
