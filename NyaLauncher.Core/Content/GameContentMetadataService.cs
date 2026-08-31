using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Core.Content;

public sealed record GameContentEntry(
    string Name,
    string MetadataLine,
    string Description,
    string? IconPath,
    string FallbackGlyph,
    string SourcePath,
    bool IsDisabled);

public sealed record GameInstanceVisual(string? IconPath, string FallbackGlyph);

/// <summary>
/// 从 JAR、ZIP 和 level.dat 中读取 Mod、资源包、光影和存档的元数据。
/// </summary>
public static partial class GameContentMetadataService
{
    private const long MaximumMetadataBytes = 2 * 1024 * 1024;
    private const long MaximumIconBytes = 8 * 1024 * 1024;

    public static IReadOnlyList<GameContentEntry> ReadMods(
        string directory,
        CancellationToken cancellationToken) =>
        EnumerateFiles(directory, "*.jar")
            .Concat(EnumerateFiles(directory, "*.jar.disabled"))
            .Select(path => ReadMod(path, cancellationToken))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<GameContentEntry> ReadResourcePacks(
        string directory,
        CancellationToken cancellationToken) =>
        EnumerateArchivesAndDirectories(directory)
            .Select(path => ReadPack(path, "▣", cancellationToken))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<GameContentEntry> ReadShaders(
        string directory,
        CancellationToken cancellationToken) =>
        EnumerateArchivesAndDirectories(directory)
            .Select(path => ReadPack(path, "✦", cancellationToken))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<GameContentEntry> ReadSaves(
        string directory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return [];

        var result = new List<GameContentEntry>();
        foreach (var path in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(ReadSave(path));
        }
        return result.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static GameInstanceVisual ResolveInstanceVisual(
        GameInstanceSnapshot snapshot,
        string versionId,
        string? loaderName = null)
    {
        var instanceDirectory = Path.Combine(snapshot.MinecraftDirectory, "versions", versionId);
        string? launcherRoot = null;
        if (GameInstanceLayoutResolver.TryResolveExternalInstance(snapshot.SourcePath, out var external) &&
            string.Equals(external.InstanceId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            instanceDirectory = external.InstanceDirectory;
            launcherRoot = external.LauncherRoot;
        }

        // 优先级：显式内置图标偏好 → profile 自定义图标偏好 → 实例目录自带图标 → 按加载器默认。
        // 前置判断 gameicon: 直接返回，无需触碰磁盘。
        var profileOverride = GameVersionProfileStore.GetInstanceIconOverride(
            snapshot.MinecraftDirectory, versionId);
        if (profileOverride is { Length: > 0 } &&
            profileOverride.StartsWith("gameicon:", StringComparison.Ordinal))
            return new GameInstanceVisual(profileOverride, LoaderGlyph(loaderName));

        // 自定义图标偏好（"custom"）优先读取用户手动设置的图标文件，其次实例目录自带图标
        var useCustom = string.Equals(profileOverride, "custom", StringComparison.Ordinal);
        var icon = useCustom ? CustomInstanceIconStore.GetPath(snapshot.MinecraftDirectory, versionId) : null;
        icon ??= FindInstanceIcon(instanceDirectory, launcherRoot);
        loaderName ??= DetectLoaderFromMetadata(snapshot.MinecraftDirectory, instanceDirectory, versionId);
        icon ??= DefaultInstanceIconCatalog.GetIconPath(loaderName);
        return new GameInstanceVisual(icon, LoaderGlyph(loaderName));
    }

    private static GameContentEntry ReadMod(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fallbackName = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var fabric = FindEntry(archive, "fabric.mod.json");
            if (fabric is not null)
                return ReadFabricMod(path, archive, fabric, fallbackName);

            var quilt = FindEntry(archive, "quilt.mod.json");
            if (quilt is not null)
                return ReadQuiltMod(path, archive, quilt, fallbackName);

            var toml = FindEntry(archive, "META-INF/neoforge.mods.toml") ??
                       FindEntry(archive, "META-INF/mods.toml");
            if (toml is not null)
                return ReadTomlMod(path, archive, toml, fallbackName);

            var legacy = FindEntry(archive, "mcmod.info");
            if (legacy is not null)
                return ReadLegacyMod(path, archive, legacy, fallbackName);
        }
        catch (InvalidDataException)
        {
        }
        catch (JsonException)
        {
            // 手写/损坏的 fabric.mod.json、quilt.mod.json、mcmod.info（如字符串里带真实换行符）
            // 不应让整个版本详情页读取失败，降级为未知条目（PCL/HMCL 对此类损坏同样宽容）
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return UnknownEntry(fallbackName, "◆", path);
    }

    private static bool IsDisabledFile(string path) =>
        path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

    private static GameContentEntry ReadFabricMod(
        string sourcePath,
        ZipArchive archive,
        ZipArchiveEntry metadata,
        string fallbackName)
    {
        using var document = ReadJson(metadata);
        var root = document.RootElement;
        var name = ReadString(root, "name") ?? ReadString(root, "id") ?? fallbackName;
        var version = ReadString(root, "version") ?? "未提供";
        var authors = ReadPeople(root, "authors");
        var description = ReadDescription(root, "description");
        var iconEntry = ReadIconProperty(root, "icon");
        return CreateEntry(
            sourcePath,
            archive,
            name,
            authors,
            version,
            description,
            iconEntry,
            "◆");
    }

    private static GameContentEntry ReadQuiltMod(
        string sourcePath,
        ZipArchive archive,
        ZipArchiveEntry metadata,
        string fallbackName)
    {
        using var document = ReadJson(metadata);
        var root = document.RootElement;
        var loader = root.TryGetProperty("quilt_loader", out var value) ? value : root;
        var metadataRoot = loader.TryGetProperty("metadata", out value) ? value : loader;
        var name = ReadString(metadataRoot, "name") ?? ReadString(loader, "id") ?? fallbackName;
        var version = ReadString(loader, "version") ?? "未提供";
        var authors = ReadPeople(metadataRoot, "contributors");
        var description = ReadDescription(metadataRoot, "description");
        var iconEntry = ReadIconProperty(metadataRoot, "icon");
        return CreateEntry(sourcePath, archive, name, authors, version, description, iconEntry, "◆");
    }

    private static GameContentEntry ReadTomlMod(
        string sourcePath,
        ZipArchive archive,
        ZipArchiveEntry metadata,
        string fallbackName)
    {
        var text = ReadText(metadata);
        var modId = ReadTomlValue(text, "modId");
        if (IsTemplateValue(modId))
            modId = null;
        var name = ReadTomlValue(text, "displayName");
        if (IsTemplateValue(name))
            name = modId;
        name = string.IsNullOrWhiteSpace(name) ? fallbackName : name;
        var version = ReadTomlValue(text, "version");
        if (IsTemplateValue(version))
            version = ReadManifestValue(archive, "Implementation-Version") ??
                      ReadManifestValue(archive, "Specification-Version");
        version = string.IsNullOrWhiteSpace(version) ? "未提供" : version;
        var authors = ReadTomlValue(text, "authors");
        if (IsTemplateValue(authors))
            authors = null;
        authors = string.IsNullOrWhiteSpace(authors) ? "未提供" : authors;
        var description = ReadTomlValue(text, "description") ?? string.Empty;
        if (IsTemplateValue(description))
            description = string.Empty;
        var logo = ReadTomlValue(text, "logoFile");
        return CreateEntry(sourcePath, archive, name, authors, version, description, logo, "◆");
    }

    private static GameContentEntry ReadLegacyMod(
        string sourcePath,
        ZipArchive archive,
        ZipArchiveEntry metadata,
        string fallbackName)
    {
        using var document = ReadJson(metadata);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            root = root[0];
        var name = ReadString(root, "name") ?? ReadString(root, "modid") ?? fallbackName;
        var version = ReadString(root, "version") ?? "未提供";
        var authors = ReadPeople(root, "authorList");
        var description = ReadDescription(root, "description");
        var logo = ReadString(root, "logoFile");
        return CreateEntry(sourcePath, archive, name, authors, version, description, logo, "◆");
    }

    private static GameContentEntry ReadPack(
        string path,
        string fallbackGlyph,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fallbackName = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (Directory.Exists(path))
            {
                var metadataPath = Path.Combine(path, "pack.mcmeta");
                var directoryIcon = FindFirstExisting(path, "pack.png", "icon.png", "preview.png");
                if (!File.Exists(metadataPath))
                    return new GameContentEntry(fallbackName, "作者 未提供 · 版本 未提供", path, directoryIcon, fallbackGlyph, path, false);
                using var stream = File.OpenRead(metadataPath);
                using var directoryDocument = JsonDocument.Parse(stream);
                return ReadPackDocument(
                    directoryDocument.RootElement,
                    fallbackName,
                    path,
                    directoryIcon,
                    fallbackGlyph,
                    path);
            }

            using var archive = ZipFile.OpenRead(path);
            var metadata = FindEntry(archive, "pack.mcmeta");
            var iconEntry = FindEntry(archive, "pack.png") ??
                            FindEntry(archive, "icon.png") ??
                            FindEntry(archive, "preview.png");
            var icon = iconEntry is null ? null : ExtractIcon(path, iconEntry);
            if (metadata is null)
                return new GameContentEntry(fallbackName, "作者 未提供 · 版本 未提供", Path.GetFileName(path), icon, fallbackGlyph, path, false);
            using var document = ReadJson(metadata);
            return ReadPackDocument(document.RootElement, fallbackName, Path.GetFileName(path), icon, fallbackGlyph, path);
        }
        catch (InvalidDataException)
        {
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return UnknownEntry(fallbackName, fallbackGlyph, path);
    }

    private static GameContentEntry ReadPackDocument(
        JsonElement root,
        string fallbackName,
        string sourceLabel,
        string? icon,
        string fallbackGlyph,
        string sourcePath)
    {
        var pack = root.TryGetProperty("pack", out var value) ? value : root;
        var name = ReadString(root, "name") ?? ReadString(pack, "name") ?? fallbackName;
        var author = ReadString(root, "author") ?? ReadString(pack, "author") ?? "未提供";
        var version = ReadString(root, "version") ?? ReadString(pack, "version") ?? "未提供";
        var description = ReadDescription(pack, "description");
        if (string.IsNullOrWhiteSpace(description))
            description = sourceLabel;
        return new GameContentEntry(name, $"作者 {author} · 版本 {version}", description, icon, fallbackGlyph, sourcePath, false);
    }

    private static GameContentEntry ReadSave(string path)
    {
        var folderName = Path.GetFileName(path);
        var name = folderName;
        var gameVersion = "未提供";
        DateTime? lastPlayed = null;
        try
        {
            var levelDat = Path.Combine(path, "level.dat");
            if (File.Exists(levelDat))
            {
                var values = LevelDatReader.Read(levelDat);
                name = string.IsNullOrWhiteSpace(values.LevelName) ? folderName : values.LevelName;
                gameVersion = string.IsNullOrWhiteSpace(values.GameVersion) ? "未提供" : values.GameVersion;
                lastPlayed = values.LastPlayed;
            }
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var created = DateTime.MinValue;
        var icon = FindFirstExisting(path, "icon.png");
        try
        {
            created = Directory.GetCreationTime(path);
        }
        catch (Exception)
        {
            // 目录被删除/权限不足时用占位时间，避免整个存档扫描中断
        }

        return new GameContentEntry(
            name,
            $"创建日期 {created:yyyy-MM-dd HH:mm} · Minecraft {gameVersion}",
            lastPlayed is null
                ? $"存档文件夹：{folderName}"
                : $"最后游玩：{lastPlayed:yyyy-MM-dd HH:mm} · 存档文件夹：{folderName}",
            icon,
            "material:Apps",
            path,
            false);
    }

    private static GameContentEntry CreateEntry(
        string sourcePath,
        ZipArchive archive,
        string name,
        string authors,
        string version,
        string description,
        string? iconEntry,
        string fallbackGlyph)
    {
        var entry = string.IsNullOrWhiteSpace(iconEntry) ? null : FindEntry(archive, iconEntry);
        var icon = entry is null ? null : ExtractIcon(sourcePath, entry);
        return new GameContentEntry(
            name,
            $"作者 {NormalizeDisplay(authors)} · 版本 {NormalizeDisplay(version)}",
            string.IsNullOrWhiteSpace(description) ? Path.GetFileName(sourcePath) : description.Trim(),
            icon,
            fallbackGlyph,
            sourcePath,
            IsDisabledFile(sourcePath));
    }

    private static GameContentEntry UnknownEntry(string name, string glyph, string sourcePath) =>
        new(name, "作者 未提供 · 版本 未提供", Path.GetFileName(sourcePath), null, glyph, sourcePath, IsDisabledFile(sourcePath));

    private static string NormalizeDisplay(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未提供" : value.Trim();

    private static string ReadPeople(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var authors))
            return "未提供";
        if (authors.ValueKind == JsonValueKind.String)
            return authors.GetString() ?? "未提供";
        if (authors.ValueKind == JsonValueKind.Object)
            return string.Join("、", authors.EnumerateObject().Select(author => author.Name));
        if (authors.ValueKind != JsonValueKind.Array)
            return "未提供";
        return string.Join("、", authors.EnumerateArray().Select(author =>
            author.ValueKind == JsonValueKind.String
                ? author.GetString()
                : author.ValueKind == JsonValueKind.Object
                    ? ReadString(author, "name")
                    : null).Where(name => !string.IsNullOrWhiteSpace(name))!);
    }

    private static string ReadDescription(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var description))
            return string.Empty;
        if (description.ValueKind == JsonValueKind.String)
            return description.GetString() ?? string.Empty;
        if (description.ValueKind == JsonValueKind.Object)
        {
            return ReadString(description, "text") ??
                   ReadString(description, "translate") ??
                   description.ToString();
        }
        return description.ToString();
    }

    private static string? ReadIconProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var icon))
            return null;
        if (icon.ValueKind == JsonValueKind.String)
            return icon.GetString();
        if (icon.ValueKind == JsonValueKind.Object)
        {
            return icon.EnumerateObject()
                .OrderByDescending(property => int.TryParse(property.Name, out var size) ? size : 0)
                .Select(property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        return null;
    }

    private static JsonDocument ReadJson(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumMetadataBytes)
            throw new InvalidDataException("Metadata entry is too large.");
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumMetadataBytes)
            throw new InvalidDataException("Metadata entry is too large.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static string? ReadTomlValue(string text, string key)
    {
        var match = Regex.Match(
            text,
            $"(?im)^\\s*{Regex.Escape(key)}\\s*=\\s*(?:\"\"\"(?<value>[\\s\\S]*?)\"\"\"|'''(?<value>[\\s\\S]*?)'''|\"(?<value>[^\"]*)\"|'(?<value>[^']*)')");
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? ReadManifestValue(ZipArchive archive, string key)
    {
        var manifest = FindEntry(archive, "META-INF/MANIFEST.MF");
        if (manifest is null)
            return null;
        var lines = ReadText(manifest).Replace("\r\n ", string.Empty).Split('\n');
        var prefix = key + ":";
        return lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?[prefix.Length..].Trim();
    }

    private static bool IsTemplateValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("${", StringComparison.Ordinal);

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractIcon(string sourcePath, ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > MaximumIconBytes)
            return null;
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            return null;

        var source = new FileInfo(sourcePath);
        var cacheKey = string.Join('|', source.FullName, source.Length, source.LastWriteTimeUtc.Ticks, entry.FullName, entry.Length);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        var cacheDirectory = Path.Combine(LauncherConfig.StorageDirectory, "content-icons");
        var output = Path.Combine(cacheDirectory, hash + extension);
        if (File.Exists(output))
            return output;

        Directory.CreateDirectory(cacheDirectory);
        var temporary = output + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var input = entry.Open())
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.CopyTo(target);
            File.Move(temporary, output, false);
            return output;
        }
        catch (IOException) when (File.Exists(output))
        {
            return output;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string? FindInstanceIcon(string instanceDirectory, string? launcherRoot)
    {
        var direct = FindFirstExisting(
            instanceDirectory,
            "icon.png",
            "icon.jpg",
            "instance.png",
            "logo.png",
            "profile.png",
            Path.Combine("PCL", "Logo.png"),
            Path.Combine("PCL", "Logo.jpg"),
            Path.Combine("minecraft", "icon.png"),
            Path.Combine(".minecraft", "icon.png"));
        if (direct is not null)
            return direct;

        foreach (var metadataName in new[] { "minecraftinstance.json", "profile.json", "instance.json" })
        {
            var metadataPath = Path.Combine(instanceDirectory, metadataName);
            var referenced = ReadReferencedIcon(metadataPath, instanceDirectory);
            if (referenced is not null)
                return referenced;
        }

        var cfgPath = Path.Combine(instanceDirectory, "instance.cfg");
        if (File.Exists(cfgPath) && !string.IsNullOrWhiteSpace(launcherRoot))
        {
            var iconKey = File.ReadLines(cfgPath)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("iconKey", StringComparison.OrdinalIgnoreCase))
                .Select(parts => parts[1].Trim())
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(iconKey) && !iconKey.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                var icon = FindFirstExisting(Path.Combine(launcherRoot, "icons"), iconKey + ".png", iconKey + ".jpg");
                if (icon is not null)
                    return icon;
            }
        }

        return null;
    }

    private static string? ReadReferencedIcon(string metadataPath, string instanceDirectory)
    {
        if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length > MaximumMetadataBytes)
            return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
            return FindReferencedIcon(document.RootElement, instanceDirectory);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindReferencedIcon(JsonElement element, string instanceDirectory)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && IsIconProperty(property.Name))
                {
                    var value = property.Value.GetString();
                    if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps)
                    {
                        return value;
                    }
                    if (uri is { IsFile: true } && File.Exists(uri.LocalPath))
                        return Path.GetFullPath(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var candidate = Path.IsPathRooted(value)
                            ? value
                            : Path.Combine(instanceDirectory, value.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(candidate))
                            return Path.GetFullPath(candidate);
                    }
                }
                var nested = FindReferencedIcon(property.Value, instanceDirectory);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindReferencedIcon(child, instanceDirectory);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }

    private static bool IsIconProperty(string name) =>
        name.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("iconUrl", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("iconPath", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("icon_path", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("profileImagePath", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("profile_image_path", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("logo", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("logoUrl", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("imageUrl", StringComparison.OrdinalIgnoreCase);

    private static string DetectLoaderFromMetadata(
        string minecraftDirectory,
        string instanceDirectory,
        string versionId)
    {
        var signals = new StringBuilder(versionId);
        var json = Path.Combine(minecraftDirectory, "versions", versionId, versionId + ".json");
        if (File.Exists(json))
        {
            using var stream = new FileStream(json, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new char[Math.Min(1024 * 1024, (int)Math.Min(stream.Length, int.MaxValue))];
            signals.Append(reader.Read(buffer, 0, buffer.Length) > 0 ? buffer : []);
        }
        var pack = Path.Combine(instanceDirectory, "mmc-pack.json");
        if (File.Exists(pack))
            signals.Append(File.ReadAllText(pack));
        var text = signals.ToString();
        if (text.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return "NeoForge";
        if (text.Contains("fabric", StringComparison.OrdinalIgnoreCase))
            return "Fabric";
        if (text.Contains("quilt", StringComparison.OrdinalIgnoreCase))
            return "Quilt";
        if (text.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return "Forge";
        return "原版";
    }

    // 回退字形统一使用 Material 图标字形（Core 只存字符串，由 UI 层渲染为图标）
    private static string LoaderGlyph(string? loaderName) => "material:Apps";

    private static string? FindFirstExisting(string root, params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var candidate = Path.Combine(root, relativePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern) : [];

    private static IEnumerable<string> EnumerateArchivesAndDirectories(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.zip").Concat(Directory.EnumerateDirectories(directory))
            : [];

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static class LevelDatReader
    {
        public static (string? LevelName, string? GameVersion, DateTime? LastPlayed) Read(string path)
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.UTF8, false);
            string? levelName = null;
            string? gameVersion = null;
            long? lastPlayed = null;
            var rootType = reader.ReadByte();
            if (rootType != 10)
                throw new InvalidDataException("level.dat root is not a compound tag.");
            _ = ReadNbtString(reader);
            ReadCompound(reader, string.Empty, ref levelName, ref gameVersion, ref lastPlayed, 0);
            DateTime? timestamp = lastPlayed is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastPlayed.Value).LocalDateTime
                : null;
            return (levelName, gameVersion, timestamp);
        }

        private static void ReadCompound(
            BinaryReader reader,
            string path,
            ref string? levelName,
            ref string? gameVersion,
            ref long? lastPlayed,
            int depth)
        {
            if (depth > 32)
                throw new InvalidDataException("NBT nesting is too deep.");
            while (true)
            {
                var type = reader.ReadByte();
                if (type == 0)
                    return;
                var name = ReadNbtString(reader);
                var currentPath = string.IsNullOrEmpty(path) ? name : path + "." + name;
                if (type == 8 && currentPath.EndsWith("Data.LevelName", StringComparison.Ordinal))
                    levelName = ReadNbtString(reader);
                else if (type == 8 && currentPath.EndsWith("Data.Version.Name", StringComparison.Ordinal))
                    gameVersion = ReadNbtString(reader);
                else if (type == 4 && currentPath.EndsWith("Data.LastPlayed", StringComparison.Ordinal))
                    lastPlayed = ReadInt64(reader);
                else if (type == 10)
                    ReadCompound(reader, currentPath, ref levelName, ref gameVersion, ref lastPlayed, depth + 1);
                else
                    SkipPayload(reader, type, depth + 1);
            }
        }

        private static void SkipPayload(BinaryReader reader, byte type, int depth)
        {
            switch (type)
            {
                case 1: SkipBytes(reader, 1); break;
                case 2: SkipBytes(reader, 2); break;
                case 3: SkipBytes(reader, 4); break;
                case 4: SkipBytes(reader, 8); break;
                case 5: SkipBytes(reader, 4); break;
                case 6: SkipBytes(reader, 8); break;
                case 7: SkipBytes(reader, ReadArrayLength(reader)); break;
                case 8: _ = ReadNbtString(reader); break;
                case 9:
                    var elementType = reader.ReadByte();
                    var count = ReadInt32(reader);
                    if (count < 0 || count > 10_000_000)
                        throw new InvalidDataException("Invalid NBT list size.");
                    for (var index = 0; index < count; index++)
                        SkipPayload(reader, elementType, depth + 1);
                    break;
                case 10:
                    string? unusedName = null;
                    string? unusedVersion = null;
                    long? unusedTime = null;
                    ReadCompound(reader, string.Empty, ref unusedName, ref unusedVersion, ref unusedTime, depth + 1);
                    break;
                case 11: SkipBytes(reader, checked(ReadArrayLength(reader) * 4L)); break;
                case 12: SkipBytes(reader, checked(ReadArrayLength(reader) * 8L)); break;
                default: throw new InvalidDataException($"Unsupported NBT tag {type}.");
            }
        }

        private static string ReadNbtString(BinaryReader reader)
        {
            Span<byte> lengthBytes = stackalloc byte[2];
            reader.BaseStream.ReadExactly(lengthBytes);
            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        private static int ReadInt32(BinaryReader reader)
        {
            Span<byte> bytes = stackalloc byte[4];
            reader.BaseStream.ReadExactly(bytes);
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }

        private static long ReadInt64(BinaryReader reader)
        {
            Span<byte> bytes = stackalloc byte[8];
            reader.BaseStream.ReadExactly(bytes);
            return BinaryPrimitives.ReadInt64BigEndian(bytes);
        }

        private static int ReadArrayLength(BinaryReader reader)
        {
            var length = ReadInt32(reader);
            if (length < 0 || length > 100_000_000)
                throw new InvalidDataException("Invalid NBT array size.");
            return length;
        }

        private static void SkipBytes(BinaryReader reader, long count)
        {
            if (count < 0 || count > 512L * 1024 * 1024)
                throw new InvalidDataException("NBT payload is too large.");
            Span<byte> buffer = stackalloc byte[4096];
            while (count > 0)
            {
                var read = reader.Read(buffer[..(int)Math.Min(buffer.Length, count)]);
                if (read == 0)
                    throw new EndOfStreamException();
                count -= read;
            }
        }
    }

    /// <summary>
    /// 按加载器名称给出内置 GameIcons 资源符号（"gameicon:{key}"）。
    /// UI 层的 ComponentImageLoader 将符号解码为程序集内嵌的 Resources/GameIcons PNG；
    /// Fabric、Quilt 及未知加载器用 command_block 兜底。
    /// </summary>
    private static class DefaultInstanceIconCatalog
    {
        public static string? GetIconPath(string loaderName) => loaderName switch
        {
            "NeoForge" => "gameicon:neoforge",
            "Forge" => "gameicon:forge",
            "Fabric" => "gameicon:fabric",
            "原版" => "gameicon:vanilla",
            _ => "gameicon:command_block"
        };
    }
}
