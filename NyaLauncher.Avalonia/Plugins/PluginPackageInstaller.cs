using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Plugins;

internal sealed class PluginPackageInstallResult
{
    private readonly Action _complete;
    private readonly Func<string?> _rollback;
    private int _finished;

    internal PluginPackageInstallResult(
        PluginManifest manifest,
        string packageDirectory,
        bool updated,
        Action complete,
        Func<string?> rollback)
    {
        Manifest = manifest;
        PackageDirectory = packageDirectory;
        Updated = updated;
        _complete = complete;
        _rollback = rollback;
    }

    public PluginManifest Manifest { get; }

    public string PackageDirectory { get; }

    public bool Updated { get; }

    public string? Complete()
    {
        if (Interlocked.CompareExchange(ref _finished, 3, 0) != 0)
            return "插件安装事务已经结束，不能再次提交。";
        try
        {
            _complete();
            Volatile.Write(ref _finished, 1);
            return null;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _finished, 0);
            return exception.Message;
        }
    }

    public string? Rollback()
    {
        return Interlocked.CompareExchange(ref _finished, 2, 0) == 0
            ? _rollback()
            : "插件安装事务已经结束，不能再次回滚。";
    }
}

internal sealed class PluginPackageRemovalResult
{
    private readonly Action _complete;
    private readonly Func<string?> _rollback;
    private int _finished;

    internal PluginPackageRemovalResult(Action complete, Func<string?> rollback)
    {
        _complete = complete;
        _rollback = rollback;
    }

    public string? Complete()
    {
        if (Interlocked.CompareExchange(ref _finished, 3, 0) != 0)
            return "插件卸载事务已经结束，不能再次提交。";
        try
        {
            _complete();
            Volatile.Write(ref _finished, 1);
            return null;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _finished, 0);
            return exception.Message;
        }
    }

    public string? Rollback() =>
        Interlocked.CompareExchange(ref _finished, 2, 0) == 0
            ? _rollback()
            : "插件卸载事务已经结束，不能再次回滚。";
}

/// <summary>
/// Expands an already registry-described package into an isolated same-volume
/// transaction directory, asks the normal catalog to validate it, and then
/// swaps the complete package directory with rollback on failure.
/// </summary>
internal static class PluginPackageInstaller
{
    private const int TransactionJournalVersion = 2;
    private const int MaximumEntries = 4096;
    private const long MaximumEntryBytes = 512L * 1024 * 1024;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private const int MaximumPathLength = 512;
    private const int MaximumCompressionRatio = 200;
    private const int MaximumTransactionJournalBytes = 32 * 1024;
    private static readonly HashSet<string> WindowsReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions TransactionJournalJsonOptions = new()
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string? RecoverInterruptedTransactions(PluginCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            return RecoverInterruptedTransactionsCore(catalog);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return $"无法检查插件安装事务：{exception.Message}";
        }
    }

    private static string? RecoverInterruptedTransactionsCore(PluginCatalog catalog)
    {
        var transactionsRoot = Path.Combine(catalog.RootDirectory, "repository", "transactions");
        if (!Directory.Exists(transactionsRoot))
            return null;

        var failures = new List<string>();
        try
        {
            RejectReparsePoint(catalog.RootDirectory, "插件根目录");
            RejectReparsePoint(Path.Combine(catalog.RootDirectory, "repository"), "插件仓库工作目录");
            RejectReparsePoint(transactionsRoot, "插件仓库事务目录");
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"无法安全恢复插件安装事务：{exception.Message}";
        }

        FileStream installationLock;
        try
        {
            installationLock = AcquireInstallationLock(
                Path.Combine(catalog.RootDirectory, "repository"));
        }
        catch (IOException)
        {
            return "另一个 NyaLauncher 进程正在安装插件，暂不恢复历史事务";
        }
        using (installationLock)
        {
            foreach (var transactionDirectory in Directory.EnumerateDirectories(transactionsRoot)
                         .Take(128))
            {
                var name = Path.GetFileName(transactionDirectory);
                if (!Guid.TryParseExact(name, "N", out _))
                {
                    failures.Add($"忽略未知事务目录 {name}");
                    continue;
                }

                try
                {
                    RejectReparsePoint(transactionDirectory, "插件安装事务目录");
                    var backupDirectory = Path.Combine(transactionDirectory, "backup");
                    var journalPath = Path.Combine(transactionDirectory, "journal.json");
                    if (!File.Exists(journalPath))
                    {
                        // A crash during download/extraction happens before any
                        // package rename and cannot contain an old-package backup.
                        if (!Directory.Exists(backupDirectory))
                            TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
                        else
                            failures.Add($"事务 {name} 缺少日志，旧包备份已保留供人工恢复");
                        continue;
                    }

                    var journalBytes = ReadJournalBytes(journalPath);
                    var journal = JsonSerializer.Deserialize<TransactionJournal>(
                                      journalBytes,
                                      TransactionJournalJsonOptions) ??
                                  throw new InvalidDataException("事务日志为空。");
                    if (journal.Version != TransactionJournalVersion ||
                        string.IsNullOrWhiteSpace(journal.TargetDirectoryName) ||
                        Path.GetFileName(journal.TargetDirectoryName) != journal.TargetDirectoryName ||
                        journal.Phase is not ("prepared" or "committed") ||
                        journal.Operation is not (null or "install" or "remove"))
                    {
                        throw new InvalidDataException("事务日志版本或目标目录无效。");
                    }

                    ValidateRemovalStateSnapshot(journal);

                    var targetDirectory = ResolveTargetDirectory(
                        catalog.PackagesDirectory,
                        journal.TargetDirectoryName,
                        Path.Combine(catalog.PackagesDirectory, journal.TargetDirectoryName));
                    var targetExists = Directory.Exists(targetDirectory);
                    var backupExists = Directory.Exists(backupDirectory);
                    var isRemoval = string.Equals(
                        journal.Operation,
                        "remove",
                        StringComparison.Ordinal);
                    if (string.Equals(journal.Phase, "committed", StringComparison.Ordinal) &&
                        isRemoval)
                    {
                        if (targetExists)
                        {
                            failures.Add(
                                $"已提交卸载事务 {name} 的目标目录意外存在，未删除任何目录");
                            continue;
                        }
                        if (backupExists)
                        {
                            DeleteTreeWithoutFollowingLinks(new DirectoryInfo(backupDirectory));
                            backupExists = false;
                        }
                    }
                    else if (string.Equals(journal.Phase, "committed", StringComparison.Ordinal))
                    {
                        if (!targetExists && backupExists)
                        {
                            // A committed update without its new target is not
                            // viable. Restore the only complete package left.
                            Directory.Move(backupDirectory, targetDirectory);
                            targetExists = true;
                            backupExists = false;
                        }
                        else if (targetExists && backupExists)
                        {
                            DeleteTreeWithoutFollowingLinks(new DirectoryInfo(backupDirectory));
                            backupExists = false;
                        }
                    }
                    else if (isRemoval)
                    {
                        if (targetExists && backupExists)
                        {
                            failures.Add(
                                $"待回滚卸载事务 {name} 同时存在目标和备份，未覆盖任何目录");
                            continue;
                        }
                        if (!targetExists && backupExists)
                        {
                            Directory.Move(backupDirectory, targetDirectory);
                            targetExists = true;
                            backupExists = false;
                        }
                        else if (!targetExists)
                        {
                            failures.Add($"待回滚卸载事务 {name} 的目标和备份均已丢失");
                            continue;
                        }

                        if (journal.HadPreviousState == true)
                        {
                            catalog.RestoreStateSnapshot(
                                journal.PluginId!,
                                journal.PreviousState!);
                        }
                        else
                        {
                            catalog.RemoveState(journal.PluginId!);
                        }
                    }
                    else if (journal.HadExistingTarget && targetExists && backupExists)
                    {
                        // Both directories only remain when the process exited
                        // after committing a replacement but before completing
                        // the transaction, or while rollback was pending. The
                        // old package is authoritative until the manager has
                        // refreshed successfully and explicitly calls Complete.
                        DeleteTreeWithoutFollowingLinks(new DirectoryInfo(targetDirectory));
                        Directory.Move(backupDirectory, targetDirectory);
                        targetExists = true;
                        backupExists = false;
                    }
                    else if (!journal.HadExistingTarget && targetExists && !backupExists)
                    {
                        // A prepared transaction without a backup is a new
                        // installation that was never confirmed by the manager.
                        // Remove it instead of orphaning a potentially partial
                        // package after discarding the recovery journal.
                        DeleteTreeWithoutFollowingLinks(new DirectoryInfo(targetDirectory));
                        targetExists = false;
                    }
                    else if (journal.HadExistingTarget && backupExists && !targetExists)
                    {
                        Directory.Move(backupDirectory, targetDirectory);
                        targetExists = true;
                        backupExists = false;
                    }
                    else if (journal.HadExistingTarget && !targetExists && !backupExists)
                    {
                        failures.Add($"事务 {name} 声明安装前存在插件，但目标和备份均已丢失");
                        continue;
                    }
                    else if (!journal.HadExistingTarget && backupExists)
                    {
                        failures.Add($"事务 {name} 的新安装不应包含旧包备份，已保留供人工检查");
                        continue;
                    }

                    if (targetExists || !backupExists)
                        TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
                }
                catch (Exception exception) when (exception is
                    IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    failures.Add($"事务 {name} 恢复失败：{exception.Message}");
                }
            }

            if (Directory.EnumerateDirectories(transactionsRoot).Skip(128).Any())
                failures.Add("待恢复的插件安装事务超过 128 个，其余未处理");
        }
        return failures.Count == 0 ? null : string.Join("；", failures);
    }

    public static PluginPackageRemovalResult StageRemoval(
        PluginCatalog catalog,
        string packageDirectory,
        string pluginId,
        bool hadPreviousState,
        PluginStateEntry previousState)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var validatedPreviousState = hadPreviousState
            ? PluginCatalog.CloneValidatedStateSnapshot(pluginId, previousState)
            : null;
        var repositoryRoot = Path.Combine(catalog.RootDirectory, "repository");
        var transactionsRoot = Path.Combine(repositoryRoot, "transactions");
        RejectReparsePoint(catalog.RootDirectory, "插件根目录");
        Directory.CreateDirectory(repositoryRoot);
        RejectReparsePoint(repositoryRoot, "插件仓库工作目录");
        Directory.CreateDirectory(transactionsRoot);
        RejectReparsePoint(transactionsRoot, "插件仓库事务目录");
        var installationLock = AcquireInstallationLock(repositoryRoot);
        var lockTransferred = false;
        try
        {
            var targetDirectory = ResolveTargetDirectory(
                catalog.PackagesDirectory,
                Path.GetFileName(packageDirectory),
                packageDirectory);
            if (!Directory.Exists(targetDirectory))
                throw new DirectoryNotFoundException("插件安装目录不存在。");

            var transactionDirectory = Path.Combine(
                transactionsRoot,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transactionDirectory);
            RejectReparsePoint(transactionDirectory, "插件卸载事务目录");
            var backupDirectory = Path.Combine(transactionDirectory, "backup");
            var journalPath = Path.Combine(transactionDirectory, "journal.json");
            var moved = false;
            var preserveTransaction = false;
            try
            {
                WriteJournalDurably(
                    journalPath,
                    new TransactionJournal
                    {
                        Version = TransactionJournalVersion,
                        Operation = "remove",
                        TargetDirectoryName = Path.GetFileName(targetDirectory),
                        HadExistingTarget = true,
                        Phase = "prepared",
                        PluginId = pluginId,
                        HadPreviousState = hadPreviousState,
                        PreviousState = validatedPreviousState
                    });
                Directory.Move(targetDirectory, backupDirectory);
                moved = true;
                preserveTransaction = true;
                lockTransferred = true;
                var ownedLock = installationLock;
                return new PluginPackageRemovalResult(
                    complete: () =>
                    {
                        WriteJournalDurably(
                            journalPath,
                            new TransactionJournal
                            {
                                Version = TransactionJournalVersion,
                                Operation = "remove",
                                TargetDirectoryName = Path.GetFileName(targetDirectory),
                                HadExistingTarget = true,
                                Phase = "committed",
                                PluginId = pluginId
                            });
                        TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
                        DisposeLockNoThrow(ownedLock);
                    },
                    rollback: () =>
                    {
                        try
                        {
                            if (Directory.Exists(targetDirectory))
                                return "卸载回滚目标已被其他目录占用；原包备份仍保留。";
                            if (!Directory.Exists(backupDirectory))
                                return "卸载回滚所需的原包备份不存在。";
                            Directory.Move(backupDirectory, targetDirectory);
                            if (hadPreviousState)
                                catalog.RestoreStateSnapshot(pluginId, validatedPreviousState!);
                            else
                                catalog.RemoveState(pluginId);
                            TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
                            return null;
                        }
                        catch (Exception exception) when (exception is
                            IOException or UnauthorizedAccessException or InvalidDataException)
                        {
                            return exception.Message;
                        }
                        finally
                        {
                            DisposeLockNoThrow(ownedLock);
                        }
                    });
            }
            catch
            {
                if (moved && !Directory.Exists(targetDirectory) && Directory.Exists(backupDirectory))
                {
                    try
                    {
                        Directory.Move(backupDirectory, targetDirectory);
                        moved = false;
                    }
                    catch (Exception exception) when (exception is
                        IOException or UnauthorizedAccessException)
                    {
                        preserveTransaction = true;
                        throw new IOException(
                            $"插件卸载事务启动失败，且原包未能自动恢复。备份保留在 {backupDirectory}。",
                            exception);
                    }
                }
                throw;
            }
            finally
            {
                if (!preserveTransaction)
                    TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
            }
        }
        finally
        {
            if (!lockTransferred)
                installationLock.Dispose();
        }
    }

    public static async Task<PluginPackageInstallResult> InstallAsync(
        PluginCatalog catalog,
        PluginRepositoryClient client,
        RepositoryPlugin plugin,
        RepositoryRelease release,
        string? existingPackageDirectory,
        IProgress<RepositoryDownloadProgress>? progress,
        CancellationToken cancellationToken,
        Action? beforeCommit = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(release);

        var repositoryRoot = Path.Combine(catalog.RootDirectory, "repository");
        var transactionsRoot = Path.Combine(repositoryRoot, "transactions");
        RejectReparsePoint(catalog.RootDirectory, "插件根目录");
        Directory.CreateDirectory(repositoryRoot);
        RejectReparsePoint(repositoryRoot, "插件仓库工作目录");
        Directory.CreateDirectory(transactionsRoot);
        RejectReparsePoint(transactionsRoot, "插件仓库事务目录");
        var installationLock = AcquireInstallationLock(repositoryRoot);
        var lockTransferred = false;
        try
        {
            var transactionDirectory = Path.Combine(transactionsRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transactionDirectory);
            RejectReparsePoint(transactionDirectory, "插件安装事务目录");
            var archivePath = Path.Combine(transactionDirectory, "package.zip");
            var journalPath = Path.Combine(transactionDirectory, "journal.json");
            var inspectionStorage = Path.Combine(transactionDirectory, "inspection");
            var stagedPackage = Path.Combine(
                inspectionStorage,
                "plugins",
                "packages",
                plugin.Id);
            var backupDirectory = Path.Combine(transactionDirectory, "backup");
            var oldMoved = false;
            var newMoved = false;
            var preserveTransaction = false;
            string? targetDirectory = null;

            try
            {
                await client.DownloadPackageAsync(
                    plugin,
                    release,
                    archivePath,
                    progress,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(stagedPackage);
                await ExtractSafelyAsync(archivePath, stagedPackage, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureTreeHasNoReparsePoints(stagedPackage);

                var inspectionCatalog = new PluginCatalog(inspectionStorage);
                var inspected = inspectionCatalog.Scan();
                if (inspected.Count != 1 ||
                    inspected[0].Manifest is null ||
                    inspected[0].Status is PluginStatus.Invalid or PluginStatus.Incompatible)
                {
                    var diagnostic = inspected.FirstOrDefault()?.Error ?? "包内未发现有效插件清单。";
                    throw new InvalidDataException($"下载的插件包未通过宿主校验：{diagnostic}");
                }

                var manifest = inspected[0].Manifest!;
                ValidateManifestMatchesIndex(plugin, release, manifest, stagedPackage);
                // This launcher-owned provenance travels with the package
                // directory through the same rename/backup transaction. A
                // rollback therefore restores the old package and old origin
                // together, and a crash recovery never has to infer identity
                // from a mutable URL or plugin ID alone.
                PluginCatalog.WriteInstallOrigin(
                    stagedPackage,
                    PluginCatalog.CreateInstallOrigin(plugin, release));

                targetDirectory = ResolveTargetDirectory(
                    catalog.PackagesDirectory,
                    plugin.Id,
                    existingPackageDirectory);
                beforeCommit?.Invoke();
                var hadExistingTarget = Directory.Exists(targetDirectory);
                WriteJournalDurably(
                    journalPath,
                    new TransactionJournal
                    {
                        Version = TransactionJournalVersion,
                        Operation = "install",
                        TargetDirectoryName = Path.GetFileName(targetDirectory),
                        HadExistingTarget = hadExistingTarget,
                        Phase = "prepared"
                    });
                if (hadExistingTarget)
                {
                    Directory.Move(targetDirectory, backupDirectory);
                    oldMoved = true;
                }

                // Cancellation is no longer observed after the first directory
                // rename: the commit must complete or roll back as one operation.
                Directory.Move(stagedPackage, targetDirectory);
                newMoved = true;
                preserveTransaction = true;
                lockTransferred = true;
                var ownedLock = installationLock;
                return new PluginPackageInstallResult(
                    manifest,
                    targetDirectory,
                    oldMoved,
                    complete: () =>
                    {
                        // Keep the cross-process install lock when durable
                        // commit recording fails. The manager will immediately
                        // invoke Rollback, which releases the lock only after
                        // restoring the old directory.
                        WriteJournalDurably(
                            journalPath,
                            new TransactionJournal
                            {
                                Version = TransactionJournalVersion,
                                Operation = "install",
                                TargetDirectoryName = Path.GetFileName(targetDirectory),
                                HadExistingTarget = oldMoved,
                                Phase = "committed"
                            });
                        TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
                        DisposeLockNoThrow(ownedLock);
                    },
                    rollback: () =>
                    {
                        try
                        {
                            return RollbackCommittedInstallation(
                                transactionsRoot,
                                transactionDirectory,
                                targetDirectory,
                                backupDirectory,
                                oldMoved);
                        }
                        finally
                        {
                            DisposeLockNoThrow(ownedLock);
                        }
                    });
            }
            catch
            {
                if (oldMoved && !newMoved && targetDirectory is not null &&
                    !Directory.Exists(targetDirectory) && Directory.Exists(backupDirectory))
                {
                    try
                    {
                        Directory.Move(backupDirectory, targetDirectory);
                        oldMoved = false;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        preserveTransaction = true;
                        throw new IOException(
                            $"插件安装失败，且旧包未能自动恢复。备份保留在 {backupDirectory}。",
                            exception);
                    }
                }

                throw;
            }
            finally
            {
                if (!preserveTransaction)
                    TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
            }
        }
        finally
        {
            if (!lockTransferred)
                installationLock.Dispose();
        }
    }

    private static async Task ExtractSafelyAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException($"插件 ZIP 必须包含 1 到 {MaximumEntries} 个条目。");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = ValidateEntry(entry);
            if (!seen.Add(relativePath))
                throw new InvalidDataException($"插件 ZIP 包含重复或仅大小写不同的路径：{relativePath}");

            declaredTotal = checked(declaredTotal + entry.Length);
            if (declaredTotal > MaximumExpandedBytes)
                throw new InvalidDataException("插件 ZIP 解压后超过 1 GiB 上限。");

            var isDirectory = entry.FullName.EndsWith('/');
            var destinationPath = ResolveExtractionPath(destinationDirectory, relativePath);
            if (isDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath) ??
                         throw new InvalidDataException("ZIP 条目缺少父目录。");
            Directory.CreateDirectory(parent);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long actual = 0;
            try
            {
                int read;
                while ((read = await source.ReadAsync(
                           buffer.AsMemory(0, buffer.Length),
                           cancellationToken)) > 0)
                {
                    actual += read;
                    if (actual > entry.Length || actual > MaximumEntryBytes)
                        throw new InvalidDataException($"ZIP 条目 {entry.FullName} 超过大小上限。");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (actual != entry.Length)
                throw new InvalidDataException($"ZIP 条目 {entry.FullName} 的长度与目录记录不一致。");
        }
    }

    private static string ValidateEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > MaximumPathLength ||
            !string.Equals(name, name.Normalize(NormalizationForm.FormC), StringComparison.Ordinal) ||
            name.Contains('\\') ||
            name.Contains('\0') ||
            name.StartsWith('/') ||
            Path.IsPathFullyQualified(name))
        {
            throw new InvalidDataException($"ZIP 包含不安全路径：{name}");
        }

        var isDirectory = name.EndsWith('/');
        var pathWithoutDirectoryMarker = isDirectory ? name[..^1] : name;
        var segments = pathWithoutDirectoryMarker.Split('/');
        if (segments.Length == 0 || segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".." ||
                segment.Contains(':') ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                WindowsReservedNames.Contains(segment.Split('.', 2)[0])))
        {
            throw new InvalidDataException($"ZIP 包含不安全路径：{name}");
        }

        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType != 0 && unixType != 0x8000 && unixType != 0x4000)
            throw new InvalidDataException($"ZIP 条目 {name} 是符号链接或特殊文件。");
        if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"ZIP 条目 {name} 是重解析点。");
        if (isDirectory && entry.Length != 0)
            throw new InvalidDataException($"ZIP 目录条目 {name} 声明了文件内容。");
        if (!isDirectory && unixType == 0x4000)
            throw new InvalidDataException($"ZIP 条目 {name} 的目录标记不一致。");
        if (isDirectory && unixType == 0x8000)
            throw new InvalidDataException($"ZIP 条目 {name} 的文件标记不一致。");
        if (entry.Length > MaximumEntryBytes)
            throw new InvalidDataException($"ZIP 条目 {name} 超过 512 MiB 上限。");
        if (entry.Length > 1024 &&
            (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > MaximumCompressionRatio))
        {
            throw new InvalidDataException($"ZIP 条目 {name} 的压缩比异常。");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static string ResolveExtractionPath(string rootDirectory, string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("ZIP 条目试图逃逸解压目录。");
        return path;
    }

    private static void ValidateManifestMatchesIndex(
        RepositoryPlugin plugin,
        RepositoryRelease release,
        PluginManifest manifest,
        string packageDirectory)
    {
        if (File.Exists(Path.Combine(packageDirectory, PluginCatalog.InstallOriginFileName)))
        {
            throw new InvalidDataException(
                $"插件 ZIP 不能携带启动器所有的 {PluginCatalog.InstallOriginFileName}。");
        }
        if (!string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal) ||
            !string.Equals(release.Version, manifest.Version, StringComparison.Ordinal) ||
            release.Compatibility.ManifestVersion != manifest.ManifestVersion ||
            !string.Equals(release.Compatibility.ApiVersion, manifest.ApiVersion, StringComparison.Ordinal) ||
            !string.Equals(
                release.Compatibility.MinimumLauncherVersion,
                manifest.MinimumLauncherVersion,
                StringComparison.Ordinal) ||
            !SetEquals(release.RequiredCapabilities, manifest.RequiredCapabilities) ||
            !SetEquals(release.OptionalCapabilities, manifest.OptionalCapabilities))
        {
            throw new InvalidDataException(
                "下载包内 plugin.json 的 ID、版本、兼容性或能力与仓库索引不一致。");
        }

        if (Directory.EnumerateFiles(
                packageDirectory,
                "*",
                SearchOption.AllDirectories).Any(path => string.Equals(
                    Path.GetFileName(path),
                    "NyaLauncher.Plugin.Abstractions.dll",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "插件包不能私自携带 NyaLauncher.Plugin.Abstractions.dll，请使用宿主提供的 API 程序集。");
        }
    }

    private static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right) =>
        new HashSet<string>(left, StringComparer.OrdinalIgnoreCase).SetEquals(right);

    private static string ResolveTargetDirectory(
        string packagesDirectory,
        string pluginId,
        string? existingPackageDirectory)
    {
        var packagesRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packagesDirectory));
        var target = Path.GetFullPath(existingPackageDirectory ?? Path.Combine(packagesRoot, pluginId));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(target), packagesRoot, comparison) ||
            !target.StartsWith(packagesRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException("插件安装目标不是 packages 的直接子目录。");
        }

        RejectReparsePoint(packagesRoot, "插件包目录");
        if (Directory.Exists(target))
            RejectReparsePoint(target, "现有插件目录");
        return target;
    }

    private static void EnsureTreeHasNoReparsePoints(string rootDirectory)
    {
        RejectReparsePoint(rootDirectory, "插件暂存目录");
        foreach (var path in Directory.EnumerateFileSystemEntries(
                     rootDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            RejectReparsePoint(path, "插件包条目");
        }
    }

    private static void RejectReparsePoint(string path, string displayName)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{displayName}不能是符号链接或重解析点。");
    }

    private static void TryDeleteTransactionDirectory(
        string transactionsRoot,
        string transactionDirectory)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(transactionsRoot));
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(transactionDirectory));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(target), root, comparison) ||
                !target.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
                !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
            {
                return;
            }

            if (Directory.Exists(target))
                DeleteTreeWithoutFollowingLinks(new DirectoryInfo(target));
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static byte[] ReadJournalBytes(string journalPath)
    {
        using var stream = new FileStream(
            journalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.SequentialScan);
        if (stream.Length is 0 or > MaximumTransactionJournalBytes)
            throw new InvalidDataException("事务日志大小无效。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("事务日志在读取时超过大小上限。");
        return bytes;
    }

    private static void ValidateRemovalStateSnapshot(TransactionJournal journal)
    {
        var isRemoval = string.Equals(journal.Operation, "remove", StringComparison.Ordinal);
        if (!isRemoval)
        {
            if (journal.PluginId is not null ||
                journal.HadPreviousState is not null ||
                journal.PreviousState is not null)
            {
                throw new InvalidDataException("安装事务不得携带卸载状态快照。");
            }
            return;
        }

        if (!journal.HadExistingTarget ||
            string.IsNullOrWhiteSpace(journal.PluginId))
        {
            throw new InvalidDataException("卸载事务缺少插件身份或原包声明。");
        }
        if (string.Equals(journal.Phase, "committed", StringComparison.Ordinal))
        {
            if (journal.HadPreviousState is not null || journal.PreviousState is not null)
                throw new InvalidDataException("已提交卸载事务不得保留旧状态快照。");
            return;
        }

        if (journal.HadPreviousState is null ||
            journal.HadPreviousState == true && journal.PreviousState is null ||
            journal.HadPreviousState == false && journal.PreviousState is not null)
        {
            throw new InvalidDataException("待回滚卸载事务的旧状态声明不一致。");
        }
        if (journal.HadPreviousState == true)
        {
            _ = PluginCatalog.CloneValidatedStateSnapshot(
                journal.PluginId,
                journal.PreviousState!);
        }
        else
        {
            // Validate the ID even when no prior state entry existed.
            _ = PluginCatalog.CloneValidatedStateSnapshot(
                journal.PluginId,
                new PluginStateEntry());
        }
    }

    private static void WriteJournalDurably(string journalPath, TransactionJournal journal)
    {
        var directory = Path.GetDirectoryName(journalPath) ??
                        throw new InvalidOperationException("事务日志缺少父目录。");
        var temporaryPath = Path.Combine(directory, $"journal.{Guid.NewGuid():N}.tmp");
        var contents = JsonSerializer.SerializeToUtf8Bytes(
            journal,
            TransactionJournalJsonOptions);
        if (contents.Length > MaximumTransactionJournalBytes)
            throw new InvalidDataException("事务日志超过安全大小上限。");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, journalPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static FileStream AcquireInstallationLock(string repositoryRoot)
    {
        Directory.CreateDirectory(repositoryRoot);
        RejectReparsePoint(repositoryRoot, "插件仓库工作目录");
        return new FileStream(
            Path.Combine(repositoryRoot, ".install.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.WriteThrough);
    }

    private static void DisposeLockNoThrow(FileStream lockStream)
    {
        try
        {
            lockStream.Dispose();
        }
        catch (IOException)
        {
            // Directory state and its durable journal are authoritative. A
            // cleanup failure must not reinterpret a committed transaction as
            // rollback-pending; the OS will release the handle at process exit.
        }
    }

    public static FileStream AcquireManagerLock(PluginCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(catalog.RootDirectory));
        if (OperatingSystem.IsWindows())
            canonicalRoot = canonicalRoot.ToUpperInvariant();
        var lockName = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRoot))).ToLowerInvariant();
        var lockRoot = Path.Combine(
            Path.GetTempPath(),
            "NyaLauncher",
            "PluginManagerLocks");
        Directory.CreateDirectory(lockRoot);
        RejectReparsePoint(lockRoot, "插件管理器锁目录");
        return new FileStream(
            Path.Combine(lockRoot, lockName + ".lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.WriteThrough);
    }

    private static string? RollbackCommittedInstallation(
        string transactionsRoot,
        string transactionDirectory,
        string targetDirectory,
        string backupDirectory,
        bool hadExistingTarget)
    {
        try
        {
            if (hadExistingTarget && !Directory.Exists(backupDirectory))
            {
                return "更新回滚所需的旧插件备份不存在；新包和事务记录已保留供恢复。";
            }
            if (!hadExistingTarget && Directory.Exists(backupDirectory))
            {
                return "新安装事务意外包含旧包备份；目标包和事务记录已保留供检查。";
            }

            if (hadExistingTarget)
            {
                if (Directory.Exists(targetDirectory))
                    DeleteTreeWithoutFollowingLinks(new DirectoryInfo(targetDirectory));
                Directory.Move(backupDirectory, targetDirectory);
            }
            else if (Directory.Exists(targetDirectory))
            {
                DeleteTreeWithoutFollowingLinks(new DirectoryInfo(targetDirectory));
            }

            TryDeleteTransactionDirectory(transactionsRoot, transactionDirectory);
            return null;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return exception.Message;
        }
    }

    private static void DeleteTreeWithoutFollowingLinks(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete(recursive: false);
            return;
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entry.Delete();
            }
            else if (entry is DirectoryInfo childDirectory)
            {
                DeleteTreeWithoutFollowingLinks(childDirectory);
            }
            else
            {
                entry.Delete();
            }
        }

        directory.Delete(recursive: false);
    }

    private sealed class TransactionJournal
    {
        public int Version { get; set; }

        public string? Operation { get; set; }

        public string TargetDirectoryName { get; set; } = string.Empty;

        public bool HadExistingTarget { get; set; }

        public string Phase { get; set; } = string.Empty;

        public string? PluginId { get; set; }

        public bool? HadPreviousState { get; set; }

        public PluginStateEntry? PreviousState { get; set; }
    }
}
