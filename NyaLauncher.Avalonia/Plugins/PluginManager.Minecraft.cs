using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Launch;
using NyaLauncher.Plugin.Abstractions.Minecraft;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Plugins;

/// <summary>
/// Minecraft instance actions and deterministic launch-plan composition.
/// Kept with the manager so lifecycle serialization remains one authority.
/// </summary>
internal sealed partial class PluginManager
{
    private static readonly TimeSpan ContributorTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan InstanceActionTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InstanceSessionCleanupTimeout = TimeSpan.FromSeconds(5);
    private const int MaxContributionEntries = 4096;
    private const int MaxEnvironmentVariables = 256;
    private const int MaxContributionStringLength = 32768;
    private const int MaxContributionCharacters = 4 * 1024 * 1024;
    private const int MaxLaunchContributors = 512;
    private const int MaxMergedEntries = 16384;
    private const int MaxMergedCharacters = 16 * 1024 * 1024;

    public Task<MinecraftLaunchTransform> BuildLaunchTransformAsync(
        GameInstanceSnapshot instance,
        string versionId,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        var descriptor = CreateInstanceDescriptor(instance, versionId, gameDirectory);
        return BuildLaunchTransformAsync(descriptor, cancellationToken);
    }

    internal static MinecraftInstanceDescriptor CreateInstanceDescriptor(
        GameInstanceSnapshot instance,
        string versionId,
        string gameDirectory)
    {
        var minecraftDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(instance.MinecraftDirectory));
        var normalizedGameDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(gameDirectory));
        var identityPath = OperatingSystem.IsWindows()
            ? minecraftDirectory.ToUpperInvariant()
            : minecraftDirectory;
        var identityText = $"{identityPath}\0{versionId}";
        var instanceId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identityText))).ToLowerInvariant();
        return new MinecraftInstanceDescriptor
        {
            InstanceId = instanceId,
            DisplayName = versionId,
            VersionId = versionId,
            MinecraftDirectory = minecraftDirectory,
            GameDirectory = normalizedGameDirectory,
            Metadata = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["versionDirectory"] = Path.Combine(
                        minecraftDirectory,
                        "versions",
                        versionId),
                    ["sourcePath"] = instance.SourcePath
                })
        };
    }

    /// <summary>
    /// Runs all enabled contributors against per-contributor immutable snapshots.
    /// A failed contributor contributes nothing and aborts this launch.
    /// </summary>
    public async Task<MinecraftLaunchContribution> BuildLaunchContributionAsync(
        MinecraftInstanceDescriptor instance,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(instance);
        await InitializeAsync(cancellationToken);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            var registeredContributors = _runtimes.Values
                .Where(runtime => runtime.IsStarted && !_quarantined.Contains(runtime.Manifest.Id))
                .SelectMany(runtime => runtime.LaunchContributors.Select(registration => new
                {
                    Runtime = runtime,
                    Registration = registration
                }))
                .Take(MaxLaunchContributors + 1)
                .ToArray();
            if (registeredContributors.Length > MaxLaunchContributors)
                throw new InvalidOperationException(
                    $"一次启动最多允许 {MaxLaunchContributors} 个插件启动贡献。");
            var contributors = registeredContributors
                .OrderBy(item => item.Registration.Order)
                .ThenBy(item => item.Runtime.Manifest.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Registration.Id, StringComparer.Ordinal)
                .ToArray();

            var files = new MinecraftInstanceFiles(instance);
            var merger = new LaunchContributionMerger();
            foreach (var item in contributors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = new MinecraftLaunchContext
                {
                    Instance = instance,
                    Files = item.Registration.CanReadInstanceFiles
                        ? files
                        : MinecraftInstanceFilesAccessDenied.Instance,
                    CurrentPlan = merger.CreatePlanSnapshot()
                };

                MinecraftLaunchContribution contribution;
                Task<MinecraftLaunchContribution>? contributionTask = null;
                try
                {
                    var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    timeout.CancelAfter(ContributorTimeout);
                    // Both invocation and snapshotting run away from the UI
                    // thread. This also bounds plugins that block before they
                    // return their ValueTask or expose a hostile enumerable.
                    IDisposable invocationLease;
                    try
                    {
                        invocationLease = item.Runtime.EnterInvocation();
                    }
                    catch
                    {
                        timeout.Dispose();
                        throw;
                    }

                    try
                    {
                        contributionTask = Task.Run(async () =>
                        {
                            try
                            {
                                using (invocationLease)
                                {
                                    var built = await item.Registration.Contributor
                                        .BuildAsync(context, timeout.Token)
                                        .ConfigureAwait(false) ??
                                        throw new InvalidDataException("启动贡献返回了 null。");
                                    return Snapshot(built);
                                }
                            }
                            finally
                            {
                                timeout.Dispose();
                            }
                        }, CancellationToken.None);
                    }
                    catch
                    {
                        invocationLease.Dispose();
                        timeout.Dispose();
                        throw;
                    }
                    ObserveBackgroundFailure(contributionTask);
                    contribution = await contributionTask
                        .WaitAsync(ContributorTimeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    await QuarantineAsync(
                        item.Runtime.Manifest.Id,
                        $"启动贡献 {item.Registration.Id} 超时；请重启启动器。");
                    throw new InvalidOperationException(
                        $"插件 {item.Runtime.Manifest.Id} 的启动贡献超时，已中止本次启动。");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await QuarantineAsync(
                        item.Runtime.Manifest.Id,
                        $"启动贡献 {item.Registration.Id} 超时；请重启启动器。");
                    throw new InvalidOperationException(
                        $"插件 {item.Runtime.Manifest.Id} 的启动贡献超时，已中止本次启动。");
                }
                catch (OperationCanceledException)
                {
                    if (contributionTask is { IsCompleted: false })
                    {
                        await QuarantineAsync(
                            item.Runtime.Manifest.Id,
                            $"启动贡献 {item.Registration.Id} 取消后仍未退出；请重启启动器。");
                    }

                    throw;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"插件 {item.Runtime.Manifest.Id} 的启动贡献 " +
                        $"{item.Registration.Id} 失败：{exception.Message}",
                        exception);
                }

                try
                {
                    merger.Merge(
                        item.Runtime.Manifest.Id,
                        item.Registration.Id,
                        contribution);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"插件 {item.Runtime.Manifest.Id} 的启动贡献 " +
                        $"{item.Registration.Id} 无效：{exception.Message}",
                        exception);
                }
            }

            return merger.Build();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<MinecraftLaunchTransform> BuildLaunchTransformAsync(
        MinecraftInstanceDescriptor instance,
        CancellationToken cancellationToken = default)
    {
        var contribution = await BuildLaunchContributionAsync(instance, cancellationToken);
        return new MinecraftLaunchTransform
        {
            ReplaceClasspath = contribution.ReplaceClasspath,
            ClasspathReplacements = contribution.ReplaceClasspathEntries
                .Select(replacement => new NyaLauncher.Core.Launch.MinecraftClasspathReplacement(
                    replacement.ExistingPath,
                    replacement.ReplacementPath))
                .ToArray(),
            RemoveClasspath = contribution.RemoveClasspath,
            PrependClasspath = contribution.PrependClasspath,
            AppendClasspath = contribution.AppendClasspath,
            MainClassOverride = contribution.MainClass,
            JavaExecutableOverride = contribution.JavaExecutable,
            WorkingDirectoryOverride = contribution.WorkingDirectory,
            PrependJvmArguments = contribution.PrependJvmArguments,
            AppendJvmArguments = contribution.AppendJvmArguments,
            PrependGameArguments = contribution.PrependGameArguments,
            AppendGameArguments = contribution.AppendGameArguments,
            EnvironmentVariables = contribution.EnvironmentVariables
        };
    }

    /// <summary>Executes one user-approved persistent instance command.</summary>
    public async Task<MinecraftInstanceActionResult> InvokeInstanceActionAsync(
        string pluginId,
        string extensionId,
        string actionId,
        MinecraftInstanceDescriptor instance,
        IReadOnlyDictionary<string, string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ThrowIfStorageTransition();
            if (!_runtimes.TryGetValue(pluginId, out var runtime) || !runtime.IsStarted)
                return MinecraftInstanceActionResult.Failed("插件未启用。");
            var extension = runtime.InstanceExtensions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, extensionId, StringComparison.OrdinalIgnoreCase));
            if (extension is null || !extension.Actions.Any(action => string.Equals(
                    action.Id,
                    actionId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return MinecraftInstanceActionResult.Failed("实例操作不存在。");
            }

            var cacheDirectory = Path.Combine(
                _catalog.GetPluginDataDirectory(pluginId),
                "cache");
            var session = new MinecraftEditSession(instance, cacheDirectory);
            var context = new MinecraftInstanceActionContext
            {
                ActionId = actionId,
                Instance = instance,
                EditSession = session,
                Arguments = arguments ?? new Dictionary<string, string>()
            };
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(InstanceActionTimeout);
            IDisposable invocationLease;
            try
            {
                invocationLease = runtime.EnterInvocation();
            }
            catch
            {
                timeout.Dispose();
                session.Revoke();
                await session.DisposeAsync();
                throw;
            }

            Task<MinecraftInstanceActionResult> invocation;
            try
            {
                invocation = Task.Run(
                    async () =>
                    {
                        try
                        {
                            using (invocationLease)
                            {
                                return await extension.Extension
                                    .InvokeAsync(context, timeout.Token)
                                    .ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            timeout.Dispose();
                        }
                    },
                    CancellationToken.None);
            }
            catch
            {
                invocationLease.Dispose();
                timeout.Dispose();
                session.Revoke();
                await session.DisposeAsync();
                throw;
            }
            ObserveBackgroundFailure(invocation);
            var disposalDeferred = false;
            MinecraftInstanceActionResult? actionResult = null;
            Exception? actionFailure = null;
            OperationCanceledException? cancellationFailure = null;
            string? cleanupFailure = null;
            try
            {
                try
                {
                    actionResult = await invocation.WaitAsync(
                        InstanceActionTimeout,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is TimeoutException ||
                    exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    var commitWasRevoked = session.Revoke();
                    // Uncooperative plugin code may still hold the session. Its
                    // eventual completion owns delayed cleanup and is observed.
                    DisposeSessionAfter(invocation, session);
                    disposalDeferred = true;
                    await QuarantineAsync(pluginId, $"实例操作 {actionId} 超时；请重启启动器。");
                    actionResult = MinecraftInstanceActionResult.Failed(
                        commitWasRevoked
                            ? "插件实例操作超时，后续提交已撤销；请检查实例，未完成事务会在操作退出后回滚。"
                            : "插件实例操作超时，但事务此前已经提交；请检查实例并按插件说明恢复。");
                }
                catch (OperationCanceledException exception)
                {
                    if (!invocation.IsCompleted)
                    {
                        session.Revoke();
                        DisposeSessionAfter(invocation, session);
                        disposalDeferred = true;
                        await QuarantineAsync(
                            pluginId,
                            $"实例操作 {actionId} 在取消后仍未退出；请重启启动器。");
                    }

                    cancellationFailure = exception;
                }
                catch (Exception exception)
                {
                    actionFailure = exception;
                }
            }
            finally
            {
                if (!disposalDeferred)
                {
                    // Once InvokeAsync has returned, no new commit may start.
                    // A fire-and-forget plugin task can hold the session gate,
                    // so cleanup is bounded while its task remains observed.
                    session.Revoke();
                    cleanupFailure = await DisposeSessionBoundedAsync(
                        pluginId,
                        actionId,
                        session);
                }
            }

            if (cancellationFailure is not null)
                throw cancellationFailure;
            if (actionFailure is not null)
            {
                var failure = FormatInstanceActionFailure(actionFailure);
                return MinecraftInstanceActionResult.Failed(
                    cleanupFailure is null ? failure : $"{failure} {cleanupFailure}");
            }
            if (cleanupFailure is not null)
                return MinecraftInstanceActionResult.Failed(cleanupFailure);
            return actionResult ?? MinecraftInstanceActionResult.Failed("实例操作返回了 null。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MinecraftInstanceActionResult.Failed(
                FormatInstanceActionFailure(exception));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static MinecraftLaunchContribution Snapshot(MinecraftLaunchContribution source)
    {
        var replacements = (source.ReplaceClasspathEntries ??
                throw new InvalidDataException("ReplaceClasspathEntries 不能为 null。"))
            .Take(MaxContributionEntries + 1)
            .ToArray();
        if (replacements.Length > MaxContributionEntries)
            throw new InvalidDataException("单个启动贡献的 classpath 替换项过多。");
        foreach (var replacement in replacements)
        {
            if (replacement is null)
                throw new InvalidDataException("classpath 替换项不能为 null。");
            ValidateContributionString(replacement.ExistingPath, "原 classpath 路径");
            ValidateContributionString(replacement.ReplacementPath, "新 classpath 路径");
        }

        var environment = (source.EnvironmentVariables ??
                throw new InvalidDataException("EnvironmentVariables 不能为 null。"))
            .Take(MaxEnvironmentVariables + 1)
            .ToArray();
        if (environment.Length > MaxEnvironmentVariables)
            throw new InvalidDataException("单个启动贡献的环境变量过多。");
        foreach (var (key, value) in environment)
        {
            ValidateContributionString(key, "环境变量名");
            if (value is not null)
                ValidateContributionString(value, $"环境变量 {key}");
        }
        if (source.MainClass is not null)
            ValidateContributionString(source.MainClass, "MainClass");
        if (source.JavaExecutable is not null)
            ValidateContributionString(source.JavaExecutable, "JavaExecutable");
        if (source.WorkingDirectory is not null)
            ValidateContributionString(source.WorkingDirectory, "WorkingDirectory");

        var replaceClasspath = source.ReplaceClasspath is null
            ? null
            : SnapshotStrings(source.ReplaceClasspath, "ReplaceClasspath");
        var removeClasspath = SnapshotStrings(source.RemoveClasspath, "RemoveClasspath");
        var prependClasspath = SnapshotStrings(source.PrependClasspath, "PrependClasspath");
        var appendClasspath = SnapshotStrings(source.AppendClasspath, "AppendClasspath");
        var prependJvm = SnapshotStrings(source.PrependJvmArguments, "PrependJvmArguments");
        var appendJvm = SnapshotStrings(source.AppendJvmArguments, "AppendJvmArguments");
        var prependGame = SnapshotStrings(source.PrependGameArguments, "PrependGameArguments");
        var appendGame = SnapshotStrings(source.AppendGameArguments, "AppendGameArguments");
        EnsureContributionBudget(
            source,
            replaceClasspath,
            replacements,
            removeClasspath,
            prependClasspath,
            appendClasspath,
            prependJvm,
            appendJvm,
            prependGame,
            appendGame,
            environment);

        return source with
        {
            ReplaceClasspath = replaceClasspath,
            ReplaceClasspathEntries = replacements,
            RemoveClasspath = removeClasspath,
            PrependClasspath = prependClasspath,
            AppendClasspath = appendClasspath,
            PrependJvmArguments = prependJvm,
            AppendJvmArguments = appendJvm,
            PrependGameArguments = prependGame,
            AppendGameArguments = appendGame,
            EnvironmentVariables = new Dictionary<string, string?>(
                environment,
                EnvironmentVariableComparer)
        };
    }

    private static IReadOnlyList<string> SnapshotStrings(
        IEnumerable<string>? source,
        string field)
    {
        if (source is null)
            throw new InvalidDataException($"{field} 不能为 null。");
        var values = source.Take(MaxContributionEntries + 1).ToArray();
        if (values.Length > MaxContributionEntries)
            throw new InvalidDataException($"{field} 的条目过多。");
        foreach (var value in values)
            ValidateContributionString(value, field);
        return values;
    }

    private static void EnsureContributionBudget(
        MinecraftLaunchContribution source,
        IReadOnlyList<string>? replaceClasspath,
        IReadOnlyList<MinecraftClasspathEntryReplacement> replacements,
        IReadOnlyList<string> removeClasspath,
        IReadOnlyList<string> prependClasspath,
        IReadOnlyList<string> appendClasspath,
        IReadOnlyList<string> prependJvm,
        IReadOnlyList<string> appendJvm,
        IReadOnlyList<string> prependGame,
        IReadOnlyList<string> appendGame,
        IReadOnlyList<KeyValuePair<string, string?>> environment)
    {
        var sequences = new IReadOnlyList<string>[]
        {
            replaceClasspath ?? [],
            removeClasspath,
            prependClasspath,
            appendClasspath,
            prependJvm,
            appendJvm,
            prependGame,
            appendGame
        };
        var entryCount = sequences.Sum(sequence => sequence.Count) +
                         replacements.Count + environment.Count;
        if (entryCount > MaxContributionEntries)
            throw new InvalidDataException(
                $"单个启动贡献总计最多包含 {MaxContributionEntries} 个条目。");

        long characterCount = sequences.Sum(sequence =>
            sequence.Sum(value => (long)value.Length));
        characterCount += replacements.Sum(replacement =>
            (long)replacement.ExistingPath.Length + replacement.ReplacementPath.Length);
        characterCount += environment.Sum(pair =>
            (long)pair.Key.Length + (pair.Value?.Length ?? 0));
        characterCount += source.MainClass?.Length ?? 0;
        characterCount += source.JavaExecutable?.Length ?? 0;
        characterCount += source.WorkingDirectory?.Length ?? 0;
        if (characterCount > MaxContributionCharacters)
            throw new InvalidDataException(
                $"单个启动贡献的文本总量不能超过 {MaxContributionCharacters} 个字符。");
    }

    private static void ValidateContributionString(string? value, string field)
    {
        if (value is null)
            throw new InvalidDataException($"{field} 包含 null。");
        if (value.Length > MaxContributionStringLength)
            throw new InvalidDataException($"{field} 包含过长字符串。");
    }

    private static StringComparer EnvironmentVariableComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private async Task<string?> DisposeSessionBoundedAsync(
        string pluginId,
        string actionId,
        MinecraftEditSession session)
    {
        Task cleanup;
        try
        {
            cleanup = session.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            await QuarantineAsync(
                pluginId,
                $"实例操作 {actionId} 的事务清理失败：{exception.Message}");
            return "高风险：实例事务清理失败，插件已隔离；请重启启动器并检查实例。";
        }

        ObserveBackgroundFailure(cleanup);
        try
        {
            await cleanup.WaitAsync(InstanceSessionCleanupTimeout);
            return null;
        }
        catch (TimeoutException)
        {
            await QuarantineAsync(
                pluginId,
                $"实例操作 {actionId} 返回后仍占用事务；请重启启动器。");
            return "高风险：插件返回后仍占用实例事务；后续提交已撤销，插件已隔离。请重启启动器并检查实例。";
        }
        catch (Exception exception)
        {
            await QuarantineAsync(
                pluginId,
                $"实例操作 {actionId} 的事务清理失败：{exception.Message}");
            return $"高风险：实例事务清理失败，插件已隔离；请检查实例。原因：{exception.Message}";
        }
    }

    private static string FormatInstanceActionFailure(Exception exception)
    {
        if (!ContainsRollbackFailure(exception))
            return exception.Message;

        return "高风险：实例事务执行失败且未能完全回滚，实例可能已部分修改。" +
               $"请立即检查或从备份恢复实例。原始错误：{exception.Message}";
    }

    private static bool ContainsRollbackFailure(Exception exception)
    {
        if (exception.Data["NyaLauncher.InstanceRollbackErrors"] is AggregateException)
            return true;
        if (exception is AggregateException aggregate &&
            aggregate.InnerExceptions.Any(ContainsRollbackFailure))
        {
            return true;
        }

        return exception.InnerException is not null &&
               ContainsRollbackFailure(exception.InnerException);
    }

    private static void DisposeSessionAfter(Task invocation, MinecraftEditSession session)
    {
        var cleanup = invocation.ContinueWith(
            async _ => await session.DisposeAsync(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
        ObserveBackgroundFailure(cleanup);
    }

    private sealed class LaunchContributionMerger
    {
        private readonly List<string> _removeClasspath = [];
        private readonly List<string> _prependClasspath = [];
        private readonly List<string> _appendClasspath = [];
        private readonly HashSet<string> _removeClasspathSet = new(PathComparer);
        private readonly HashSet<string> _prependClasspathSet = new(PathComparer);
        private readonly HashSet<string> _appendClasspathSet = new(PathComparer);
        private readonly Dictionary<string, MinecraftClasspathEntryReplacement> _classpathReplacements =
            new(PathComparer);
        private readonly Dictionary<string, string> _classpathReplacementOwners =
            new(PathComparer);
        private readonly List<string> _prependJvm = [];
        private readonly List<string> _appendJvm = [];
        private readonly List<string> _prependGame = [];
        private readonly List<string> _appendGame = [];
        private readonly Dictionary<string, string?> _environment =
            new(EnvironmentVariableComparer);
        private IReadOnlyList<string>? _replaceClasspath;
        private string? _replaceClasspathOwner;
        private string? _mainClass;
        private string? _mainClassOwner;
        private string? _javaExecutable;
        private string? _javaOwner;
        private string? _workingDirectory;
        private string? _workingDirectoryOwner;
        private int _mergedEntries;
        private long _mergedCharacters;

        public void Merge(
            string pluginId,
            string contributorId,
            MinecraftLaunchContribution contribution)
        {
            var owner = $"{pluginId} ({contributorId})";
            var contributionEntries = CountEntries(contribution);
            var contributionCharacters = CountCharacters(contribution);
            if (_mergedEntries + contributionEntries > MaxMergedEntries ||
                _mergedCharacters + contributionCharacters > MaxMergedCharacters)
            {
                throw new InvalidDataException(
                    "所有插件合并后的启动贡献超过宿主资源预算。");
            }
            _mergedEntries += contributionEntries;
            _mergedCharacters += contributionCharacters;

            if (contribution.ReplaceClasspath is not null)
            {
                if (_replaceClasspath is not null &&
                    !_replaceClasspath.SequenceEqual(
                        contribution.ReplaceClasspath,
                        PathComparer))
                {
                    throw Conflict("classpath 替换", _replaceClasspathOwner!, owner);
                }

                _replaceClasspath = [.. contribution.ReplaceClasspath];
                _replaceClasspathOwner ??= owner;
            }

            MergeExclusive(ref _mainClass, ref _mainClassOwner, contribution.MainClass, owner, "main class");
            MergeExclusive(ref _javaExecutable, ref _javaOwner, contribution.JavaExecutable, owner, "Java 路径");
            MergeExclusive(
                ref _workingDirectory,
                ref _workingDirectoryOwner,
                contribution.WorkingDirectory,
                owner,
                "工作目录");

            MergeDistinct(_removeClasspath, _removeClasspathSet, contribution.RemoveClasspath);
            MergeDistinct(_prependClasspath, _prependClasspathSet, contribution.PrependClasspath);
            MergeDistinct(_appendClasspath, _appendClasspathSet, contribution.AppendClasspath);
            foreach (var replacement in contribution.ReplaceClasspathEntries)
            {
                if (replacement is null ||
                    string.IsNullOrWhiteSpace(replacement.ExistingPath) ||
                    string.IsNullOrWhiteSpace(replacement.ReplacementPath))
                {
                    throw new InvalidDataException($"插件 {owner} 提供了无效 classpath 精确替换。");
                }

                if (_classpathReplacements.TryGetValue(replacement.ExistingPath, out var existing) &&
                    !PathComparer.Equals(existing.ReplacementPath, replacement.ReplacementPath))
                {
                    throw Conflict(
                        $"classpath 项 {replacement.ExistingPath}",
                        _classpathReplacementOwners[replacement.ExistingPath],
                        owner);
                }

                _classpathReplacements[replacement.ExistingPath] = replacement with { };
                _classpathReplacementOwners.TryAdd(replacement.ExistingPath, owner);
            }
            _prependJvm.AddRange(contribution.PrependJvmArguments);
            _appendJvm.AddRange(contribution.AppendJvmArguments);
            _prependGame.AddRange(contribution.PrependGameArguments);
            _appendGame.AddRange(contribution.AppendGameArguments);

            foreach (var (key, value) in contribution.EnvironmentVariables)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Contains('='))
                    throw new InvalidDataException($"插件 {owner} 提供了无效环境变量名。");
                if (_environment.TryGetValue(key, out var existing) &&
                    !string.Equals(existing, value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"环境变量 {key} 的插件贡献冲突（{existing ?? "删除"} / {value ?? "删除"}）。");
                }

                _environment[key] = value;
            }
        }

        public MinecraftLaunchPlanSnapshot CreatePlanSnapshot()
        {
            var classpath = (_replaceClasspath ?? [])
                .Where(path => !_removeClasspath.Contains(path, PathComparer))
                .Select(path => _classpathReplacements.TryGetValue(path, out var replacement)
                    ? replacement.ReplacementPath
                    : path)
                .PrependRange(_prependClasspath)
                .Concat(_appendClasspath)
                .Distinct(PathComparer)
                .ToArray();
            return new MinecraftLaunchPlanSnapshot
            {
                IsClasspathReplaced = _replaceClasspath is not null,
                Classpath = classpath,
                MainClass = _mainClass,
                JavaExecutable = _javaExecutable,
                WorkingDirectory = _workingDirectory,
                JvmArguments = [.. _prependJvm, .. _appendJvm],
                GameArguments = [.. _prependGame, .. _appendGame],
                EnvironmentVariables = _environment
                    .Where(pair => pair.Value is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value!, EnvironmentVariableComparer)
            };
        }

        public MinecraftLaunchContribution Build() => new()
        {
            ReplaceClasspath = _replaceClasspath,
            ReplaceClasspathEntries = _classpathReplacements.Values.ToArray(),
            RemoveClasspath = _removeClasspath.ToArray(),
            PrependClasspath = _prependClasspath.ToArray(),
            AppendClasspath = _appendClasspath.ToArray(),
            MainClass = _mainClass,
            JavaExecutable = _javaExecutable,
            WorkingDirectory = _workingDirectory,
            PrependJvmArguments = _prependJvm.ToArray(),
            AppendJvmArguments = _appendJvm.ToArray(),
            PrependGameArguments = _prependGame.ToArray(),
            AppendGameArguments = _appendGame.ToArray(),
            EnvironmentVariables = new Dictionary<string, string?>(
                _environment,
                EnvironmentVariableComparer)
        };

        private static void MergeExclusive(
            ref string? current,
            ref string? currentOwner,
            string? value,
            string owner,
            string field)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (current is not null && !string.Equals(current, value, PathComparison(field)))
                throw Conflict(field, currentOwner!, owner);
            current = value;
            currentOwner ??= owner;
        }

        private static void MergeDistinct(
            ICollection<string> target,
            ISet<string> seen,
            IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                    target.Add(value);
            }
        }

        private static InvalidOperationException Conflict(
            string field,
            string firstOwner,
            string secondOwner) =>
            new($"插件启动贡献冲突：{field} 同时由 {firstOwner} 和 {secondOwner} 设置。");

        private static int CountEntries(MinecraftLaunchContribution contribution) =>
            (contribution.ReplaceClasspath?.Count ?? 0) +
            contribution.ReplaceClasspathEntries.Count +
            contribution.RemoveClasspath.Count +
            contribution.PrependClasspath.Count +
            contribution.AppendClasspath.Count +
            contribution.PrependJvmArguments.Count +
            contribution.AppendJvmArguments.Count +
            contribution.PrependGameArguments.Count +
            contribution.AppendGameArguments.Count +
            contribution.EnvironmentVariables.Count;

        private static long CountCharacters(MinecraftLaunchContribution contribution) =>
            (contribution.ReplaceClasspath?.Sum(value => (long)value.Length) ?? 0) +
            contribution.ReplaceClasspathEntries.Sum(replacement =>
                (long)replacement.ExistingPath.Length + replacement.ReplacementPath.Length) +
            contribution.RemoveClasspath.Sum(value => (long)value.Length) +
            contribution.PrependClasspath.Sum(value => (long)value.Length) +
            contribution.AppendClasspath.Sum(value => (long)value.Length) +
            contribution.PrependJvmArguments.Sum(value => (long)value.Length) +
            contribution.AppendJvmArguments.Sum(value => (long)value.Length) +
            contribution.PrependGameArguments.Sum(value => (long)value.Length) +
            contribution.AppendGameArguments.Sum(value => (long)value.Length) +
            contribution.EnvironmentVariables.Sum(pair =>
                (long)pair.Key.Length + (pair.Value?.Length ?? 0)) +
            (contribution.MainClass?.Length ?? 0) +
            (contribution.JavaExecutable?.Length ?? 0) +
            (contribution.WorkingDirectory?.Length ?? 0);

        private static StringComparison PathComparison(string field) =>
            field is "Java 路径" or "工作目录" && OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static StringComparer PathComparer =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}

internal sealed class MinecraftInstanceFilesAccessDenied : IMinecraftInstanceFiles
{
    public static MinecraftInstanceFilesAccessDenied Instance { get; } = new();

    public ValueTask<bool> ExistsAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(CreateException());

    public ValueTask<Stream> OpenReadAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<Stream>(CreateException());

    public async IAsyncEnumerable<MinecraftFileEntry> EnumerateAsync(
        MinecraftInstancePath directory,
        string searchPattern = "*",
        bool recursive = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        await Task.FromException(CreateException());
        yield break;
    }

    private static UnauthorizedAccessException CreateException() => new(
        $"插件未获 {PluginCapabilities.MinecraftInstanceRead} 能力，不能通过宿主读取实例文件。");
}

internal static class PluginEnumerableExtensions
{
    public static IEnumerable<T> PrependRange<T>(
        this IEnumerable<T> source,
        IEnumerable<T> values) => values.Concat(source);
}
