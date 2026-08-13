using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Avalonia.Plugins;

internal sealed partial class PluginManager
{
    public async Task<PluginOperationResult> InstallFromRepositoryAsync(
        PluginRepositoryClient repositoryClient,
        RepositoryPlugin plugin,
        RepositoryRelease release,
        IProgress<RepositoryDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(repositoryClient);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(release);

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownCancellation.Token);
        var operationToken = operationCancellation.Token;
        try
        {
            await _lifecycleGate.WaitAsync(operationToken);
        }
        catch (OperationCanceledException)
        {
            return PluginOperationResult.Failed("插件下载或安装已取消。");
        }
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            if (!TryRecoverRepositoryTransactions())
            {
                Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
                return PluginOperationResult.Failed(
                    $"上次插件安装事务尚未安全恢复，当前不能继续安装：{_repositoryRecoveryError}");
            }
            if (release.Yanked ||
                !string.Equals(release.Channel, "stable", StringComparison.Ordinal) ||
                !PluginRepositoryClient.IsCompatible(release) ||
                !plugin.Releases.Any(candidate =>
                    string.Equals(candidate.Version, release.Version, StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.Download.Sha256,
                        release.Download.Sha256,
                        StringComparison.Ordinal)))
            {
                return PluginOperationResult.Failed("该插件版本已撤回、与当前启动器不兼容或不属于此条目。");
            }

            _packages.TryGetValue(plugin.Id, out var existing);
            if (existing is { Manifest: null })
                return PluginOperationResult.Failed("同 ID 的本地插件清单无效，请先处理冲突目录。");
            if (existing is not null &&
                existing.Status is PluginStatus.Invalid or PluginStatus.Incompatible)
            {
                return PluginOperationResult.Failed(existing.Error ?? "本地插件包不可更新。");
            }

            var directTarget = Path.Combine(_catalog.PackagesDirectory, plugin.Id);
            if (existing is null && Directory.Exists(directTarget))
            {
                return PluginOperationResult.Failed(
                    $"目标目录 {directTarget} 已存在但不是可识别的同 ID 插件，请先手动处理。");
            }
            if (existing is null && _packages.Count >= 256)
                return PluginOperationResult.Failed("本地插件包已达到 256 个扫描上限。");

            if (_quarantined.Contains(plugin.Id) ||
                _retiredRuntimes.Any(runtime => string.Equals(
                    runtime.Manifest.Id,
                    plugin.Id,
                    StringComparison.OrdinalIgnoreCase)) ||
                _runtimes.TryGetValue(plugin.Id, out var runtime) && runtime.IsStarted ||
                existing is not null && _catalog.GetState(plugin.Id).Enabled)
            {
                return PluginOperationResult.Failed(
                    "插件仍在运行或等待重启清理。请先禁用插件；若页面提示需要重启，请重启后再更新。");
            }

            if (existing?.Manifest is { } existingManifest)
            {
                if (string.Equals(
                        existingManifest.Version,
                        release.Version,
                        StringComparison.Ordinal))
                {
                    return PluginOperationResult.Completed(
                        $"插件 {plugin.Name} {release.Version} 已安装。");
                }
                if (SemanticVersion.TryParse(existingManifest.Version, out var currentVersion) &&
                    SemanticVersion.TryParse(release.Version, out var repositoryVersion) &&
                    currentVersion.CompareTo(repositoryVersion) > 0)
                {
                    return PluginOperationResult.Failed(
                        $"本地版本 {existingManifest.Version} 高于仓库版本 {release.Version}，不会自动降级。");
                }
            }

            var newInstallStateReset = false;
            var result = await PluginPackageInstaller.InstallAsync(
                _catalog,
                repositoryClient,
                plugin,
                release,
                existing?.PackageDirectory,
                progress,
                operationToken,
                beforeCommit: existing is null
                    ? () =>
                    {
                        // Deleted plugins leave state records behind. Revoke
                        // that old trust before the package can enter packages,
                        // so neither a crash nor another scan can auto-start it.
                        _catalog.UpdateState(plugin.Id, entry =>
                        {
                            entry.Enabled = false;
                            entry.GrantedCapabilities.Clear();
                            entry.LastError = null;
                        });
                        newInstallStateReset = true;
                    }
            : null);

            // The directory transaction has committed. Refresh without caller
            // cancellation so the in-memory catalog cannot intentionally remain
            // bound to the previous package snapshot.
            try
            {
                await RefreshCoreAsync(CancellationToken.None);
                var completionError = result.Complete();
                if (completionError is not null)
                    throw new IOException($"无法确认插件安装事务：{completionError}");
            }
            catch (Exception refreshException)
            {
                var rollbackError = result.Rollback();
                if (rollbackError is null)
                {
                    try
                    {
                        await RefreshCoreAsync(CancellationToken.None);
                    }
                    catch (Exception restoreRefreshException)
                    {
                        _repositoryRecoveryError =
                            "插件包已回滚，但内存目录未能重新建立：" +
                            restoreRefreshException.Message;
                        Publish(CreateCatalogSnapshot(error:
                            _repositoryRecoveryError));
                    }

                    return PluginOperationResult.Failed(
                        $"插件安装未能安全完成，已恢复安装前软件包：{refreshException.Message}" +
                        (newInstallStateReset
                            ? "。该插件的历史启用状态与能力授权已安全撤销。"
                            : string.Empty));
                }

                _repositoryRecoveryError =
                    $"插件安装自动回滚未完成：{rollbackError}";
                Publish(CreateCatalogSnapshot(error:
                    $"{_repositoryRecoveryError}。为避免加载不完整包，已阻止后续扫描和安装。"));
                return PluginOperationResult.Failed(
                    $"插件包已写入但目录刷新失败，旧包备份仍保留：" +
                    $"{refreshException.Message}；回滚错误：{rollbackError}");
            }
            return PluginOperationResult.Completed(
                result.Updated
                    ? $"插件 {plugin.Name} 已更新到 {release.Version}，当前保持禁用。"
                    : $"插件 {plugin.Name} {release.Version} 已安装，启用前请检查能力授权。");
        }
        catch (OperationCanceledException)
        {
            return PluginOperationResult.Failed("插件下载或安装已取消。");
        }
        catch (Exception exception)
        {
            if (!TryRecoverRepositoryTransactions())
                Publish(CreateCatalogSnapshot(error: _repositoryRecoveryError));
            return PluginOperationResult.Failed(exception.Message);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }
}
