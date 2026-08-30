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
    private readonly SettingsPage _gameSettings;
    private readonly LauncherSettingsPage _launcherSettings;
    private PersonalizationWindow? _personalization;
    private int _activeTab;    // 0=游戏, 1=启动器, 2=工作区, 3=关于
    private int _previousTab;  // 切换前的标签（用于正确识别换页动画的「旧页」，动画期间新旧页同时可见，不能靠 IsVisible 猜）
    private string _searchQuery = string.Empty;   // 当前搜索词（原始输入）
    private int _lastGameCount = -1;              // 最近一次搜索的游戏设置命中数（-1 = 非搜索态）
    private int _lastLauncherCount = -1;          // 最近一次搜索的启动器设置命中数

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
        _gameSettings = new SettingsPage();
        _gameSettings.AccountManageRequested += (_, _) =>
            AccountManageRequested?.Invoke(this, EventArgs.Empty);
        _gameSettings.InstanceManageRequested += (_, _) =>
            InstanceManageRequested?.Invoke(this, EventArgs.Empty);
        _gameSettings.JavaRuntimeManageRequested += (_, _) =>
            JavaRuntimeManageRequested?.Invoke(this, EventArgs.Empty);
        LegacySettingsHost.Content = _gameSettings;
        _launcherSettings = new LauncherSettingsPage();
        LauncherSettingsHost.Content = _launcherSettings;
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
        _activeTab = section switch
        {
            SettingsSection.Personalization => 2,
            _ => 0,
        };
        ApplyTabVisuals();
    }

    public void ReloadPersonalization(string storageDirectory)
    {
        _gameSettings.ReloadMemorySettings();
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

    private void OnTabLauncherClick(object? sender, PointerPressedEventArgs e)
    {
        _previousTab = _activeTab;
        _activeTab = 1;
        ApplyTabVisuals();
    }

    private void OnTabWorkspaceClick(object? sender, PointerPressedEventArgs e)
    {
        _previousTab = _activeTab;
        _activeTab = 2;
        ApplyTabVisuals();
    }

    private void OnTabAboutClick(object? sender, PointerPressedEventArgs e)
    {
        _previousTab = _activeTab;
        _activeTab = 3;
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
        SetTabStyle(TabLauncher, _activeTab == 1);
        SetTabStyle(TabWorkspace, _activeTab == 2);
        SetTabStyle(TabAbout, _activeTab == 3);

        // 标签变化影响空态提示的可见性
        UpdateSearchEmptyState();
    }

    private Control HostFor(int tab) => tab switch
    {
        0 => LegacySettingsHost,
        1 => LauncherSettingsHost,
        2 => PersonalizationHost,
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

    // ------------------------------------------------------------------
    // 设置页搜索：跨「游戏/启动器」两个标签过滤卡片
    // ------------------------------------------------------------------

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchQuery = SettingsSearchBox.Text ?? string.Empty;
        ApplySearch();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc 一键清空搜索（经 TextChanged 触发恢复流程）
        if (e.Key == Key.Escape && SettingsSearchBox.Text?.Length > 0)
        {
            SettingsSearchBox.Text = string.Empty;
            e.Handled = true;
        }
    }

    /// <summary>执行搜索：过滤两个设置页的卡片、刷新标签计数徽章，必要时自动跳到有命中的标签。</summary>
    private void ApplySearch()
    {
        var query = _searchQuery.Trim();
        _lastGameCount = _gameSettings.ApplySearchFilter(query);
        _lastLauncherCount = _launcherSettings.ApplySearchFilter(query);
        var searching = _lastGameCount >= 0;

        TabGameSearchChip.IsVisible = searching;
        TabLauncherSearchChip.IsVisible = searching;
        if (searching)
        {
            TabGameSearchCount.Text = _lastGameCount.ToString();
            TabLauncherSearchCount.Text = _lastLauncherCount.ToString();

            // 当前标签无命中而另一标签有 → 自动跳到有结果的标签
            if (_activeTab == 0 && _lastGameCount == 0 && _lastLauncherCount > 0)
            {
                _previousTab = _activeTab;
                _activeTab = 1;
                ApplyTabVisuals();
            }
            else if (_activeTab == 1 && _lastLauncherCount == 0 && _lastGameCount > 0)
            {
                _previousTab = _activeTab;
                _activeTab = 0;
                ApplyTabVisuals();
            }
        }

        UpdateSearchEmptyState();
    }

    /// <summary>搜索态下当前可搜索标签命中数为 0 时显示空态提示（工作区/关于不参与索引，不显示）。</summary>
    private void UpdateSearchEmptyState()
    {
        var searching = _searchQuery.Trim().Length > 0;
        var count = _activeTab switch
        {
            0 => _lastGameCount,
            1 => _lastLauncherCount,
            _ => -1,
        };
        SearchEmptyState.IsVisible = searching && count == 0;
    }
}
