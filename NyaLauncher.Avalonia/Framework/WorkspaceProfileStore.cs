using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Persists front-end personalization independently from launcher business data.
/// Invalid or outdated files fall back to the registered defaults.
/// </summary>
public sealed class WorkspaceProfileStore
{
    private const string ProfileFileName = "workspace.json";
    private const string LocationFileName = "workspace-location.txt";

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

    public WorkspaceProfileStore(string? storageDirectory = null)
    {
        StorageDirectory = NormalizeStorageDirectory(
            storageDirectory ?? LoadConfiguredDirectory() ?? PlatformDefaultDirectory);
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
            MigrateLegacyAreaIds(profile);
            return profile;
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

    public void ChangeStorageDirectory(string storageDirectory, WorkspaceProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        ArgumentNullException.ThrowIfNull(profile);

        var targetDirectory = NormalizeStorageDirectory(storageDirectory);
        if (File.Exists(targetDirectory))
            throw new IOException("所选路径不是文件夹。");

        var previousFilePath = FilePath;
        var targetFilePath = Path.Combine(targetDirectory, ProfileFileName);
        SaveToPath(profile, targetFilePath);
        SaveConfiguredDirectory(targetDirectory);
        StorageDirectory = targetDirectory;

        if (!PathsEqual(previousFilePath, targetFilePath) && File.Exists(previousFilePath))
        {
            File.Delete(previousFilePath);
            TryDeleteEmptyDirectory(Path.GetDirectoryName(previousFilePath));
        }
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
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(filePath, json);
    }

    private static string? LoadConfiguredDirectory()
    {
        try
        {
            if (!File.Exists(LocationFilePath))
                return null;

            var directory = File.ReadAllText(LocationFilePath).Trim();
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

    private static void SaveConfiguredDirectory(string directory)
    {
        if (PathsEqual(directory, PlatformDefaultDirectory))
        {
            if (File.Exists(LocationFilePath))
                File.Delete(LocationFilePath);
            return;
        }

        var locatorDirectory = Path.GetDirectoryName(LocationFilePath);
        if (!string.IsNullOrWhiteSpace(locatorDirectory))
            Directory.CreateDirectory(locatorDirectory);

        File.WriteAllText(LocationFilePath, directory);
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

    private static void MigrateLegacyAreaIds(WorkspaceProfile profile)
    {
        var legacyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["launch"] = "area-001",
            ["resources"] = "area-002",
            ["launcher"] = "area-003"
        };

        foreach (var area in profile.Areas)
        {
            if (legacyIds.TryGetValue(area.AreaId, out var migratedId))
                area.AreaId = migratedId;
        }

        MigrateLayoutNode(profile.Layout, legacyIds);
    }

    private static void MigrateLayoutNode(
        DockLayoutProfile? node,
        IReadOnlyDictionary<string, string> legacyIds)
    {
        if (node is null)
            return;

        if (node.AreaId is not null && legacyIds.TryGetValue(node.AreaId, out var migratedId))
            node.AreaId = migratedId;

        foreach (var child in node.Children)
            MigrateLayoutNode(child, legacyIds);
    }
}
