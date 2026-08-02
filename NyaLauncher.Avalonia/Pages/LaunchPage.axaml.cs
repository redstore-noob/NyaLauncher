using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Pages;

public partial class LaunchPage : UserControl
{
    private readonly IOfflineMinecraftLauncher _launcher = new OfflineMinecraftLauncher();
    private readonly IMicrosoftAuthenticator _authenticator = new MicrosoftDeviceCodeAuthenticator();
    private string _minecraftDirectory = string.Empty;
    private string? _gameDirectory;
    private readonly string? _javaExecutable;
    private readonly string _javaRuntimeDirectory;
    private Process? _gameProcess;

    public LaunchPage()
    {
        InitializeComponent();

        // 优先使用 config.json 中保存的游戏目录，其次环境变量，最后默认目录。
        MinecraftPathBox.Text =
            LauncherConfig.GameDirectory ??
            Environment.GetEnvironmentVariable("NYALAUNCHER_MINECRAFT_DIR") ??
            MinecraftDirectoryLocator.GetDefaultDirectory();

        // 配置中保存的首选 Java 优先；runtime 目录用于自动探测兜底。
        _javaExecutable = LauncherConfig.JavaExecutable;
        _javaRuntimeDirectory =
            Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA_RUNTIME") ??
            System.IO.Path.Combine(MinecraftDirectoryLocator.GetDefaultDirectory(), "runtime");

        AccountSelector.ItemsSource = AccountStore.Current;
        AccountSelector.SelectedItem = AccountStore.Current.FirstOrDefault();
        AccountStore.Changed += OnAccountsChanged;
        AccountLoginOverlay.AccountAdded += OnAccountAdded;

        RescanInstallation();
    }

    // ------------------------------------------------------------------
    // 账号数据与事件同步
    // ------------------------------------------------------------------

    /// <summary>
    /// 账号列表发生增删/排序时，修复下拉框选中项：
    /// 若当前选中项仍存在则保持，否则回退到第一个（默认）账号。
    /// ObservableCollection 本身会让下拉框自动刷新，这里只处理选中项。
    /// </summary>
    private void OnAccountsChanged()
    {
        var selected = AccountSelector.SelectedItem as LaunchAccount;
        if (selected is null || !AccountStore.Current.Contains(selected))
        {
            AccountSelector.SelectedItem = AccountStore.Current.FirstOrDefault();
        }
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

    // ------------------------------------------------------------------
    // 目录扫描与启动
    // ------------------------------------------------------------------

    private void RescanInstallation()
    {
        try
        {
            var location = MinecraftDirectoryLocator.ResolveInstallationPath(
                MinecraftPathBox.Text ?? string.Empty);
            _minecraftDirectory = location.MinecraftDirectory;
            _gameDirectory = location.GameDirectory;

            var versions = MinecraftDirectoryLocator.GetInstalledVersionIds(_minecraftDirectory);
            VersionSelector.ItemsSource = versions;
            VersionSelector.SelectedItem =
                location.PreferredVersionId is not null &&
                versions.Contains(location.PreferredVersionId)
                    ? location.PreferredVersionId
                    : versions.FirstOrDefault();
            LaunchButton.IsEnabled = versions.Count > 0;
            LaunchStatusText.Text = versions.Count > 0
                ? $"已找到 {versions.Count} 个本地版本 · 资源根目录：{_minecraftDirectory}"
                : $"未在 {_minecraftDirectory} 找到已安装版本";
        }
        catch (Exception ex)
        {
            VersionSelector.ItemsSource = null;
            LaunchButton.IsEnabled = false;
            LaunchStatusText.Text = $"目录扫描失败：{ex.Message}";
        }
    }

    private void OnRescanClick(object? sender, RoutedEventArgs e)
    {
        SaveGameDirectory();
        RescanInstallation();
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
        if (_gameProcess is { HasExited: false })
        {
            LaunchStatusText.Text = "游戏已经在运行。";
            return;
        }

        if (VersionSelector.SelectedItem is not string versionId)
        {
            LaunchStatusText.Text = "请先安装并选择一个 Minecraft 版本。";
            return;
        }

        if (AccountSelector.SelectedItem is not LaunchAccount selectedAccount)
        {
            LaunchStatusText.Text = AccountStore.Current.Count == 0
                ? "账号列表为空，请先点击「＋ 新建账户」添加账号。"
                : "请先选择账号。";
            return;
        }

        LaunchButton.IsEnabled = false;
        LaunchButton.Content = "启动中…";

        try
        {
            // 启动前把当前输入固化到 config.json，方便下次直接使用。
            SaveGameDirectory();

            IMinecraftAccount launchAccount;
            if (selectedAccount.Type == "microsoft" && selectedAccount.Microsoft is { } msAccount)
            {
                // 正版启动：先校验/刷新令牌，再走正版启动管线。
                try
                {
                    msAccount = await _authenticator.ValidateAsync(msAccount);
                }
                catch (Exception ex)
                {
                    LaunchStatusText.Text =
                        $"正版账号令牌已失效（{ex.Message}），请删除后重新添加该账号。";
                    LaunchButton.IsEnabled = true;
                    LaunchButton.Content = GetLaunchButtonText();
                    return;
                }

                selectedAccount.Microsoft = msAccount;
                AccountStore.Save();
                launchAccount = msAccount;
            }
            else
            {
                launchAccount = OfflineAccount.Create(
                    selectedAccount.OfflineName ?? "Player_01");
            }

            var options = new MinecraftLaunchOptions
            {
                MinecraftDirectory = _minecraftDirectory,
                GameDirectory = _gameDirectory,
                JavaExecutable = _javaExecutable,
                JavaRuntimeDirectory = _javaRuntimeDirectory,
                VersionId = versionId,
                Account = launchAccount
            };

            MinecraftLaunchResult result = launchAccount is MicrosoftAccount
                ? await new MicrosoftMinecraftLauncher(_launcher)
                    .LaunchAsync((MicrosoftAccount)launchAccount, options)
                : await _launcher.LaunchAsync(options);

            _gameProcess = result.Process;
            _gameProcess.Exited += OnGameExited;
            var javaHint = result.RequiredJavaMajorVersion is int javaMajor
                ? $"（至少需要 Java {javaMajor}，兼容更高版本）"
                : string.Empty;
            LaunchStatusText.Text =
                $"已启动 {result.VersionId}，账号：{result.Username} {javaHint}";

            if (_gameProcess.HasExited)
            {
                OnGameExited(_gameProcess, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            LaunchStatusText.Text = $"启动失败：{ex.Message}";
            LaunchButton.IsEnabled = true;
            LaunchButton.Content = GetLaunchButtonText();
        }
    }

    private string GetLaunchButtonText() =>
        AccountSelector.SelectedItem is LaunchAccount { Type: "microsoft" }
            ? "正版启动"
            : "离线启动";

    private void OnGameExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_gameProcess, exitedProcess))
                return;

            var exitCode = exitedProcess.ExitCode;
            LaunchStatusText.Text = exitCode == 0
                ? "游戏已正常退出。"
                : $"游戏已退出，退出代码：{exitCode}";
            LaunchButton.IsEnabled = true;
            LaunchButton.Content = GetLaunchButtonText();
            exitedProcess.Exited -= OnGameExited;
            exitedProcess.Dispose();
            _gameProcess = null;
        });
    }
}

