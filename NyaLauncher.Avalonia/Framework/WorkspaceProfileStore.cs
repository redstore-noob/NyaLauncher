using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Persists front-end personalization independently from launcher business data.
/// Invalid or unreadable files fall back to the registered defaults. Supported
/// older schemas are upgraded in memory; newer schemas are rejected unchanged.
/// </summary>
public sealed class WorkspaceProfileStore
{
    public const string ProfileFileName = "workspace.json";
    public const string LauncherConfigFileName = "config.json";
    public const string PluginDirectoryName = "plugins";
    private const string LocationFileName = "workspace-location.txt";

    private static readonly string[] ConfigurationFileNames =
    [
        ProfileFileName,
        LauncherConfigFileName
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string PlatformDefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NyaLauncher");

    public static string LocationFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NyaLauncher",
        LocationFileName);

    public string StorageDirectory { get; private set; }

    public string FilePath => Path.Combine(StorageDirectory, ProfileFileName);

    private readonly string _locationFilePath;

    public WorkspaceProfileStore(
        string? storageDirectory = null,
        string? locationFilePath = null)
    {
        _locationFilePath = locationFilePath ?? LocationFilePath;
        StorageDirectory = NormalizeStorageDirectory(
            storageDirectory ??
            LoadConfiguredDirectory(_locationFilePath) ??
            PlatformDefaultDirectory);
    }

    public WorkspaceProfile Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return WorkspaceDefaultProfile.Create();

            var json = File.ReadAllText(FilePath);
            var profile = JsonSerializer.Deserialize<WorkspaceProfile>(json, SerializerOptions)
                          ?? WorkspaceDefaultProfile.Create();
            return WorkspaceProfileMigrator.Migrate(profile);
        }
        catch (JsonException)
        {
            return WorkspaceDefaultProfile.Create();
        }
        catch (IOException)
        {
            return WorkspaceDefaultProfile.Create();
        }
        catch (UnauthorizedAccessException)
        {
            return WorkspaceDefaultProfile.Create();
        }
    }

    public void Save(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        SaveToPath(profile, FilePath);
    }

    public StorageDirectoryInspection InspectStorageDirectory(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);

        var targetDirectory = NormalizeStorageDirectory(storageDirectory);
        if (File.Exists(targetDirectory))
            throw new IOException("所选路径不是文件夹。");

        EnsurePathHasNoReparsePoints(targetDirectory);
        var pluginDirectory = Path.Combine(targetDirectory, PluginDirectoryName);
        var existingFiles = Directory.Exists(targetDirectory)
            ? Directory.EnumerateFileSystemEntries(targetDirectory)
                // An empty plugins directory is harmless and can be reused by a
                // first migration. Every other entry makes this an existing
                // target that must contain a complete, valid configuration.
                .Where(entry => IsMeaningfulTargetEntry(entry, pluginDirectory))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        if (existingFiles.Length > 0)
        {
            var hasWorkspace = existingFiles.Contains(ProfileFileName);
            var hasLauncherConfig = existingFiles.Contains(LauncherConfigFileName);
            if (!hasWorkspace || !hasLauncherConfig)
            {
                throw new InvalidDataException(
                    "现有目标目录必须同时包含 workspace.json 与 config.json，" +
                    "不能带着部分配置或单独的插件数据切换。");
            }

            _ = ValidateConfigurationBundle(targetDirectory);
        }

        return new StorageDirectoryInspection(targetDirectory, existingFiles);
    }

    private static bool IsMeaningfulTargetEntry(string entry, string pluginDirectory)
    {
        var attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"目标配置目录包含不允许的符号链接或 junction：{entry}");
        }

        if (!PathsEqual(entry, pluginDirectory))
            return true;
        if ((attributes & FileAttributes.Directory) == 0)
            throw new InvalidDataException("目标目录中的 plugins 必须是文件夹。");

        return Directory.EnumerateFileSystemEntries(entry).Any();
    }

    /// <summary>
    /// Prepares a storage-directory switch without changing the persisted
    /// locator or removing the old files. The caller must bind every consumer
    /// (especially <c>PluginManager</c>) to <see cref="StorageDirectoryChangeTransaction.TargetDirectory"/>
    /// before calling <see cref="StorageDirectoryChangeTransaction.Complete"/>.
    /// </summary>
    public StorageDirectoryChangeTransaction PrepareStorageDirectoryChange(
        string storageDirectory,
        WorkspaceProfile profile,
        ExistingConfigurationAction existingConfigurationAction = ExistingConfigurationAction.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        ArgumentNullException.ThrowIfNull(profile);

        var inspection = InspectStorageDirectory(storageDirectory);
        var targetDirectory = inspection.Directory;
        if (PathsEqual(StorageDirectory, targetDirectory))
        {
            Save(profile);
            return StorageDirectoryChangeTransaction.CreateNoOp(
                this,
                StorageDirectory,
                profile);
        }

        if (inspection.HasConfiguration &&
            existingConfigurationAction is not (
                ExistingConfigurationAction.DeletePrevious or
                ExistingConfigurationAction.BackupPrevious))
        {
            throw new InvalidOperationException(
                "目标目录已存在配置文件，必须先指定旧配置的处理方式。");
        }

        var previousDirectory = StorageDirectory;
        EnsurePathHasNoReparsePoints(previousDirectory);
        if (IsContainedBy(previousDirectory, targetDirectory) ||
            IsContainedBy(targetDirectory, previousDirectory))
        {
            throw new InvalidOperationException(
                "新旧配置目录不能互为父子目录。请选择彼此独立的位置。");
        }
        if (inspection.HasConfiguration && !inspection.HasCompleteConfiguration)
        {
            throw new InvalidDataException(
                "目标目录只包含部分配置。必须同时存在 workspace.json 与 config.json，" +
                "或使用一个不含配置的新目录。");
        }
        string? backupDirectory = null;
        var targetDirectoryExisted = Directory.Exists(targetDirectory);
        var targetPluginDirectory = Path.Combine(targetDirectory, PluginDirectoryName);
        var targetPluginDirectoryExisted = Directory.Exists(targetPluginDirectory);

        try
        {
            WorkspaceProfile appliedProfile;
            if (inspection.HasConfiguration)
            {
                // Existing targets are read-only until the switch commits. Parse
                // both JSON files now so malformed or future-schema data cannot
                // become the launcher's next startup root.
                appliedProfile = ValidateConfigurationBundle(targetDirectory);
                if (existingConfigurationAction == ExistingConfigurationAction.BackupPrevious)
                {
                    backupDirectory = CopyPreviousConfigurationToBackup(
                        previousDirectory,
                        targetDirectory);
                }
            }
            else
            {
                CopyConfigurationFiles(previousDirectory, targetDirectory);
                SaveToPath(profile, Path.Combine(targetDirectory, ProfileFileName));
                appliedProfile = ValidateConfigurationBundle(targetDirectory);
            }

            return new StorageDirectoryChangeTransaction(
                this,
                previousDirectory,
                targetDirectory,
                appliedProfile,
                inspection.HasConfiguration,
                backupDirectory,
                targetDirectoryExisted,
                targetPluginDirectoryExisted);
        }
        catch
        {
            // A new target belongs to this attempt. Remove only the files copied
            // by migration; an existing complete target remains user-owned.
            if (!inspection.HasConfiguration)
            {
                _ = CleanupPreparedTarget(
                    targetDirectory,
                    targetDirectoryExisted,
                    targetPluginDirectoryExisted);
            }
            else if (backupDirectory is not null)
            {
                TryDeleteOwnedDirectory(backupDirectory);
            }

            throw;
        }
    }

    internal void CompleteStorageDirectoryChange(StorageDirectoryChangeTransaction transaction)
    {
        // The locator is the commit marker. Source configuration and plugins are
        // deliberately untouched until every runtime has accepted the target.
        SaveConfiguredDirectory(transaction.TargetDirectory);
        StorageDirectory = transaction.TargetDirectory;
        transaction.SetCleanupFailures(DeleteConfigurationFiles(transaction.PreviousDirectory));
        TryDeleteEmptyDirectory(transaction.PreviousDirectory);
    }

    internal IReadOnlyList<string> RollbackStorageDirectoryChange(
        StorageDirectoryChangeTransaction transaction)
    {
        // StorageDirectory and the locator still point at PreviousDirectory
        // because they are only changed by CompleteStorageDirectoryChange.
        if (transaction.AppliedExistingConfiguration)
        {
            if (transaction.BackupDirectory is null)
                return [];

            return TryDeleteOwnedDirectory(transaction.BackupDirectory);
        }

        return CleanupPreparedTarget(
            transaction.TargetDirectory,
            transaction.TargetDirectoryExisted,
            transaction.TargetPluginDirectoryExisted);
    }

    private static void CopyConfigurationFiles(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var fileName in ConfigurationFileNames)
        {
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
                continue;

            File.Copy(sourcePath, Path.Combine(targetDirectory, fileName), overwrite: false);
        }

        var sourcePlugins = Path.Combine(sourceDirectory, PluginDirectoryName);
        var targetPlugins = Path.Combine(targetDirectory, PluginDirectoryName);
        if (Directory.Exists(sourcePlugins))
            CopyDirectory(sourcePlugins, targetPlugins);
    }

    private static string? CopyPreviousConfigurationToBackup(
        string previousDirectory,
        string targetDirectory)
    {
        var existingSourceFiles = ConfigurationFileNames
            .Where(fileName => File.Exists(Path.Combine(previousDirectory, fileName)))
            .ToArray();
        var sourcePlugins = Path.Combine(previousDirectory, PluginDirectoryName);
        if (existingSourceFiles.Length == 0 && !Directory.Exists(sourcePlugins))
            return null;

        var backupRoot = Path.Combine(targetDirectory, "backup");
        EnsurePathHasNoReparsePoints(backupRoot);
        Directory.CreateDirectory(backupRoot);
        EnsurePathHasNoReparsePoints(backupRoot);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupDirectory = Path.Combine(backupRoot, $"previous-config-{timestamp}");
        for (var suffix = 2; Directory.Exists(backupDirectory); suffix++)
        {
            backupDirectory = Path.Combine(
                backupRoot,
                $"previous-config-{timestamp}-{suffix}");
        }

        Directory.CreateDirectory(backupDirectory);
        try
        {
            foreach (var fileName in existingSourceFiles)
            {
                File.Copy(
                    Path.Combine(previousDirectory, fileName),
                    Path.Combine(backupDirectory, fileName),
                    overwrite: false);
            }

            if (Directory.Exists(sourcePlugins))
                CopyDirectory(sourcePlugins, Path.Combine(backupDirectory, PluginDirectoryName));

            return backupDirectory;
        }
        catch
        {
            // This directory was uniquely created by the current attempt, so a
            // failed backup must not accumulate an ambiguous partial snapshot.
            TryDeleteOwnedDirectory(backupDirectory);
            throw;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        if (Directory.Exists(targetDirectory) &&
            Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            throw new IOException($"目标插件目录已存在：{targetDirectory}");

        Directory.CreateDirectory(targetDirectory);
        var pending = new Stack<(string Source, string Target)>();
        pending.Push((sourceDirectory, targetDirectory));
        while (pending.Count > 0)
        {
            var (source, target) = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"插件目录包含不允许迁移的符号链接或 junction：{entry}");
                }

                var destination = Path.Combine(target, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.CreateDirectory(destination);
                    pending.Push((entry, destination));
                }
                else
                {
                    File.Copy(entry, destination, overwrite: false);
                }
            }
        }
    }

    private static IReadOnlyList<string> DeleteConfigurationFiles(string directory)
    {
        var failures = new List<string>();
        foreach (var fileName in ConfigurationFileNames)
        {
            var filePath = Path.Combine(directory, fileName);
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (IOException exception)
            {
                failures.Add($"{fileName}：{exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                failures.Add($"{fileName}：{exception.Message}");
            }
        }

        var pluginDirectory = Path.GetFullPath(Path.Combine(directory, PluginDirectoryName));
        try
        {
            DeleteDirectoryTree(pluginDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{PluginDirectoryName}：{exception.Message}");
        }

        return failures;
    }

    private static IReadOnlyList<string> CleanupPreparedTarget(
        string targetDirectory,
        bool targetDirectoryExisted,
        bool targetPluginDirectoryExisted)
    {
        var failures = new List<string>();
        foreach (var fileName in ConfigurationFileNames)
        {
            var filePath = Path.Combine(targetDirectory, fileName);
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{fileName}：{exception.Message}");
            }
        }

        var pluginDirectory = Path.Combine(targetDirectory, PluginDirectoryName);
        try
        {
            DeleteDirectoryTree(pluginDirectory);
            if (targetPluginDirectoryExisted)
                Directory.CreateDirectory(pluginDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{PluginDirectoryName}：{exception.Message}");
        }

        if (!targetDirectoryExisted)
            TryDeleteEmptyDirectory(targetDirectory);

        return failures;
    }

    private static IReadOnlyList<string> TryDeleteOwnedDirectory(string directory)
    {
        try
        {
            DeleteDirectoryTree(directory);
            TryDeleteEmptyDirectory(Path.GetDirectoryName(directory));
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"{directory}：{exception.Message}"];
        }
    }

    private static void DeleteDirectoryTree(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"拒绝删除重解析点目录：{root}");

        // Validate the complete tree before deleting its first entry. A plugin
        // package cannot turn a migration cleanup into traversal outside root.
        var files = new List<string>();
        var directories = new List<string> { root };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var fullPath = Path.GetFullPath(entry);
                if (!IsContainedBy(root, fullPath))
                    throw new IOException($"插件目录条目越过迁移根目录：{entry}");
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"插件目录包含重解析点，已保留旧目录：{entry}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(fullPath);
                    pending.Push(fullPath);
                }
                else
                {
                    files.Add(fullPath);
                }
            }
        }

        foreach (var file in files)
            File.Delete(file);
        foreach (var child in directories.OrderByDescending(path => path.Length))
            Directory.Delete(child, recursive: false);
    }

    private static bool IsContainedBy(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(
            prefix,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static void EnsurePathHasNoReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"配置目录不能经过符号链接或 junction：{current}");
        }
    }

    private static WorkspaceProfile ValidateConfigurationBundle(string directory)
    {
        var workspacePath = Path.Combine(directory, ProfileFileName);
        var configPath = Path.Combine(directory, LauncherConfigFileName);
        ValidateJsonFileSize(workspacePath, 16 * 1024 * 1024);
        ValidateJsonFileSize(configPath, 4 * 1024 * 1024);

        try
        {
            var workspace = JsonSerializer.Deserialize<WorkspaceProfile>(
                                File.ReadAllText(workspacePath),
                                SerializerOptions) ??
                            throw new InvalidDataException("workspace.json 不能是 null。");
            var migratedWorkspace = WorkspaceProfileMigrator.Migrate(workspace);
            using var config = JsonDocument.Parse(File.ReadAllText(configPath));
            if (config.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("config.json 的根节点必须是 JSON 对象。");
            return migratedWorkspace;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("目标目录中的配置 JSON 无效。", exception);
        }
    }

    private static void ValidateJsonFileSize(string path, long maximumBytes)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"目标目录缺少 {Path.GetFileName(path)}。");
        if (new FileInfo(path).Length > maximumBytes)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} 超过 {maximumBytes} 字节限制。");
    }

    public static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeStorageDirectory(left),
            NormalizeStorageDirectory(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static void SaveToPath(WorkspaceProfile profile, string filePath)
    {
        profile = WorkspaceProfileMigrator.Migrate(profile);
        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        WriteTextAtomically(filePath, json);
    }

    private static string? LoadConfiguredDirectory(string locationFilePath)
    {
        try
        {
            if (!File.Exists(locationFilePath))
                return null;

            var directory = File.ReadAllText(locationFilePath).Trim();
            return string.IsNullOrWhiteSpace(directory) ? null : directory;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveConfiguredDirectory(string directory)
    {
        if (PathsEqual(directory, PlatformDefaultDirectory))
        {
            if (File.Exists(_locationFilePath))
                File.Delete(_locationFilePath);
            return;
        }

        var locatorDirectory = Path.GetDirectoryName(_locationFilePath);
        if (!string.IsNullOrWhiteSpace(locatorDirectory))
            Directory.CreateDirectory(locatorDirectory);

        WriteTextAtomically(_locationFilePath, directory);
    }

    private static void WriteTextAtomically(string filePath, string contents)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory ?? string.Empty,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            // Rename occurs inside one directory, so readers observe either the
            // previous complete document or the new complete document.
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Preserve the original write/replace outcome; a locked temp
                    // file can be cleaned by a later maintenance pass.
                }
            }
        }
    }

    private static string NormalizeStorageDirectory(string directory)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    private static void TryDeleteEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        try
        {
            if (Directory.GetFileSystemEntries(directory).Length == 0)
                Directory.Delete(directory);
        }
        catch (IOException)
        {
            // The profile was already migrated; a locked empty directory is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Directory cleanup is best-effort and must not invalidate the new profile.
        }
    }

}

public enum ExistingConfigurationAction
{
    None,
    DeletePrevious,
    BackupPrevious
}

public sealed record StorageDirectoryInspection(
    string Directory,
    IReadOnlyList<string> ExistingFileNames)
{
    public bool HasConfiguration => ExistingFileNames.Count > 0;

    public bool HasCompleteConfiguration =>
        ExistingFileNames.Contains(WorkspaceProfileStore.ProfileFileName) &&
        ExistingFileNames.Contains(WorkspaceProfileStore.LauncherConfigFileName);
}

/// <summary>
/// A prepared storage switch. Preparation may copy data to an empty target,
/// but only <see cref="Complete"/> changes the startup locator and removes the
/// previous files. Dispose-like implicit commits are intentionally avoided.
/// </summary>
public sealed class StorageDirectoryChangeTransaction
{
    private readonly WorkspaceProfileStore _owner;
    private readonly object _syncRoot = new();
    private readonly bool _isNoOp;
    private TransactionState _state;

    internal StorageDirectoryChangeTransaction(
        WorkspaceProfileStore owner,
        string previousDirectory,
        string targetDirectory,
        WorkspaceProfile appliedProfile,
        bool appliedExistingConfiguration,
        string? backupDirectory,
        bool targetDirectoryExisted,
        bool targetPluginDirectoryExisted,
        bool isNoOp = false)
    {
        _owner = owner;
        PreviousDirectory = previousDirectory;
        TargetDirectory = targetDirectory;
        AppliedProfile = appliedProfile;
        AppliedExistingConfiguration = appliedExistingConfiguration;
        BackupDirectory = backupDirectory;
        TargetDirectoryExisted = targetDirectoryExisted;
        TargetPluginDirectoryExisted = targetPluginDirectoryExisted;
        _isNoOp = isNoOp;
    }

    public string PreviousDirectory { get; }

    public string TargetDirectory { get; }

    public WorkspaceProfile AppliedProfile { get; }

    public bool AppliedExistingConfiguration { get; }

    public string? BackupDirectory { get; }

    public IReadOnlyList<string> CleanupFailures { get; private set; } = [];

    public IReadOnlyList<string> RollbackFailures { get; private set; } = [];

    internal bool TargetDirectoryExisted { get; }

    internal bool TargetPluginDirectoryExisted { get; }

    internal static StorageDirectoryChangeTransaction CreateNoOp(
        WorkspaceProfileStore owner,
        string directory,
        WorkspaceProfile profile) =>
        new(
            owner,
            directory,
            directory,
            profile,
            appliedExistingConfiguration: false,
            backupDirectory: null,
            targetDirectoryExisted: true,
            targetPluginDirectoryExisted: Directory.Exists(
                Path.Combine(directory, WorkspaceProfileStore.PluginDirectoryName)),
            isNoOp: true);

    /// <summary>
    /// Persists the target locator and only then performs best-effort cleanup of
    /// the previous configuration and plugin tree.
    /// </summary>
    public void Complete()
    {
        lock (_syncRoot)
        {
            if (_state == TransactionState.Completed)
                return;
            if (_state == TransactionState.RolledBack)
                throw new InvalidOperationException("存储目录切换已回滚，不能再提交。");

            if (!_isNoOp)
                _owner.CompleteStorageDirectoryChange(this);
            _state = TransactionState.Completed;
        }
    }

    /// <summary>
    /// Removes artifacts created while preparing an empty target. Existing
    /// target configuration is never deleted; only this attempt's backup is.
    /// </summary>
    public IReadOnlyList<string> Rollback()
    {
        lock (_syncRoot)
        {
            if (_state == TransactionState.RolledBack)
                return RollbackFailures;
            if (_state == TransactionState.Completed)
                throw new InvalidOperationException("存储目录切换已提交，不能再回滚。");

            RollbackFailures = _isNoOp
                ? []
                : _owner.RollbackStorageDirectoryChange(this);
            _state = TransactionState.RolledBack;
            return RollbackFailures;
        }
    }

    internal void SetCleanupFailures(IReadOnlyList<string> failures) =>
        CleanupFailures = failures;

    private enum TransactionState
    {
        Prepared,
        Completed,
        RolledBack
    }
}
