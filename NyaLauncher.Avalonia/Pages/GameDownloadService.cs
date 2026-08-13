using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Pages;

internal enum GameDownloadPhase
{
    Idle,
    Preparing,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

internal sealed record GameDownloadSnapshot(
    long Revision,
    long TaskId,
    GameDownloadPhase Phase,
    string VersionId,
    int StageIndex,
    string StageName,
    string Detail,
    double Percentage,
    long CompletedBytes,
    long TotalBytes,
    int CompletedFiles,
    int TotalFiles,
    double BytesPerSecond)
{
    public static GameDownloadSnapshot Idle { get; } = new(
        0,
        0,
        GameDownloadPhase.Idle,
        string.Empty,
        0,
        "尚无下载任务",
        "请在资源下载页选择 Minecraft 版本。",
        0,
        0,
        0,
        0,
        0,
        0);

    public bool HasTask => Phase != GameDownloadPhase.Idle;

    public bool IsActive => Phase is GameDownloadPhase.Preparing or GameDownloadPhase.Downloading;

    public bool IsTerminal => Phase is GameDownloadPhase.Completed or
        GameDownloadPhase.Failed or GameDownloadPhase.Cancelled;
}

internal sealed class GameDownloadService
{
    public static readonly string[] StageNames =
    [
        "获取版本元数据",
        "分析下载清单",
        "下载游戏客户端",
        "下载依赖库",
        "下载资源索引",
        "下载游戏资源",
        "完成校验与安装"
    ];

    private readonly object _gate = new();
    private readonly MinecraftVersionInstaller _installer = new();
    private GameDownloadSnapshot _current = GameDownloadSnapshot.Idle;
    private CancellationTokenSource? _activeTask;
    private long _revision;
    private long _taskId;

    public GameDownloadSnapshot Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event Action<GameDownloadSnapshot>? Changed;

    public async Task<bool> StartAsync(
        MinecraftVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (string.IsNullOrWhiteSpace(version.Id) || string.IsNullOrWhiteSpace(version.Url))
            return false;

        CancellationTokenSource taskCancellation;
        long taskId;
        lock (_gate)
        {
            if (_activeTask is not null)
                return false;

            taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeTask = taskCancellation;
            taskId = ++_taskId;
        }

        Publish(new GameDownloadSnapshot(
            NextRevision(),
            taskId,
            GameDownloadPhase.Preparing,
            version.Id,
            1,
            StageNames[0],
            $"正在准备下载 Minecraft {version.Id}",
            0,
            0,
            0,
            0,
            0,
            0));

        try
        {
            var targetRoot = ResolveTargetMinecraftDirectory();
            var progress = new InlineProgress<MinecraftInstallProgress>(update =>
            {
                if (taskCancellation.IsCancellationRequested || taskId != Volatile.Read(ref _taskId))
                    return;

                Publish(new GameDownloadSnapshot(
                    NextRevision(),
                    taskId,
                    GameDownloadPhase.Downloading,
                    version.Id,
                    update.StageIndex,
                    update.StageName,
                    update.Detail,
                    update.Percentage,
                    update.CompletedBytes,
                    update.TotalBytes,
                    update.CompletedFiles,
                    update.TotalFiles,
                    update.BytesPerSecond));
            });
            await _installer.InstallAsync(
                    version.Id,
                    version.Url,
                    targetRoot,
                    progress,
                    taskCancellation.Token)
                .ConfigureAwait(false);

            var sourcePath = LauncherConfig.GameDirectory;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                LauncherConfig.SaveGameDirectory(targetRoot);
                sourcePath = targetRoot;
            }

            await GameInstanceStore.RefreshAsync(sourcePath).ConfigureAwait(false);
            GameInstanceStore.Select(version.Id);

            var previous = Current;
            Publish(previous with
            {
                Revision = NextRevision(),
                Phase = GameDownloadPhase.Completed,
                StageIndex = MinecraftVersionInstaller.StageCount,
                StageName = StageNames[^1],
                Detail = $"Minecraft {version.Id} 下载并安装完成",
                Percentage = 100
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            PublishTerminal(
                taskId,
                GameDownloadPhase.Cancelled,
                "下载已取消",
                $"Minecraft {version.Id} 下载已取消");
            return false;
        }
        catch (Exception exception)
        {
            PublishTerminal(
                taskId,
                GameDownloadPhase.Failed,
                "下载失败",
                exception.Message);
            return false;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeTask, taskCancellation))
                {
                    _activeTask.Dispose();
                    _activeTask = null;
                }
            }
        }
    }

    public bool CancelActive()
    {
        lock (_gate)
        {
            if (_activeTask is null)
                return false;
            _activeTask.Cancel();
            return true;
        }
    }

    private static string ResolveTargetMinecraftDirectory()
    {
        var configured = LauncherConfig.GameDirectory ??
                         Environment.GetEnvironmentVariable("NYALAUNCHER_MINECRAFT_DIR") ??
                         MinecraftDirectoryLocator.GetDefaultDirectory();
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        var directory = new DirectoryInfo(fullPath);
        if (directory.Parent is { } versionsDirectory &&
            string.Equals(
                versionsDirectory.Name,
                "versions",
                StringComparison.OrdinalIgnoreCase) &&
            versionsDirectory.Parent is not null)
        {
            fullPath = versionsDirectory.Parent.FullName;
        }

        Directory.CreateDirectory(fullPath);
        Directory.CreateDirectory(Path.Combine(fullPath, "versions"));
        return fullPath;
    }

    private void PublishTerminal(
        long taskId,
        GameDownloadPhase phase,
        string stageName,
        string detail)
    {
        var previous = Current;
        if (previous.TaskId != taskId)
            return;
        Publish(previous with
        {
            Revision = NextRevision(),
            Phase = phase,
            StageName = stageName,
            Detail = detail
        });
    }

    private long NextRevision() => Interlocked.Increment(ref _revision);

    private void Publish(GameDownloadSnapshot snapshot)
    {
        lock (_gate)
            _current = snapshot;

        var handlers = Changed;
        if (handlers is null)
            return;
        foreach (Action<GameDownloadSnapshot> subscriber in handlers.GetInvocationList())
        {
            try
            {
                subscriber(snapshot);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GameDownloadService.Changed 订阅者异常：{exception}");
            }
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
