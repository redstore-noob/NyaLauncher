using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Plugins;

/// <summary>
/// Coordinates manifest discovery, lifecycle state and launcher-owned
/// contributions. All lifecycle operations are serialized; a launch build
/// holds the same gate so disabling a plugin cannot tear it down mid-hook.
/// </summary>
internal sealed partial class PluginManager : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RuntimeCreationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);
    private static readonly object RetainedManagerLocksGate = new();
    private static readonly List<FileStream> RetainedManagerLocks = [];

    private readonly FeatureAreaRegistry _featureAreas;
    private readonly Func<string, CancellationToken, Task> _drainPluginComponents;
    private readonly object _initializationGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly Dictionary<string, PluginPackage> _packages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginRuntimeHost> _runtimes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginRuntimeHost> _retiredRuntimes = [];
    private readonly Dictionary<string, PluginSettingsStore> _settings =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginStatus> _status =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _quarantined = new(StringComparer.OrdinalIgnoreCase);
    private FileStream? _repositoryManagerLock;
    private PluginCatalog _catalog;
    private PluginCatalogSnapshot _current;
    private Task? _initializationTask;
    private string? _repositoryRecoveryError;
    private bool _storageTransition;
    private bool _disposed;

    public PluginManager(
        string storageDirectory,
        FeatureAreaRegistry featureAreas,
        Func<string, CancellationToken, Task>? drainPluginComponents = null)
    {
        _featureAreas = featureAreas ?? throw new ArgumentNullException(nameof(featureAreas));
        _drainPluginComponents = drainPluginComponents ?? ((_, _) => Task.CompletedTask);
        _catalog = new PluginCatalog(storageDirectory, loadState: false);
        try
        {
            _repositoryManagerLock = PluginPackageInstaller.AcquireManagerLock(_catalog);
            _catalog.ReloadState();
            _repositoryRecoveryError =
                PluginPackageInstaller.RecoverInterruptedTransactions(_catalog);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _repositoryRecoveryError =
                $"另一个 NyaLauncher 进程正在使用插件目录，或锁文件不可用：{exception.Message}";
        }
        _current = PluginCatalogSnapshot.Empty(_catalog.PackagesDirectory);
    }

    public PluginCatalogSnapshot Current => Volatile.Read(ref _current);

    public event EventHandler? Changed;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task initialization;
        lock (_initializationGate)
        {
            if (_initializationTask is null ||
                _initializationTask.IsFaulted ||
                _initializationTask.IsCanceled)
            {
                _initializationTask = RefreshAsync(CancellationToken.None);
            }

            initialization = _initializationTask;
        }
        return cancellationToken.CanBeCanceled
            ? initialization.WaitAsync(cancellationToken)
            : initialization;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            Publish(CreateCatalogSnapshot(isScanning: true));
            if (!TryRecoverRepositoryTransactions())
            {
                Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
                return;
            }
            await RefreshCoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _repositoryRecoveryError = $"插件目录刷新失败：{exception.Message}";
            Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<PluginOperationResult> SetEnabledAsync(
        string pluginId,
        bool enabled,
        IReadOnlyCollection<string>? approvedCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(pluginId))
            return PluginOperationResult.Failed("插件 ID 不能为空。");

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            if (enabled && !string.IsNullOrWhiteSpace(_repositoryRecoveryError))
            {
                return PluginOperationResult.Failed(
                    $"插件目录尚未安全恢复，不能启用插件：{_repositoryRecoveryError}");
            }
            if (!_packages.TryGetValue(pluginId, out var package) || package.Manifest is null)
                return PluginOperationResult.Failed("插件包不存在或清单无效。");
            if (package.Status is PluginStatus.Invalid or PluginStatus.Incompatible)
                return PluginOperationResult.Failed(package.Error ?? "插件包不可用。");
            if (_quarantined.Contains(pluginId))
                return PluginOperationResult.Failed("插件运行时未能安全停止，请重启启动器后重试。");

            var state = _catalog.GetState(pluginId);
            if (enabled && state.Enabled &&
                _runtimes.TryGetValue(pluginId, out var running) && running.IsStarted)
            {
                return PluginOperationResult.Completed("插件已经启用。");
            }
            if (!enabled && !state.Enabled &&
                (!_runtimes.TryGetValue(pluginId, out var stopped) || !stopped.IsStarted))
            {
                return PluginOperationResult.Completed("插件已经禁用。");
            }

            return enabled
                ? await EnableCoreAsync(package, cancellationToken, approvedCapabilities)
                : await DisableCoreAsync(package, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return PluginOperationResult.Failed("插件操作已取消。");
        }
        catch (Exception exception)
        {
            _status[pluginId] = PluginStatus.Failed;
            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Failed(exception.Message);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<PluginOperationResult> UninstallAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(pluginId))
            return PluginOperationResult.Failed("插件 ID 不能为空。");

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            if (!TryRecoverRepositoryTransactions())
            {
                return PluginOperationResult.Failed(
                    $"插件目录尚未安全恢复，不能卸载插件：{_repositoryRecoveryError}");
            }
            if (!_packages.TryGetValue(pluginId, out var package) || package.Manifest is null)
                return PluginOperationResult.Failed("插件包不存在或清单无效。");

            var previousState = _catalog.GetState(pluginId);
            if (previousState.Enabled || _runtimes.ContainsKey(pluginId))
            {
                var disabled = await DisableCoreAsync(package, cancellationToken);
                if (!disabled.Success)
                {
                    return PluginOperationResult.Failed(
                        $"插件未能安全停止，因此没有删除任何安装文件：{disabled.Message}");
                }
            }
            if (_quarantined.Contains(pluginId) ||
                _retiredRuntimes.Any(candidate => string.Equals(
                    candidate.Manifest.Id,
                    pluginId,
                    StringComparison.OrdinalIgnoreCase)) ||
                _runtimes.ContainsKey(pluginId))
            {
                return PluginOperationResult.Failed(
                    "插件代码仍在进程中或等待重启清理。为避免删除仍在使用的程序集，请重启后再卸载。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            PluginPackageRemovalResult removal;
            try
            {
                removal = PluginPackageInstaller.StageRemoval(
                    _catalog,
                    package.PackageDirectory);
            }
            catch (Exception exception)
            {
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(
                    "插件已安全禁用，但卸载事务未能开始，安装文件未删除：" +
                    exception.Message);
            }
            try
            {
                // From the first directory rename onward, complete or roll back
                // without caller cancellation so no half-uninstalled catalog is
                // intentionally left in memory.
                await RefreshCoreAsync(CancellationToken.None);
                _catalog.RemoveState(pluginId);
                var completionError = removal.Complete();
                if (completionError is not null)
                    throw new IOException($"无法确认插件卸载事务：{completionError}");
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Completed(
                    $"插件 {package.Manifest.Name} 已卸载；能力授权和来源快照已撤销，" +
                    "历史代私有数据仍保留供手工恢复或审计。");
            }
            catch (Exception exception)
            {
                var rollbackError = removal.Rollback();
                if (rollbackError is null)
                {
                    RestoreState(pluginId, previousState);
                    try
                    {
                        await RefreshCoreAsync(CancellationToken.None);
                    }
                    catch (Exception refreshException)
                    {
                        _repositoryRecoveryError =
                            $"卸载已回滚，但插件目录未能重新载入：{refreshException.Message}";
                        Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
                    }
                    return PluginOperationResult.Failed(
                        $"插件卸载失败，原安装已恢复：{exception.Message}");
                }

                _repositoryRecoveryError =
                    $"插件卸载失败且原包未能自动恢复：{rollbackError}";
                Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
                return PluginOperationResult.Failed(
                    $"插件卸载未完成：{exception.Message}；回滚错误：{rollbackError}");
            }
        }
        catch (OperationCanceledException)
        {
            return PluginOperationResult.Failed("插件卸载已取消；尚未删除安装文件。");
        }
        catch (Exception exception)
        {
            if (!TryRecoverRepositoryTransactions())
                Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
            return PluginOperationResult.Failed($"插件卸载失败：{exception.Message}");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<PluginOperationResult> SaveSettingsAsync(
        string pluginId,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(values);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            if (!_settings.TryGetValue(pluginId, out var settings))
                return PluginOperationResult.Failed("插件不存在或没有可用的设置清单。");
            if (!_packages.TryGetValue(pluginId, out var package) || package.Manifest is null)
                return PluginOperationResult.Failed("插件清单不存在或无效。");

            var directoryKeys = package.Manifest.Settings
                .Where(definition => definition.Kind == PluginSettingKind.Directory)
                .Select(definition => definition.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directoryWasSubmitted = values.Any(pair =>
                directoryKeys.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));
            var declaredCapabilities = package.Manifest.RequiredCapabilities
                .Concat(package.Manifest.OptionalCapabilities)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var canReadUserFiles = _catalog.GetState(pluginId).GrantedCapabilities.Any(
                capability => declaredCapabilities.Contains(capability) &&
                              string.Equals(
                                  capability,
                                  PluginCapabilities.UserFilesRead,
                                  StringComparison.OrdinalIgnoreCase));
            if (directoryWasSubmitted && !canReadUserFiles)
            {
                return PluginOperationResult.Failed(
                    $"Directory 设置需要先授权 {PluginCapabilities.UserFilesRead} 能力。");
            }

            settings.SaveGlobalDisplayValues(values);
            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Completed("插件设置已保存。");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return PluginOperationResult.Failed(exception.Message);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<PluginOperationResult> SetOptionalCapabilitiesAsync(
        string pluginId,
        IReadOnlyCollection<string> grantedOptionalCapabilities,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(grantedOptionalCapabilities);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            if (!string.IsNullOrWhiteSpace(_repositoryRecoveryError))
            {
                return PluginOperationResult.Failed(
                    $"插件目录尚未安全恢复，不能修改授权：{_repositoryRecoveryError}");
            }
            if (!_packages.TryGetValue(pluginId, out var package) || package.Manifest is null)
                return PluginOperationResult.Failed("插件不存在或清单无效。");
            if (_quarantined.Contains(pluginId))
                return PluginOperationResult.Failed("该插件必须重启启动器后才能更改授权。");

            var manifest = package.Manifest;
            var optional = manifest.OptionalCapabilities
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = grantedOptionalCapabilities
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selected.Any(capability => !optional.Contains(capability)))
                return PluginOperationResult.Failed("请求中包含插件未声明的可选能力。");

            var wasRunning = _runtimes.TryGetValue(pluginId, out var runtime) &&
                             runtime.IsStarted;
            if (wasRunning)
            {
                var stopped = await DisableCoreAsync(package, cancellationToken);
                if (!stopped.Success)
                    return stopped;
            }

            var required = manifest.RequiredCapabilities
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _catalog.UpdateState(pluginId, entry =>
            {
                entry.GrantedCapabilities = entry.GrantedCapabilities
                    .Where(required.Contains)
                    .Concat(selected)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(capability => capability, StringComparer.Ordinal)
                    .ToList();
                entry.LastError = null;
            });

            if (wasRunning)
                return await EnableCoreAsync(package, cancellationToken);

            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Completed("可选能力授权已保存，将在下次启用插件时生效。");
        }
        catch (OperationCanceledException)
        {
            return PluginOperationResult.Failed("能力授权修改已取消。");
        }
        catch (Exception exception)
        {
            return PluginOperationResult.Failed(exception.Message);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ChangeStorageDirectoryAsync(
        string storageDirectory,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken);
        FileStream? nextManagerLock = null;
        try
        {
            ThrowIfDisposed();
            // Validate and scan the destination before stopping anything in the
            // current catalog. Once suspension starts, the switch is committed
            // and cancellation must not leave the launcher bound to old data.
            var nextCatalog = new PluginCatalog(storageDirectory, loadState: false);
            nextManagerLock = PluginPackageInstaller.AcquireManagerLock(nextCatalog);
            nextCatalog.ReloadState();
            var recoveryError = PluginPackageInstaller.RecoverInterruptedTransactions(nextCatalog);
            if (!string.IsNullOrWhiteSpace(recoveryError))
                throw new InvalidDataException($"目标插件目录存在未恢复事务：{recoveryError}");
            var nextPackages = await Task.Run(nextCatalog.Scan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var blockedUntilRestart = new HashSet<string>(
                _quarantined,
                StringComparer.OrdinalIgnoreCase);
            foreach (var (pluginId, runtime) in _runtimes.ToArray())
            {
                _featureAreas.SuspendPlugin(pluginId);
                try
                {
                    await _drainPluginComponents(pluginId, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Component code may still be executing. Keep the old ALC
                    // alive and block a second copy until the process restarts.
                    runtime.Quarantine();
                    blockedUntilRestart.Add(pluginId);
                    _retiredRuntimes.Add(runtime);
                    continue;
                }

                await runtime.DisposeAsync();
                if (!runtime.IsUnloaded)
                {
                    runtime.Quarantine();
                    blockedUntilRestart.Add(pluginId);
                    _retiredRuntimes.Add(runtime);
                }
            }

            _runtimes.Clear();
            _packages.Clear();
            _settings.Clear();
            _status.Clear();
            _quarantined.Clear();
            _quarantined.UnionWith(blockedUntilRestart);
            _catalog = nextCatalog;
            var previousManagerLock = _repositoryManagerLock;
            _repositoryManagerLock = nextManagerLock;
            nextManagerLock = null;
            previousManagerLock?.Dispose();
            _repositoryRecoveryError = null;
            Publish(PluginCatalogSnapshot.Empty(_catalog.PackagesDirectory));
            await RefreshCoreAsync(CancellationToken.None, nextPackages);
            _storageTransition = false;
        }
        finally
        {
            nextManagerLock?.Dispose();
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stops every active runtime without changing its persisted enabled state.
    /// The caller can then copy/move the plugin tree as one stable snapshot.
    /// Cancellation is honored before the transition starts; once a plugin has
    /// stopped, the method completes or restores the remaining old catalog.
    /// </summary>
    public async Task PrepareStorageDirectoryChangeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!TryRecoverRepositoryTransactions())
            {
                throw new InvalidOperationException(
                    $"插件目录尚未安全就绪，不能迁移存储目录：{_repositoryRecoveryError}");
            }
            if (_storageTransition)
                throw new InvalidOperationException("插件存储目录迁移已经在进行中。");
            if (_quarantined.Count > 0 || _retiredRuntimes.Count > 0)
            {
                throw new InvalidOperationException(
                    "有插件代码正在等待重启清理，当前不能安全迁移插件目录。");
            }

            var activeIds = _runtimes
                .Where(pair => pair.Value.IsStarted)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var pluginId in activeIds)
            {
                if (!_packages.TryGetValue(pluginId, out var package) || package.Manifest is null)
                    throw new InvalidOperationException($"无法确定插件 {pluginId} 的当前包。");

                var previousState = _catalog.GetState(pluginId);
                var result = await DisableCoreAsync(package, CancellationToken.None);
                RestoreState(pluginId, previousState);
                if (!result.Success)
                    throw new InvalidOperationException(result.Message);
                _status[pluginId] = PluginStatus.Disabled;
            }

            _storageTransition = true;
            Publish(CreateCatalogSnapshot());
        }
        catch
        {
            _storageTransition = false;
            // Plugins stopped earlier in the sequence are safe to recreate from
            // the unchanged old catalog; quarantined generations remain blocked.
            try
            {
                await RefreshCoreAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // Preserve the original migration error. The snapshot carries
                // any secondary plugin diagnostics for the user.
            }
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task AbortStorageDirectoryChangeAsync()
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            _storageTransition = false;
            if (!TryRecoverRepositoryTransactions())
            {
                Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
                throw new InvalidDataException(_repositoryRecoveryError);
            }
            await RefreshCoreAsync(CancellationToken.None);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_initializationGate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _shutdownCancellation.Cancel();

        using var shutdown = new CancellationTokenSource(ShutdownTimeout);
        var gateEntered = false;
        var shutdownCompleted = false;
        try
        {
            await _lifecycleGate.WaitAsync(shutdown.Token);
            gateEntered = true;
            var runtimeGroups = _runtimes.Values
                .Concat(_retiredRuntimes)
                .Distinct<PluginRuntimeHost>(ReferenceEqualityComparer.Instance)
                .GroupBy(runtime => runtime.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var safeToDispose = new List<PluginRuntimeHost>();
            var runtimeShutdownSafe = true;
            foreach (var group in runtimeGroups)
            {
                if (_quarantined.Contains(group.Key) ||
                    group.Any(runtime => _retiredRuntimes.Contains(
                        runtime,
                        ReferenceEqualityComparer.Instance)))
                {
                    runtimeShutdownSafe = false;
                    continue;
                }

                try
                {
                    _featureAreas.SuspendPlugin(group.Key);
                    await _drainPluginComponents(group.Key, shutdown.Token);
                    safeToDispose.AddRange(group);
                }
                catch (Exception)
                {
                    // A component may still be executing code from this ALC.
                    // Keep the whole generation alive until process exit.
                    runtimeShutdownSafe = false;
                }
            }

            foreach (var runtime in safeToDispose)
            {
                if (_runtimes.TryGetValue(runtime.Manifest.Id, out var current) &&
                    ReferenceEquals(current, runtime))
                {
                    _runtimes.Remove(runtime.Manifest.Id);
                }
                _retiredRuntimes.Remove(runtime);
            }

            var disposal = Task.WhenAll(safeToDispose.Select(DisposeRuntimeSafelyAsync));
            ObserveBackgroundFailure(disposal);
            var disposalResults = await disposal.WaitAsync(shutdown.Token);
            shutdownCompleted = runtimeShutdownSafe && disposalResults.All(result => result);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // In-process third-party code cannot be killed safely. RuntimeHost
            // keeps deferred cleanup references alive; the window may close
            // once the single global shutdown budget is exhausted.
        }
        finally
        {
            if (gateEntered)
                _lifecycleGate.Release();
            var managerLock = Interlocked.Exchange(ref _repositoryManagerLock, null);
            if (managerLock is not null)
            {
                if (shutdownCompleted)
                {
                    managerLock.Dispose();
                }
                else
                {
                    // A repository commit or third-party runtime may still be
                    // active after the bounded shutdown wait. Keep ownership of
                    // this plugin tree until process exit so another launcher
                    // cannot race the unfinished operation.
                    lock (RetainedManagerLocksGate)
                        RetainedManagerLocks.Add(managerLock);
                }
            }
        }
    }

    private async Task RefreshCoreAsync(
        CancellationToken cancellationToken,
        IReadOnlyList<PluginPackage>? scannedPackages = null)
    {
        var scanned = scannedPackages ??
            await Task.Run(_catalog.Scan, cancellationToken);
        var validPackages = scanned
            .Where(package =>
                package.Manifest is not null &&
                package.Status is not (PluginStatus.Invalid or PluginStatus.Incompatible))
            .GroupBy(package => package.Manifest!.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Single())
            .ToDictionary(package => package.Manifest!.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var removedId in _runtimes.Keys
                     .Where(id => !validPackages.ContainsKey(id))
                     .ToArray())
        {
            _featureAreas.SuspendPlugin(removedId);
            try
            {
                await _drainPluginComponents(removedId, CancellationToken.None);
            }
            catch (Exception)
            {
                var retained = _runtimes[removedId];
                retained.Quarantine();
                _quarantined.Add(removedId);
                _retiredRuntimes.Add(retained);
                _runtimes.Remove(removedId);
                _settings.Remove(removedId);
                _status[removedId] = PluginStatus.RestartRequired;
                continue;
            }
            var removedRuntime = _runtimes[removedId];
            await removedRuntime.DisposeAsync();
            _runtimes.Remove(removedId);
            if (!removedRuntime.IsUnloaded)
                RetainUntilRestart(removedId, removedRuntime);
            _settings.Remove(removedId);
            if (removedRuntime.IsUnloaded)
                _status.Remove(removedId);
        }

        foreach (var removedId in _settings.Keys
                     .Where(id => !validPackages.ContainsKey(id))
                     .ToArray())
        {
            _settings.Remove(removedId);
            if (!_quarantined.Contains(removedId))
                _status.Remove(removedId);
        }

        _packages.Clear();
        foreach (var package in scanned)
            _packages[package.CatalogKey] = package;

        foreach (var package in scanned.Where(item => item.Manifest is not null))
        {
            var manifest = package.Manifest!;
            if (package.Status is PluginStatus.Invalid or PluginStatus.Incompatible)
                continue;
            // The metadata stored inside the package directory is authoritative
            // because it participates in package rename/rollback. State keeps a
            // synchronized cache solely for generation-isolated data paths.
            _catalog.SynchronizeInstallOrigin(manifest.Id, package.InstallOrigin);
            if (!_runtimes.TryGetValue(manifest.Id, out var settingsRuntime) ||
                !settingsRuntime.IsStarted)
            {
                _settings[manifest.Id] = _catalog.OpenSettings(manifest);
            }
            else if (!_settings.ContainsKey(manifest.Id))
            {
                _settings[manifest.Id] = _catalog.OpenSettings(settingsRuntime.Manifest);
            }
            var state = _catalog.GetState(manifest.Id);
            if (_quarantined.Contains(manifest.Id))
            {
                _status[manifest.Id] = PluginStatus.RestartRequired;
                continue;
            }
            if (!state.Enabled)
            {
                _status.TryAdd(manifest.Id, PluginStatus.Disabled);
                continue;
            }

            if (_runtimes.TryGetValue(manifest.Id, out var existing) && existing.IsStarted)
            {
                if (!string.Equals(
                        existing.Package.ManifestPath,
                        package.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.Manifest.Version, manifest.Version, StringComparison.Ordinal))
                {
                    _status[manifest.Id] = PluginStatus.RestartRequired;
                }
                else
                {
                    _status[manifest.Id] = PluginStatus.Enabled;
                }
                continue;
            }

            await EnableCoreAsync(package, cancellationToken, persistState: false);
        }

        Publish(CreateCatalogSnapshot());
    }

    private async Task<PluginOperationResult> EnableCoreAsync(
        PluginPackage package,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? approvedCapabilities = null,
        bool persistState = true)
    {
        var manifest = package.Manifest!;
        var state = _catalog.GetState(manifest.Id);
        var declaredCapabilities = manifest.RequiredCapabilities
            .Concat(manifest.OptionalCapabilities)
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grants = state.GrantedCapabilities
            .Where(declaredCapabilities.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingRequired = manifest.RequiredCapabilities
            .Where(capability => !grants.Contains(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
        if (missingRequired.Length > 0)
        {
            var approved = approvedCapabilities?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (approved is null || !approved.SetEquals(missingRequired))
            {
                // A persisted enabled state without its grants is treated as
                // revoked on startup; it must never silently recreate consent.
                if (!persistState)
                {
                    _status[manifest.Id] = PluginStatus.Disabled;
                    _catalog.UpdateState(manifest.Id, entry =>
                    {
                        entry.Enabled = false;
                        entry.LastError = "必要能力授权已缺失，请重新确认后启用。";
                    });
                }

                return PluginOperationResult.ApprovalRequired(
                    $"插件 {manifest.Name} 需要确认 {missingRequired.Length} 项必要能力。",
                    missingRequired);
            }

            grants.UnionWith(missingRequired);
            // Consent is stored independently of successful startup, so a
            // broken plugin does not ask for the same approval on every retry.
            _catalog.UpdateState(manifest.Id, entry =>
            {
                entry.GrantedCapabilities =
                    [.. grants.OrderBy(capability => capability, StringComparer.Ordinal)];
            });
        }

        _status[manifest.Id] = PluginStatus.Enabling;
        Publish(CreateCatalogSnapshot());

        if (!_settings.TryGetValue(manifest.Id, out var settings))
        {
            settings = _catalog.OpenSettings(manifest);
            _settings[manifest.Id] = settings;
        }

        if (!_runtimes.TryGetValue(manifest.Id, out var runtime))
        {
            var creationTask = Task.Run(
                () => PluginRuntimeHost.Create(package, settings, grants),
                CancellationToken.None);
            try
            {
                // Loading, static initialization and the entry constructor are
                // third-party code too; isolate them before StartAsync.
                runtime = await creationTask.WaitAsync(
                    RuntimeCreationTimeout,
                    cancellationToken);
                _runtimes[manifest.Id] = runtime;
            }
            catch (Exception exception) when (
                exception is TimeoutException or OperationCanceledException)
            {
                DisposeRuntimeWhenCreated(creationTask);
                _quarantined.Add(manifest.Id);
                _status[manifest.Id] = PluginStatus.RestartRequired;
                var error = cancellationToken.IsCancellationRequested
                    ? "插件运行时创建被取消；后台构造已隔离，请重启启动器。"
                    : "插件入口类型或构造函数执行超时；请重启启动器。";
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = false;
                    entry.LastError = error;
                });
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(error);
            }
            catch (Exception exception)
            {
                _status[manifest.Id] = PluginStatus.Failed;
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = false;
                    entry.LastError = exception.Message;
                });
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(exception.Message);
            }
        }

        try
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(StartTimeout);
            Task startTask;
            try
            {
                startTask = runtime.StartAsync(linked.Token);
            }
            catch
            {
                linked.Dispose();
                throw;
            }
            DisposeCancellationWhenCompleted(startTask, linked);
            ObserveBackgroundFailure(startTask);
            await startTask.WaitAsync(StartTimeout, cancellationToken);

            _featureAreas.PublishPlugin(
                manifest.Id,
                runtime.ComponentAreas.Select(area => new FeatureAreaDefinition
                {
                    Id = area.Id,
                    Title = area.Title,
                    Subtitle = area.Subtitle,
                    Glyph = area.Glyph,
                    IconPath = ResolveAreaIcon(package.PackageDirectory, area.Icon),
                    PolygonComponents = area.Components
                }));
            _status[manifest.Id] = PluginStatus.Enabled;
            if (persistState)
            {
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = true;
                    entry.GrantedCapabilities = [.. grants.OrderBy(item => item, StringComparer.Ordinal)];
                    entry.LastError = null;
                });
            }
            else
            {
                _catalog.UpdateState(manifest.Id, entry => entry.LastError = null);
            }

            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Completed($"插件 {manifest.Name} 已启用。");
        }
        catch (TimeoutException)
        {
            _quarantined.Add(manifest.Id);
            runtime.Quarantine();
            _featureAreas.SuspendPlugin(manifest.Id);
            _status[manifest.Id] = PluginStatus.RestartRequired;
            _catalog.UpdateState(manifest.Id, entry =>
            {
                entry.Enabled = false;
                entry.LastError = "插件启动超时，运行时已隔离；请重启启动器。";
            });
            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Failed("插件启动超时，已阻止其贡献；请重启启动器。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _quarantined.Add(manifest.Id);
            runtime.Quarantine();
            _featureAreas.SuspendPlugin(manifest.Id);
            _status[manifest.Id] = PluginStatus.RestartRequired;
            _catalog.UpdateState(manifest.Id, entry =>
            {
                entry.Enabled = false;
                entry.LastError = "插件未在启动时限内完成，运行时已隔离；请重启启动器。";
            });
            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Failed("插件启动超时，已阻止其贡献；请重启启动器。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _featureAreas.SuspendPlugin(manifest.Id);
            _status[manifest.Id] = PluginStatus.Failed;
            _catalog.UpdateState(manifest.Id, entry =>
            {
                entry.Enabled = false;
                entry.LastError = exception.Message;
            });
            if (_runtimes.Remove(manifest.Id, out var failedRuntime))
            {
                await failedRuntime.DisposeAsync();
                if (!failedRuntime.IsUnloaded)
                    RetainUntilRestart(manifest.Id, failedRuntime);
            }
            Publish(CreateCatalogSnapshot());
            return PluginOperationResult.Failed(exception.Message);
        }
        catch (OperationCanceledException)
        {
            await QuarantineAsync(
                manifest.Id,
                "插件启用被取消；未完成的运行时已隔离，请重启启动器。");
            return PluginOperationResult.Failed("插件启用已取消；为避免半启动状态，请重启后重试。");
        }
    }

    private async Task<PluginOperationResult> DisableCoreAsync(
        PluginPackage package,
        CancellationToken cancellationToken)
    {
        var manifest = package.Manifest!;
        _status[manifest.Id] = PluginStatus.Disabling;
        Publish(CreateCatalogSnapshot());

        // Suspend first: no new component action can enter plugin code while
        // its background services are stopping. Dormant placeholders retain layout.
        _featureAreas.SuspendPlugin(manifest.Id);
        try
        {
            await _drainPluginComponents(manifest.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await QuarantineAsync(manifest.Id, $"组件释放失败：{exception.Message}");
            return PluginOperationResult.Failed(
                "插件组件未能完全释放；功能已休眠，程序集将在重启后卸载。");
        }
        catch (OperationCanceledException)
        {
            await QuarantineAsync(
                manifest.Id,
                "插件组件释放被取消；运行时等待重启清理。");
            return PluginOperationResult.Failed(
                "插件贡献已休眠，但组件释放被取消；程序集将在重启后卸载。");
        }
        if (_runtimes.TryGetValue(manifest.Id, out var runtime) && runtime.IsStarted)
        {
            try
            {
                var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(StopTimeout);
                Task stopTask;
                try
                {
                    stopTask = runtime.StopAsync(linked.Token);
                }
                catch
                {
                    linked.Dispose();
                    throw;
                }
                DisposeCancellationWhenCompleted(stopTask, linked);
                ObserveBackgroundFailure(stopTask);
                await stopTask.WaitAsync(StopTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _quarantined.Add(manifest.Id);
                runtime.Quarantine();
                _status[manifest.Id] = PluginStatus.RestartRequired;
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = false;
                    entry.LastError = "插件停止超时；贡献已禁用，程序集将在重启后卸载。";
                });
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(
                    "插件功能已禁用，但清理超时；程序集将在重启后卸载。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _quarantined.Add(manifest.Id);
                runtime.Quarantine();
                _status[manifest.Id] = PluginStatus.RestartRequired;
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = false;
                    entry.LastError = "插件未在停止时限内完成；贡献已禁用。";
                });
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(
                    "插件功能已禁用，但清理超时；程序集将在重启后卸载。");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _quarantined.Add(manifest.Id);
                runtime.Quarantine();
                _status[manifest.Id] = PluginStatus.RestartRequired;
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = false;
                    entry.LastError = exception.Message;
                });
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(
                    $"插件功能已禁用，但停止时发生错误：{exception.Message}");
            }
            catch (OperationCanceledException)
            {
                await QuarantineAsync(
                    manifest.Id,
                    "插件禁用被取消；贡献已休眠，运行时等待重启清理。");
                return PluginOperationResult.Failed(
                    "插件贡献已休眠，但清理被取消；程序集将在重启后卸载。");
            }
        }

        if (_runtimes.Remove(manifest.Id, out var stoppedRuntime))
        {
            await stoppedRuntime.DisposeAsync();
            if (!stoppedRuntime.IsUnloaded)
            {
                RetainUntilRestart(manifest.Id, stoppedRuntime);
                const string deferredError =
                    "插件功能已禁用，但仍有代码尚未退出；程序集将在重启后卸载。";
                _catalog.UpdateState(manifest.Id, entry =>
                {
                    entry.Enabled = false;
                    entry.LastError = deferredError;
                });
                Publish(CreateCatalogSnapshot());
                return PluginOperationResult.Failed(deferredError);
            }
        }

        _catalog.UpdateState(manifest.Id, entry =>
        {
            entry.Enabled = false;
            entry.LastError = null;
        });
        _status[manifest.Id] = PluginStatus.Disabled;
        Publish(CreateCatalogSnapshot());
        return PluginOperationResult.Completed($"插件 {manifest.Name} 已禁用，布局和设置已保留。");
    }

    private PluginCatalogSnapshot CreateCatalogSnapshot(
        bool isScanning = false,
        string? error = null)
    {
        var snapshots = _packages.Values
            .OrderBy(package => package.Manifest?.Name ?? package.PackageDirectory,
                StringComparer.CurrentCultureIgnoreCase)
            .Select(CreatePluginSnapshot)
            .ToArray();
        return new PluginCatalogSnapshot
        {
            PackagesDirectory = _catalog.PackagesDirectory,
            Plugins = snapshots,
            InstanceActions = _runtimes.Values
                .Where(runtime => runtime.IsStarted &&
                                  !_quarantined.Contains(runtime.Manifest.Id))
                .OrderBy(runtime => runtime.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(runtime => runtime.Manifest.Id, StringComparer.Ordinal)
                .SelectMany(runtime => runtime.InstanceExtensions.SelectMany(extension =>
                    extension.Actions.Select(action => new PluginInstanceActionSnapshot(
                        runtime.Manifest.Id,
                        runtime.Manifest.Name,
                        extension.Id,
                        action.Id,
                        action.Title,
                        action.Description,
                        action.Glyph,
                        action.IsDestructive,
                        action.ConfirmationMessage))))
                .ToArray(),
            IsScanning = isScanning,
            Error = error
        };
    }

    private PluginSnapshot CreatePluginSnapshot(PluginPackage package)
    {
        var manifest = package.Manifest;
        if (manifest is null)
        {
            return new PluginSnapshot
            {
                Id = Path.GetFileName(package.PackageDirectory),
                Name = Path.GetFileName(package.PackageDirectory),
                Version = "-",
                PackageDirectory = package.PackageDirectory,
                Status = package.Status,
                Error = package.Error
            };
        }

        var state = _catalog.GetState(manifest.Id);
        var status = package.Status is PluginStatus.Invalid or PluginStatus.Incompatible
            ? package.Status
            : _status.GetValueOrDefault(
                manifest.Id,
                state.Enabled ? PluginStatus.Enabled : PluginStatus.Disabled);
        _settings.TryGetValue(manifest.Id, out var settings);
        string? iconPath = null;
        if (!string.IsNullOrWhiteSpace(manifest.Icon) &&
            PluginCatalog.TryResolvePackagePath(
                package.PackageDirectory,
                manifest.Icon,
                out var resolvedIcon) &&
            File.Exists(resolvedIcon))
        {
            iconPath = resolvedIcon;
        }
        var declaredCapabilities = manifest.RequiredCapabilities
            .Concat(manifest.OptionalCapabilities)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grantedCapabilities = state.GrantedCapabilities
            .Where(declaredCapabilities.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PluginSnapshot
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            Description = manifest.Description,
            Authors = [.. manifest.Authors],
            PackageDirectory = package.PackageDirectory,
            IconPath = iconPath,
            Status = status,
            IsEnabled = !_quarantined.Contains(manifest.Id) &&
                        state.Enabled &&
                        status is PluginStatus.Enabled or PluginStatus.RestartRequired,
            Error = package.Error ?? state.LastError ?? settings?.LoadError,
            Capabilities = manifest.RequiredCapabilities
                .Select(capability =>
                    $"必要 · {capability} · {(grantedCapabilities.Contains(capability) ? "已授权" : "未授权")}")
                .Concat(manifest.OptionalCapabilities.Select(capability =>
                    $"可选 · {capability} · {(grantedCapabilities.Contains(capability) ? "已授权" : "未授权")}"))
                .ToArray(),
            GrantedCapabilities = [.. grantedCapabilities.OrderBy(
                capability => capability,
                StringComparer.Ordinal)],
            RequiredCapabilities = [.. manifest.RequiredCapabilities],
            OptionalCapabilities = [.. manifest.OptionalCapabilities],
            InstallOrigin = package.InstallOrigin,
            InstallOriginWarning = package.InstallOriginWarning,
            SettingDefinitions = [.. manifest.Settings],
            Settings = settings?.GetGlobalDisplayValues() ??
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private void Publish(PluginCatalogSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
        var handlers = Changed?.GetInvocationList().OfType<EventHandler>().ToArray();
        if (handlers is null or { Length: 0 })
            return;

        // Never call subscribers while a lifecycle gate is held. A faulty or
        // re-entrant page must not break plugin state transitions.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // UI observers are isolated from the authoritative state.
                }
            }
        });
    }

    private async Task QuarantineAsync(string pluginId, string error)
    {
        // In-memory isolation and contribution suspension must happen before
        // fallible persistence. A read-only/corrupt state file cannot leave a
        // quarantined plugin callable in the current process.
        _quarantined.Add(pluginId);
        if (_runtimes.TryGetValue(pluginId, out var runtime))
            runtime.Quarantine();
        _status[pluginId] = PluginStatus.RestartRequired;
        string? isolationError = null;
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                _featureAreas.SuspendPlugin(pluginId);
            else
                await Dispatcher.UIThread.InvokeAsync(() => _featureAreas.SuspendPlugin(pluginId));
        }
        catch (Exception exception)
        {
            // The runtime gate above is authoritative even if a UI observer
            // throws while declarative contributions are being suspended.
            isolationError = $"插件运行时已隔离，但组件休眠报告错误：{exception.Message}";
        }

        string? persistenceError = null;
        try
        {
            _catalog.UpdateState(pluginId, entry =>
            {
                entry.Enabled = false;
                entry.LastError = error;
            });
        }
        catch (Exception exception)
        {
            persistenceError = $"{error} 插件已在内存中隔离，但状态保存失败：{exception.Message}";
        }

        var diagnostic = string.Join(
            " ",
            new[] { isolationError, persistenceError }.Where(message => message is not null));
        Publish(CreateCatalogSnapshot(error: diagnostic.Length == 0 ? null : diagnostic));
    }

    private static string? ResolveAreaIcon(string packageDirectory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            !PluginCatalog.TryResolvePackagePath(packageDirectory, relativePath, out var path) ||
            !File.Exists(path))
        {
            return null;
        }

        return path;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ThrowIfStorageTransition()
    {
        if (_storageTransition)
            throw new InvalidOperationException("插件存储目录正在迁移，请稍后重试。");
    }

    private static void ObserveBackgroundFailure(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void DisposeCancellationWhenCompleted(
        Task task,
        CancellationTokenSource cancellation)
    {
        _ = task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void DisposeRuntimeWhenCreated(Task<PluginRuntimeHost> creationTask)
    {
        var cleanup = creationTask.ContinueWith(
            async completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion)
                {
                    completed.Result.Quarantine();
                    await completed.Result.DisposeAsync();
                }
                else
                {
                    _ = completed.Exception;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
        ObserveBackgroundFailure(cleanup);
    }

    private void RetainUntilRestart(string pluginId, PluginRuntimeHost runtime)
    {
        runtime.Quarantine();
        _quarantined.Add(pluginId);
        if (!_retiredRuntimes.Contains(runtime, ReferenceEqualityComparer.Instance))
            _retiredRuntimes.Add(runtime);
        _status[pluginId] = PluginStatus.RestartRequired;
    }

    private void RestoreState(string pluginId, PluginStateEntry state) =>
        _catalog.UpdateState(pluginId, entry =>
        {
            entry.Enabled = state.Enabled;
            entry.GrantedCapabilities = [.. state.GrantedCapabilities];
            entry.LastError = state.LastError;
            entry.InstallOrigin = state.InstallOrigin;
        });

    private bool TryRecoverRepositoryTransactions()
    {
        if (_repositoryManagerLock is null)
        {
            try
            {
                _repositoryManagerLock = PluginPackageInstaller.AcquireManagerLock(_catalog);
                _catalog.ReloadState();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _repositoryRecoveryError =
                    $"另一个 NyaLauncher 进程正在使用插件目录，或锁文件不可用：{exception.Message}";
                return false;
            }
        }

        _repositoryRecoveryError =
            PluginPackageInstaller.RecoverInterruptedTransactions(_catalog);
        return string.IsNullOrWhiteSpace(_repositoryRecoveryError);
    }

    private static async Task<bool> DisposeRuntimeSafelyAsync(PluginRuntimeHost runtime)
    {
        try
        {
            await runtime.DisposeAsync();
            return runtime.IsUnloaded;
        }
        catch (Exception)
        {
            // Shutdown is best-effort once plugin code has been isolated.
            return false;
        }
    }
}
