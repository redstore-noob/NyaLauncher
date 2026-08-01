using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Pages;

namespace NyaLauncher.Avalonia;

public partial class MainWindow : Window
{
    private readonly WorkspaceProfileStore _profileStore = new();
    private readonly LaunchPage _launchPage;
    private readonly DownloadPage _downloadPage;
    private readonly SettingsHubPage _settingsPage;
    private ComponentLibraryWindow? _componentLibraryWindow;

    /// <summary>
    /// Shared extension point for built-in modules and future plugins.
    /// A plugin can register an area at runtime and the workspace updates itself.
    /// </summary>
    public FeatureAreaRegistry FeatureAreas { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        FeatureAreas.Register(new BuiltInFeatureAreaProvider(NavigateFromAction));
        var profile = _profileStore.Load();
        FeatureAreas.SetGlobalComponentScale(profile.GlobalComponentScale);
        FeatureAreas.SynchronizeUserAreas(profile.CustomAreas);
        FeatureAreas.ApplyPersonalization(profile.Areas);

        Workspace.UseRegistry(FeatureAreas);
        Workspace.ImportLayout(
            profile.Layout,
            profile.Sidebars,
            profile.ComponentPlacements,
            profile.GlobalComponentScale);
        Workspace.LayoutChanged += (_, _) => SaveWorkspaceProfile();
        Workspace.ComponentDropRequested += OnComponentDropRequested;

        _launchPage = new LaunchPage();
        _downloadPage = new DownloadPage();
        _settingsPage = new SettingsHubPage(
            FeatureAreas,
            _profileStore.StorageDirectory);
        _settingsPage.PersonalizationSaved += OnPersonalizationSaved;

        AddHandler(
            KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
                UpdateWindowStateIcons();
        };
        UpdateWindowStateIcons();
        Closing += (_, _) => SaveWorkspaceProfile();
    }

    private void NavigateFromAction(string actionId)
    {
        switch (actionId)
        {
            case "select-instance":
            case "account":
            case "launch":
            case "instances":
                ShowPage(_launchPage, "启动游戏");
                break;

            case "downloads":
            case "tasks":
                ShowPage(_downloadPage, "资源下载");
                break;

            case "settings":
            case "runtime":
            case "plugins":
                ShowSettings(SettingsSection.Launcher);
                break;

            default:
                ShowStatus($"尚未注册页面：{actionId}");
                break;
        }
    }

    private void ShowPage(Control page, string title)
    {
        PageHost.Content = page;
        CurrentPageTitle.Text = title;
        Workspace.IsVisible = false;
        PageSurface.IsVisible = true;
        HeaderStatusText.Text = title;
        ShowStatus($"已进入：{title}");
    }

    private void ShowSettings(SettingsSection section)
    {
        _settingsPage.SelectSection(section);
        ShowPage(_settingsPage, "设置");
    }

    private void ShowWorkspace()
    {
        PageSurface.IsVisible = false;
        Workspace.IsVisible = true;
        PageHost.Content = null;
        CurrentPageTitle.Text = string.Empty;
        HeaderStatusText.Text = "工作区已就绪";
        ShowStatus("已返回工作区");
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    private void OnBackToWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        ShowWorkspace();
    }

    private void OnComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (_componentLibraryWindow is not null)
        {
            _componentLibraryWindow.Activate();
            return;
        }

        _componentLibraryWindow = new ComponentLibraryWindow(FeatureAreas);
        _componentLibraryWindow.ComponentRemovalRequested += OnComponentRemovalRequested;
        _componentLibraryWindow.Closed += (_, _) => _componentLibraryWindow = null;
        _componentLibraryWindow.Show(this);
    }

    private void OnComponentRemovalRequested(
        object? sender,
        ComponentRemovalRequestedEventArgs e)
    {
        if (!FeatureAreas.RemoveComponent(e.ComponentId, e.SourceAreaId))
            return;

        Workspace.RemoveComponentPlacement(e.ComponentId, e.SourceAreaId);
        SaveWorkspaceProfile();
        var component = FeatureAreas.AvailableActions.FirstOrDefault(action =>
            string.Equals(action.Id, e.ComponentId, StringComparison.OrdinalIgnoreCase));
        ShowStatus($"组件“{component?.Title ?? e.ComponentId}”已从功能区移除并保存");
    }

    private void OnComponentDropRequested(
        object? sender,
        Controls.ComponentDropRequestedEventArgs e)
    {
        var membershipChanged = FeatureAreas.PlaceComponent(
            e.ComponentId,
            e.TargetAreaId,
            e.SourceAreaId);
        if (!membershipChanged && string.IsNullOrWhiteSpace(e.SourceAreaId))
        {
            return;
        }

        var placementChanged = Workspace.SetComponentPlacement(
            e.ComponentId,
            e.TargetAreaId,
            e.SourceAreaId,
            e.RelativeX,
            e.RelativeY);
        if (!membershipChanged && !placementChanged)
            return;

        SaveWorkspaceProfile();
        var component = FeatureAreas.AvailableActions.FirstOrDefault(action =>
            string.Equals(action.Id, e.ComponentId, StringComparison.OrdinalIgnoreCase));
        var target = FeatureAreas.Areas.FirstOrDefault(area =>
            string.Equals(area.Id, e.TargetAreaId, StringComparison.OrdinalIgnoreCase));
        var operation = string.IsNullOrWhiteSpace(e.SourceAreaId)
            ? "添加"
            : string.Equals(e.SourceAreaId, e.TargetAreaId, StringComparison.OrdinalIgnoreCase)
                ? "重新摆放"
                : "移动";
        ShowStatus($"组件“{component?.Title ?? e.ComponentId}”已{operation}至“{target?.Title ?? e.TargetAreaId}”并保存");
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        ShowSettings(SettingsSection.Launcher);
        e.Handled = true;
    }

    private void OnPersonalizationSaved(
        object? sender,
        PersonalizationResult result)
    {
        var profile = result.Profile;

        FeatureAreas.SetGlobalComponentScale(profile.GlobalComponentScale);
        FeatureAreas.SynchronizeUserAreas(profile.CustomAreas);
        FeatureAreas.ApplyPersonalization(profile.Areas);
        Workspace.SetGlobalComponentScale(profile.GlobalComponentScale);
        profile.GlobalComponentScale = Workspace.GlobalComponentScale;
        profile.Layout = Workspace.ExportLayout();
        profile.Sidebars = [.. Workspace.ExportSidebars()];
        profile.ComponentPlacements = [.. Workspace.ExportComponentPlacements()];

        try
        {
            var directoryChanged = !WorkspaceProfileStore.PathsEqual(
                _profileStore.StorageDirectory,
                result.StorageDirectory);
            if (directoryChanged)
                _profileStore.ChangeStorageDirectory(result.StorageDirectory, profile);
            else
                _profileStore.Save(profile);

            _settingsPage.ReloadPersonalization(_profileStore.StorageDirectory);
            ShowStatus(directoryChanged
                ? $"个性化配置已迁移至：{_profileStore.StorageDirectory}"
                : "个性化配置已保存并应用");
        }
        catch (Exception exception)
        {
            ShowStatus($"配置已应用，但保存失败：{exception.Message}");
        }
    }

    private void SaveWorkspaceProfile()
    {
        try
        {
            var profile = FeatureAreas.CreateCurrentProfile();
            profile.GlobalComponentScale = Workspace.GlobalComponentScale;
            profile.Layout = Workspace.ExportLayout();
            profile.Sidebars = [.. Workspace.ExportSidebars()];
            profile.ComponentPlacements = [.. Workspace.ExportComponentPlacements()];
            _profileStore.Save(profile);
        }
        catch (Exception exception)
        {
            ShowStatus($"工作区配置保存失败：{exception.Message}");
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximized();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState != WindowState.Normal ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void OnResizeWestPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.West, e);

    private void OnResizeEastPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.East, e);

    private void OnResizeNorthPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.North, e);

    private void OnResizeSouthPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.South, e);

    private void OnResizeNorthWestPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.NorthWest, e);

    private void OnResizeNorthEastPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.NorthEast, e);

    private void OnResizeSouthWestPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.SouthWest, e);

    private void OnResizeSouthEastPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.SouthEast, e);

    private void ToggleMaximized()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateWindowStateIcons()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WorkspaceMaximizeIcon.IsVisible = !isMaximized;
        WorkspaceRestoreIcon.IsVisible = isMaximized;
        PageMaximizeIcon.IsVisible = !isMaximized;
        PageRestoreIcon.IsVisible = isMaximized;
    }
}
