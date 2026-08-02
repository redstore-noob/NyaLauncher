using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Pages;

public partial class LaunchPage : UserControl
{
    private readonly IOfflineMinecraftLauncher _launcher = new OfflineMinecraftLauncher();
    private readonly IMicrosoftAuthenticator _authenticator = new MicrosoftDeviceCodeAuthenticator();
    private MicrosoftAccount? _microsoftAccount;
    private CancellationTokenSource? _deviceCodeCancellation;
    private string _minecraftDirectory = string.Empty;
    private string? _gameDirectory;
    private readonly string _javaRuntimeDirectory;
    private Process? _gameProcess;

    public LaunchPage()
    {
        InitializeComponent();
        MinecraftPathBox.Text =
            Environment.GetEnvironmentVariable("NYALAUNCHER_MINECRAFT_DIR") ??
            MinecraftDirectoryLocator.GetDefaultDirectory();
        _javaRuntimeDirectory =
            Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA_RUNTIME") ??
            System.IO.Path.Combine(MinecraftDirectoryLocator.GetDefaultDirectory(), "runtime");
        RescanInstallation();
    }

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
        RescanInstallation();
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

        LaunchButton.IsEnabled = false;
        LaunchButton.Content = "启动中…";

        try
        {
            MinecraftLaunchResult result;
            var options = new MinecraftLaunchOptions
            {
                MinecraftDirectory = _minecraftDirectory,
                GameDirectory = _gameDirectory,
                JavaRuntimeDirectory = _javaRuntimeDirectory,
                VersionId = versionId,
                Account = (IMinecraftAccount?)_microsoftAccount ??
                          OfflineAccount.Create(OfflineUsernameBox.Text ?? string.Empty)
            };

            if (_microsoftAccount is not null)
            {
                // 正版启动：先校验/刷新令牌，再走正版启动管线。
                _microsoftAccount = await _authenticator.ValidateAsync(_microsoftAccount);
                result = await new MicrosoftMinecraftLauncher(_launcher)
                    .LaunchAsync(_microsoftAccount, options);
            }
            else
            {
                result = await _launcher.LaunchAsync(options);
            }

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

    // ------------------------------------------------------------------
    // 微软账号登录
    // ------------------------------------------------------------------

    private async void OnMicrosoftLoginClick(object? sender, RoutedEventArgs e)
    {
        using var cancellation = new CancellationTokenSource();
        _deviceCodeCancellation = cancellation;
        MicrosoftLoginButton.IsEnabled = false;
        MicrosoftLoginButton.Content = "等待授权…";

        try
        {
            _microsoftAccount = await _authenticator.AuthenticateAsync(
                async (info, _) =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DeviceCodeHintText.Text =
                            "请在浏览器中打开以下地址，然后输入验证码";
                        DeviceCodeText.Text = info.UserCode;
                        DeviceCodeUrlText.Text = info.VerificationUri;
                        DeviceCodeOverlay.IsVisible = true;
                    });

                    try
                    {
                        Process.Start(new ProcessStartInfo(
                            info.VerificationUriFull.ToString())
                        {
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // 自动打开浏览器失败时，用户仍可点击"打开浏览器"按钮。
                    }
                },
                cancellation.Token);

            DeviceCodeOverlay.IsVisible = false;
            LaunchStatusText.Text = $"已登录正版账号：{_microsoftAccount.Username}";
        }
        catch (Exception ex) when (
            ex is MicrosoftAuthenticationException or OperationCanceledException)
        {
            DeviceCodeOverlay.IsVisible = false;
            LaunchStatusText.Text = ex is OperationCanceledException
                ? "已取消微软账号登录。"
                : $"微软账号登录失败：{ex.Message}";
        }
        finally
        {
            MicrosoftLoginButton.IsEnabled = true;
            MicrosoftLoginButton.Content = "微软登录";
            UpdateAccountUi();
        }
    }

    private async void OnMicrosoftRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_microsoftAccount is null)
            return;

        MicrosoftRefreshButton.IsEnabled = false;
        try
        {
            _microsoftAccount = await _authenticator.RefreshAsync(_microsoftAccount);
            LaunchStatusText.Text = "正版账号令牌已刷新。";
        }
        catch (Exception ex)
        {
            LaunchStatusText.Text = $"刷新令牌失败：{ex.Message}";
        }
        finally
        {
            MicrosoftRefreshButton.IsEnabled = true;
            UpdateAccountUi();
        }
    }

    private void OnMicrosoftLogoutClick(object? sender, RoutedEventArgs e)
    {
        _microsoftAccount = null;
        UpdateAccountUi();
        LaunchStatusText.Text = "已退出正版账号，切换为离线模式。";
    }

    private void OnDeviceCodeOpenBrowserClick(object? sender, RoutedEventArgs e)
    {
        var codeText = DeviceCodeText.Text;
        if (string.IsNullOrWhiteSpace(codeText))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(
                $"https://www.microsoft.com/link?user_code={codeText}")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            LaunchStatusText.Text = "打开浏览器失败，请手动在浏览器中访问微软登录页。";
        }
    }

    private void OnDeviceCodeCancelClick(object? sender, RoutedEventArgs e)
    {
        _deviceCodeCancellation?.Cancel();
        DeviceCodeOverlay.IsVisible = false;
    }

    /// <summary>根据当前账号模式刷新账号区与启动按钮的显示状态。</summary>
    private void UpdateAccountUi()
    {
        var hasAccount = _microsoftAccount is not null;
        MicrosoftAccountPanel.IsVisible = hasAccount;
        OfflineUsernameBox.IsEnabled = !hasAccount;
        AccountLabel.Text = hasAccount ? "微软账号" : "离线用户名";
        if (hasAccount)
        {
            MicrosoftAccountName.Text = _microsoftAccount!.Username;
            MicrosoftAccountState.Text = _microsoftAccount.IsExpired
                ? "令牌已过期，启动时将自动刷新"
                : "已登录";
        }

        LaunchButton.Content = GetLaunchButtonText();
    }

    private string GetLaunchButtonText() =>
        _microsoftAccount is not null ? "正版启动" : "离线启动";

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
