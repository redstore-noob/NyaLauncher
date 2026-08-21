using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Core.Config;

public sealed record GameVersionProfile
{
    public string MinecraftDirectory { get; init; } = string.Empty;

    public string VersionId { get; init; } = string.Empty;

    public int MinimumMemoryMb { get; init; } = 512;

    public int MaximumMemoryMb { get; init; } = 4096;

    public bool UseIndependentMemorySettings { get; init; }

    public bool FollowGlobalAdvancedSettings { get; init; } = true;

    public int WindowWidth { get; init; } = 854;

    public int WindowHeight { get; init; } = 480;

    /// <summary>
    /// Null preserves the legacy default inferred from selecting a versions/id
    /// folder directly; true/false is the user's explicit per-instance choice.
    /// </summary>
    public bool? IsVersionIsolationEnabled { get; init; }

    public string JavaExecutable { get; init; } = string.Empty;

    public string[] AdditionalJvmArguments { get; init; } = [];

    public string[] AdditionalGameArguments { get; init; } = [];

}

public static class GameVersionIsolation
{
    public static GameVersionLayout Resolve(GameInstanceSnapshot snapshot, string versionId)
    {
        if (GameInstanceLayoutResolver.TryResolveExternalInstance(
                snapshot.SourcePath,
                out var external) &&
            string.Equals(external.InstanceId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            return new GameVersionLayout(
                true,
                external.ContentDirectory,
                external.Provider,
                external.Evidence);
        }

        var profile = GameVersionProfileStore.Get(snapshot.MinecraftDirectory, versionId);

        // 优先使用版本自身的显式设置；若未设置则使用全局默认；都未设置则交给自动检测。
        var isolationOverride = profile.IsVersionIsolationEnabled
                                ?? LauncherConfig.DefaultVersionIsolation;

        return GameInstanceLayoutResolver.Resolve(
            snapshot.MinecraftDirectory,
            snapshot.SourcePath,
            versionId,
            isolationOverride);
    }

    public static bool IsEnabled(GameInstanceSnapshot snapshot, string versionId) =>
        Resolve(snapshot, versionId).IsIsolated;

    public static string? GetGameDirectory(GameInstanceSnapshot snapshot, string versionId)
    {
        var layout = Resolve(snapshot, versionId);
        return layout.IsIsolated ? layout.ContentDirectory : null;
    }

    public static string GetContentDirectory(GameInstanceSnapshot snapshot, string versionId) =>
        Resolve(snapshot, versionId).ContentDirectory;

    public static bool IsVersionDirectorySource(string? sourcePath) =>
        GameInstanceLayoutResolver.IsVersionDirectorySource(sourcePath);
}

/// <summary>
/// Persists the folder catalog and editable per-version launch settings in
/// config.json. The physical version ID and directory remain unchanged.
/// </summary>
public static class GameVersionProfileStore
{
    private const string FoldersKey = "gameVersionFolders";
    private const string ProfilesKey = "gameVersionProfiles";
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static event Action? Changed;

    public static IReadOnlyList<string> GetFolders()
    {
        lock (Gate)
        {
            var folders = Deserialize<List<string>>(LauncherConfig.GetValue(FoldersKey)) ?? [];
            var configured = LauncherConfig.GameDirectory;
            if (!string.IsNullOrWhiteSpace(configured))
                folders.Insert(0, configured);

            var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
            if (Directory.Exists(defaultDir))
                folders.Add(defaultDir);

            return folders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePathOrOriginal)
                .Distinct(PathComparer)
                .ToArray();
        }
    }

    public static bool AddFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (!Directory.Exists(normalized))
            return false;

        lock (Gate)
        {
            var folders = GetFolders().ToList();
            if (!folders.Contains(normalized, PathComparer))
                folders.Add(normalized);
            if (!SaveFolders(folders))
                return false;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// 从文件夹列表中移除指定目录。不允许移除平台默认 Minecraft 目录；
    /// 若移除的是当前活跃游戏目录，则自动切换到列表中剩余的首个目录。
    /// </summary>
    /// <returns>移除成功返回 true；目录不在列表中或为默认目录时返回 false。</returns>
    public static bool RemoveFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

        var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
        if (PathsEqual(normalized, Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultDir))))
            return false;

        lock (Gate)
        {
            var folders = GetFolders().ToList();
            if (!folders.Contains(normalized, PathComparer))
                return false;

            folders.RemoveAll(f => PathsEqual(f, normalized));
            if (!SaveFolders(folders))
                return false;

            if (PathsEqual(LauncherConfig.GameDirectory ?? string.Empty, normalized))
            {
                var next = folders.FirstOrDefault(f => !PathsEqual(f, normalized));
                LauncherConfig.SaveGameDirectory(next ?? string.Empty);
            }
        }

        RaiseChanged();
        return true;
    }

    public static GameVersionProfile Get(string minecraftDirectory, string versionId)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionId))
            return new GameVersionProfile { VersionId = versionId ?? string.Empty };

        var normalizedDirectory = NormalizePathOrOriginal(minecraftDirectory);
        lock (Gate)
        {
            var profiles = LoadProfiles();
            return profiles.LastOrDefault(profile =>
                       PathsEqual(profile.MinecraftDirectory, normalizedDirectory) &&
                       string.Equals(profile.VersionId, versionId, StringComparison.OrdinalIgnoreCase))
                   ?? new GameVersionProfile
                   {
                       MinecraftDirectory = normalizedDirectory,
                       VersionId = versionId
                   };
        }
    }

    public static bool Save(GameVersionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.MinecraftDirectory) ||
            string.IsNullOrWhiteSpace(profile.VersionId) ||
            profile.MinimumMemoryMb < 256 ||
            profile.MaximumMemoryMb < profile.MinimumMemoryMb ||
            profile.WindowWidth < 320 ||
            profile.WindowHeight < 240)
        {
            return false;
        }

        var normalized = profile with
        {
            MinecraftDirectory = NormalizePathOrOriginal(profile.MinecraftDirectory),
            JavaExecutable = profile.JavaExecutable.Trim(),
            AdditionalJvmArguments = NormalizeArguments(profile.AdditionalJvmArguments),
            AdditionalGameArguments = NormalizeArguments(profile.AdditionalGameArguments)
        };

        lock (Gate)
        {
            var profiles = LoadProfiles();
            profiles.RemoveAll(candidate =>
                PathsEqual(candidate.MinecraftDirectory, normalized.MinecraftDirectory) &&
                string.Equals(candidate.VersionId, normalized.VersionId, StringComparison.OrdinalIgnoreCase));
            profiles.Add(normalized);
            if (!LauncherConfig.SetValue(ProfilesKey, JsonSerializer.Serialize(profiles)))
                return false;
        }

        RaiseChanged();
        return true;
    }

    public static void MigrateRenamedVersion(
        string minecraftDirectory,
        string oldVersionId,
        string newVersionId,
        string oldVersionDirectory,
        string newVersionDirectory)
    {
        lock (Gate)
        {
            var profiles = LoadProfiles();
            for (var index = 0; index < profiles.Count; index++)
            {
                var profile = profiles[index];
                if (PathsEqual(profile.MinecraftDirectory, minecraftDirectory) &&
                    string.Equals(profile.VersionId, oldVersionId, StringComparison.OrdinalIgnoreCase))
                {
                    profiles[index] = profile with { VersionId = newVersionId };
                }
            }

            LauncherConfig.SetValue(ProfilesKey, JsonSerializer.Serialize(profiles));
            var folders = Deserialize<List<string>>(LauncherConfig.GetValue(FoldersKey)) ?? [];
            for (var index = 0; index < folders.Count; index++)
            {
                if (PathsEqual(folders[index], oldVersionDirectory))
                    folders[index] = newVersionDirectory;
            }
            SaveFolders(folders.Distinct(PathComparer).ToArray());

            if (PathsEqual(LauncherConfig.GameDirectory ?? string.Empty, oldVersionDirectory))
                LauncherConfig.SaveGameDirectory(newVersionDirectory);
            LauncherConfig.SetValue("selectedGameInstance", newVersionId);
        }

        RaiseChanged();
    }

    private static List<GameVersionProfile> LoadProfiles() =>
        Deserialize<List<GameVersionProfile>>(LauncherConfig.GetValue(ProfilesKey)) ?? [];

    private static bool SaveFolders(IReadOnlyList<string> folders) =>
        LauncherConfig.SetValue(FoldersKey, JsonSerializer.Serialize(folders));

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string[] NormalizeArguments(IEnumerable<string>? arguments) =>
        arguments?
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .Select(argument => argument.Trim())
            .ToArray() ?? [];

    private static string NormalizePathOrOriginal(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch
        {
            return path.Trim();
        }
    }

    private static bool PathsEqual(string left, string right) =>
        NyaLauncher.Core.Tools.PathUtil.PathsEqual(left, right);

    private static void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null)
            return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GameVersionProfileStore.Changed 订阅者异常：{exception}");
            }
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
