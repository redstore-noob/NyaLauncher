using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NyaLauncher.Core.Config;
using NyaLauncher.Core.Content;

namespace NyaLauncher.Core.Launch;

public sealed record GameVersionDetails(
    string VersionId,
    string VersionDirectory,
    string ContentDirectory,
    string LayoutProvider,
    string LayoutEvidence,
    bool IsIsolated,
    bool IsExternallyManaged,
    string VersionType,
    string BaseGameVersion,
    string LoaderName,
    string LoaderVersion,
    string? InstanceIconPath,
    string InstanceIconGlyph,
    string ReleaseTime,
    string MainClass,
    string JavaRequirement,
    IReadOnlyList<GameContentEntry> Mods,
    IReadOnlyList<GameContentEntry> ResourcePacks,
    IReadOnlyList<GameContentEntry> Shaders,
    IReadOnlyList<GameContentEntry> Saves,
    bool HasShaderDirectory)
{
    public bool IsVanilla => string.Equals(LoaderName, "原版", StringComparison.Ordinal);
}

/// <summary>
/// 加载单个已安装版本的详情信息（Mod 加载器识别、inheritsFrom 继承链解析、内容列表扫描）。
/// </summary>
public static class GameVersionDetailsService
{
    public static Task<GameVersionDetails> LoadAsync(
        GameInstanceSnapshot snapshot,
        string versionId,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(snapshot, versionId, cancellationToken), cancellationToken);

    private static GameVersionDetails Load(
        GameInstanceSnapshot snapshot,
        string versionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isExternal = GameInstanceLayoutResolver.TryResolveExternalInstance(
                             snapshot.SourcePath,
                             out var external) &&
                         string.Equals(external.InstanceId, versionId, StringComparison.OrdinalIgnoreCase);
        var versionDirectory = isExternal
            ? external.InstanceDirectory
            : Path.Combine(snapshot.MinecraftDirectory, "versions", versionId);
        var layout = GameVersionIsolation.Resolve(snapshot, versionId);
        var contentDirectory = layout.ContentDirectory;

        string versionType = "未知";
        string releaseTime = "未提供";
        string mainClass = "未提供";
        string javaRequirement = "自动检测";
        var loaderSignals = new List<string> { versionId };
        var libraryNames = new List<string>();
        string? baseGameVersion = null;

        var currentVersionId = versionId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Count < 16 && visited.Add(currentVersionId))
        {
            var currentJson = Path.Combine(
                snapshot.MinecraftDirectory,
                "versions",
                currentVersionId,
                $"{currentVersionId}.json");
            if (!File.Exists(currentJson))
                break;
            using var document = JsonDocument.Parse(File.ReadAllBytes(currentJson));
            var root = document.RootElement;
            baseGameVersion ??=
                ReadString(root, "clientVersion") ??
                ReadString(root, "minecraftVersion");
            if (versionType == "未知")
                versionType = ReadString(root, "type") ?? versionType;
            if (releaseTime == "未提供")
                releaseTime = ReadString(root, "releaseTime") ?? ReadString(root, "time") ?? releaseTime;
            if (mainClass == "未提供")
                mainClass = ReadString(root, "mainClass") ?? mainClass;
            loaderSignals.Add(ReadString(root, "mainClass") ?? string.Empty);
            if (javaRequirement == "自动检测" &&
                root.TryGetProperty("javaVersion", out var javaVersion) &&
                javaVersion.TryGetProperty("majorVersion", out var majorVersion) &&
                majorVersion.TryGetInt32(out var javaMajor))
            {
                javaRequirement = $"Java {javaMajor}";
            }

            if (root.TryGetProperty("libraries", out var libraries) &&
                libraries.ValueKind == JsonValueKind.Array)
            {
                foreach (var library in libraries.EnumerateArray())
                {
                    if (library.TryGetProperty("name", out var name))
                    {
                        var libraryName = name.GetString() ?? string.Empty;
                        loaderSignals.Add(libraryName);
                        libraryNames.Add(libraryName);
                    }
                }
            }

            var parentId = ReadString(root, "inheritsFrom");
            if (string.IsNullOrWhiteSpace(parentId))
                break;
            baseGameVersion ??= parentId;
            loaderSignals.Add(parentId);
            currentVersionId = parentId;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (isExternal)
            ReadExternalComponentMetadata(versionDirectory, loaderSignals, ref baseGameVersion);
        var loader = DetectLoader(loaderSignals);
        var loaderVersion = DetectLoaderVersion(libraryNames, loaderSignals, loader, ref baseGameVersion);
        baseGameVersion ??= loader == "原版" ? versionId : "未识别";
        var instanceVisual = GameContentMetadataService.ResolveInstanceVisual(snapshot, versionId, loader);
        var mods = GameContentMetadataService.ReadMods(
            Path.Combine(contentDirectory, "mods"),
            cancellationToken);
        var resourcePacks = GameContentMetadataService.ReadResourcePacks(
            Path.Combine(contentDirectory, "resourcepacks"),
            cancellationToken);
        var shaderDirectory = Path.Combine(contentDirectory, "shaderpacks");
        var hasShaderDirectory = Directory.Exists(shaderDirectory);
        var shaders = hasShaderDirectory
            ? GameContentMetadataService.ReadShaders(shaderDirectory, cancellationToken)
            : [];
        var saves = GameContentMetadataService.ReadSaves(
            Path.Combine(contentDirectory, "saves"),
            cancellationToken);

        return new GameVersionDetails(
            versionId,
            versionDirectory,
            contentDirectory,
            layout.Provider,
            layout.Evidence,
            layout.IsIsolated,
            isExternal,
            versionType,
            baseGameVersion,
            loader,
            loaderVersion,
            instanceVisual.IconPath,
            instanceVisual.FallbackGlyph,
            releaseTime,
            mainClass,
            javaRequirement,
            mods,
            resourcePacks,
            shaders,
            saves,
            hasShaderDirectory);
    }

    private static string DetectLoader(IEnumerable<string> signals)
    {
        var text = string.Join('\n', signals).ToLowerInvariant();
        if (text.Contains("neoforge", StringComparison.Ordinal))
            return "NeoForge";
        if (text.Contains("minecraftforge", StringComparison.Ordinal) ||
            text.Contains("net.minecraftforge", StringComparison.Ordinal) ||
            text.Contains("forge", StringComparison.Ordinal))
        {
            return "Forge";
        }
        if (text.Contains("fabric", StringComparison.Ordinal))
            return "Fabric";
        if (text.Contains("quilt", StringComparison.Ordinal))
            return "Quilt";
        return "原版";
    }

    private static string DetectLoaderVersion(
        IReadOnlyList<string> libraryNames,
        IReadOnlyList<string> allSignals,
        string loader,
        ref string? baseGameVersion)
    {
        if (loader == "原版")
            return "不适用";

        var candidates = libraryNames.Concat(allSignals).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var parts = candidate.Split(':');
            if (parts.Length == 2)
            {
                var uid = parts[0];
                if ((loader == "Forge" && uid.Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase)) ||
                    (loader == "NeoForge" && uid.Equals("net.neoforged", StringComparison.OrdinalIgnoreCase)) ||
                    (loader == "Fabric" && uid.Equals("net.fabricmc.fabric-loader", StringComparison.OrdinalIgnoreCase)) ||
                    (loader == "Quilt" && uid.Equals("org.quiltmc.quilt-loader", StringComparison.OrdinalIgnoreCase)))
                {
                    return parts[1];
                }
            }
            if (parts.Length < 3)
                continue;

            var group = parts[0];
            var artifact = parts[1];
            var version = parts[2];
            if (loader == "NeoForge" &&
                string.Equals(group, "net.neoforged", StringComparison.OrdinalIgnoreCase) &&
                (artifact.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
                 artifact.Contains("loader", StringComparison.OrdinalIgnoreCase)))
            {
                return version;
            }
            if (loader == "Fabric" &&
                string.Equals(group, "net.fabricmc", StringComparison.OrdinalIgnoreCase) &&
                artifact.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
            if (loader == "Quilt" &&
                string.Equals(group, "org.quiltmc", StringComparison.OrdinalIgnoreCase) &&
                artifact.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
            if (loader == "Forge" &&
                string.Equals(group, "net.minecraftforge", StringComparison.OrdinalIgnoreCase) &&
                (artifact.Equals("fmlloader", StringComparison.OrdinalIgnoreCase) ||
                 artifact.Equals("forge", StringComparison.OrdinalIgnoreCase)))
            {
                var separator = version.IndexOf('-');
                if (separator > 0 && separator < version.Length - 1)
                {
                    baseGameVersion ??= version[..separator];
                    return version[(separator + 1)..];
                }
                return version;
            }
        }

        return "未提供";
    }

    private static void ReadExternalComponentMetadata(
        string instanceDirectory,
        ICollection<string> loaderSignals,
        ref string? baseGameVersion)
    {
        var packPath = Path.Combine(instanceDirectory, "mmc-pack.json");
        if (!File.Exists(packPath))
            return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(packPath));
            if (!document.RootElement.TryGetProperty("components", out var components) ||
                components.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var component in components.EnumerateArray())
            {
                var uid = ReadString(component, "uid");
                var version = ReadString(component, "version");
                if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(version))
                    continue;
                if (string.Equals(uid, "net.minecraft", StringComparison.OrdinalIgnoreCase))
                    baseGameVersion ??= version;
                loaderSignals.Add($"{uid}:{version}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
