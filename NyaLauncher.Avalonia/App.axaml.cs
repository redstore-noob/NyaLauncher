using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        global::Avalonia.DeveloperToolsExtensions.AttachDeveloperTools(this);
#endif
        
        // 1. 读取用户选择的主题风格和明暗模式（主题家族与明暗模式完全解耦）
        var themeFamily = ThemeSettings.LoadThemeFamily();
        var themeMode = ThemeSettings.LoadThemeMode();

        System.Diagnostics.Debug.WriteLine($"[App] themeFamily={themeFamily}, themeMode={themeMode}");

        // 2. 设置 FluentTheme 的明暗模式（影响 ComboBox、TextBox 等标准控件）
        System.Diagnostics.Debug.WriteLine($"[App] Setting RequestedThemeVariant={themeMode}");
        RequestedThemeVariant = themeMode == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        // 3. 从家族资源文件中加载当前明暗变体到 Application.Current.Resources
        System.Diagnostics.Debug.WriteLine($"[App] Applying family={themeFamily}, mode={themeMode}");
        StyleAlter.ApplyTheme(themeFamily, themeMode);
        System.Diagnostics.Debug.WriteLine($"[App] Theme applied");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
