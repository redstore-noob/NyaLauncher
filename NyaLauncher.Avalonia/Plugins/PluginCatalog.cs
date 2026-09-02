using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Plugins;

internal enum PluginStatus
{
    Disabled,
    Enabling,
    Enabled,
    Disabling,
    Invalid,
    Incompatible,
    Failed,
    RestartRequired
}

/// <summary>
/// Launcher-owned data shown by the plugin page. It deliberately contains no
/// plugin objects so a page cannot keep an AssemblyLoadContext alive.
/// </summary>
internal sealed record PluginSnapshot
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Authors { get; init; } = [];

    public required string PackageDirectory { get; init; }

    public string? IconPath { get; init; }

    public PluginStatus Status { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsBusy => Status is PluginStatus.Enabling or PluginStatus.Disabling;

    public string? Error { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<string> GrantedCapabilities { get; init; } = [];

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyList<string> OptionalCapabilities { get; init; } = [];

    /// <summary>
    /// Launcher-owned origin bound atomically to the installed package. Null
    /// means a manual or pre-origin installation and is never auto-updated.
    /// </summary>
    public PluginInstallOrigin? InstallOrigin { get; init; }

    public string? InstallOriginWarning { get; init; }

    public IReadOnlyList<PluginSettingDefinition> SettingDefinitions { get; init; } = [];

    public IReadOnlyDictionary<string, string?> Settings { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Immutable launcher-owned view of a persistent command contributed by an
/// enabled plugin. It is safe for instance pages to retain across refreshes.
/// </summary>
internal sealed record PluginInstanceActionSnapshot(
    string PluginId,
    string PluginName,
    string ExtensionId,
    string ActionId,
    string Title,
    string Description,
    string Glyph,
    bool IsDestructive,
    string? ConfirmationMessage);

internal sealed record PluginCatalogSnapshot
{
    public required string PackagesDirectory { get; init; }

    public IReadOnlyList<PluginSnapshot> Plugins { get; init; } = [];

    public IReadOnlyList<PluginInstanceActionSnapshot> InstanceActions { get; init; } = [];

    public bool IsScanning { get; init; }

    public string? Error { get; init; }

    public static PluginCatalogSnapshot Empty(string packagesDirectory) => new()
    {
        PackagesDirectory = packagesDirectory
    };
}

internal sealed record PluginOperationResult(
    bool Success,
    string Message,
    bool RequiresApproval = false,
    IReadOnlyList<string>? PendingCapabilities = null)
{
    public static PluginOperationResult Completed(string message) => new(true, message);

    public static PluginOperationResult Failed(string message) => new(false, message);

    public static PluginOperationResult ApprovalRequired(
        string message,
        IReadOnlyList<string> pendingCapabilities) =>
        new(false, message, true, pendingCapabilities);
}

internal sealed record PluginPackage(
    string PackageDirectory,
    string ManifestPath,
    PluginManifest? Manifest,
    PluginStatus Status,
    string? Error,
    PluginInstallOrigin? InstallOrigin = null,
    string? InstallOriginWarning = null)
{
    public string CatalogKey =>
        Manifest is not null && Status is not PluginStatus.Invalid
            ? Manifest.Id
            : PackageDirectory;
}

internal sealed record PluginInstallOrigin
{
    public int SchemaVersion { get; init; } = 1;

    public required int SourceIndexSchemaVersion { get; init; }

    public required string Id { get; init; }

    public required string LineageId { get; init; }

    public required int Generation { get; init; }

    public long? RepositoryId { get; init; }

    public long? OwnerId { get; init; }

    public required string RepositoryUrl { get; init; }

    public required string Version { get; init; }

    public required string Sha256 { get; init; }
}

internal sealed class PluginStateEntry
{
    public bool Enabled { get; set; }

    public List<string> GrantedCapabilities { get; set; } = [];

    public string? LastError { get; set; }

    /// <summary>
    /// Cache of the package-owned origin used only to choose an isolated data
    /// directory. The metadata file inside the package is authoritative and is
    /// synchronized here after every scan/recovery.
    /// </summary>
    public PluginInstallOrigin? InstallOrigin { get; set; }
}

/// <summary>
/// Scans package manifests and owns launcher-side state/settings persistence.
/// Executable assemblies are intentionally loaded by PluginRuntimeHost only.
/// </summary>
internal sealed class PluginCatalog
{
    internal const string InstallOriginFileName = ".nyalauncher-origin.json";

    private const int StateVersion = 1;
    private const int MaximumPackageCount = 256;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumInstallOriginBytes = 16 * 1024;
    private const int MaximumStateBytes = 4 * 1024 * 1024;
    private const int MaximumRememberedPlugins = 4096;
    private const int MaximumCapabilityCount = 64;
    private const int MaximumSettingCount = 256;
    private static readonly Regex PluginIdPattern = new(
        "^[a-z][a-z0-9]*(?:\\.[a-z0-9][a-z0-9-]*)+$",
        RegexOptions.CultureInvariant);
    private static readonly Regex SettingKeyPattern = new(
        "^[a-zA-Z][a-zA-Z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex OriginLineagePattern = new(
        "^(?:[a-z][a-z0-9]*(?:\\.[a-z0-9][a-z0-9-]*)+|" +
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$",
        RegexOptions.CultureInvariant);
    private static readonly Regex GitHubRepositoryPattern = new(
        "^https://github\\.com/" +
        "[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?/" +
        "[A-Za-z0-9_.-]{1,100}/?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> KnownCapabilities = new(
        [
            PluginCapabilities.Components,
            PluginCapabilities.NativeUi,
            PluginCapabilities.NetworkHttp,
            PluginCapabilities.SystemInformationRead,
            PluginCapabilities.UserFilesRead,
            PluginCapabilities.UserFilesWrite,
            PluginCapabilities.ProcessStart,
            PluginCapabilities.MinecraftInstanceRead,
            PluginCapabilities.MinecraftInstanceModify,
            PluginCapabilities.MinecraftLaunchModify
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly SemanticVersion LauncherVersion =
        SemanticVersion.LauncherVersion;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
        }
    };
    private static readonly JsonSerializerOptions InstallOriginJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _stateGate = new();
    private PluginStateDocument _state = new();

    public PluginCatalog(string storageDirectory, bool loadState = true)
    {
        SetStorageDirectory(storageDirectory);
        if (loadState)
            LoadState();
    }

    public string StorageDirectory { get; private set; } = string.Empty;

    public string RootDirectory { get; private set; } = string.Empty;

    public string PackagesDirectory { get; private set; } = string.Empty;

    public string DataDirectory { get; private set; } = string.Empty;

    public string StateFilePath { get; private set; } = string.Empty;

    public void SetStorageDirectory(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);

        StorageDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(storageDirectory));
        RootDirectory = Path.Combine(StorageDirectory, "plugins");
        PackagesDirectory = Path.Combine(RootDirectory, "packages");
        DataDirectory = Path.Combine(RootDirectory, "data");
        StateFilePath = Path.Combine(RootDirectory, "state.json");
        Directory.CreateDirectory(PackagesDirectory);
        Directory.CreateDirectory(DataDirectory);
    }

    public IReadOnlyList<PluginPackage> Scan()
    {
        Directory.CreateDirectory(PackagesDirectory);
        var packages = new List<PluginPackage>();
        var directories = Directory.EnumerateDirectories(PackagesDirectory)
            .Take(MaximumPackageCount + 1)
            .ToArray();

        foreach (var directory in directories
                     .Take(MaximumPackageCount)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var nominalManifestPath = Path.Combine(directory, "plugin.json");
            if (!File.Exists(nominalManifestPath))
            {
                packages.Add(new PluginPackage(
                    directory,
                    nominalManifestPath,
                    null,
                    PluginStatus.Invalid,
                    "插件目录缺少 plugin.json。"));
                continue;
            }

            var manifestPath = nominalManifestPath;
            try
            {
                if (!TryResolvePackagePath(directory, "plugin.json", out manifestPath))
                    throw new InvalidDataException("插件目录或 plugin.json 不能是符号链接或重解析点。");
                var manifest = JsonSerializer.Deserialize<PluginManifest>(
                    ReadBoundedUtf8Text(
                        manifestPath,
                        MaximumManifestBytes,
                        "plugin.json"),
                    JsonOptions);
                if (manifest is not null)
                {
                    // JSON may explicitly assign null despite non-nullable SDK
                    // annotations. Normalize collections before validation.
                    manifest = manifest with
                    {
                        Id = manifest.Id ?? string.Empty,
                        Name = manifest.Name ?? string.Empty,
                        Version = manifest.Version ?? string.Empty,
                        ApiVersion = manifest.ApiVersion ?? string.Empty,
                        Description = manifest.Description ?? string.Empty,
                        EntryAssembly = manifest.EntryAssembly ?? string.Empty,
                        EntryType = manifest.EntryType ?? string.Empty,
                        Authors = manifest.Authors?.Where(value => value is not null).ToArray() ?? [],
                        RequiredCapabilities = manifest.RequiredCapabilities?
                            .Where(value => value is not null).ToArray() ?? [],
                        OptionalCapabilities = manifest.OptionalCapabilities?
                            .Where(value => value is not null).ToArray() ?? [],
                        Settings = manifest.Settings?
                            .Where(setting => setting is not null)
                            .Select(setting => setting with
                            {
                                Key = setting.Key ?? string.Empty,
                                Title = setting.Title ?? string.Empty,
                                Description = setting.Description ?? string.Empty,
                                Options = setting.Options?
                                    .Where(option => option is not null)
                                    .Select(option => option with
                                    {
                                        Value = option.Value ?? string.Empty,
                                        Label = option.Label ?? string.Empty,
                                        Description = option.Description ?? string.Empty
                                    })
                                    .ToArray() ?? [],
                                FileExtensions = setting.FileExtensions?
                                    .Where(extension => extension is not null).ToArray() ?? []
                            })
                            .ToArray() ?? []
                    };
                }
                var error = ValidateManifest(manifest, directory);
                if (error is null)
                {
                    var (origin, originWarning) = ReadInstallOrigin(directory, manifest!);
                    packages.Add(new PluginPackage(
                        directory,
                        manifestPath,
                        manifest,
                        PluginStatus.Disabled,
                        null,
                        origin,
                        originWarning));
                }
                else
                {
                    packages.Add(new PluginPackage(
                        directory,
                        manifestPath,
                        null,
                        error.StartsWith("API ", StringComparison.Ordinal)
                            ? PluginStatus.Incompatible
                            : PluginStatus.Invalid,
                        error));
                }
            }
            catch (JsonException exception)
            {
                packages.Add(new PluginPackage(
                    directory,
                    manifestPath,
                    null,
                    PluginStatus.Invalid,
                    $"plugin.json 格式错误：{exception.Message}"));
            }
            catch (IOException exception)
            {
                packages.Add(new PluginPackage(
                    directory,
                    manifestPath,
                    null,
                    PluginStatus.Invalid,
                    $"无法读取 plugin.json：{exception.Message}"));
            }
            catch (UnauthorizedAccessException exception)
            {
                packages.Add(new PluginPackage(
                    directory,
                    manifestPath,
                    null,
                    PluginStatus.Invalid,
                    $"无权读取 plugin.json：{exception.Message}"));
            }
            catch (InvalidDataException exception)
            {
                packages.Add(new PluginPackage(
                    directory,
                    manifestPath,
                    null,
                    PluginStatus.Invalid,
                    exception.Message));
            }
        }

        if (directories.Length > MaximumPackageCount)
        {
            var diagnosticPath = Path.Combine(PackagesDirectory, "[scan-limit]");
            packages.Add(new PluginPackage(
                diagnosticPath,
                Path.Combine(diagnosticPath, "plugin.json"),
                null,
                PluginStatus.Invalid,
                $"插件包数量超过 {MaximumPackageCount}，其余目录未扫描。"));
        }

        MarkDuplicateIds(packages);
        return packages;
    }

    public PluginStateEntry GetState(string pluginId)
    {
        lock (_stateGate)
        {
            if (!_state.Plugins.TryGetValue(pluginId, out var state))
                return new PluginStateEntry();

            return CloneState(state);
        }
    }

    public void UpdateState(string pluginId, Action<PluginStateEntry> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(update);

        lock (_stateGate)
        {
            var next = CloneStateDocument(_state);
            if (!next.Plugins.TryGetValue(pluginId, out var state))
            {
                state = new PluginStateEntry();
                next.Plugins[pluginId] = state;
            }

            update(state);
            NormalizeStateEntry(state);
            if (next.Plugins.Count > MaximumRememberedPlugins)
                throw new InvalidDataException(
                    $"插件状态最多记录 {MaximumRememberedPlugins} 个插件。");
            SaveJsonAtomically(StateFilePath, next, MaximumStateBytes);
            _state = next;
        }
    }

    internal bool TryGetState(string pluginId, out PluginStateEntry state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_stateGate)
        {
            if (_state.Plugins.TryGetValue(pluginId, out var existing))
            {
                state = CloneState(existing);
                return true;
            }

            state = new PluginStateEntry();
            return false;
        }
    }

    public void RemoveState(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_stateGate)
        {
            if (!_state.Plugins.ContainsKey(pluginId))
                return;
            var next = CloneStateDocument(_state);
            next.Plugins.Remove(pluginId);
            SaveJsonAtomically(StateFilePath, next, MaximumStateBytes);
            _state = next;
        }
    }

    internal void RestoreStateSnapshot(string pluginId, PluginStateEntry state)
    {
        var validated = CloneValidatedStateSnapshot(pluginId, state);
        lock (_stateGate)
        {
            var next = CloneStateDocument(_state);
            next.Plugins[pluginId] = validated;
            if (next.Plugins.Count > MaximumRememberedPlugins)
                throw new InvalidDataException(
                    $"插件状态最多记录 {MaximumRememberedPlugins} 个插件。");
            SaveJsonAtomically(StateFilePath, next, MaximumStateBytes);
            _state = next;
        }
    }

    internal static PluginStateEntry CloneValidatedStateSnapshot(
        string pluginId,
        PluginStateEntry state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(pluginId) ||
            pluginId.Length > 128 ||
            !PluginIdPattern.IsMatch(pluginId) ||
            state.GrantedCapabilities is null ||
            state.GrantedCapabilities.Count > MaximumCapabilityCount ||
            state.GrantedCapabilities.Any(capability =>
                string.IsNullOrWhiteSpace(capability) || capability.Length > 128) ||
            state.GrantedCapabilities.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            state.GrantedCapabilities.Count ||
            state.LastError?.Length > 8192)
        {
            throw new InvalidDataException("插件卸载状态快照无效或超过上限。");
        }
        if (state.InstallOrigin is not null)
            ValidateInstallOrigin(state.InstallOrigin, pluginId, expectedVersion: null);
        return CloneState(state);
    }

    public void SynchronizeInstallOrigin(string pluginId, PluginInstallOrigin? origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (origin is not null)
            ValidateInstallOrigin(origin, pluginId, expectedVersion: null);
        lock (_stateGate)
        {
            if (Equals(_state.Plugins.GetValueOrDefault(pluginId)?.InstallOrigin, origin))
                return;
            var next = CloneStateDocument(_state);
            if (!next.Plugins.TryGetValue(pluginId, out var state))
            {
                state = new PluginStateEntry();
                next.Plugins[pluginId] = state;
            }
            state.InstallOrigin = origin;
            SaveJsonAtomically(StateFilePath, next, MaximumStateBytes);
            _state = next;
        }
    }

    public string GetPluginDataDirectory(string pluginId)
    {
        var state = GetState(pluginId);
        var directoryName = SanitizeDirectoryName(pluginId);
        if (state.InstallOrigin is { } origin &&
            string.Equals(origin.Id, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            var identityBytes = Encoding.UTF8.GetBytes(
                $"{origin.LineageId}\n" +
                $"{origin.Generation.ToString(CultureInfo.InvariantCulture)}\n" +
                $"{origin.RepositoryId?.ToString(CultureInfo.InvariantCulture) ?? "unbound"}\n" +
                $"{origin.OwnerId?.ToString(CultureInfo.InvariantCulture) ?? "unbound"}");
            var identitySuffix = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(identityBytes))[..16]
                .ToLowerInvariant();
            directoryName += $"--g{origin.Generation}-{identitySuffix}";
        }
        return Path.Combine(DataDirectory, directoryName);
    }

    public PluginSettingsStore OpenSettings(PluginManifest manifest) =>
        new(GetPluginDataDirectory(manifest.Id), manifest.Settings);

    internal static PluginInstallOrigin CreateInstallOrigin(
        RepositoryPlugin plugin,
        RepositoryRelease release)
    {
        var binding = RepositoryCatalogPolicy.FindGeneration(plugin, release.Generation) ??
                      throw new InvalidDataException("插件发行记录缺少发布者代际绑定。");
        var hasNumericIdentity = binding.Publisher.RepositoryId > 0 &&
                                 binding.Publisher.OwnerId > 0;
        var origin = new PluginInstallOrigin
        {
            SourceIndexSchemaVersion = plugin.LineageId is null ? 1 : 2,
            Id = plugin.Id,
            LineageId = plugin.EffectiveLineageId,
            Generation = release.Generation,
            RepositoryId = hasNumericIdentity ? binding.Publisher.RepositoryId : null,
            OwnerId = hasNumericIdentity ? binding.Publisher.OwnerId : null,
            RepositoryUrl = binding.RepositoryUrl,
            Version = release.Version,
            Sha256 = release.Download.Sha256
        };
        ValidateInstallOrigin(origin, plugin.Id, release.Version);
        return origin;
    }

    internal static void WriteInstallOrigin(
        string packageDirectory,
        PluginInstallOrigin origin)
    {
        ValidateInstallOrigin(origin, origin.Id, origin.Version);
        if (!TryResolvePackagePath(packageDirectory, InstallOriginFileName, out var path))
            throw new InvalidDataException("无法安全创建插件来源快照。");
        SaveJsonAtomically(
            path,
            origin,
            MaximumInstallOriginBytes,
            InstallOriginJsonOptions);
    }

    public void ReloadState()
    {
        lock (_stateGate)
            LoadStateCore();
    }

    private void LoadState()
    {
        lock (_stateGate)
            LoadStateCore();
    }

    private void LoadStateCore()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                _state = new PluginStateDocument();
                return;
            }

            _state = JsonSerializer.Deserialize<PluginStateDocument>(
                         ReadBoundedUtf8Text(
                             StateFilePath,
                             MaximumStateBytes,
                             "插件状态文件"),
                         JsonOptions) ?? new PluginStateDocument();
            if (_state.Version != StateVersion)
                throw new InvalidDataException($"不支持的插件状态版本 {_state.Version}。");
            var loadedPlugins = _state.Plugins ?? new Dictionary<string, PluginStateEntry>();
            if (loadedPlugins.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                loadedPlugins.Count)
            {
                throw new InvalidDataException("插件状态包含仅大小写不同的重复 ID。");
            }

            _state.Plugins = new Dictionary<string, PluginStateEntry>(
                loadedPlugins,
                StringComparer.OrdinalIgnoreCase);
            if (_state.Plugins.Count > MaximumRememberedPlugins)
                throw new InvalidDataException(
                    $"插件状态超过 {MaximumRememberedPlugins} 条记录。");
            foreach (var pluginId in _state.Plugins.Keys.ToArray())
            {
                var entry = _state.Plugins[pluginId];
                if (entry is null)
                {
                    _state.Plugins[pluginId] = new PluginStateEntry();
                    continue;
                }

                if (entry.InstallOrigin is not null)
                    ValidateInstallOrigin(entry.InstallOrigin, pluginId, expectedVersion: null);
                NormalizeStateEntry(entry);
            }
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            BackupInvalidStateFile();
            _state = new PluginStateDocument();
        }
    }

    private void BackupInvalidStateFile()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return;
            var backupPath = Path.Combine(
                RootDirectory,
                $"state.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.Move(StateFilePath, backupPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Falling back to disabled plugins is still safer than preventing
            // launcher startup when a diagnostic backup cannot be written.
        }
    }

    private static string? ValidateManifest(PluginManifest? manifest, string packageDirectory)
    {
        if (manifest is null)
            return "plugin.json 不能是空值。";
        if (manifest.ManifestVersion != PluginManifest.CurrentManifestVersion)
            return $"不支持 manifestVersion={manifest.ManifestVersion}。";
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 128 ||
            !PluginIdPattern.IsMatch(manifest.Id))
            return "id 必须是小写反向域名，例如 dev.example.clock。";
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 256 ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64)
            return "name 和 version 不能为空。";
        if (!TryParseSemanticVersion(manifest.Version, out _))
            return "version 必须是语义版本号。";
        if (manifest.ApiVersion is null || manifest.ApiVersion.Length > 32 ||
            manifest.MinimumLauncherVersion?.Length > 64 ||
            manifest.Description.Length > 8192 ||
            manifest.Homepage?.Length > 2048 ||
            manifest.License?.Length > 256 ||
            manifest.Icon?.Length > 4096 ||
            manifest.EntryAssembly.Length > 4096 ||
            manifest.EntryType.Length > 1024)
        {
            return "plugin.json 包含超过宿主限制的文本字段。";
        }
        if (manifest.Authors.Count > 64 || manifest.Authors.Any(author =>
                string.IsNullOrWhiteSpace(author) || author.Length > 256))
            return "authors 最多包含 64 个非空作者，且每项不能超过 256 个字符。";
        if (manifest.RequiredCapabilities.Count + manifest.OptionalCapabilities.Count >
            MaximumCapabilityCount || manifest.RequiredCapabilities
                .Concat(manifest.OptionalCapabilities)
                .Any(capability => string.IsNullOrWhiteSpace(capability) || capability.Length > 128))
        {
            return $"能力声明最多包含 {MaximumCapabilityCount} 项合法名称。";
        }
        if (manifest.RequiredCapabilities
            .Concat(manifest.OptionalCapabilities)
            .GroupBy(capability => capability, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return "requiredCapabilities 与 optionalCapabilities 不能包含重复能力。";
        }
        if (manifest.Settings.Count > MaximumSettingCount)
            return $"单个插件最多声明 {MaximumSettingCount} 项设置。";
        if (!IsSupportedApiVersion(manifest.ApiVersion))
        {
            return $"API 版本 {manifest.ApiVersion} 与当前插件 API（{PluginSdk.ApiVersion} / " +
                   $"apiVersion {PluginSdk.ManifestApiVersion}）不兼容。";
        }
        if (!string.IsNullOrWhiteSpace(manifest.MinimumLauncherVersion))
        {
            if (!TryParseSemanticVersion(manifest.MinimumLauncherVersion, out var minimumVersion))
                return "minimumLauncherVersion 必须是语义版本号。";
            if (minimumVersion.CompareTo(LauncherVersion) > 0)
            {
                return $"API 最低需要 NyaLauncher {minimumVersion}，当前为 {LauncherVersion}.";
            }
        }
        var unsupportedCapability = manifest.RequiredCapabilities.FirstOrDefault(capability =>
            string.IsNullOrWhiteSpace(capability) || !KnownCapabilities.Contains(capability));
        if (unsupportedCapability is not null)
            return $"API 不支持必要能力 {unsupportedCapability}。";
        if (string.IsNullOrWhiteSpace(manifest.EntryType))
            return "entryType 不能为空。";
        if (!TryResolvePackagePath(packageDirectory, manifest.EntryAssembly, out var assemblyPath) ||
            !File.Exists(assemblyPath) ||
            !string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return "entryAssembly 必须指向包目录内已存在的 DLL。";
        }

        if (!string.IsNullOrWhiteSpace(manifest.Icon) &&
            !TryResolvePackagePath(packageDirectory, manifest.Icon, out _))
        {
            return "icon 必须是包目录内的相对路径。";
        }

        var duplicateSetting = manifest.Settings
            .Where(setting => setting is not null)
            .GroupBy(setting => setting.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSetting is not null)
            return $"设置键 {duplicateSetting.Key} 重复。";

        foreach (var setting in manifest.Settings)
        {
            if (setting is null ||
                string.IsNullOrWhiteSpace(setting.Key) ||
                !SettingKeyPattern.IsMatch(setting.Key) ||
                string.IsNullOrWhiteSpace(setting.Title) || setting.Title.Length > 256 ||
                setting.Description.Length > 4096 || setting.Placeholder?.Length > 1024 ||
                setting.Pattern?.Length > 2048 ||
                !Enum.IsDefined(setting.Kind) || !Enum.IsDefined(setting.Scope))
            {
                return "每项设置都必须包含合法 key 和非空 title。";
            }

            if (setting.Options.Count > 256 || setting.Options.Any(option =>
                    option.Value.Length > 1024 || option.Label.Length > 256 ||
                    option.Description.Length > 2048))
                return $"设置 {setting.Key} 的 options 超过数量或文本限制。";
            if (setting.FileExtensions.Count > 64 ||
                setting.FileExtensions.Any(extension => extension.Length > 32))
                return $"设置 {setting.Key} 的 fileExtensions 超过宿主限制。";

            if (setting.Kind == PluginSettingKind.Choice && setting.Options.Count == 0)
                return $"Choice 设置 {setting.Key} 必须声明 options。";
            if (setting.Kind == PluginSettingKind.Choice && setting.Options.Any(option =>
                    string.IsNullOrWhiteSpace(option.Value) ||
                    string.IsNullOrWhiteSpace(option.Label)))
            {
                return $"Choice 设置 {setting.Key} 的每个选项都必须包含 value 和 label。";
            }
            if (setting.Kind == PluginSettingKind.Choice && setting.Options
                    .GroupBy(option => option.Value, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1))
            {
                return $"Choice 设置 {setting.Key} 包含重复 value。";
            }
            if (setting.Minimum is double minimum &&
                setting.Maximum is double maximum &&
                minimum > maximum)
            {
                return $"设置 {setting.Key} 的 minimum 不能大于 maximum。";
            }
            if (setting.Step is <= 0)
                return $"设置 {setting.Key} 的 step 必须大于 0。";
            if (setting.MaximumLength is <= 0)
                return $"设置 {setting.Key} 的 maximumLength 必须大于 0。";
            if (setting.Kind == PluginSettingKind.File && setting.FileExtensions.Any(extension =>
                    string.IsNullOrWhiteSpace(extension) ||
                    !extension.StartsWith(".", StringComparison.Ordinal) ||
                    extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                return $"File 设置 {setting.Key} 的 fileExtensions 必须是带点号的文件后缀。";
            }
            if (!string.IsNullOrWhiteSpace(setting.Pattern))
            {
                try
                {
                    _ = new Regex(
                        setting.Pattern,
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(250));
                }
                catch (ArgumentException)
                {
                    return $"设置 {setting.Key} 的 pattern 不是合法正则表达式。";
                }
            }
            if (setting.DefaultValue is { } defaultValue)
            {
                if (defaultValue.GetRawText().Length > 32768)
                    return $"设置 {setting.Key} 的 defaultValue 过大。";
                if (setting.Kind is PluginSettingKind.File or PluginSettingKind.Directory &&
                    defaultValue.ValueKind is not JsonValueKind.Null)
                {
                    return $"路径设置 {setting.Key} 不能声明非空 defaultValue，必须由用户选择。";
                }
                try
                {
                    PluginSettingsStore.ValidateDefinitionValue(
                        setting,
                        JsonNode.Parse(defaultValue.GetRawText()));
                }
                catch (ArgumentException exception)
                {
                    return $"设置 {setting.Key} 的 defaultValue 无效：{exception.Message}";
                }
            }
        }

        return null;
    }

    private static (PluginInstallOrigin? Origin, string? Warning) ReadInstallOrigin(
        string packageDirectory,
        PluginManifest manifest)
    {
        try
        {
            if (!TryResolvePackagePath(packageDirectory, InstallOriginFileName, out var path))
                return (null, "插件来源快照路径不安全；已禁止仓库自动更新。");
            if (!File.Exists(path))
                return (null, "这是手动安装或旧版启动器安装的插件；来源身份未知，不能自动更新。");

            var origin = JsonSerializer.Deserialize<PluginInstallOrigin>(
                             ReadBoundedUtf8Text(
                                 path,
                                 MaximumInstallOriginBytes,
                                 "插件来源快照"),
                             InstallOriginJsonOptions) ??
                         throw new InvalidDataException("插件来源快照不能为空。");
            ValidateInstallOrigin(origin, manifest.Id, manifest.Version);
            return (origin, null);
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            return (null, $"插件来源快照无效，已禁止仓库自动更新：{exception.Message}");
        }
    }

    private static void ValidateInstallOrigin(
        PluginInstallOrigin origin,
        string expectedId,
        string? expectedVersion)
    {
        if (origin.SchemaVersion != 1 ||
            origin.SourceIndexSchemaVersion is not (1 or 2) ||
            string.IsNullOrWhiteSpace(origin.Id) ||
            !PluginIdPattern.IsMatch(origin.Id) ||
            !string.Equals(origin.Id, expectedId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(origin.LineageId) ||
            !OriginLineagePattern.IsMatch(origin.LineageId) ||
            origin.Generation < 1 ||
            string.IsNullOrWhiteSpace(origin.RepositoryUrl) ||
            origin.RepositoryUrl.Length > 2048 ||
            !GitHubRepositoryPattern.IsMatch(origin.RepositoryUrl) ||
            !Uri.TryCreate(origin.RepositoryUrl, UriKind.Absolute, out var repository) ||
            !string.Equals(repository.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(repository.Host, "github.com", StringComparison.Ordinal) ||
            repository.Port != 443 ||
            !string.IsNullOrEmpty(repository.UserInfo) ||
            repository.Segments.Length != 3 ||
            repository.Query.Length != 0 ||
            repository.Fragment.Length != 0 ||
            repository.Segments[2].TrimEnd('/') is "." or ".." ||
            repository.Segments[2].TrimEnd('/').EndsWith(".", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(origin.Version) ||
            !TryParseSemanticVersion(origin.Version, out _) ||
            expectedVersion is not null &&
            !string.Equals(origin.Version, expectedVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(origin.Sha256) ||
            !Sha256Pattern.IsMatch(origin.Sha256) ||
            (origin.RepositoryId is null) != (origin.OwnerId is null) ||
            origin.RepositoryId is not null and <= 0 ||
            origin.OwnerId is not null and <= 0 ||
            origin.SourceIndexSchemaVersion == 1 &&
            (origin.RepositoryId is not null ||
             origin.OwnerId is not null ||
             origin.Generation != 1 ||
             !string.Equals(origin.LineageId, origin.Id, StringComparison.Ordinal)) ||
            origin.SourceIndexSchemaVersion == 2 &&
            (origin.RepositoryId is null ||
             origin.OwnerId is null ||
             !Guid.TryParseExact(origin.LineageId, "D", out _)))
        {
            throw new InvalidDataException("插件来源身份字段不一致。");
        }
    }

    internal static bool TryResolvePackagePath(
        string packageDirectory,
        string? relativePath,
        out string fullPath) =>
        TryResolveContainedPath(packageDirectory, relativePath, out fullPath);

    internal static bool TryResolveContainedPath(
        string rootDirectory,
        string? relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathFullyQualified(relativePath))
            {
                return false;
            }

            // Keep normalization inside the guarded block. Invalid path syntax
            // belongs to one bad package and must never abort the whole scan.
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                return false;

            // Lexical containment alone is insufficient: a package-controlled
            // junction or symlink could otherwise redirect an entry DLL or asset.
            var current = root;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return false;
            foreach (var segment in Path.GetRelativePath(root, candidate)
                         .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                             StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        return false;
                }
                catch (FileNotFoundException)
                {
                    // A not-yet-created child is safe after all existing
                    // ancestors have been checked.
                }
                catch (DirectoryNotFoundException)
                {
                }
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static void SaveJsonAtomically<T>(
        string filePath,
        T value,
        int? maximumBytes = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        var directory = Path.GetDirectoryName(filePath) ??
            throw new InvalidOperationException("文件路径缺少父目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var contents = JsonSerializer.SerializeToUtf8Bytes(
                value,
                serializerOptions ?? JsonOptions);
            if (maximumBytes is int limit && contents.Length > limit)
                throw new InvalidDataException($"JSON 文件不能超过 {limit} 字节。");
            File.WriteAllBytes(temporaryPath, contents);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // A stale temp file is harmless and may be cleaned on a later scan.
            }
        }
    }

    private static void MarkDuplicateIds(List<PluginPackage> packages)
    {
        foreach (var group in packages
                     .Where(package => package.Manifest is not null)
                     .GroupBy(package => package.Manifest!.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var duplicate in group.ToArray())
            {
                var index = packages.IndexOf(duplicate);
                packages[index] = duplicate with
                {
                    Status = PluginStatus.Invalid,
                    Error = $"插件 ID {group.Key} 与另一个包重复。"
                };
            }
        }
    }

    internal static bool IsSupportedApiVersion(string? apiVersion)
    {
        if (!TryParseApiVersion(apiVersion, out var candidate) ||
            !TryParseApiVersion(PluginSdk.ManifestApiVersion, out var supported))
        {
            return false;
        }

        return candidate.Major == supported.Major &&
               candidate.CompareTo(supported) <= 0;
    }

    private static bool TryParseApiVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 3)
            return false;

        Span<int> numbers = stackalloc int[3];
        numbers.Clear();
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[index]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static bool TryParseSemanticVersion(string value, out SemanticVersion version) =>
        SemanticVersion.TryParse(value, out version);

    private static string SanitizeDirectoryName(string pluginId) =>
        string.Concat(pluginId.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    internal static string ReadBoundedUtf8Text(
        string filePath,
        int maximumBytes,
        string displayName)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException($"{displayName} 不能超过 {maximumBytes} 字节。");

        using var memory = new MemoryStream((int)Math.Min(stream.Length, maximumBytes));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException($"{displayName} 不能超过 {maximumBytes} 字节。");
            memory.Write(buffer, 0, read);
        }

        var bytes = memory.GetBuffer().AsSpan(0, (int)memory.Length);
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        try
        {
            return new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{displayName} 不是有效的 UTF-8 文本。", exception);
        }
    }

    private static void NormalizeStateEntry(PluginStateEntry entry)
    {
        entry.GrantedCapabilities = entry.GrantedCapabilities?
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Select(capability => capability.Length <= 128
                ? capability
                : capability[..128])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCapabilityCount)
            .ToList() ?? [];
        if (entry.LastError?.Length > 8192)
            entry.LastError = entry.LastError[..8192];
    }

    private static PluginStateEntry CloneState(PluginStateEntry state) => new()
    {
        Enabled = state.Enabled,
        GrantedCapabilities = [.. state.GrantedCapabilities],
        LastError = state.LastError,
        InstallOrigin = state.InstallOrigin
    };

    private static PluginStateDocument CloneStateDocument(PluginStateDocument source) => new()
    {
        Version = source.Version,
        Plugins = source.Plugins.ToDictionary(
            pair => pair.Key,
            pair => CloneState(pair.Value),
            StringComparer.OrdinalIgnoreCase)
    };

    private sealed class PluginStateDocument
    {
        public int Version { get; set; } = StateVersion;

        public Dictionary<string, PluginStateEntry> Plugins { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
