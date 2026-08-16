using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core;

namespace NyaLauncher.Avalonia.Pages;

public partial class LaunchPage : UserControl
{
    private readonly GameLaunchService _launchService;
    private bool _synchronizingAccountSelection;
    private bool _synchronizingVersionSelection;

    public LaunchPage()
        : this(new GameLaunchService())
    {
    }

    internal LaunchPage(GameLaunchService launchService)
    {
        _launchService = launchService;
        InitializeComponent();

        AccountSelector.ItemsSource = AccountStore.Current;
        AccountSelector.SelectedItem = AccountStore.Current.FirstOrDefault();
        AccountStore.Changed += OnAccountsChanged;
        GameInstanceStore.Changed += OnGameInstancesChanged;
        _launchService.Changed += OnGameLaunchChanged;
        AccountLoginOverlay.AccountAdded += OnAccountAdded;

        ReloadConfiguration();
    }

    /// <summary>配置目录切换后，重新载入游戏目录和首选 Java。</summary>
    public void ReloadConfiguration()
    {
        MinecraftPathBox.Text =
            LauncherConfig.GameDirectory ??
            System.Environment.GetEnvironmentVariable("NYALAUNCHER_MINECRAFT_DIR") ??
            MinecraftDirectoryLocator.GetDefaultDirectory();
        _ = RescanInstallationAsync();
    }

    // ------------------------------------------------------------------
    // 账号数据与事件同步
    // ------------------------------------------------------------------

    /// <summary>
    /// 账号列表首项是跨页面共享的当前账号；组件或设置页切换后，
    /// 启动页的选择会同步跟随。
    /// </summary>
    private void OnAccountsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnAccountsChanged);
            return;
        }

        _synchronizingAccountSelection = true;
        try
        {
            AccountSelector.SelectedItem = AccountStore.Selected;
        }
        finally
        {
            _synchronizingAccountSelection = false;
        }

        LaunchButton.Content = GetLaunchButtonText();
        LaunchButton.IsEnabled = CanLaunch();
    }

    /// <summary>新建账户成功后自动选中新账号。</summary>
    private void OnAccountAdded(object? sender, LaunchAccount account)
    {
        AccountSelector.SelectedItem = account;
        LaunchStatusText.Text = $"已添加账号：{account.DisplayName}";
    }

    private void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        AccountLoginOverlay.Show();
    }

    public void ShowAccountLogin() => AccountLoginOverlay.Show();

    private void OnAccountSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingAccountSelection ||
            AccountSelector.SelectedItem is not LaunchAccount selected ||
            ReferenceEquals(AccountStore.Selected, selected))
        {
            return;
        }

        AccountStore.MoveToTop(selected);
    }

    // ------------------------------------------------------------------
    // 目录扫描与启动
    // ------------------------------------------------------------------

    private Task RescanInstallationAsync()
    {
        return GameInstanceStore.RefreshAsync(MinecraftPathBox.Text);
    }

    private async void OnRescanClick(object? sender, RoutedEventArgs e)
    {
        SaveGameDirectory();
        await RescanInstallationAsync();
    }

    /// <summary>
    /// 调出系统自带的文件夹选择框，选中后更新路径并立即重新扫描。
    /// </summary>
    private async void OnBrowseDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 Minecraft 游戏目录",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        MinecraftPathBox.Text = path;
        SaveGameDirectory();
        await RescanInstallationAsync();
    }

    private void OnGameInstancesChanged(GameInstanceSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameInstancesChanged(snapshot));
            return;
        }

        _synchronizingVersionSelection = true;
        try
        {
            if (snapshot.IsLoading)
            {
                VersionSelector.ItemsSource = null;
                VersionSelector.SelectedItem = null;
                LaunchButton.IsEnabled = false;
                LaunchStatusText.Text = "正在扫描本地 Minecraft 游戏实例…";
                return;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                VersionSelector.ItemsSource = null;
                VersionSelector.SelectedItem = null;
                LaunchButton.IsEnabled = false;
                LaunchStatusText.Text = $"目录扫描失败：{snapshot.ErrorMessage}";
                return;
            }

            VersionSelector.ItemsSource = snapshot.VersionIds;
            VersionSelector.SelectedItem = snapshot.SelectedVersionId;
            LaunchButton.IsEnabled = CanLaunch();
            LaunchStatusText.Text = snapshot.VersionIds.Count > 0
                ? $"已选择 {snapshot.SelectedVersionId} · 共 {snapshot.VersionIds.Count} 个本地实例 · 资源根目录：{snapshot.MinecraftDirectory}"
                : $"未在 {snapshot.MinecraftDirectory} 找到已安装版本";
        }
        finally
        {
            _synchronizingVersionSelection = false;
        }
    }

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingVersionSelection ||
            VersionSelector.SelectedItem is not string versionId)
        {
            return;
        }

        GameInstanceStore.Select(versionId);
    }

    private void OnGameLaunchChanged(GameLaunchSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameLaunchChanged(snapshot));
            return;
        }

        LaunchButton.Content = GetLaunchButtonText();
        LaunchButton.IsEnabled = CanLaunch();
        LaunchStatusText.Text = $"{snapshot.Title}：{snapshot.Message}";
    }

    /// <summary>把当前输入框中的游戏目录保存到 config.json。</summary>
    private void SaveGameDirectory()
    {
        var text = MinecraftPathBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            LauncherConfig.SaveGameDirectory(text);
        }
    }

    private async void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        SaveGameDirectory();
        var result = await _launchService.LaunchSelectedAsync();
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Message))
            LaunchStatusText.Text = $"启动失败：{result.Message}";
    }

    private string GetLaunchButtonText() =>
        _launchService.Current.Phase switch
        {
            GameLaunchPhase.Preparing => "启动中…",
            GameLaunchPhase.Running => "游戏运行中",
            _ => AccountSelector.SelectedItem is LaunchAccount { Type: "microsoft" }
                ? "正版启动"
                : "离线启动"
        };

    private bool CanLaunch() =>
        GameInstanceStore.Current.VersionIds.Count > 0 &&
        !_launchService.Current.IsBusy &&
        !_launchService.Current.IsGameRunning;

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        BottomVersionText.Text = "NyaLauncher版本号:" + NyaLauncherInfo.MainVersion +"."+ NyaLauncherInfo.SubVersion +"."+ NyaLauncherInfo.FixVersion + NyaLauncherInfo.Suffix;
    }
}

