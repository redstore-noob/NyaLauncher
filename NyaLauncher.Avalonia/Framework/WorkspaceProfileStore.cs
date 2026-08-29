using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 工作区档案存储：负责 <c>workspace.json</c> 的读写、版本迁移与配置目录管理。
/// <para>
/// 与启动器业务数据（<c>config.json</c>）相互独立，但存放在<b>同一个目录</b>，
/// 用户改配置目录时两份一起走。
/// </para>
/// <para>
/// 容错策略：文件缺失、损坏或不可读时回退到默认档案；
/// 支持的旧版本格式在内存中就地升级；<b>更高</b>版本则原样拒绝（不猜测、不降级覆盖）。
/// </para>
/// </summary>
public sealed class WorkspaceProfileStore
{
    /// <summary>工作区档案文件名。</summary>
    public const string ProfileFileName = "workspace.json";

    /// <summary>启动器业务配置文件名（与本档案同目录，切换目录时一并迁移）。</summary>
    public const string LauncherConfigFileName = "config.json";

    /// <summary>插件安装目录名（随存储目录一起迁移/备份/回滚）。</summary>
    public const string PluginDirectoryName = "plugins";

    /// <summary>记录用户所选配置目录的定位文件名，存放在 %APPDATA%\NyaLauncher 下，不含个性化内容。</summary>
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

    // 默认存储目录统一引用 Core 侧配置的默认值，避免重复计算
    /// <summary>
    /// 平台默认配置目录。直接取自 <c>LauncherConfig.DefaultStorageDirectory</c>，
    /// 由运行平台映射到当前用户的数据目录。
    /// </summary>
    public static string PlatformDefaultDirectory { get; } =
        NyaLauncher.Core.Config.LauncherConfig.DefaultStorageDirectory;

    /// <summary>
    /// 定位文件的完整路径（<c>%APPDATA%\NyaLauncher\workspace-location.txt</c>）。
    /// 它只记录用户选过的目录，不含任何个性化内容。
    /// </summary>
    public static string LocationFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NyaLauncher",
        LocationFileName);

    /// <summary>当前实际使用的配置目录。切换目录后会同步更新。</summary>
    public string StorageDirectory { get; private set; }

    /// <summary>当前工作区档案的完整路径。</summary>
    public string FilePath => Path.Combine(StorageDirectory, ProfileFileName);

    private readonly string _locationFilePath;

    /// <summary>
    /// 创建档案存储并解析配置目录。
    /// <para>
    /// 目录优先级：显式传入 → 定位文件记录 → 平台默认目录。
    /// 目录由自动解析得出时（即未显式传参），会尝试从旧默认目录迁移配置。
    /// </para>
    /// </summary>
    /// <param name="storageDirectory">显式指定的配置目录；为 <c>null</c> 时自动解析。</param>
    /// <param name="locationFilePath">定位文件路径，主要用于测试；为 <c>null</c> 时使用 <see cref="LocationFilePath"/>。</param>
    public WorkspaceProfileStore(
        string? storageDirectory = null,
        string? locationFilePath = null)
    {
        _locationFilePath = locationFilePath ?? LocationFilePath;
        StorageDirectory = NormalizeStorageDirectory(
            storageDirectory ??
            LoadConfiguredDirectory(_locationFilePath) ??
            PlatformDefaultDirectory);
        // 默认存储目录从 %LOCALAPPDATA%\NyaLauncher 迁到 %USERPROFILE%\NyaLauncher。
        // 启动时自动迁移：复制缺失配置 → 校验新目录已就位 → 清理旧目录文件与空目录 → 清 locator。
        // 全程 best-effort，任何失败都不阻断启动，也不影响旧配置。
        // 仅当目录由自动解析（locator/默认值）得出时触发；显式传参构造不迁移。
        if (storageDirectory is null)
            MigrateLegacyDefaultConfiguration();
    }

    /// <summary>
    /// 自动迁移旧默认目录（%LOCALAPPDATA%\NyaLauncher）中的配置到新默认目录（%USERPROFILE%\NyaLauncher）。
    /// 仅在最终目录解析为「新平台默认」或「locator 残留指向旧默认」时触发；用户自定义目录不迁移。
    /// </summary>
    private void MigrateLegacyDefaultConfiguration()
    {
        var legacyDirectory =
            NyaLauncher.Core.Config.LauncherConfig.LegacyDefaultStorageDirectory;
        var isDefault = PathUtil.PathsEqual(StorageDirectory, PlatformDefaultDirectory);
        var isLegacy = PathUtil.PathsEqual(StorageDirectory, legacyDirectory);
        if (!isDefault && !isLegacy)
            return;

        // 旧目录本来就没有配置，无需迁移
        if (!File.Exists(Path.Combine(legacyDirectory, ProfileFileName)) &&
            !File.Exists(Path.Combine(legacyDirectory, LauncherConfigFileName)))
            return;

        try
        {
            // 1) 把旧目录缺失的配置复制到新默认目录
            CopyMissingConfigurationFile(ProfileFileName, legacyDirectory);
            CopyMissingConfigurationFile(LauncherConfigFileName, legacyDirectory);

            // 2) 校验新目录确实持有 config.json 后才清理旧文件（避免复制失败导致误删）
            var targetDirectory = PlatformDefaultDirectory;
            if (!File.Exists(Path.Combine(targetDirectory, LauncherConfigFileName)))
                return;

            // 3) 清理旧目录中的配置与空目录（best-effort）
            _ = DeleteConfigurationFiles(legacyDirectory);
            TryDeleteEmptyDirectory(legacyDirectory);

            // 4) 若 locator 残留指向旧默认目录，清掉它，使后续解析回落新默认
            if (isLegacy)
            {
                TryDeleteLocatorFile();
                StorageDirectory = NormalizeStorageDirectory(targetDirectory);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 迁移失败不阻断启动：新目录缺少的文件会按默认配置重新生成，旧配置原样保留。
        }
    }

    private void CopyMissingConfigurationFile(string fileName, string legacyDirectory)
    {
        var targetPath = Path.Combine(PlatformDefaultDirectory, fileName);
        if (File.Exists(targetPath))
            return;

        var sourcePath = Path.Combine(legacyDirectory, fileName);
        if (!File.Exists(sourcePath))
            return;

        Directory.CreateDirectory(PlatformDefaultDirectory);
        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private void TryDeleteLocatorFile()
    {
        try
        {
            if (File.Exists(_locationFilePath))
                File.Delete(_locationFilePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // locator 清理失败不影响启动；下次解析可能再次落到旧目录并重试迁移。
        }
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

        if (!PathUtil.PathsEqual(entry, pluginDirectory))
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
        if (PathUtil.PathsEqual(StorageDirectory, targetDirectory))
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
        if (PathUtil.PathsEqual(directory, PlatformDefaultDirectory))
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

/// <summary>目标目录已有配置时的处理方式（在切换配置目录的确认对话框里由用户选择）。</summary>
public enum ExistingConfigurationAction
{
    /// <summary>什么都不做：采用目标目录现有的配置，原目录配置保持不动。</summary>
    None,

    /// <summary>采用目标目录配置后，删除原目录中的配置文件。</summary>
    DeletePrevious,

    /// <summary>采用目标目录配置前，先把原目录配置备份到指定位置。</summary>
    BackupPrevious
}

/// <summary>对某个候选配置目录的体检结果：它里面已经存在哪些配置文件。</summary>
/// <param name="Directory">被检查的目录。</param>
/// <param name="ExistingFileNames">该目录中已存在的配置文件名列表。</param>
public sealed record StorageDirectoryInspection(
    string Directory,
    IReadOnlyList<string> ExistingFileNames)
{
    /// <summary>目录中是否已有任意配置文件（说明它不是空目录）。</summary>
    public bool HasConfiguration => ExistingFileNames.Count > 0;

    /// <summary>
    /// 两份配置（<c>workspace.json</c> 与 <c>config.json</c>）是否都齐全。
    /// 只有部分存在时，切换流程需要额外提示用户确认。
    /// </summary>
    public bool HasCompleteConfiguration =>
        ExistingFileNames.Contains(WorkspaceProfileStore.ProfileFileName) &&
        ExistingFileNames.Contains(WorkspaceProfileStore.LauncherConfigFileName);
}

/// <summary>
/// 一次已经「备好」的配置目录切换。
/// <para>
/// 准备阶段可能已经把数据复制到了空的目标目录，但<b>只有 <see cref="Complete"/>
/// 才会真正改写启动定位文件并删除原目录的文件</b>。
/// 这里刻意不使用 <c>Dispose</c> 之类的隐式提交——切换目录属于破坏性操作，
/// 必须由调用方显式确认后再提交。
/// </para>
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

    /// <summary>切换前的原配置目录。</summary>
    public string PreviousDirectory { get; }

    /// <summary>切换后的目标配置目录。</summary>
    public string TargetDirectory { get; }

    /// <summary>本次切换最终采用的档案（可能来自目标目录，也可能是被迁移过去的原档案）。</summary>
    public WorkspaceProfile AppliedProfile { get; }

    /// <summary>是否采用了目标目录中<b>已存在</b>的配置，而不是把原配置迁移过去。</summary>
    public bool AppliedExistingConfiguration { get; }

    /// <summary>备份目录路径；用户选择 <see cref="ExistingConfigurationAction.BackupPrevious"/> 时非空。</summary>
    public string? BackupDirectory { get; }

    /// <summary>
    /// 清理原目录配置文件时失败的文件列表。清理是「尽力而为」的，
    /// 失败不会让整次切换失效，但需要告知用户手动处理。
    /// </summary>
    public IReadOnlyList<string> CleanupFailures { get; private set; } = [];

    /// <summary>回滚过程中失败的文件列表；为空表示回滚干净。</summary>
    public IReadOnlyList<string> RollbackFailures { get; private set; } = [];

    /// <summary>目标目录在准备阶段之前是否已经存在（决定回滚时能不能删它）。</summary>
    internal bool TargetDirectoryExisted { get; }

    /// <summary>目标插件目录在准备阶段之前是否已经存在（决定回滚时能不能删它）。</summary>
    internal bool TargetPluginDirectoryExisted { get; }

    /// <summary>
    /// 构造一个「什么都不用做」的事务：目标目录与当前目录相同。
    /// 提交与回滚都是空操作，用于让调用方不必分叉处理。
    /// </summary>
    /// <param name="owner">档案存储实例。</param>
    /// <param name="directory">当前（也即目标）目录。</param>
    /// <param name="profile">当前档案。</param>
    /// <returns>已完成状态等价的空事务。</returns>
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
    /// 提交切换：<b>先</b>写入目标目录的定位文件，确认落盘成功后，
    /// 才对原目录的配置文件做「尽力而为」的清理。
    /// <para>
    /// 顺序很重要：先落定位文件可以保证即使后续清理失败，
    /// 下次启动也只会从新目录读取，不会出现配置丢失。
    /// 已回滚的事务再提交会抛异常。
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">事务已回滚。</exception>
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
    /// 回滚切换：只清理准备阶段自己造出来的东西。
    /// <para>
    /// 目标目录里<b>原本就有</b>的配置绝不会被删除；
    /// 若目标目录是本次新建的则整个删掉，只保留本次生成的备份。
    /// </para>
    /// </summary>
    /// <returns>回滚失败的文件列表；为空表示回滚干净。</returns>
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

    /// <summary>由档案存储在清理原目录后回填失败列表（内部使用，调用方无需关心）。</summary>
    /// <param name="failures">清理失败的文件名列表。</param>
    internal void SetCleanupFailures(IReadOnlyList<string> failures) =>
        CleanupFailures = failures;

    private enum TransactionState
    {
        Prepared,
        Completed,
        RolledBack
    }
}
