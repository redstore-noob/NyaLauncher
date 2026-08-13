using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Launch.Auth;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Pages;

internal enum GameLaunchPhase
{
    Idle,
    Preparing,
    Running,
    Failed,
    Exited
}

internal sealed record GameLaunchSnapshot(
    long Revision,
    GameLaunchPhase Phase,
    string Title,
    string Message,
    string? VersionId,
    string? AccountName,
    int? ProcessId)
{
    public static GameLaunchSnapshot Idle { get; } = new(
        0,
        GameLaunchPhase.Idle,
        "尚未启动游戏",
        "选择账号和游戏实例后即可启动。",
        null,
        null,
        null);

    public bool IsBusy => Phase == GameLaunchPhase.Preparing;

    public bool IsGameRunning => Phase == GameLaunchPhase.Running;

    public bool ShouldShowIndicator => Phase is GameLaunchPhase.Preparing or GameLaunchPhase.Running;
}

/// <summary>
/// Owns the single active launch pipeline shared by the launch page and the
/// polygon component. Lifecycle state is published as immutable snapshots;
/// process output is retained in a bounded in-memory log for the log window.
/// </summary>
internal sealed class GameLaunchService
{
    private const int MaximumLogLines = 2000;
    private readonly object _gate = new();
    private readonly IOfflineMinecraftLauncher _launcher = new OfflineMinecraftLauncher();
    private readonly IMicrosoftAuthenticator _authenticator = new MicrosoftDeviceCodeAuthenticator();
    private readonly List<string> _logLines = [];
    private GameLaunchSnapshot _current = GameLaunchSnapshot.Idle;
    private Process? _gameProcess;
    private long _revision;
    private long _launchId;
    private int _launchInProgress;

    public GameLaunchSnapshot Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event Action<GameLaunchSnapshot>? Changed;

    public string GetLogText()
    {
        lock (_gate)
            return string.Join(Environment.NewLine, _logLines);
    }

    public async Task<ComponentActionResult> LaunchSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _launchInProgress, 1, 0) != 0)
            return ComponentActionResult.Failed("游戏正在启动，请稍候。");

        try
        {
            if (IsProcessRunning())
                return ComponentActionResult.Failed("游戏已经在运行。");

            var instance = GameInstanceStore.Current;
            if (instance.IsLoading)
                return ComponentActionResult.Failed("游戏实例仍在扫描，请稍候。");
            if (!string.IsNullOrWhiteSpace(instance.ErrorMessage))
                return ComponentActionResult.Failed($"Minecraft 目录无效：{instance.ErrorMessage}");
            if (string.IsNullOrWhiteSpace(instance.SelectedVersionId) ||
                string.IsNullOrWhiteSpace(instance.MinecraftDirectory))
            {
                return ComponentActionResult.Failed("请先选择一个已安装的游戏实例。");
            }
            if (GameInstanceLayoutResolver.TryResolveExternalInstance(
                    instance.SourcePath,
                    out var external) &&
                string.Equals(
                    external.InstanceId,
                    instance.SelectedVersionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ComponentActionResult.Failed(
                    $"已识别 {external.Provider} 实例并可管理其内容，但其原生版本补丁元数据暂不能由 NyaLauncher 直接启动。");
            }

            var selectedAccount = AccountStore.Selected;
            if (selectedAccount is null)
            {
                return ComponentActionResult.Failed(
                    AccountStore.Current.Count == 0
                        ? "请先添加并选择一个账号。"
                        : "请先选择账号。");
            }

            var launchId = Interlocked.Increment(ref _launchId);
            ResetLog();
            AppendLog($"准备启动 Minecraft {instance.SelectedVersionId}。");
            Publish(new GameLaunchSnapshot(
                NextRevision(),
                GameLaunchPhase.Preparing,
                $"正在启动 {instance.SelectedVersionId}",
                "正在准备账号与 Java 启动参数…",
                instance.SelectedVersionId,
                selectedAccount.DisplayName,
                null));

            cancellationToken.ThrowIfCancellationRequested();
            IMinecraftAccount launchAccount;
            if (selectedAccount.Type == "microsoft" &&
                selectedAccount.Microsoft is { } microsoftAccount)
            {
                AppendLog("正在校验正版账号凭据。");
                microsoftAccount = await _authenticator
                    .ValidateAsync(microsoftAccount, cancellationToken)
                    .ConfigureAwait(false);
                selectedAccount.Microsoft = microsoftAccount;
                AccountStore.Save();
                launchAccount = microsoftAccount;
                AppendLog("正版账号凭据校验完成。");
            }
            else
            {
                launchAccount = OfflineAccount.Create(
                    selectedAccount.OfflineName ?? "Player_01");
                AppendLog("已准备离线账号。");
            }

            var versionProfile = GameVersionProfileStore.Get(
                instance.MinecraftDirectory,
                instance.SelectedVersionId);
            var isolatedGameDirectory = GameVersionIsolation.GetGameDirectory(
                instance,
                instance.SelectedVersionId);
            var memoryDecision = GameMemorySettings.ResolveForLaunch(
                versionProfile.UseIndependentMemorySettings
                    ? versionProfile.MaximumMemoryMb
                    : null);
            var effectiveMinimumMemory = Math.Min(
                versionProfile.UseIndependentMemorySettings
                    ? versionProfile.MinimumMemoryMb
                    : 512,
                memoryDecision.MaximumMemoryMb);
            var globalLaunchSettings = GlobalLaunchSettingsStore.Load();
            var javaExecutable = versionProfile.FollowGlobalAdvancedSettings
                ? globalLaunchSettings.JavaExecutable
                : versionProfile.JavaExecutable;
            var windowWidth = versionProfile.FollowGlobalAdvancedSettings
                ? globalLaunchSettings.WindowWidth
                : versionProfile.WindowWidth;
            var windowHeight = versionProfile.FollowGlobalAdvancedSettings
                ? globalLaunchSettings.WindowHeight
                : versionProfile.WindowHeight;
            var additionalJvmArguments = versionProfile.FollowGlobalAdvancedSettings
                ? globalLaunchSettings.AdditionalJvmArguments
                : versionProfile.AdditionalJvmArguments;
            var additionalGameArguments = versionProfile.FollowGlobalAdvancedSettings
                ? globalLaunchSettings.AdditionalGameArguments
                : versionProfile.AdditionalGameArguments;
            AppendLog(memoryDecision.IsAutomatic
                ? $"已根据启动前可用内存自动设置：可用 {memoryDecision.AvailableMemoryMb} MiB，" +
                  $"保留 {memoryDecision.ReservedMemoryMb} MiB，游戏最大 {memoryDecision.MaximumMemoryMb} MiB。"
                : versionProfile.UseIndependentMemorySettings
                    ? $"已应用独立内存设置：实例上限 {versionProfile.MaximumMemoryMb} MiB，" +
                      $"全局手动上限生效后游戏最大 {memoryDecision.MaximumMemoryMb} MiB。"
                    : $"实例未开启独立调整，已使用全局手动内存：最大 {memoryDecision.MaximumMemoryMb} MiB。");
            var options = new MinecraftLaunchOptions
            {
                MinecraftDirectory = instance.MinecraftDirectory,
                GameDirectory = isolatedGameDirectory,
                JavaExecutable = string.IsNullOrWhiteSpace(javaExecutable)
                    ? LauncherConfig.JavaExecutable
                    : javaExecutable,
                JavaRuntimeDirectory =
                    Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA_RUNTIME") ??
                    Path.Combine(MinecraftDirectoryLocator.GetDefaultDirectory(), "runtime"),
                VersionId = instance.SelectedVersionId,
                Account = launchAccount,
                MinimumMemoryMb = effectiveMinimumMemory,
                MaximumMemoryMb = memoryDecision.MaximumMemoryMb,
                WindowWidth = windowWidth,
                WindowHeight = windowHeight,
                AdditionalJvmArguments = additionalJvmArguments,
                AdditionalGameArguments = additionalGameArguments
            };

            AppendLog(versionProfile.FollowGlobalAdvancedSettings
                ? "已应用全局高级启动设置。"
                : "已应用当前实例的独立高级启动设置。");

            AppendLog("正在解析版本、依赖库与 Java 运行时。");
            var result = launchAccount is MicrosoftAccount authenticatedAccount
                ? await new MicrosoftMinecraftLauncher(_launcher)
                    .LaunchAsync(authenticatedAccount, options, cancellationToken)
                    .ConfigureAwait(false)
                : await _launcher
                    .LaunchAsync(options, cancellationToken)
                    .ConfigureAwait(false);

            var beginExitObservation = PrepareProcessObservation(result.Process, launchId);
            var javaHint = result.RequiredJavaMajorVersion is int javaMajor
                ? $"至少需要 Java {javaMajor}"
                : "Java 版本要求已满足";
            AppendLog($"Java 进程已启动，进程 ID：{result.Process.Id}。");
            Publish(new GameLaunchSnapshot(
                NextRevision(),
                GameLaunchPhase.Running,
                $"{result.VersionId} 正在运行",
                $"账号：{result.Username} · {javaHint}",
                result.VersionId,
                selectedAccount.DisplayName,
                result.Process.Id));
            beginExitObservation();
            return ComponentActionResult.Completed($"已启动 {result.VersionId}。");
        }
        catch (OperationCanceledException)
        {
            AppendLog("启动操作已取消。");
            PublishFailure("启动已取消", "游戏启动操作已取消。", null);
            return ComponentActionResult.Failed("游戏启动已取消。");
        }
        catch (Exception exception)
        {
            AppendLog($"启动失败：{exception.Message}");
            PublishFailure("启动失败", exception.Message, null);
            return ComponentActionResult.Failed(exception.Message);
        }
        finally
        {
            Volatile.Write(ref _launchInProgress, 0);
        }
    }

    private bool IsProcessRunning()
    {
        lock (_gate)
        {
            try
            {
                return _gameProcess is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private Action PrepareProcessObservation(Process process, long launchId)
    {
        lock (_gate)
            _gameProcess = process;

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                AppendLog(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                AppendLog($"[stderr] {args.Data}");
        };

        var exitObserved = 0;
        void BeginExitObservation()
        {
            if (Interlocked.Exchange(ref exitObserved, 1) != 0)
                return;

            _ = Task.Run(() => CompleteProcessExit(process, launchId));
        }

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException exception)
        {
            // A Java process that fails immediately may exit before the
            // asynchronous readers are attached. Exit observation still
            // reports its code, while this detail remains available in logs.
            AppendLog($"启动日志流提前关闭：{exception.Message}");
        }

        return () =>
        {
            process.Exited += (_, _) => BeginExitObservation();
            if (process.HasExited)
                BeginExitObservation();
        };
    }

    private void CompleteProcessExit(Process process, long launchId)
    {
        int exitCode;
        try
        {
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception exception)
        {
            AppendLog($"读取游戏退出状态失败：{exception.Message}");
            exitCode = -1;
        }

        lock (_gate)
        {
            if (launchId != _launchId || !ReferenceEquals(_gameProcess, process))
            {
                process.Dispose();
                return;
            }

            _gameProcess = null;
        }

        AppendLog(exitCode == 0
            ? "游戏进程已正常退出。"
            : $"游戏进程已退出，退出代码：{exitCode}。");
        var current = Current;
        Publish(new GameLaunchSnapshot(
            NextRevision(),
            GameLaunchPhase.Exited,
            exitCode == 0 ? "游戏已退出" : "游戏异常退出",
            exitCode == 0 ? "退出代码：0" : $"退出代码：{exitCode}",
            current.VersionId,
            current.AccountName,
            null));
        process.Dispose();
    }

    private void PublishFailure(string title, string message, int? processId)
    {
        var current = Current;
        Publish(new GameLaunchSnapshot(
            NextRevision(),
            GameLaunchPhase.Failed,
            title,
            message,
            current.VersionId,
            current.AccountName,
            processId));
    }

    private long NextRevision() => Interlocked.Increment(ref _revision);

    private void ResetLog()
    {
        lock (_gate)
            _logLines.Clear();
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_gate)
        {
            _logLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_logLines.Count > MaximumLogLines)
                _logLines.RemoveRange(0, _logLines.Count - MaximumLogLines);
        }
    }

    private void Publish(GameLaunchSnapshot snapshot)
    {
        lock (_gate)
            _current = snapshot;

        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (Action<GameLaunchSnapshot> subscriber in handlers.GetInvocationList())
        {
            try
            {
                subscriber(snapshot);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"GameLaunchService.Changed 订阅者异常：{exception}");
            }
        }
    }
}
