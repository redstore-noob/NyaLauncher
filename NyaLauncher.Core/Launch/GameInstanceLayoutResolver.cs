using System;
using System.IO;
using System.Linq;

namespace NyaLauncher.Core.Launch;

public sealed record GameVersionLayout(
    bool IsIsolated,
    string ContentDirectory,
    string Provider,
    string Evidence);

public sealed record ExternalGameInstanceLayout(
    string InstanceId,
    string InstanceDirectory,
    string ContentDirectory,
    string LauncherRoot,
    string Provider,
    string Evidence);

/// <summary>
/// Resolves the effective game/content directory used by an installed version.
/// The result is launcher-agnostic: every content scanner and the launch pipeline
/// consume this same path instead of independently guessing isolation state.
/// </summary>
public static class GameInstanceLayoutResolver
{
    private static readonly string[] ContentDirectories =
        ["mods", "resourcepacks", "shaderpacks", "saves", "config"];

    private static readonly string[] ContentFiles =
        ["options.txt", "servers.dat"];

    public static GameVersionLayout Resolve(
        string minecraftDirectory,
        string? sourcePath,
        string versionId,
        bool? explicitIsolation)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(minecraftDirectory));
        var versionDirectory = Path.Combine(root, "versions", versionId);
        var isolatedDirectory = ResolveIsolatedContentDirectory(versionDirectory);

        if (explicitIsolation is { } userSetting)
        {
            return userSetting
                ? Isolated(isolatedDirectory, "NyaLauncher", "用户已明确开启版本隔离")
                : Shared(root, "NyaLauncher", "用户已明确关闭版本隔离");
        }

        if (TryReadPclIsolation(versionDirectory) is { } pclSetting)
        {
            return pclSetting
                ? Isolated(isolatedDirectory, "PCL", "PCL Setup.ini 已开启版本隔离")
                : Shared(root, "PCL", "PCL Setup.ini 已关闭版本隔离");
        }

        if (TryResolveMultiMcFamily(versionDirectory, out var multiMcDirectory))
        {
            return Isolated(
                multiMcDirectory,
                "MultiMC / Prism Launcher",
                "检测到 instance.cfg 与独立 minecraft/.minecraft 内容目录");
        }

        if (File.Exists(Path.Combine(versionDirectory, ".hmclversion.cfg")) &&
            HasMinecraftContent(isolatedDirectory))
        {
            return Isolated(
                isolatedDirectory,
                "HMCL",
                "检测到 .hmclversion.cfg 与版本独立内容");
        }

        if (TryResolveKnownPackLayout(versionDirectory, out var packDirectory, out var provider))
        {
            return Isolated(
                packDirectory,
                provider,
                "检测到第三方启动器实例元数据与独立内容目录");
        }

        if (!PathsEqual(isolatedDirectory, versionDirectory) &&
            HasMinecraftContent(isolatedDirectory))
        {
            return Isolated(
                isolatedDirectory,
                "通用实例布局",
                "检测到独立 minecraft/.minecraft 内容目录");
        }

        if (HasMinecraftContent(versionDirectory))
        {
            return Isolated(
                versionDirectory,
                "通用版本隔离",
                "版本目录内存在模组、资源包、光影、配置或存档内容");
        }

        if (IsVersionDirectorySource(sourcePath))
        {
            return Isolated(
                isolatedDirectory,
                "路径结构",
                "当前添加路径是 versions/<版本> 实例目录");
        }

        return Shared(root, "官方 / 共享布局", "未检测到版本独立内容或隔离设置");
    }

    public static bool IsVersionDirectorySource(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(sourcePath));
            return directory.Parent is { } parent &&
                   string.Equals(parent.Name, "versions", StringComparison.OrdinalIgnoreCase) &&
                   parent.Parent is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryResolveExternalInstance(
        string? sourcePath,
        out ExternalGameInstanceLayout layout)
    {
        layout = null!;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        try
        {
            var selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
            if (!Directory.Exists(selected))
                return false;

            var instanceDirectory = selected;
            if ((string.Equals(Path.GetFileName(selected), ".minecraft", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFileName(selected), "minecraft", StringComparison.OrdinalIgnoreCase)) &&
                Directory.GetParent(selected) is { } parent &&
                HasExternalMarker(parent.FullName))
            {
                instanceDirectory = parent.FullName;
            }

            var marker = GetExternalMarker(instanceDirectory);
            if (marker is null)
                return false;

            var contentDirectory = ResolveExternalContentDirectory(instanceDirectory);
            if (contentDirectory is null)
                return false;

            var instanceParent = Directory.GetParent(instanceDirectory);
            var launcherRoot = instanceParent is not null &&
                               string.Equals(instanceParent.Name, "instances", StringComparison.OrdinalIgnoreCase)
                ? instanceParent.Parent?.FullName ?? instanceDirectory
                : instanceDirectory;
            var instanceId = Path.GetFileName(instanceDirectory);
            if (string.IsNullOrWhiteSpace(instanceId))
                return false;

            layout = new ExternalGameInstanceLayout(
                instanceId,
                instanceDirectory,
                contentDirectory,
                launcherRoot,
                marker.Value.Provider,
                marker.Value.Evidence);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ResolveIsolatedContentDirectory(string versionDirectory)
    {
        foreach (var name in new[] { ".minecraft", "minecraft" })
        {
            var candidate = Path.Combine(versionDirectory, name);
            if (HasMinecraftContent(candidate))
                return candidate;
        }

        return versionDirectory;
    }

    private static bool TryResolveMultiMcFamily(
        string versionDirectory,
        out string contentDirectory)
    {
        contentDirectory = string.Empty;
        if (!File.Exists(Path.Combine(versionDirectory, "instance.cfg")))
            return false;

        foreach (var name in new[] { ".minecraft", "minecraft" })
        {
            var candidate = Path.Combine(versionDirectory, name);
            if (!Directory.Exists(candidate))
                continue;

            contentDirectory = candidate;
            return true;
        }

        if (HasMinecraftContent(versionDirectory))
        {
            contentDirectory = versionDirectory;
            return true;
        }

        return false;
    }

    private static bool TryResolveKnownPackLayout(
        string versionDirectory,
        out string contentDirectory,
        out string provider)
    {
        var layouts = new (string Marker, string Provider)[]
        {
            ("minecraftinstance.json", "CurseForge"),
            ("profile.json", "Modrinth App"),
            ("instance.json", "ATLauncher")
        };

        foreach (var layout in layouts)
        {
            if (!File.Exists(Path.Combine(versionDirectory, layout.Marker)))
                continue;

            contentDirectory = ResolveIsolatedContentDirectory(versionDirectory);
            provider = layout.Provider;
            return Directory.Exists(contentDirectory);
        }

        contentDirectory = string.Empty;
        provider = string.Empty;
        return false;
    }

    private static bool HasExternalMarker(string directory) =>
        GetExternalMarker(directory) is not null;

    private static (string Provider, string Evidence)? GetExternalMarker(string directory)
    {
        if (File.Exists(Path.Combine(directory, "instance.cfg")))
        {
            return (
                "MultiMC / Prism Launcher",
                "检测到外部 instance.cfg 与独立 minecraft/.minecraft 内容目录");
        }
        if (File.Exists(Path.Combine(directory, "minecraftinstance.json")))
            return ("CurseForge", "检测到外部 minecraftinstance.json 实例元数据");
        if (File.Exists(Path.Combine(directory, "profile.json")))
            return ("Modrinth App", "检测到外部 profile.json 实例元数据");
        if (File.Exists(Path.Combine(directory, "instance.json")))
            return ("ATLauncher", "检测到外部 instance.json 实例元数据");
        return null;
    }

    private static string? ResolveExternalContentDirectory(string instanceDirectory)
    {
        foreach (var name in new[] { ".minecraft", "minecraft" })
        {
            var candidate = Path.Combine(instanceDirectory, name);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return Directory.Exists(instanceDirectory) ? instanceDirectory : null;
    }

    private static bool? TryReadPclIsolation(string versionDirectory)
    {
        var setupPath = Path.Combine(versionDirectory, "PCL", "Setup.ini");
        if (!File.Exists(setupPath))
            return null;

        try
        {
            bool? legacy = null;
            foreach (var line in File.ReadLines(setupPath))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                var key = line[..separator].Trim();
                var parsed = ParseBoolean(line[(separator + 1)..].Trim());
                if (parsed is null)
                    continue;
                if (string.Equals(key, "VersionArgumentIndieV2", StringComparison.OrdinalIgnoreCase))
                    return parsed;
                if (string.Equals(key, "VersionArgumentIndie", StringComparison.OrdinalIgnoreCase))
                    legacy = parsed;
            }

            return legacy;
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

    private static bool HasMinecraftContent(string directory) =>
        Directory.Exists(directory) &&
        (ContentDirectories.Any(name => Directory.Exists(Path.Combine(directory, name))) ||
         ContentFiles.Any(name => File.Exists(Path.Combine(directory, name))));

    private static bool? ParseBoolean(string value)
    {
        if (bool.TryParse(value, out var parsed))
            return parsed;
        return value switch
        {
            "1" => true,
            "0" => false,
            _ => null
        };
    }

    private static GameVersionLayout Isolated(
        string directory,
        string provider,
        string evidence) =>
        new(true, directory, provider, evidence);

    private static GameVersionLayout Shared(
        string directory,
        string provider,
        string evidence) =>
        new(false, directory, provider, evidence);

    private static bool PathsEqual(string left, string right) =>
        NyaLauncher.Core.Tools.PathUtil.PathsEqual(left, right);
}
