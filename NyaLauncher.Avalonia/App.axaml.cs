using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        global::Avalonia.DeveloperToolsExtensions.AttachDeveloperTools(this);
#endif

        // 先解析工作区配置位置，确保启动主题与设置页写入的是同一份 config.json。
        var profileStore = new WorkspaceProfileStore();
        LauncherConfig.SetStorageDirectory(profileStore.StorageDirectory);

        // 读取用户选择的主题风格和明暗模式（主题家族与明暗模式完全解耦）
        var themeFamily = ThemeSettings.LoadThemeFamily();
        var themeMode = ThemeSettings.LoadThemeMode();

        System.Diagnostics.Debug.WriteLine($"[App] themeFamily={themeFamily}, themeMode={themeMode}");

        // 加载家族资源，并同步 FluentTheme 的明暗模式。
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
