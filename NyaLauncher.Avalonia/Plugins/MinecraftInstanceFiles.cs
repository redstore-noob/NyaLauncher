using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Minecraft;

namespace NyaLauncher.Avalonia.Plugins;

/// <summary>
/// Resolves SDK paths below the two instance roots. Existing reparse points are
/// rejected so a relative path cannot escape through a symlink or junction.
/// </summary>
internal class MinecraftInstanceFiles(MinecraftInstanceDescriptor instance)
    : IMinecraftInstanceFiles
{
    protected const long MaximumFileBytes = 512L * 1024 * 1024;

    public MinecraftInstanceDescriptor Instance { get; } = instance ??
        throw new ArgumentNullException(nameof(instance));

    public virtual ValueTask<bool> ExistsAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Resolve(path, allowMissingLeaf: true);
        return ValueTask.FromResult(File.Exists(target) || Directory.Exists(target));
    }

    public virtual ValueTask<Stream> OpenReadAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Resolve(path, allowMissingLeaf: false);
        Stream stream = OpenReadStream(target);
        return ValueTask.FromResult(stream);
    }

    public virtual async IAsyncEnumerable<MinecraftFileEntry> EnumerateAsync(
        MinecraftInstancePath directory,
        string searchPattern = "*",
        bool recursive = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchPattern) ||
            searchPattern.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException("搜索模式不能包含目录分隔符。", nameof(searchPattern));
        }

        var rootPath = Resolve(directory, allowMissingLeaf: false);
        if (!Directory.Exists(rootPath))
            yield break;

        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         current,
                         searchPattern,
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var info = isDirectory ? null : new FileInfo(entry);
                yield return new MinecraftFileEntry
                {
                    Path = new MinecraftInstancePath(
                        directory.Root,
                        Path.GetRelativePath(GetRoot(directory.Root), entry)),
                    IsDirectory = isDirectory,
                    Length = info?.Length ?? 0,
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(entry)
                };
                await Task.Yield();
            }

            if (!recursive)
                continue;
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
            }
        }
    }

    protected string Resolve(MinecraftInstancePath path, bool allowMissingLeaf)
    {
        if (string.IsNullOrWhiteSpace(path.RelativePath) ||
            Path.IsPathFullyQualified(path.RelativePath))
        {
            throw new ArgumentException("实例路径必须是非空相对路径。", nameof(path));
        }

        var root = GetRoot(path.Root);
        var target = Path.GetFullPath(Path.Combine(root, path.RelativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new UnauthorizedAccessException("实例路径越过了允许的根目录。");

        RejectReparsePoints(root, target, allowMissingLeaf);
        return target;
    }

    protected string GetRoot(MinecraftPathRoot root)
    {
        var path = root switch
        {
            MinecraftPathRoot.MinecraftDirectory => Instance.MinecraftDirectory,
            MinecraftPathRoot.GameDirectory => Instance.GameDirectory,
            _ => throw new ArgumentOutOfRangeException(nameof(root))
        };
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    protected static Stream OpenReadStream(string target) => new FileStream(
        target,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read | FileShare.Delete,
        bufferSize: 81920,
        useAsync: true);

    private static void RejectReparsePoints(
        string root,
        string target,
        bool allowMissingLeaf)
    {
        if (Directory.Exists(root) &&
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("实例根目录不能是符号链接或 junction。");
        }

        var relative = Path.GetRelativePath(root, target);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            var exists = File.Exists(current) || Directory.Exists(current);
            if (!exists)
            {
                if (allowMissingLeaf || index < segments.Length - 1)
                    continue;
                throw new FileNotFoundException("实例文件不存在。", current);
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("实例路径不能穿过符号链接或 junction。");
        }
    }
}

/// <summary>
/// Stages a bounded set of writes/deletes and verifies that target files did
/// not change before commit. A failed or interrupted commit restores every
/// file whose publication may have started.
/// </summary>
internal sealed class MinecraftEditSession : MinecraftInstanceFiles, IMinecraftEditSession
{
    private const int MaximumOperationCount = 2048;
    private const long MaximumStagingBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumBackupBytes = 2L * 1024 * 1024 * 1024;
    private readonly Dictionary<string, EditOperation> _operations;
    private readonly StringComparer _pathComparer;
    private readonly string _stagingDirectory;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private long _stagedBytes;
    private bool _commitAttempted;
    // 0 = active, 1 = host-revoked, 2 = committed. The final commit and a
    // timeout revoke race through one CAS so both outcomes cannot win.
    private int _commitState;
    private bool _disposed;

    public MinecraftEditSession(
        MinecraftInstanceDescriptor instance,
        string pluginCacheDirectory)
        : base(instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginCacheDirectory);
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _operations = new Dictionary<string, EditOperation>(_pathComparer);

        _stagingDirectory = Path.Combine(
            Path.GetFullPath(pluginCacheDirectory),
            "instance-edits",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stagingDirectory);
    }

    public override async ValueTask<bool> ExistsAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            var target = Resolve(path, allowMissingLeaf: true);
            if (_operations.TryGetValue(target, out var operation))
                return operation.Kind == EditKind.Write;
            return File.Exists(target) || Directory.Exists(target);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public override async ValueTask<Stream> OpenReadAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            var target = Resolve(path, allowMissingLeaf: true);
            if (_operations.TryGetValue(target, out var operation))
            {
                if (operation.Kind == EditKind.Delete)
                    throw new FileNotFoundException("该文件已在当前事务中标记删除。", target);
                return OpenReadStream(operation.StagedPath!);
            }

            target = Resolve(path, allowMissingLeaf: false);
            return OpenReadStream(target);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public override async IAsyncEnumerable<MinecraftFileEntry> EnumerateAsync(
        MinecraftInstancePath directory,
        string searchPattern = "*",
        bool recursive = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<MinecraftFileEntry> snapshot = [];
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            await foreach (var entry in base.EnumerateAsync(
                               directory,
                               searchPattern,
                               recursive,
                               cancellationToken))
            {
                snapshot.Add(entry);
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        foreach (var entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public async ValueTask WriteFileAsync(
        MinecraftInstancePath path,
        Stream content,
        MinecraftFileWriteMode mode = MinecraftFileWriteMode.CreateOrReplace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            var target = Resolve(path, allowMissingLeaf: true);
            EnsureOperationCapacity(target);
            _operations.TryGetValue(target, out var previous);
            var original = previous?.Original ??
                await CaptureOriginalAsync(target, cancellationToken);
            var replacedStagedBytes = previous?.StagedLength ?? 0;
            var stagedPath = Path.Combine(
                _stagingDirectory,
                Guid.NewGuid().ToString("N") + ".new");

            long stagedLength = 0;
            try
            {
                await using var output = new FileStream(
                    stagedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    stagedLength = checked(stagedLength + read);
                    if (stagedLength > MaximumFileBytes)
                        throw new InvalidDataException("单个实例事务文件不能超过 512 MiB。");
                    // The old staged file still exists while its replacement is
                    // copied, so quota the actual transient disk usage as well.
                    if (checked(_stagedBytes + stagedLength) > MaximumStagingBytes)
                        throw new InvalidDataException("单次实例事务的暂存文件总量不能超过 2 GiB。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            catch
            {
                TryDeleteFile(stagedPath);
                throw;
            }

            try
            {
                RemoveOldStagedFile(previous);
            }
            catch
            {
                TryDeleteFile(stagedPath);
                throw;
            }

            _operations[target] = new EditOperation(
                path,
                target,
                EditKind.Write,
                mode,
                stagedPath,
                stagedLength,
                original);
            _stagedBytes = checked(_stagedBytes - replacedStagedBytes + stagedLength);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async ValueTask DeleteFileAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            var target = Resolve(path, allowMissingLeaf: true);
            EnsureOperationCapacity(target);
            _operations.TryGetValue(target, out var previous);
            var original = previous?.Original ??
                await CaptureOriginalAsync(target, cancellationToken);

            if (!original.Exists)
            {
                // Writing and then deleting a previously absent file is a no-op.
                if (previous?.Kind == EditKind.Write)
                {
                    RemoveOldStagedFile(previous);
                    _stagedBytes -= previous.StagedLength;
                    _operations.Remove(target);
                    return;
                }

                throw new FileNotFoundException("不能删除不存在的实例文件。", target);
            }

            RemoveOldStagedFile(previous);
            _stagedBytes -= previous?.StagedLength ?? 0;
            _operations[target] = new EditOperation(
                path,
                target,
                EditKind.Delete,
                MinecraftFileWriteMode.CreateOrReplace,
                null,
                0,
                original);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            _commitAttempted = true;
            var operations = _operations.Values
                .OrderBy(item => item.TargetPath, _pathComparer)
                .ToArray();
            await ValidateCommitAsync(operations, cancellationToken);

            var backupDirectory = Path.Combine(_stagingDirectory, "backup");
            Directory.CreateDirectory(backupDirectory);
            var applied = new List<AppliedOperation>(operations.Length);

            try
            {
                foreach (var operation in operations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfRevoked();
                    EnsureResolvedTarget(operation);
                    var current = await CaptureOriginalAsync(
                        operation.TargetPath,
                        cancellationToken);
                    if (!operation.Original.Equals(current))
                    {
                        throw new IOException(
                            $"实例文件在事务期间被其他程序修改：{operation.Path.RelativePath}");
                    }

                    string? backupPath = null;
                    if (operation.Original.Exists)
                    {
                        backupPath = Path.Combine(
                            backupDirectory,
                            Guid.NewGuid().ToString("N") + ".bak");
                        try
                        {
                            await CopyAndVerifyBackupAsync(
                                operation,
                                backupPath,
                                cancellationToken);
                        }
                        catch
                        {
                            TryDeleteFile(backupPath);
                            throw;
                        }
                    }

                    ThrowIfRevoked();
                    // Add immediately before publication so even a partially
                    // failed move is included in best-effort rollback.
                    applied.Add(new AppliedOperation(operation, backupPath));
                    Apply(operation);
                    ThrowIfRevoked();
                }

                if (Interlocked.CompareExchange(ref _commitState, 2, 0) != 0)
                    throw new OperationCanceledException("实例事务的提交权限已被宿主撤销。");
            }
            catch (Exception primaryError)
            {
                var rollbackErrors = RollBack(applied);
                if (rollbackErrors.Count > 0)
                {
                    primaryError.Data["NyaLauncher.InstanceRollbackErrors"] =
                        new AggregateException(
                            "实例事务回滚未完全成功。",
                            rollbackErrors);
                }

                throw;
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sessionGate.WaitAsync();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            TryDeleteDirectory(_stagingDirectory);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <summary>
    /// Permanently prevents a timed-out action from publishing a later commit.
    /// Returns false only when the transaction had already committed first.
    /// </summary>
    internal bool Revoke() => Interlocked.CompareExchange(ref _commitState, 1, 0) != 2;

    private async Task ValidateCommitAsync(
        IReadOnlyList<EditOperation> operations,
        CancellationToken cancellationToken)
    {
        long backupBytes = 0;
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureResolvedTarget(operation);
            var current = await CaptureOriginalAsync(
                operation.TargetPath,
                cancellationToken);
            if (!operation.Original.Equals(current))
            {
                throw new IOException(
                    $"实例文件在事务期间被其他程序修改：{operation.Path.RelativePath}");
            }

            ValidateWriteMode(operation);
            if (!operation.Original.Exists)
                continue;
            backupBytes = checked(backupBytes + operation.Original.Length);
            if (backupBytes > MaximumBackupBytes)
                throw new InvalidDataException("单次实例事务的备份总量不能超过 2 GiB。");
        }
    }

    private void EnsureResolvedTarget(EditOperation operation)
    {
        // Resolve again at commit/rollback time: a parent directory may have
        // become a reparse point since the operation was staged.
        var resolved = Resolve(operation.Path, allowMissingLeaf: true);
        if (!_pathComparer.Equals(resolved, operation.TargetPath))
            throw new UnauthorizedAccessException("实例事务目标路径在提交前发生了变化。");
        if (Directory.Exists(resolved))
            throw new IOException("实例事务只支持文件，不支持目录。");
    }

    private void EnsureOperationCapacity(string target)
    {
        if (!_operations.ContainsKey(target) &&
            _operations.Count >= MaximumOperationCount)
        {
            throw new InvalidOperationException(
                $"单次实例事务最多包含 {MaximumOperationCount} 个文件操作。");
        }
    }

    private static void RemoveOldStagedFile(EditOperation? operation)
    {
        if (operation?.StagedPath is not null && File.Exists(operation.StagedPath))
            File.Delete(operation.StagedPath);
    }

    private static async Task<OriginalFileState> CaptureOriginalAsync(
        string target,
        CancellationToken cancellationToken)
    {
        // Check directories before files; File.Exists is false for directories.
        if (Directory.Exists(target))
            throw new IOException("实例事务只支持文件，不支持目录。");
        if (!File.Exists(target))
            return OriginalFileState.Missing;

        await using var stream = new FileStream(
            target,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        var length = stream.Length;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new OriginalFileState(true, length, Convert.ToHexString(hash));
    }

    private static void ValidateWriteMode(EditOperation operation)
    {
        if (operation.Kind != EditKind.Write)
            return;
        if (operation.WriteMode == MinecraftFileWriteMode.CreateNew &&
            operation.Original.Exists)
        {
            throw new IOException(
                $"目标文件已经存在：{operation.Path.RelativePath}");
        }

        if (operation.WriteMode == MinecraftFileWriteMode.ReplaceExisting &&
            !operation.Original.Exists)
        {
            throw new FileNotFoundException(
                "要替换的实例文件不存在。",
                operation.TargetPath);
        }
    }

    private static async Task CopyAndVerifyBackupAsync(
        EditOperation operation,
        string backupPath,
        CancellationToken cancellationToken)
    {
        await using (var source = new FileStream(
                         operation.TargetPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         81920,
                         useAsync: true))
        await using (var destination = new FileStream(
                         backupPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         useAsync: true))
        {
            if (source.Length != operation.Original.Length)
                throw new IOException("实例文件在打开事务备份源时发生了变化。");
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        var backupState = await CaptureOriginalAsync(backupPath, cancellationToken);
        if (!operation.Original.Equals(backupState))
            throw new IOException("实例文件在创建事务备份时发生了变化。");
    }

    private void Apply(EditOperation operation)
    {
        EnsureResolvedTarget(operation);
        if (operation.Kind == EditKind.Delete)
        {
            File.Delete(operation.TargetPath);
            return;
        }

        var parent = Path.GetDirectoryName(operation.TargetPath)!;
        Directory.CreateDirectory(parent);
        EnsureResolvedTarget(operation);
        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(operation.TargetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(operation.StagedPath!, temporary, overwrite: false);
            EnsureResolvedTarget(operation);
            File.Move(temporary, operation.TargetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private List<Exception> RollBack(IEnumerable<AppliedOperation> applied)
    {
        var errors = new List<Exception>();
        foreach (var item in applied.Reverse())
        {
            try
            {
                EnsureResolvedTarget(item.Operation);
                if (item.BackupPath is null)
                {
                    if (File.Exists(item.Operation.TargetPath))
                        File.Delete(item.Operation.TargetPath);
                }
                else
                {
                    RestoreBackup(item.Operation, item.BackupPath);
                }
            }
            catch (Exception error)
            {
                // Continue restoring independent files. The caller keeps the
                // primary commit exception and attaches these secondary errors.
                errors.Add(error);
            }
        }

        return errors;
    }

    private void RestoreBackup(EditOperation operation, string backupPath)
    {
        if (!File.Exists(backupPath) || Directory.Exists(backupPath))
            throw new FileNotFoundException("实例事务恢复备份不存在。", backupPath);
        if ((File.GetAttributes(backupPath) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("实例事务恢复备份不能是重解析点。");

        var parent = Path.GetDirectoryName(operation.TargetPath)!;
        Directory.CreateDirectory(parent);
        EnsureResolvedTarget(operation);
        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(operation.TargetPath)}.{Guid.NewGuid():N}.restore");
        try
        {
            File.Copy(backupPath, temporary, overwrite: false);
            EnsureResolvedTarget(operation);
            File.Move(temporary, operation.TargetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _commitState) == 2)
            throw new InvalidOperationException("实例事务已经提交。");
        ThrowIfRevoked();
        if (_commitAttempted)
            throw new InvalidOperationException("实例事务已经尝试提交，不能继续复用。");
    }

    private void ThrowIfRevoked()
    {
        if (Volatile.Read(ref _commitState) == 1)
            throw new OperationCanceledException("实例事务的提交权限已被宿主撤销。");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Staging cleanup is best effort; transaction state stays intact.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup is best effort; committed target files are unaffected.
        }
    }

    private enum EditKind
    {
        Write,
        Delete
    }

    private sealed record EditOperation(
        MinecraftInstancePath Path,
        string TargetPath,
        EditKind Kind,
        MinecraftFileWriteMode WriteMode,
        string? StagedPath,
        long StagedLength,
        OriginalFileState Original);

    private readonly record struct OriginalFileState(bool Exists, long Length, string Hash)
    {
        public static OriginalFileState Missing { get; } = new(false, 0, string.Empty);
    }

    private sealed record AppliedOperation(EditOperation Operation, string? BackupPath);

}
