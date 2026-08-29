using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Plugins;

/// <summary>Typed settings adapter shared by running plugins and the settings page.</summary>
internal sealed class PluginSettingsStore : IPluginSettings
{
    private const long MaximumImportedFileBytes = 512L * 1024 * 1024;
    private const long MaximumBatchImportedBytes = 1024L * 1024 * 1024;
    private const long MaximumPrivateFileBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumPrivateFileCount = 512;
    private const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private const int MaximumStoredValueCharacters = 32768;
    private const int MaximumInstanceCount = 512;
    private const int MaximumInstanceIdLength = 256;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, PluginSettingDefinition> _definitions;
    private readonly string _dataDirectory;
    private readonly string _filePath;
    private SettingsDocument _document;

    public PluginSettingsStore(
        string dataDirectory,
        IReadOnlyList<PluginSettingDefinition> definitions)
    {
        Directory.CreateDirectory(dataDirectory);
        var settingsDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(dataDirectory));
        _dataDirectory = Path.Combine(settingsDirectory, "data");
        Directory.CreateDirectory(_dataDirectory);
        _filePath = Path.Combine(settingsDirectory, "settings.json");
        _definitions = definitions
            .ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);
        _document = Load();
    }

    public event EventHandler<PluginSettingChangedEventArgs>? Changed;

    internal string FilePath => _filePath;

    internal string? LoadError { get; private set; }

    public bool TryGet<T>(string key, out T? value, string? instanceId = null)
    {
        lock (_gate)
        {
            if (!TryGetNode(key, instanceId, out var node))
            {
                value = default;
                return false;
            }

            try
            {
                value = node.Deserialize<T>(JsonOptions);
                return true;
            }
            catch (JsonException)
            {
                value = default;
                return false;
            }
        }
    }

    public T Get<T>(string key, T fallback, string? instanceId = null) =>
        TryGet<T>(key, out var value, instanceId) && value is not null ? value : fallback;

    public ValueTask SetAsync<T>(
        string key,
        T value,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = GetDefinition(key, instanceId);
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);

        lock (_gate)
        {
            var next = CloneDocument(_document);
            var imports = new List<StagedFileImport>();
            try
            {
                node = PrepareValueForStorage(definition, node, instanceId, imports);
                ValidateDefinitionValue(definition, node);
                SetNode(next, definition, node, instanceId);
                CommitDocument(next, imports);
                _document = next;
                CleanupManagedFileOrphans(next, definition, instanceId);
            }
            finally
            {
                foreach (var import in imports)
                    import.Cleanup();
            }
        }

        Changed?.Invoke(this, new PluginSettingChangedEventArgs(
            definition.Key,
            definition.Scope,
            instanceId));
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(
        string key,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = GetDefinition(key, instanceId);

        lock (_gate)
        {
            var next = CloneDocument(_document);
            var target = GetTarget(next, definition, instanceId, create: false);
            target?.Remove(definition.Key);
            PluginCatalog.SaveJsonAtomically(_filePath, next, MaximumDocumentBytes);
            _document = next;
            CleanupManagedFileOrphans(next, definition, instanceId);
        }

        Changed?.Invoke(this, new PluginSettingChangedEventArgs(
            definition.Key,
            definition.Scope,
            instanceId));
        return ValueTask.CompletedTask;
    }

    public IReadOnlyDictionary<string, string?> GetGlobalDisplayValues()
    {
        lock (_gate)
        {
            return _definitions.Values
                .Where(definition => definition.Scope == PluginSettingScope.Global)
                .ToDictionary(
                    definition => definition.Key,
                    definition => ToDisplayValue(
                        TryGetNode(definition.Key, null, out var node)
                            ? node
                            : definition.DefaultValue is { } defaultValue
                                ? JsonNode.Parse(defaultValue.GetRawText())
                                : null,
                        definition.Kind),
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveGlobalDisplayValues(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var changes = new List<PluginSettingDefinition>();

        lock (_gate)
        {
            var next = CloneDocument(_document);
            var imports = new List<StagedFileImport>();
            try
            {
                foreach (var (key, text) in values)
                {
                    var definition = GetDefinition(key, null);
                    if (definition.Scope != PluginSettingScope.Global)
                        continue;

                    var node = ParseDisplayValue(definition, text);
                    node = PrepareValueForStorage(definition, node, null, imports);
                    ValidateDefinitionValue(definition, node);
                    SetNode(next, definition, node, null);
                    changes.Add(definition);
                }

                CommitDocument(next, imports);
                _document = next;
                foreach (var definition in changes
                             .Where(item => item.Kind == PluginSettingKind.File)
                             .DistinctBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    CleanupManagedFileOrphans(next, definition, null);
                }
            }
            finally
            {
                foreach (var import in imports)
                    import.Cleanup();
            }
        }

        foreach (var definition in changes)
        {
            Changed?.Invoke(this, new PluginSettingChangedEventArgs(
                definition.Key,
                definition.Scope,
                null));
        }
    }

    private SettingsDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new SettingsDocument();

            var document = JsonSerializer.Deserialize<SettingsDocument>(
                       PluginCatalog.ReadBoundedUtf8Text(
                           _filePath,
                           MaximumDocumentBytes,
                           "插件设置文件"),
                       JsonOptions) ?? new SettingsDocument();
            document.Global ??= new JsonObject();
            document.Instances ??= new JsonObject();
            NormalizeLoadedDocument(document);
            return document;
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            BackupInvalidSettingsFile();
            LoadError = $"设置文件无效，已隔离并使用默认值：{exception.Message}";
            return new SettingsDocument();
        }
    }

    private void NormalizeLoadedDocument(SettingsDocument document)
    {
        if (document.Instances.Count > MaximumInstanceCount)
            throw new InvalidDataException(
                $"插件设置最多包含 {MaximumInstanceCount} 个实例。" );

        var ignored = 0;
        foreach (var (key, node) in document.Global.ToArray())
        {
            if (!_definitions.TryGetValue(key, out var definition) ||
                definition.Scope != PluginSettingScope.Global ||
                !IsStoredValueValid(definition, node))
            {
                document.Global.Remove(key);
                ignored++;
            }
        }

        foreach (var (instanceId, node) in document.Instances.ToArray())
        {
            if (string.IsNullOrWhiteSpace(instanceId) ||
                instanceId.Length > MaximumInstanceIdLength ||
                node is not JsonObject instance)
            {
                document.Instances.Remove(instanceId);
                ignored++;
                continue;
            }

            foreach (var (key, value) in instance.ToArray())
            {
                if (!_definitions.TryGetValue(key, out var definition) ||
                    definition.Scope != PluginSettingScope.MinecraftInstance ||
                    !IsStoredValueValid(definition, value))
                {
                    instance.Remove(key);
                    ignored++;
                }
            }
        }

        if (ignored > 0)
            LoadError = $"已忽略 {ignored} 项不符合当前设置清单的持久值。";
    }

    private bool IsStoredValueValid(
        PluginSettingDefinition definition,
        JsonNode? node)
    {
        try
        {
            ValidateDefinitionValue(definition, node);
            var value = ToDisplayValue(node, definition.Kind);
            if (string.IsNullOrWhiteSpace(value))
                return !definition.Required;

            if (definition.Kind == PluginSettingKind.File)
            {
                if (Path.IsPathFullyQualified(value))
                    return false;
                ValidateReadableFile(ResolvePrivatePath(value), definition);
            }
            else if (definition.Kind == PluginSettingKind.Directory)
            {
                if (!Path.IsPathFullyQualified(value) || !Directory.Exists(value))
                    return false;
                EnsurePathHasNoReparsePoints(value, definition.Title);
            }

            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private void BackupInvalidSettingsFile()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;
            var directory = Path.GetDirectoryName(_filePath)!;
            var backupPath = Path.Combine(
                directory,
                $"settings.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.Move(_filePath, backupPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep the original untouched when a diagnostic move is blocked.
        }
    }

    internal bool RequiresUserFileRead(string key) =>
        _definitions.TryGetValue(key, out var definition) &&
        definition.Kind == PluginSettingKind.Directory;

    private PluginSettingDefinition GetDefinition(string key, string? instanceId)
    {
        if (!_definitions.TryGetValue(key, out var definition))
            throw new KeyNotFoundException($"未声明插件设置 {key}。");
        if (instanceId?.Length > MaximumInstanceIdLength)
            throw new ArgumentException(
                $"instanceId 不能超过 {MaximumInstanceIdLength} 个字符。",
                nameof(instanceId));
        if (definition.Scope == PluginSettingScope.MinecraftInstance !=
            !string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException(
                definition.Scope == PluginSettingScope.MinecraftInstance
                    ? $"实例设置 {key} 必须提供 instanceId。"
                    : $"全局设置 {key} 不能提供 instanceId。",
                nameof(instanceId));
        }

        return definition;
    }

    private bool TryGetNode(string key, string? instanceId, out JsonNode node)
    {
        var definition = GetDefinition(key, instanceId);
        var target = GetTarget(_document, definition, instanceId, create: false);
        if (target?.TryGetPropertyValue(definition.Key, out var stored) == true && stored is not null)
        {
            node = stored;
            return true;
        }

        if (definition.DefaultValue is { } defaultValue)
        {
            node = JsonNode.Parse(defaultValue.GetRawText())!;
            return true;
        }

        node = null!;
        return false;
    }

    private static JsonObject? GetTarget(
        SettingsDocument document,
        PluginSettingDefinition definition,
        string? instanceId,
        bool create)
    {
        if (definition.Scope == PluginSettingScope.Global)
            return document.Global;

        if (document.Instances.TryGetPropertyValue(instanceId!, out var existing) &&
            existing is JsonObject instance)
        {
            return instance;
        }

        if (!create)
            return null;
        if (document.Instances.Count >= MaximumInstanceCount)
            throw new InvalidOperationException(
                $"单个插件最多保存 {MaximumInstanceCount} 个实例的设置。" );

        var created = new JsonObject();
        document.Instances[instanceId!] = created;
        return created;
    }

    private static void SetNode(
        SettingsDocument document,
        PluginSettingDefinition definition,
        JsonNode? node,
        string? instanceId)
    {
        var target = GetTarget(document, definition, instanceId, create: true)!;
        target[definition.Key] = node?.DeepClone();
    }

    private static SettingsDocument CloneDocument(SettingsDocument source) => new()
    {
        Global = (JsonObject)source.Global.DeepClone(),
        Instances = (JsonObject)source.Instances.DeepClone()
    };

    private JsonNode? PrepareValueForStorage(
        PluginSettingDefinition definition,
        JsonNode? node,
        string? instanceId,
        ICollection<StagedFileImport> imports)
    {
        if (node is null || definition.Kind is not (PluginSettingKind.File or PluginSettingKind.Directory))
            return node;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
            throw new ArgumentException($"设置 {definition.Title} 必须是路径字符串。");
        if (string.IsNullOrWhiteSpace(text))
            return node;

        return definition.Kind == PluginSettingKind.File
            ? JsonValue.Create(PrepareFileValue(definition, text, instanceId, imports))
            : JsonValue.Create(PrepareDirectoryValue(definition, text));
    }

    private string PrepareFileValue(
        PluginSettingDefinition definition,
        string value,
        string? instanceId,
        ICollection<StagedFileImport> imports)
    {
        // A previously imported relative path already belongs to this plugin and
        // can be reused without making a redundant copy.
        if (!Path.IsPathFullyQualified(value))
        {
            var existing = ResolvePrivatePath(value);
            ValidateReadableFile(existing, definition);
            return ToPrivateRelativePath(existing);
        }

        var sourcePath = Path.GetFullPath(value);
        ValidateReadableFile(sourcePath, definition);
        var suffix = GetStoredFileSuffix(definition, sourcePath);
        var targetPath = Path.Combine(
            GetManagedFileDirectory(definition, instanceId),
            $"value{suffix}");

        if (string.Equals(sourcePath, targetPath, PathComparison))
            return ToPrivateRelativePath(targetPath);

        // Retry cleanup from an earlier interrupted/best-effort pass before
        // measuring the next import, while preserving every path in current JSON.
        CleanupManagedFileOrphans(_document, definition, instanceId);
        var sourceLength = new FileInfo(sourcePath).Length;
        EnsurePrivateFileQuota(additionalFiles: 1, additionalBytes: sourceLength);
        var import = StageFileImport(sourcePath, targetPath);
        try
        {
            // Recount after the copy to catch source-size changes and external
            // additions that raced the initial projection.
            EnsurePrivateFileQuota();
        }
        catch
        {
            import.Cleanup();
            throw;
        }
        if (imports.Sum(item => item.Length) + import.Length > MaximumBatchImportedBytes)
        {
            import.Cleanup();
            throw new ArgumentException("一次设置保存导入的文件总量不能超过 1 GiB。");
        }
        imports.Add(import);
        return ToPrivateRelativePath(targetPath);
    }

    private static string PrepareDirectoryValue(
        PluginSettingDefinition definition,
        string value)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                $"设置 {definition.Title} 必须是用户明确选择的绝对目录路径。");
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"设置 {definition.Title} 指向的目录不存在。");
        EnsurePathHasNoReparsePoints(fullPath, definition.Title);
        return fullPath;
    }

    private StagedFileImport StageFileImport(string sourcePath, string targetPath)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath) ??
                              throw new IOException("无法确定插件文件导入目录。");
        Directory.CreateDirectory(targetDirectory);
        EnsurePathHasNoReparsePoints(targetDirectory, "插件私有数据目录");
        if (File.Exists(targetPath))
            EnsurePathHasNoReparsePoints(targetPath, "插件私有文件");

        var temporaryPath = Path.Combine(
            targetDirectory,
            $".import-{Guid.NewGuid():N}.tmp");
        try
        {
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            if (source.Length > MaximumImportedFileBytes)
                throw new ArgumentException("导入文件不能超过 512 MiB。");

            using var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            long copied = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                copied += read;
                if (copied > MaximumImportedFileBytes)
                    throw new ArgumentException("导入文件不能超过 512 MiB。");
                destination.Write(buffer, 0, read);
            }
            destination.Flush(flushToDisk: true);
            return new StagedFileImport(temporaryPath, targetPath, copied);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private void CommitDocument(SettingsDocument next, IReadOnlyList<StagedFileImport> imports)
    {
        try
        {
            foreach (var import in imports)
            {
                // A backup briefly coexists with all staged files. Check each
                // replacement at its actual commit point so duplicate targets
                // and external changes cannot bypass the peak quota.
                import.Commit(existingTarget =>
                {
                    EnsurePathHasNoReparsePoints(existingTarget, "插件私有文件");
                    EnsurePrivateFileQuota(
                        additionalFiles: 1,
                        additionalBytes: new FileInfo(existingTarget).Length);
                });
            }
            PluginCatalog.SaveJsonAtomically(_filePath, next, MaximumDocumentBytes);
        }
        catch
        {
            // settings.json is the source of truth. Restore imported targets if
            // that final write fails so memory, JSON, and private files agree.
            for (var index = imports.Count - 1; index >= 0; index--)
                imports[index].TryRollback();
            throw;
        }
        finally
        {
            foreach (var import in imports)
                import.Cleanup();
        }
    }

    private string GetManagedFileDirectory(
        PluginSettingDefinition definition,
        string? instanceId)
    {
        var scopeDirectory = definition.Scope == PluginSettingScope.Global
            ? "global"
            : Path.Combine("instances", CreateStableSegment(instanceId!));
        return ResolvePrivatePath(Path.Combine(
            "settings-files",
            scopeDirectory,
            definition.Key));
    }

    private void CleanupManagedFileOrphans(
        SettingsDocument document,
        PluginSettingDefinition definition,
        string? instanceId)
    {
        if (definition.Kind != PluginSettingKind.File)
            return;

        try
        {
            var managedDirectory = GetManagedFileDirectory(definition, instanceId);
            if (!Directory.Exists(managedDirectory))
                return;
            EnsurePathHasNoReparsePoints(managedDirectory, "插件私有文件目录");

            var retainedPaths = GetStoredPrivateFilePaths(document);
            foreach (var candidate in Directory.EnumerateFiles(
                         managedDirectory,
                         "value*",
                         SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(candidate);
                if (!string.Equals(fileName, "value", StringComparison.Ordinal) &&
                    !fileName.StartsWith("value.", StringComparison.Ordinal))
                {
                    continue;
                }
                if (retainedPaths.Contains(candidate))
                    continue;
                if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                    continue;
                TryDeleteFile(candidate);
            }

            if (!Directory.EnumerateFileSystemEntries(managedDirectory).Any())
                Directory.Delete(managedDirectory, recursive: false);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // settings.json is already durable. A cleanup failure may leave an
            // orphan, but never invalidates or deletes the currently referenced file.
        }
    }

    private HashSet<string> GetStoredPrivateFilePaths(SettingsDocument document)
    {
        var paths = new HashSet<string>(PathComparer);
        AddStoredPrivateFilePaths(document.Global, paths);
        foreach (var (_, instanceNode) in document.Instances)
        {
            if (instanceNode is JsonObject instance)
                AddStoredPrivateFilePaths(instance, paths);
        }
        return paths;
    }

    private void AddStoredPrivateFilePaths(JsonObject values, ISet<string> paths)
    {
        foreach (var (key, node) in values)
        {
            if (!_definitions.TryGetValue(key, out var definition) ||
                definition.Kind != PluginSettingKind.File ||
                node is not JsonValue value ||
                !value.TryGetValue<string>(out var relativePath) ||
                string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathFullyQualified(relativePath))
            {
                continue;
            }

            try
            {
                paths.Add(ResolvePrivatePath(relativePath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Invalid loaded values are filtered earlier. Keep cleanup
                // defensive in case the in-memory document is externally raced.
            }
        }
    }

    private void EnsurePrivateFileQuota(int additionalFiles = 0, long additionalBytes = 0)
    {
        if (additionalFiles < 0 || additionalBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(additionalFiles));
        if (additionalFiles > MaximumPrivateFileCount)
        {
            throw new ArgumentException(
                $"插件私有设置文件（含事务临时文件）最多保存 {MaximumPrivateFileCount} 个。");
        }
        if (additionalBytes > MaximumPrivateFileBytes)
        {
            throw new ArgumentException(
                "插件私有设置文件（含事务临时文件）总量不能超过 2 GiB。");
        }

        var root = ResolvePrivatePath("settings-files");
        var fileCount = 0;
        long totalBytes = 0;
        if (Directory.Exists(root))
        {
            EnsurePathHasNoReparsePoints(root, "插件私有文件目录");
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new ArgumentException(
                            "插件私有文件目录不能包含符号链接或重解析点。");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                        continue;
                    }

                    fileCount++;
                    if (fileCount > MaximumPrivateFileCount - additionalFiles)
                    {
                        throw new ArgumentException(
                            $"插件私有设置文件（含事务临时文件）最多保存 {MaximumPrivateFileCount} 个。");
                    }
                    var length = new FileInfo(entry).Length;
                    if (length > MaximumPrivateFileBytes - additionalBytes - totalBytes)
                    {
                        throw new ArgumentException(
                            "插件私有设置文件（含事务临时文件）总量不能超过 2 GiB。");
                    }
                    totalBytes += length;
                }
            }
        }
    }

    private string ResolvePrivatePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
            throw new ArgumentException("插件私有文件路径必须是非空相对路径。");

        var candidate = Path.GetFullPath(Path.Combine(_dataDirectory, relativePath));
        var comparison = PathComparison;
        if (!candidate.StartsWith(_dataDirectory + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("插件私有文件路径超出了插件数据目录。");
        return candidate;
    }

    private string ToPrivateRelativePath(string fullPath) =>
        Path.GetRelativePath(_dataDirectory, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static void ValidateReadableFile(
        string fullPath,
        PluginSettingDefinition definition)
    {
        if (!File.Exists(fullPath))
            throw new ArgumentException($"设置 {definition.Title} 指向的文件不存在。");
        EnsurePathHasNoReparsePoints(fullPath, definition.Title);
        var length = new FileInfo(fullPath).Length;
        if (length > MaximumImportedFileBytes)
            throw new ArgumentException($"设置 {definition.Title} 的文件不能超过 512 MiB。");

        if (definition.FileExtensions.Count > 0 &&
            !definition.FileExtensions.Any(extension =>
                fullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"设置 {definition.Title} 的文件扩展名不受支持。");
        }
    }

    private static string GetStoredFileSuffix(
        PluginSettingDefinition definition,
        string sourcePath) => definition.FileExtensions
            .OrderByDescending(extension => extension.Length)
            .FirstOrDefault(extension =>
                sourcePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        ?? Path.GetExtension(sourcePath);

    private static void EnsurePathHasNoReparsePoints(string path, string settingTitle)
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
                break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"设置 {settingTitle} 不能使用符号链接或重解析点。");
            }
        }
    }

    private static string CreateStableSegment(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonNode? ParseDisplayValue(
        PluginSettingDefinition definition,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text) && !definition.Required)
            return null;

        return definition.Kind switch
        {
            PluginSettingKind.Boolean when bool.TryParse(text, out var boolean) =>
                JsonValue.Create(boolean),
            PluginSettingKind.Integer when long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var integer) => JsonValue.Create(integer),
            PluginSettingKind.Number when double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number) && double.IsFinite(number) => JsonValue.Create(number),
            PluginSettingKind.Boolean or PluginSettingKind.Integer or PluginSettingKind.Number =>
                throw new ArgumentException($"设置 {definition.Title} 的值格式无效。"),
            _ => JsonValue.Create(text ?? string.Empty)
        };
    }

    internal static void ValidateDefinitionValue(
        PluginSettingDefinition definition,
        JsonNode? node)
    {
        if (node is null)
        {
            if (definition.Required)
                throw new ArgumentException($"设置 {definition.Title} 不能为空。");
            return;
        }

        var text = ToDisplayValue(node, definition.Kind) ?? string.Empty;
        if (text.Length > MaximumStoredValueCharacters)
            throw new ArgumentException(
                $"设置 {definition.Title} 不能超过 {MaximumStoredValueCharacters} 个字符。" );
        if (definition.Required && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"设置 {definition.Title} 不能为空。");
        if (definition.MaximumLength is int maximumLength && text.Length > maximumLength)
            throw new ArgumentException($"设置 {definition.Title} 不能超过 {maximumLength} 个字符。");

        var typeIsValid = definition.Kind switch
        {
            PluginSettingKind.Boolean =>
                node is JsonValue boolean && boolean.TryGetValue<bool>(out _),
            PluginSettingKind.Integer => TryGetInteger(node, out _),
            PluginSettingKind.Number => TryGetNumber(node, out _),
            _ => node is JsonValue value && value.TryGetValue<string>(out _)
        };
        if (!typeIsValid)
            throw new ArgumentException($"设置 {definition.Title} 的 JSON 类型与声明不一致。");

        if (definition.Kind == PluginSettingKind.Choice &&
            !definition.Options.Any(option => string.Equals(
                option.Value,
                text,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException($"设置 {definition.Title} 的选项无效。");
        }

        if (definition.Kind is PluginSettingKind.Integer or PluginSettingKind.Number)
        {
            if (!TryGetNumber(node, out var number) ||
                definition.Minimum is double minimum && number < minimum ||
                definition.Maximum is double maximum && number > maximum)
            {
                throw new ArgumentException($"设置 {definition.Title} 超出允许范围。");
            }

            if (definition.Step is double step && step > 0)
            {
                var origin = definition.Minimum ?? 0;
                var steps = (number - origin) / step;
                if (Math.Abs(steps - Math.Round(steps)) > 0.0000001)
                    throw new ArgumentException($"设置 {definition.Title} 不符合步长 {step}。");
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(
                        text,
                        definition.Pattern,
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(250)))
                {
                    throw new ArgumentException($"设置 {definition.Title} 不符合格式要求。");
                }
            }
            catch (ArgumentException exception) when (
                !exception.Message.StartsWith("设置 ", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"设置 {definition.Title} 声明了无效的正则表达式。",
                    exception);
            }
            catch (RegexMatchTimeoutException exception)
            {
                throw new ArgumentException(
                    $"设置 {definition.Title} 的格式校验超时。",
                    exception);
            }
        }

        if (definition.Kind == PluginSettingKind.File &&
            !string.IsNullOrWhiteSpace(text) &&
            definition.FileExtensions.Count > 0 &&
            !definition.FileExtensions.Any(extension =>
                text.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"设置 {definition.Title} 的文件扩展名不受支持。");
        }
    }

    private static bool TryGetInteger(JsonNode node, out long number)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out number))
                return true;
            if (value.TryGetValue<int>(out var integer))
            {
                number = integer;
                return true;
            }
        }

        number = 0;
        return false;
    }

    private static bool TryGetNumber(JsonNode node, out double number)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out number) && double.IsFinite(number))
                return true;
            if (TryGetInteger(node, out var integer))
            {
                number = integer;
                return true;
            }
            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                number = (double)decimalValue;
                return double.IsFinite(number);
            }
        }

        number = 0;
        return false;
    }

    private static string? ToDisplayValue(JsonNode? node, PluginSettingKind kind)
    {
        if (node is null)
            return null;

        if (kind == PluginSettingKind.Boolean && node is JsonValue boolean &&
            boolean.TryGetValue<bool>(out var boolValue))
        {
            return boolValue ? "true" : "false";
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
            return stringValue;
        return node.ToJsonString();
    }

    private sealed class SettingsDocument
    {
        public JsonObject Global { get; set; } = new();

        public JsonObject Instances { get; set; } = new();
    }

    private sealed class StagedFileImport(
        string temporaryPath,
        string targetPath,
        long length)
    {
        private string? _backupPath;
        private bool _preserveBackup;
        private bool _targetReplaced;

        public long Length { get; } = length;

        public void Commit(Action<string> reserveBackup)
        {
            if (File.Exists(targetPath))
            {
                reserveBackup(targetPath);
                _backupPath = Path.Combine(
                    Path.GetDirectoryName(targetPath)!,
                    $".backup-{Guid.NewGuid():N}.tmp");
                File.Copy(targetPath, _backupPath, overwrite: false);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            _targetReplaced = true;
        }

        public void TryRollback()
        {
            if (!_targetReplaced)
            {
                // A failed atomic move normally leaves the old target untouched.
                // If an external race removed it, keep or restore the completed
                // backup instead of deleting the only recoverable copy.
                if (_backupPath is not null &&
                    File.Exists(_backupPath) &&
                    !File.Exists(targetPath))
                {
                    try
                    {
                        File.Move(_backupPath, targetPath, overwrite: false);
                    }
                    catch (IOException)
                    {
                        _preserveBackup = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _preserveBackup = true;
                    }
                }
                return;
            }
            try
            {
                if (_backupPath is not null && File.Exists(_backupPath))
                    File.Move(_backupPath, targetPath, overwrite: true);
                else
                    File.Delete(targetPath);
            }
            catch (IOException)
            {
                _preserveBackup = true;
            }
            catch (UnauthorizedAccessException)
            {
                _preserveBackup = true;
            }
        }

        public void Cleanup()
        {
            TryDeleteFile(temporaryPath);
            if (_backupPath is not null && !_preserveBackup)
                TryDeleteFile(_backupPath);
        }
    }
}
