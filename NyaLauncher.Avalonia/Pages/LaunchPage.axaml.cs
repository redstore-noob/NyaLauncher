using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Avalonia.Pages;

public partial class LaunchPage : UserControl
{
    private readonly IOfflineMinecraftLauncher _launcher = new OfflineMinecraftLauncher();
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
            var account = OfflineAccount.Create(OfflineUsernameBox.Text ?? string.Empty);
            var result = await _launcher.LaunchAsync(new MinecraftLaunchOptions
            {
                MinecraftDirectory = _minecraftDirectory,
                GameDirectory = _gameDirectory,
                JavaRuntimeDirectory = _javaRuntimeDirectory,
                VersionId = versionId,
                Account = account
            });

            _gameProcess = result.Process;
            _gameProcess.Exited += OnGameExited;
            var javaHint = result.RequiredJavaMajorVersion is int javaMajor
                ? $"（该版本要求 Java {javaMajor}）"
                : string.Empty;
            LaunchStatusText.Text =
                $"已启动 {result.VersionId}，离线用户名：{result.Username} {javaHint}";

            if (_gameProcess.HasExited)
            {
                OnGameExited(_gameProcess, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            LaunchStatusText.Text = $"启动失败：{ex.Message}";
            LaunchButton.IsEnabled = true;
            LaunchButton.Content = "离线启动";
        }
    }

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
            LaunchButton.Content = "离线启动";
            exitedProcess.Exited -= OnGameExited;
            exitedProcess.Dispose();
            _gameProcess = null;
        });
    }
}
