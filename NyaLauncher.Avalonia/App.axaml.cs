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

        // 1. 读取用户选择的主题风格和明暗模式（主题家族与明暗模式完全解耦；
        //    「跟随系统」模式在此解析为操作系统当前的具体明暗）
        var themeFamily = ThemeSettings.LoadThemeFamily();
        var themeMode = ThemeSettings.LoadThemeMode();

        System.Diagnostics.Debug.WriteLine($"[App] themeFamily={themeFamily}, themeMode={themeMode}");

        // 2. 通过唯一入口设置明暗、资源与插件主题快照。必须保留原始
        //    "System" 偏好，ThemeManager 才会安装系统主题变化监听。
        System.Diagnostics.Debug.WriteLine($"[App] Applying family={themeFamily}, mode={themeMode}");
        ThemeManager.ApplyTheme(themeFamily, themeMode);
        System.Diagnostics.Debug.WriteLine($"[App] Theme applied");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 窗口创建前再次应用主题：确保 Material 控件（Slider/CheckBox 等）在首次渲染
            // 时 CurrentTheme 已就绪，避免显示为默认灰色禁用态。
            // （Initialize 中已调用一次，此处幂等重复，保证时序）
            try
            {
                ThemeManager.ApplyTheme(
                    ThemeSettings.LoadThemeFamily(),
                    ThemeSettings.LoadThemeMode());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] 二次应用主题失败：{ex}");
            }

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
