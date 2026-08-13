using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia;

public partial class MainWindow : Window
{
    private readonly WorkspaceProfileStore _profileStore = new();
    private readonly MinecraftProfileService _minecraftProfileService = new();
    private readonly GameLaunchService _gameLaunchService;
    private readonly GameDownloadService _gameDownloadService;
    private readonly LaunchPage _launchPage;
    private readonly DownloadPage _downloadPage;
    private readonly VersionManagerPage _versionManagerPage;
    private readonly SettingsHubPage _settingsPage;
    private ComponentLibraryWindow? _componentLibraryWindow;
    private TaskDetailsWindow? _taskDetailsWindow;
    private bool _suppressWorkspaceSave;
    private bool _storageChangeInProgress;
    private bool _polygonShutdownInProgress;
    private bool _polygonShutdownComplete;

    /// <summary>
    /// Shared registry for the built-in feature areas and configurable components.
    /// </summary>
    public FeatureAreaRegistry FeatureAreas { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        // 让 config.json 与 workspace.json 存放在同一目录（含自定义存储目录）。
        LauncherConfig.SetStorageDirectory(_profileStore.StorageDirectory);
        _gameLaunchService = new GameLaunchService();
        _gameLaunchService.Changed += OnGameLaunchChanged;
        _gameDownloadService = new GameDownloadService();
        _gameDownloadService.Changed += OnGameDownloadChanged;

        FeatureAreas.Register(new BuiltInFeatureAreaProvider(
            NavigateFromAction,
            _minecraftProfileService,
            _gameLaunchService,
            EditPlayerAppearanceAsync));
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
        Workspace.LayoutChanged += (_, _) =>
        {
            if (!_suppressWorkspaceSave && !_storageChangeInProgress)
                SaveWorkspaceProfile();
        };
        Workspace.ComponentDropRequested += OnComponentDropRequested;

        _launchPage = new LaunchPage(_gameLaunchService);
        _downloadPage = new DownloadPage(_gameDownloadService);
        _versionManagerPage = new VersionManagerPage();
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
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_storageChangeInProgress)
        {
            e.Cancel = true;
            ShowStatus("配置目录正在迁移，请等待完成后再关闭启动器。");
            return;
        }
        if (_polygonShutdownComplete)
            return;

        e.Cancel = true;
        if (_polygonShutdownInProgress)
            return;

        _polygonShutdownInProgress = true;
        SaveWorkspaceProfile();
        try
        {
            await Workspace.ShutdownPolygonComponentsAsync();
        }
        finally
        {
            _polygonShutdownComplete = true;
            _polygonShutdownInProgress = false;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Close();
                }
                catch (InvalidOperationException)
                {
                    // The platform may have completed an operating-system shutdown
                    // while asynchronous component cleanup was in progress.
                }
            });
        }
    }

    private void NavigateFromAction(string actionId)
    {
        switch (actionId)
        {
            case "select-instance":
            case "account":
            case "launch":
                ShowPage(_launchPage, "启动游戏");
                break;

            case "instances":
            case "version-manager":
                _versionManagerPage.Activate();
                ShowPage(_versionManagerPage, "版本管理");
                break;

            case "account-login":
                ShowPage(_launchPage, "账号登录");
                _launchPage.ShowAccountLogin();
                break;

            case "downloads":
            case "tasks":
                ShowPage(_downloadPage, "资源下载");
                break;

            case "settings":
            case "runtime":
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

    private void OnGameLaunchChanged(GameLaunchSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameLaunchChanged(snapshot));
            return;
        }

        UpdateTaskActivityIndicator();

        if (snapshot.Phase is GameLaunchPhase.Preparing or GameLaunchPhase.Running)
            HeaderStatusText.Text = snapshot.Title;
        ShowStatus($"{snapshot.Title}：{snapshot.Message}");
    }

    private void OnGameDownloadChanged(GameDownloadSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameDownloadChanged(snapshot));
            return;
        }

        UpdateTaskActivityIndicator();
        ShowStatus($"{snapshot.StageName}：{snapshot.Detail}");
    }

    private void UpdateTaskActivityIndicator()
    {
        var download = _gameDownloadService.Current;
        if (download.IsActive)
        {
            TaskActivityButton.IsVisible = true;
            TaskActivityButton.Background = Brush.Parse("#2E8C78");
            TaskActivityButton.BorderBrush = Brush.Parse("#75D9BE");
            TaskActivityGlyph.Text = "↓";
            TaskActivityProgress.IsVisible = true;
            TaskActivityProgress.IsIndeterminate = download.TotalBytes <= 0;
            TaskActivityProgress.Value = download.Percentage;
            ToolTip.SetTip(
                TaskActivityButton,
                $"正在下载 Minecraft {download.VersionId}\n" +
                $"{download.StageName} · {download.Percentage:0.0}%\n点击查看下载详情");
            return;
        }

        var launch = _gameLaunchService.Current;
        TaskActivityButton.IsVisible = launch.ShouldShowIndicator;
        if (!launch.ShouldShowIndicator)
            return;

        TaskActivityButton.Background = Brush.Parse("#5968E8");
        TaskActivityButton.BorderBrush = Brush.Parse("#9AA5FF");
        TaskActivityProgress.IsVisible = launch.Phase == GameLaunchPhase.Preparing;
        TaskActivityProgress.IsIndeterminate = launch.Phase == GameLaunchPhase.Preparing;
        TaskActivityGlyph.Text = launch.Phase switch
        {
            GameLaunchPhase.Preparing => "…",
            GameLaunchPhase.Running => "▶",
            GameLaunchPhase.Failed => "!",
            GameLaunchPhase.Exited => "✓",
            _ => "▶"
        };
        ToolTip.SetTip(
            TaskActivityButton,
            $"{launch.Title}\n{launch.Message}\n点击查看启动日志");
    }

    private void OnTaskActivityClick(object? sender, RoutedEventArgs e)
    {
        var preferredView = _gameDownloadService.Current.IsActive
            ? TaskDetailView.Download
            : TaskDetailView.Launch;
        if (!_gameDownloadService.Current.IsActive &&
            !_gameLaunchService.Current.ShouldShowIndicator)
        {
            return;
        }

        if (_taskDetailsWindow is not null)
        {
            _taskDetailsWindow.ShowPreferredView();
            _taskDetailsWindow.Activate();
            return;
        }

        try
        {
            _taskDetailsWindow = new TaskDetailsWindow(
                _gameDownloadService,
                _gameLaunchService,
                preferredView);
            _taskDetailsWindow.Closed += (_, _) => _taskDetailsWindow = null;
            _taskDetailsWindow.Show(this);
        }
        catch (Exception exception)
        {
            _taskDetailsWindow = null;
            ShowStatus($"任务详情窗口打开失败：{exception.Message}");
        }
    }

    private Task<NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult>
        EditPlayerAppearanceAsync(
            PlayerAppearanceRequest request,
            CancellationToken cancellationToken)
    {
        // Polygon runtimes execute actions on a worker thread so a plugin's
        // synchronous work cannot block Avalonia. This built-in callback owns
        // file pickers, dialogs and status controls, so cross the UI boundary
        // explicitly before touching AccountStore or any window state.
        return Dispatcher.UIThread.CheckAccess()
            ? EditPlayerAppearanceOnUiThreadAsync(request, cancellationToken)
            : Dispatcher.UIThread.InvokeAsync(
                () => EditPlayerAppearanceOnUiThreadAsync(request, cancellationToken));
    }

    private async Task<NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult>
        EditPlayerAppearanceOnUiThreadAsync(
            PlayerAppearanceRequest request,
            CancellationToken cancellationToken)
    {
        var account = AccountStore.FindByStableKey(request.AccountKey);
        if (account?.Type != "microsoft" || account.Microsoft is null)
        {
            return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Failed(
                "该正版账号已不存在，请重新选择账号。");
        }

        try
        {
            return request.Command switch
            {
                PlayerAppearanceCommand.ChangeSkin =>
                    await ChangeMicrosoftSkinAsync(account, cancellationToken),
                PlayerAppearanceCommand.ChangeCape =>
                    await ChangeMicrosoftCapeAsync(account, cancellationToken),
                _ => NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Failed(
                    "未知的玩家外观操作。")
            };
        }
        catch (OperationCanceledException)
        {
            return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Completed(
                "已取消外观操作。");
        }
        catch (Exception exception)
        {
            ShowStatus($"玩家外观更新失败：{exception.Message}");
            return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Failed(
                exception.Message);
        }
    }

    private async Task<NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult>
        ChangeMicrosoftSkinAsync(LaunchAccount account, CancellationToken cancellationToken)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Minecraft Java 皮肤",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Minecraft 皮肤 PNG")
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"]
                }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Completed(
                "未选择皮肤文件。");
        }

        ValidateSkinFile(path);
        var modelDialog = new SkinModelDialog();
        var model = await modelDialog.ShowDialog<MinecraftSkinModel?>(this);
        if (model is null)
        {
            return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Completed(
                "未选择皮肤模型。");
        }

        ShowStatus($"正在为 {account.DisplayName} 上传皮肤…");
        await _minecraftProfileService.UploadSkinAsync(
            account,
            path,
            model.Value,
            cancellationToken);
        ShowStatus($"已更新 {account.DisplayName} 的正版皮肤");
        return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Completed(
            "正版皮肤已更新。");
    }

    private async Task<NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult>
        ChangeMicrosoftCapeAsync(LaunchAccount account, CancellationToken cancellationToken)
    {
        ShowStatus($"正在读取 {account.DisplayName} 的披风列表…");
        var profile = await _minecraftProfileService.GetProfileAsync(account, cancellationToken);
        var dialog = new CapeSelectionDialog(profile);
        var selection = await dialog.ShowDialog<CapeSelectionResult?>(this);
        if (selection is null)
        {
            ShowStatus("已取消披风选择");
            return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Completed();
        }

        await _minecraftProfileService.SetActiveCapeAsync(
            account,
            selection.CapeId,
            cancellationToken);
        ShowStatus(selection.CapeId is null
            ? $"已停用 {account.DisplayName} 的披风"
            : $"已更新 {account.DisplayName} 的正版披风");
        return NyaLauncher.Plugin.Abstractions.Components.ComponentActionResult.Completed(
            selection.CapeId is null ? "已停用披风。" : "正版披风已更新。");
    }

    private static void ValidateSkinFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || !string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("皮肤文件必须是本地 PNG 图片。");
        if (file.Length <= 0 || file.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("皮肤文件为空或超过 4 MiB 限制。");

        using var bitmap = new Bitmap(file.FullName);
        if (bitmap.PixelSize.Width != 64 || bitmap.PixelSize.Height is not (32 or 64))
        {
            throw new InvalidDataException(
                "Minecraft Java 皮肤尺寸必须为 64×64，或兼容旧版的 64×32。");
        }
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

    private async void OnPersonalizationSaved(
        object? sender,
        PersonalizationResult result)
    {
        if (_storageChangeInProgress)
            return;

        _storageChangeInProgress = true;
        var profile = result.Profile;
        profile.Layout = Workspace.ExportLayout();
        profile.Sidebars = [.. Workspace.ExportSidebars()];
        profile.ComponentPlacements = [.. Workspace.ExportComponentPlacements()];

        try
        {
            var directoryChanged = !WorkspaceProfileStore.PathsEqual(
                _profileStore.StorageDirectory,
                result.StorageDirectory);
            StorageDirectoryChangeTransaction? storageChange = null;
            if (directoryChanged)
            {
                var inspection = _profileStore.InspectStorageDirectory(
                    result.StorageDirectory);
                var action = ExistingConfigurationAction.None;

                if (inspection.HasConfiguration)
                {
                    var dialog = new ConfigurationConflictDialog(
                        _profileStore.StorageDirectory,
                        inspection);
                    var choice = await dialog.ShowDialog<ConfigurationConflictChoice>(this);
                    if (choice == ConfigurationConflictChoice.Cancel)
                    {
                        _settingsPage.ReloadPersonalization(
                            _profileStore.StorageDirectory);
                        ShowStatus("已取消配置目录切换。");
                        return;
                    }

                    action = choice == ConfigurationConflictChoice.DeletePrevious
                        ? ExistingConfigurationAction.DeletePrevious
                        : ExistingConfigurationAction.BackupPrevious;
                }

                try
                {
                    // Prepare copies a stable candidate without changing the
                    // active locator or deleting the source configuration.
                    storageChange = await Task.Run(() =>
                        _profileStore.PrepareStorageDirectoryChange(
                            result.StorageDirectory,
                            profile,
                            action));

                    LauncherConfig.SetStorageDirectory(storageChange.TargetDirectory);
                    storageChange.Complete();
                }
                catch (Exception migrationException)
                {
                    LauncherConfig.SetStorageDirectory(_profileStore.StorageDirectory);

                    if (storageChange is not null)
                    {
                        var rollbackFailures = await Task.Run(storageChange.Rollback);
                        if (rollbackFailures.Count > 0)
                        {
                            throw new AggregateException(
                                $"存储目录切换失败，且本次目标半成品未完全清理：" +
                                string.Join("；", rollbackFailures),
                                migrationException);
                        }
                    }

                    throw;
                }
                AccountStore.Reload();
                _launchPage.ReloadConfiguration();
                profile = storageChange.AppliedProfile;
            }
            else
            {
                _profileStore.Save(profile);
            }

            ApplyWorkspaceProfile(
                profile,
                importStoredLayout: storageChange?.AppliedExistingConfiguration == true);
            SaveWorkspaceProfile(force: true);

            _settingsPage.ReloadPersonalization(_profileStore.StorageDirectory);
            ShowStatus(CreateStorageChangeStatus(directoryChanged, storageChange));
        }
        catch (Exception exception)
        {
            ShowStatus($"配置保存或目录切换失败：{exception.Message}");
        }
        finally
        {
            _storageChangeInProgress = false;
        }
    }

    private void ApplyWorkspaceProfile(
        WorkspaceProfile profile,
        bool importStoredLayout)
    {
        _suppressWorkspaceSave = true;
        try
        {
            FeatureAreas.SetGlobalComponentScale(profile.GlobalComponentScale);
            FeatureAreas.SynchronizeUserAreas(profile.CustomAreas);
            FeatureAreas.ApplyPersonalization(profile.Areas);
            Workspace.SetGlobalComponentScale(profile.GlobalComponentScale);

            if (importStoredLayout)
            {
                Workspace.ImportLayout(
                    profile.Layout,
                    profile.Sidebars,
                    profile.ComponentPlacements,
                    profile.GlobalComponentScale);
            }
        }
        finally
        {
            _suppressWorkspaceSave = false;
        }
    }

    private static string CreateStorageChangeStatus(
        bool directoryChanged,
        StorageDirectoryChangeTransaction? storageChange)
    {
        if (!directoryChanged || storageChange is null)
            return "个性化配置已保存并应用";

        var message = storageChange.AppliedExistingConfiguration
            ? storageChange.BackupDirectory is null
                ? "已应用目标目录配置，旧配置已删除。"
                : $"已应用目标目录配置，旧配置已备份至：{storageChange.BackupDirectory}"
            : "配置已迁移至新的存储目录。";

        if (storageChange.CleanupFailures.Count > 0)
        {
            message += $" 但部分旧文件未能删除：{string.Join("；", storageChange.CleanupFailures)}";
        }

        return message;
    }

    private void SaveWorkspaceProfile(bool force = false)
    {
        // Component removal, drag/drop and layout events all converge here.
        // None may overwrite either side while a directory transaction is open.
        if (_storageChangeInProgress && !force)
            return;

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
