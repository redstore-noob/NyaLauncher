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
        CancellationToken cancellationToken = default,
        string? confirmedDowngradeFromVersion = null)
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

            var matchingReleases = plugin.Releases
                .Where(candidate => string.Equals(
                    candidate.Version,
                    release.Version,
                    StringComparison.Ordinal) &&
                    candidate.Generation == release.Generation)
                .Take(2)
                .ToArray();
            if (matchingReleases.Length != 1)
                return PluginOperationResult.Failed("该插件版本不属于此仓库条目或版本记录不唯一。");

            // Treat the version as the caller's selection key, but trust only
            // the canonical release stored on the validated repository entry.
            // This prevents a cloned DTO from changing yank, compatibility,
            // URL, size, or review-bound artifact metadata after selection.
            release = matchingReleases[0];
            if (release.Generation != plugin.Generation ||
                !RepositoryCatalogPolicy.IsCurrentGenerationInstallable(plugin) ||
                release.Yanked ||
                release.Channel is not ("stable" or "preview") ||
                !PluginRepositoryClient.IsCompatible(release))
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

            if (existing is not null)
            {
                var identityMatch = RepositoryIdentityPolicy.Compare(
                    plugin,
                    release,
                    existing.InstallOrigin);
                if (!RepositoryIdentityPolicy.IsSafeUpdate(identityMatch))
                    return PluginOperationResult.Failed(CreateIdentityMismatchMessage(identityMatch));
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

            var downgraded = false;
            if (existing?.Manifest is { } existingManifest)
            {
                if (string.Equals(
                        existingManifest.Version,
                        release.Version,
                        StringComparison.Ordinal))
                {
                    if (!string.Equals(
                            existing.InstallOrigin?.Sha256,
                            release.Download.Sha256,
                            StringComparison.Ordinal))
                    {
                        return PluginOperationResult.Failed(
                            "仓库中的同版本插件包哈希与已安装来源快照不同。为避免替换攻击，" +
                            "请卸载旧插件后重新确认安装。");
                    }
                    return PluginOperationResult.Completed(
                        $"插件 {plugin.Name} {release.Version} 已安装。");
                }
                if (SemanticVersion.TryParse(existingManifest.Version, out var currentVersion) &&
                    SemanticVersion.TryParse(release.Version, out var repositoryVersion) &&
                    currentVersion.CompareTo(repositoryVersion) > 0)
                {
                    if (!string.Equals(
                            confirmedDowngradeFromVersion,
                            existingManifest.Version,
                            StringComparison.Ordinal))
                    {
                        return PluginOperationResult.Failed(
                            $"本地版本 {existingManifest.Version} 高于仓库版本 {release.Version}，" +
                            "需要针对当前已安装版本明确确认后才能降级。");
                    }

                    downgraded = true;
                }
            }

            var newInstallStateReset = false;
            var previousState = _catalog.GetState(plugin.Id);
            var targetOrigin = PluginCatalog.CreateInstallOrigin(plugin, release);
            PluginPackageInstallResult result;
            try
            {
                result = await PluginPackageInstaller.InstallAsync(
                    _catalog,
                    repositoryClient,
                    plugin,
                    release,
                    existing?.PackageDirectory,
                    progress,
                    operationToken,
                    beforeCommit: () =>
                    {
                        _catalog.UpdateState(plugin.Id, entry =>
                        {
                            // Deleted packages leave state records behind. A
                            // new lineage/generation must never inherit runtime
                            // trust or the old generation's data directory.
                            if (existing is null)
                            {
                                entry.Enabled = false;
                                entry.GrantedCapabilities.Clear();
                                entry.LastError = null;
                                newInstallStateReset = true;
                            }
                            entry.InstallOrigin = targetOrigin;
                        });
                    });
            }
            catch
            {
                RestoreOriginAfterFailedInstall(
                    plugin.Id,
                    previousState,
                    preserveRevokedTrust: existing is null && newInstallStateReset);
                throw;
            }

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
                    RestoreOriginAfterFailedInstall(
                        plugin.Id,
                        previousState,
                        preserveRevokedTrust: existing is null && newInstallStateReset);
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
                    ? downgraded
                        ? $"插件 {plugin.Name} 已降级到 {release.Version}，当前保持禁用。"
                        : $"插件 {plugin.Name} 已更新到 {release.Version}，当前保持禁用。"
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

    private void RestoreOriginAfterFailedInstall(
        string pluginId,
        PluginStateEntry previousState,
        bool preserveRevokedTrust)
    {
        _catalog.UpdateState(pluginId, entry =>
        {
            entry.InstallOrigin = previousState.InstallOrigin;
            if (preserveRevokedTrust)
            {
                entry.Enabled = false;
                entry.GrantedCapabilities.Clear();
                entry.LastError = null;
                return;
            }
            entry.Enabled = previousState.Enabled;
            entry.GrantedCapabilities = [.. previousState.GrantedCapabilities];
            entry.LastError = previousState.LastError;
        });
    }

    private static string CreateIdentityMismatchMessage(RepositoryIdentityMatch match) => match switch
    {
        RepositoryIdentityMatch.MissingInstalledOrigin =>
            "已安装插件没有可信来源快照，可能来自手动安装或旧版启动器。为防止 ID 劫持，" +
            "不能将仓库条目当作自动更新；请先卸载旧插件，再重新确认安装。",
        RepositoryIdentityMatch.LegacyV1NeedsReinstall =>
            "已安装插件来自不含数字发布者身份的旧版 v1 索引。当前仓库已启用 v2 身份绑定，" +
            "不能仅凭相同 ID 或仓库地址自动认领；请卸载后重新安装以建立可信来源。",
        RepositoryIdentityMatch.DifferentGeneration =>
            "此插件 ID 已进入新的发布代际，不属于已安装插件的正常更新。旧插件不会被自动替换；" +
            "请先卸载旧代，再单独确认安装新代。",
        RepositoryIdentityMatch.DifferentLineage =>
            "此插件 ID 已被释放并分配给新的插件谱系，不属于已安装插件。为防止供应链劫持，" +
            "必须先卸载旧插件并重新确认安装。",
        RepositoryIdentityMatch.DifferentPublisher =>
            "仓库条目的 GitHub 数字发布者身份与已安装来源不一致，已阻止自动更新。" +
            "请核对转让记录；如确需安装，请先卸载旧插件。",
        RepositoryIdentityMatch.InvalidRepositoryHistory =>
            "仓库条目的同代改名历史没有连续包含已安装来源，已阻止自动更新。" +
            "请核对中心仓库的 repositoryUrlHistory；如确需安装，请先卸载旧插件。",
        _ => "插件来源身份不一致，已阻止自动更新。"
    };
}
