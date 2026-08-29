using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Windows;

namespace NyaLauncher.Avalonia.Pages;

public enum SettingsSection
{
    Launcher,
    Personalization
}

public partial class SettingsHubPage : UserControl
{
    private readonly SettingsPage _launcherSettings;
    private PersonalizationWindow? _personalization;
    private int _activeTab;    // 0=游戏, 1=工作区, 2=关于
    private int _previousTab;  // 切换前的标签（用于正确识别换页动画的「旧页」，动画期间新旧页同时可见，不能靠 IsVisible 猜）

    public event EventHandler<PersonalizationResult>? PersonalizationSaved;

    /// <summary>转发自 <see cref="SettingsPage.AccountManageRequested"/>，供主窗口跳转到账户管理页面。</summary>
    public event EventHandler? AccountManageRequested;

    /// <summary>转发自 <see cref="SettingsPage.InstanceManageRequested"/>，供主窗口跳转到实例管理页面。</summary>
    public event EventHandler? InstanceManageRequested;

    /// <summary>转发自 <see cref="SettingsPage.JavaRuntimeManageRequested"/>，供主窗口跳转到下载中心的 Java 标签页。</summary>
    public event EventHandler? JavaRuntimeManageRequested;

    public SettingsHubPage()
    {
        InitializeComponent();
        _launcherSettings = new SettingsPage();
        _launcherSettings.AccountManageRequested += (_, _) =>
            AccountManageRequested?.Invoke(this, EventArgs.Empty);
        _launcherSettings.InstanceManageRequested += (_, _) =>
            InstanceManageRequested?.Invoke(this, EventArgs.Empty);
        _launcherSettings.JavaRuntimeManageRequested += (_, _) =>
            JavaRuntimeManageRequested?.Invoke(this, EventArgs.Empty);
        LegacySettingsHost.Content = _launcherSettings;
        AboutHost.Content = new AboutPage();
        ApplyTabVisuals();
    }

    public SettingsHubPage(
        FeatureAreaRegistry registry,
        string storageDirectory) : this()
    {
        _personalization = new PersonalizationWindow(registry, storageDirectory);
        _personalization.Saved += (_, result) =>
            PersonalizationSaved?.Invoke(this, result);
        PersonalizationHost.Content = _personalization;
    }

    public void SelectSection(SettingsSection section)
    {
        _previousTab = _activeTab;
        _activeTab = section == SettingsSection.Personalization ? 1 : 0;
        ApplyTabVisuals();
    }

    public void ReloadPersonalization(string storageDirectory)
    {
        _launcherSettings.ReloadMemorySettings();
        _personalization?.Reload(storageDirectory);
    }

    // ------------------------------------------------------------------
    // 标签切换
    // ------------------------------------------------------------------

    private void OnTabGameClick(object? sender, PointerPressedEventArgs e)
    {
        _previousTab = _activeTab;
        _activeTab = 0;
        ApplyTabVisuals();
    }

    private void OnTabWorkspaceClick(object? sender, PointerPressedEventArgs e)
    {
        _previousTab = _activeTab;
        _activeTab = 1;
        ApplyTabVisuals();
    }

    private void OnTabAboutClick(object? sender, PointerPressedEventArgs e)
    {
        _previousTab = _activeTab;
        _activeTab = 2;
        ApplyTabVisuals();
    }

    private void ApplyTabVisuals()
    {
        // 旧页 = 切换前的标签页（动画期间新旧页同时可见，不能靠 IsVisible 推断），新页 = 当前标签页
        var oldHost = HostFor(_previousTab);
        var newHost = HostFor(_activeTab);

        // 垂直换页动画：新页从下方滑入、旧页向上划走（逻辑在 Animations 模块 SwapTransition）
        SwapTransition.SwapVertical(newHost, oldHost);
        _previousTab = _activeTab;

        // 更新标签栏视觉状态
        SetTabStyle(TabGame, _activeTab == 0);
        SetTabStyle(TabWorkspace, _activeTab == 1);
        SetTabStyle(TabAbout, _activeTab == 2);
    }

    private Control HostFor(int tab) => tab switch
    {
        0 => LegacySettingsHost,
        1 => PersonalizationHost,
        _ => AboutHost,
    };

    private void SetTabStyle(Border tab, bool isActive)
    {
        tab.Background = isActive
            ? FindBrush("SurfaceBgBrush")
            : Brushes.Transparent;

        if (tab.Child is StackPanel sp && sp.Children.Count > 0 &&
            sp.Children[0] is TextBlock title)
        {
            title.Foreground = isActive
                ? FindBrush("PrimaryTextBrush")
                : FindBrush("SubtextTextBrush");
        }
    }

    private static IBrush FindBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, null, out var value) == true
            && value is IBrush brush)
            return brush;
        return Brushes.Gray;
    }
}
