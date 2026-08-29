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

    /// <summary>
    /// 实例图标偏好：null 表示"跟随加载器自动"；"gameicon:{key}" 表示显式选择某个
    /// 内置图标；"custom" 表示使用自定义图标（文件由 <c>CustomInstanceIconStore</c> 管理）。
    /// </summary>
    public string? InstanceIconOverride { get; init; }
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

        // 隔离判定优先级：实例显式设置 > 自动检测（PCL/MultiMC/HMCL/内容证据）> 全局默认兜底 > 共享目录。
        // 全局默认只作兜底传入：若作为显式覆盖，其他启动器的隔离布局检测将永远不生效。
        return GameInstanceLayoutResolver.Resolve(
            snapshot.MinecraftDirectory,
            snapshot.SourcePath,
            versionId,
            profile.IsVersionIsolationEnabled,
            LauncherConfig.DefaultVersionIsolation);
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

    /// <summary>
    /// 清理指定 Minecraft 目录下已不存在版本的实例配置。
    /// 版本文件夹可能被用户在启动器外手动删除或改名，实例扫描完成后调用本方法，
    /// 防止这些版本的隔离、内存等残留设置无限累积在 config.json 中被后续逻辑误读。
    /// 版本存在性以扫描结果（实例列表实际展示的版本）为准，比较不区分大小写。
    /// </summary>
    /// <param name="minecraftDirectory">本次扫描的 Minecraft 根目录；只清理该目录下的配置。</param>
    /// <param name="existingVersionIds">扫描到的仍实际存在的版本 Id 集合（可为空，表示全部清除）。</param>
    /// <returns>清理掉的配置条数（0 表示无需清理）。</returns>
    public static int PruneMissingVersions(
        string minecraftDirectory,
        IReadOnlyCollection<string> existingVersionIds)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory))
            return 0;

        var normalizedDirectory = NormalizePathOrOriginal(minecraftDirectory);
        // 空集合 → 目录下已无任何有效版本，该目录全部实例配置都应清除
        var existing = new HashSet<string>(existingVersionIds, StringComparer.OrdinalIgnoreCase);

        int removed;
        lock (Gate)
        {
            var profiles = LoadProfiles();
            var kept = new List<GameVersionProfile>(profiles.Count);
            removed = 0;
            foreach (var profile in profiles)
            {
                // 其他 Minecraft 目录的实例配置不受本次扫描影响
                if (PathsEqual(profile.MinecraftDirectory, normalizedDirectory) &&
                    !existing.Contains(profile.VersionId))
                {
                    removed++;
                    continue;
                }

                kept.Add(profile);
            }

            if (removed > 0)
                LauncherConfig.SetValue(ProfilesKey, JsonSerializer.Serialize(kept));
        }

        if (removed > 0)
            RaiseChanged();
        return removed;
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
            JavaExecutable = (profile.JavaExecutable ?? string.Empty).Trim(),
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

    /// <summary>
    /// 读取实例图标偏好（见 <see cref="GameVersionProfile.InstanceIconOverride"/>）。
    /// </summary>
    public static string? GetInstanceIconOverride(string minecraftDirectory, string versionId) =>
        Get(minecraftDirectory, versionId).InstanceIconOverride;

    /// <summary>仅更新实例图标偏好，不触碰其他设置；失败返回 false。</summary>
    public static bool SaveInstanceIconOverride(
        string minecraftDirectory,
        string versionId,
        string? overrideValue)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionId))
            return false;

        var normalized = NormalizePathOrOriginal(minecraftDirectory);
        lock (Gate)
        {
            var profiles = LoadProfiles();
            profiles.RemoveAll(candidate =>
                PathsEqual(candidate.MinecraftDirectory, normalized) &&
                string.Equals(candidate.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
            profiles.Add(new GameVersionProfile
            {
                MinecraftDirectory = normalized,
                VersionId = versionId,
                InstanceIconOverride = overrideValue
            });
            return LauncherConfig.SetValue(ProfilesKey, JsonSerializer.Serialize(profiles));
        }
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
