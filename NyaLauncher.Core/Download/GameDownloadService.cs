using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

public enum GameDownloadPhase
{
    Idle,
    Preparing,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public sealed record GameDownloadSnapshot(
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

/// <summary>
/// 包装 MinecraftVersionInstaller，通过阶段/快照状态机对外发布下载进度。
/// </summary>
public sealed class GameDownloadService
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
    private readonly ModLoaderInstaller _modLoaderInstaller = new();
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

            // 全局默认隔离开启时，为新版本预建隔离内容目录骨架。
            if (LauncherConfig.DefaultVersionIsolation == true)
            {
                ScaffoldIsolatedContentDirectory(targetRoot, version.Id);
            }

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

    /// <summary>
    /// 以 Mod Loader 模式下载：先确保原版已安装，再叠加安装 Loader。
    /// </summary>
    public async Task<bool> StartModLoaderAsync(
        MinecraftVersion version,
        ModLoaderVersion loader,
        string instanceName,
        bool skipFabricApi = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

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

        var displayName = $"{loader.Type} {loader.LoaderVersion} (Minecraft {version.Id})";
        Publish(new GameDownloadSnapshot(
            NextRevision(),
            taskId,
            GameDownloadPhase.Preparing,
            instanceName,
            1,
            StageNames[0],
            $"正在准备下载 {displayName}",
            0, 0, 0, 0, 0, 0));

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
                    instanceName,
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

            await _modLoaderInstaller.InstallAsync(
                    loader,
                    instanceName,
                    targetRoot,
                    version.Id,
                    progress,
                    taskCancellation.Token)
                .ConfigureAwait(false);

            if (LauncherConfig.DefaultVersionIsolation == true)
            {
                ScaffoldIsolatedContentDirectory(targetRoot, instanceName);
            }

            var sourcePath = LauncherConfig.GameDirectory;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                LauncherConfig.SaveGameDirectory(targetRoot);
                sourcePath = targetRoot;
            }

            // Fabric 实例默认随本体一起下载 Fabric API（除非用户在下载选项中勾选跳过）。
            if (loader.Type == ModLoaderType.Fabric && !skipFabricApi)
            {
                await DownloadFabricApiIfNeededAsync(
                        loader,
                        instanceName,
                        targetRoot,
                        sourcePath,
                        version.Id,
                        taskId,
                        taskCancellation.Token)
                    .ConfigureAwait(false);
            }

            await GameInstanceStore.RefreshAsync(sourcePath).ConfigureAwait(false);
            GameInstanceStore.Select(instanceName);

            var previous = Current;
            Publish(previous with
            {
                Revision = NextRevision(),
                Phase = GameDownloadPhase.Completed,
                StageIndex = MinecraftVersionInstaller.StageCount,
                StageName = StageNames[^1],
                Detail = $"{displayName} 安装完成",
                Percentage = 100
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            PublishTerminal(taskId, GameDownloadPhase.Cancelled, "下载已取消", $"{displayName} 下载已取消");
            return false;
        }
        catch (Exception exception)
        {
            PublishTerminal(taskId, GameDownloadPhase.Failed, "下载失败", exception.Message);
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
                         MinecraftDirectoryLocator.EnsureDefaultDirectory();
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

    /// <summary>
    /// 在版本隔离启用时，于版本目录下创建标准 Minecraft 内容子文件夹骨架。
    /// 游戏启动时会以该目录作为 <c>--gameDir</c>，提前创建可避免首次运行时目录缺失。
    /// </summary>
    private static void ScaffoldIsolatedContentDirectory(string minecraftRoot, string versionId)
    {
        var versionDirectory = Path.Combine(minecraftRoot, "versions", versionId);
        if (!Directory.Exists(versionDirectory))
            return;

        foreach (var sub in IsolatedContentSubDirectories)
        {
            Directory.CreateDirectory(Path.Combine(versionDirectory, sub));
        }
    }

    private static readonly string[] IsolatedContentSubDirectories =
    [
        "saves",
        "resourcepacks",
        "mods",
        "config",
        "crash-reports",
        "logs",
        "screenshots",
        "shaderpacks",
    ];

    /// <summary>
    /// Modrinth 上的 Fabric API 项目 ID（任何 Fabric 实例都需要的运行库）。
    /// 与 ModDetailDialog 中使用的常量保持一致。
    /// </summary>
    private const string FabricApiProjectId = "P7dR8mSH";

    private const string FabricApiDisplayName = "Fabric API";

    /// <summary>
    /// Fabric 实例安装完成后，默认把 Fabric API 一起下载到该实例的 mods 目录。
    /// 失败不阻断整个安装流程，仅作为警告提示。
    /// </summary>
    private async Task DownloadFabricApiIfNeededAsync(
        ModLoaderVersion loader,
        string instanceName,
        string targetRoot,
        string sourcePath,
        string gameVersion,
        long taskId,
        CancellationToken cancellationToken)
    {
        if (loader.Type != ModLoaderType.Fabric)
            return;

        // 解析该实例实际的内容目录（版本隔离 / 共享布局 / 外部实例均可正确命中）。
        // 全局默认只作兜底传入，不覆盖自动检测（与 GameVersionIsolation.Resolve 语义一致）。
        var layout = GameInstanceLayoutResolver.Resolve(
            targetRoot, sourcePath, instanceName, null, LauncherConfig.DefaultVersionIsolation);
        var modsDir = Path.Combine(layout.ContentDirectory, "mods");
        Directory.CreateDirectory(modsDir);

        try
        {
            Publish(new GameDownloadSnapshot(
                NextRevision(), taskId, GameDownloadPhase.Downloading, instanceName,
                MinecraftVersionInstaller.StageCount, "下载 Fabric API",
                $"正在下载 {FabricApiDisplayName}…", 0, 0, 0, 0, 0, 0));

            var versions = await ModrinthVersionApi.GetVersionsAsync(
                    FabricApiProjectId,
                    gameVersions: [gameVersion],
                    loaders: ["fabric"],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var latest = versions.FirstOrDefault(
                v => v.PrimaryFile is { } f && !string.IsNullOrWhiteSpace(f.Url));
            if (latest?.PrimaryFile is null ||
                string.IsNullOrWhiteSpace(latest.PrimaryFile.Url))
            {
                Publish(new GameDownloadSnapshot(
                    NextRevision(), taskId, GameDownloadPhase.Downloading, instanceName,
                    MinecraftVersionInstaller.StageCount, "下载 Fabric API",
                    $"{FabricApiDisplayName} 无可用版本，已跳过。", 0, 0, 0, 0, 0, 0));
                return;
            }

            var targetPath = Path.Combine(modsDir, latest.PrimaryFile.Filename);
            if (File.Exists(targetPath))
                return;

            await ModDownloadService.DownloadAsync(
                    latest.PrimaryFile.Url, targetPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            Publish(new GameDownloadSnapshot(
                NextRevision(), taskId, GameDownloadPhase.Downloading, instanceName,
                MinecraftVersionInstaller.StageCount, "下载 Fabric API",
                $"{FabricApiDisplayName} 已下载至 mods/。", 0, 0, 0, 0, 0, 0));
        }
        catch (Exception exception)
        {
            // Fabric API 下载失败不应让整个实例安装失败，仅给出警告提示。
            Publish(new GameDownloadSnapshot(
                NextRevision(), taskId, GameDownloadPhase.Downloading, instanceName,
                MinecraftVersionInstaller.StageCount, "下载 Fabric API",
                $"{FabricApiDisplayName} 下载失败，请稍后手动安装：{exception.Message}",
                0, 0, 0, 0, 0, 0));
        }
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
