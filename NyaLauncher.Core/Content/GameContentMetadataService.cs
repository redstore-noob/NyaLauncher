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

        var icon = FindInstanceIcon(instanceDirectory, launcherRoot);
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

        var created = Directory.GetCreationTime(path);
        var icon = FindFirstExisting(path, "icon.png");
        return new GameContentEntry(
            name,
            $"创建日期 {created:yyyy-MM-dd HH:mm} · Minecraft {gameVersion}",
            lastPlayed is null
                ? $"存档文件夹：{folderName}"
                : $"最后游玩：{lastPlayed:yyyy-MM-dd HH:mm} · 存档文件夹：{folderName}",
            icon,
            "🌍",
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

    private static string LoaderGlyph(string? loaderName) => loaderName switch
    {
        "Fabric" => "🧵",
        "Forge" => "⚒",
        "NeoForge" => "🦊",
        "Quilt" => "◆",
        _ => "🟩"
    };

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

    private static class DefaultInstanceIconCatalog
    {
        private const int Size = 32;
        private static readonly object Gate = new();

        public static string? GetIconPath(string loaderName)
        {
            try
            {
                var key = loaderName.ToLowerInvariant() switch
                {
                    "fabric" => "fabric-cloth",
                    "forge" => "forge-anvil",
                    "neoforge" => "neoforge-fox",
                    "quilt" => "quilt-fabric",
                    _ => "vanilla-grass-block"
                };
                var directory = Path.Combine(LauncherConfig.StorageDirectory, "instance-icons");
                var path = Path.Combine(directory, key + "-v1.png");
                if (File.Exists(path))
                    return path;

                lock (Gate)
                {
                    if (File.Exists(path))
                        return path;
                    Directory.CreateDirectory(directory);
                    var pixels = new byte[Size * Size * 4];
                    switch (key)
                    {
                        case "fabric-cloth": DrawFabric(pixels); break;
                        case "forge-anvil": DrawAnvil(pixels); break;
                        case "neoforge-fox": DrawFox(pixels); break;
                        case "quilt-fabric": DrawQuilt(pixels); break;
                        default: DrawGrassBlock(pixels); break;
                    }
                    var temporary = path + ".tmp";
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                        WritePng(stream, pixels);
                    File.Move(temporary, path, true);
                }
                return path;
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

        private static void DrawGrassBlock(byte[] pixels)
        {
            Fill(pixels, 4, 7, 24, 22, 105, 67, 40);
            Fill(pixels, 4, 7, 24, 7, 80, 176, 70);
            Fill(pixels, 6, 5, 20, 4, 106, 205, 82);
            Fill(pixels, 8, 11, 5, 4, 74, 143, 55);
            Fill(pixels, 19, 10, 6, 5, 62, 133, 51);
            Fill(pixels, 7, 19, 5, 4, 139, 91, 49);
            Fill(pixels, 18, 23, 6, 4, 77, 49, 34);
        }

        private static void DrawFabric(byte[] pixels)
        {
            Fill(pixels, 5, 5, 22, 22, 214, 177, 121);
            Fill(pixels, 8, 3, 17, 4, 239, 207, 153);
            Fill(pixels, 5, 9, 22, 3, 238, 204, 148);
            Fill(pixels, 5, 17, 22, 3, 185, 141, 89);
            Fill(pixels, 9, 5, 3, 22, 232, 198, 143);
            Fill(pixels, 19, 5, 3, 22, 177, 131, 82);
            Fill(pixels, 23, 23, 4, 6, 151, 109, 70);
        }

        private static void DrawAnvil(byte[] pixels)
        {
            Fill(pixels, 3, 6, 26, 5, 92, 103, 119);
            Fill(pixels, 7, 11, 19, 5, 69, 78, 91);
            Fill(pixels, 11, 16, 11, 8, 91, 102, 116);
            Fill(pixels, 7, 24, 19, 5, 60, 69, 81);
            Fill(pixels, 5, 7, 11, 2, 157, 168, 181);
            Fill(pixels, 12, 17, 4, 6, 132, 143, 157);
        }

        private static void DrawFox(byte[] pixels)
        {
            Fill(pixels, 6, 7, 7, 8, 230, 104, 43);
            Fill(pixels, 19, 7, 7, 8, 230, 104, 43);
            Fill(pixels, 8, 5, 4, 5, 251, 143, 60);
            Fill(pixels, 20, 5, 4, 5, 251, 143, 60);
            Fill(pixels, 7, 11, 18, 14, 238, 113, 45);
            Fill(pixels, 9, 19, 14, 8, 244, 224, 190);
            Fill(pixels, 10, 14, 4, 4, 39, 34, 36);
            Fill(pixels, 18, 14, 4, 4, 39, 34, 36);
            Fill(pixels, 14, 21, 4, 3, 47, 39, 39);
        }

        private static void DrawQuilt(byte[] pixels)
        {
            Fill(pixels, 4, 4, 24, 24, 102, 76, 168);
            for (var y = 4; y < 28; y += 8)
                for (var x = 4; x < 28; x += 8)
                    Fill(pixels, x, y, 7, 7, ((x + y) / 8) % 2 == 0 ? (byte)177 : (byte)126, 103, 205);
            Fill(pixels, 11, 4, 1, 24, 223, 207, 240);
            Fill(pixels, 20, 4, 1, 24, 223, 207, 240);
            Fill(pixels, 4, 11, 24, 1, 223, 207, 240);
            Fill(pixels, 4, 20, 24, 1, 223, 207, 240);
        }

        private static void Fill(
            byte[] pixels,
            int x,
            int y,
            int width,
            int height,
            byte red,
            byte green,
            byte blue)
        {
            for (var row = Math.Max(0, y); row < Math.Min(Size, y + height); row++)
                for (var column = Math.Max(0, x); column < Math.Min(Size, x + width); column++)
                {
                    var offset = (row * Size + column) * 4;
                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                    pixels[offset + 3] = 255;
                }
        }

        private static void WritePng(Stream target, byte[] pixels) =>
            NyaLauncher.Core.Tools.PngEncoder.EncodeTo(target, Size, Size, pixels);
    }
}
