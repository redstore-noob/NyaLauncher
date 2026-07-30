using System.Text.Json;

namespace NyaLauncher.Core.Launch.Internal;

internal sealed class MinecraftVersionProfileLoader
{
    private const int MaximumInheritanceDepth = 16;

    public async Task<MinecraftVersionProfile> LoadAsync(
        string minecraftDirectory,
        string versionId,
        CancellationToken cancellationToken)
    {
        ValidateVersionId(versionId);

        var chain = new List<(string Id, JsonElement Root)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = versionId;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(currentId))
            {
                throw new MinecraftLaunchException($"版本继承出现循环：{currentId}");
            }

            if (chain.Count >= MaximumInheritanceDepth)
            {
                throw new MinecraftLaunchException("版本继承层级过深，已停止解析。");
            }

            var jsonPath = GetVersionJsonPath(minecraftDirectory, currentId);
            if (!File.Exists(jsonPath))
            {
                throw new MinecraftLaunchException($"找不到版本配置：{jsonPath}");
            }

            JsonElement root;
            try
            {
                await using var stream = File.OpenRead(jsonPath);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                root = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new MinecraftLaunchException($"版本配置不是有效 JSON：{jsonPath}", ex);
            }

            chain.Add((currentId, root));
            if (!TryGetString(root, "inheritsFrom", out var parentId))
            {
                break;
            }

            ValidateVersionId(parentId);
            currentId = parentId;
        }

        chain.Reverse();
        return MergeProfiles(versionId, chain);
    }

    private static MinecraftVersionProfile MergeProfiles(
        string requestedVersionId,
        IReadOnlyList<(string Id, JsonElement Root)> chain)
    {
        string? mainClass = null;
        string? clientJarVersionId = null;
        string? assetsId = null;
        string? versionType = null;
        string? legacyArguments = null;
        int? javaMajorVersion = null;
        var jvmArguments = new List<JsonElement>();
        var gameArguments = new List<JsonElement>();
        var libraries = new List<JsonElement>();
        var libraryIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (profileId, root) in chain)
        {
            if (TryGetString(root, "mainClass", out var mainClassValue))
                mainClass = mainClassValue;
            if (TryGetString(root, "assets", out var assetsValue))
                assetsId = assetsValue;
            if (TryGetString(root, "type", out var typeValue))
                versionType = typeValue;
            if (TryGetString(root, "minecraftArguments", out var legacyValue))
                legacyArguments = legacyValue;

            if (root.TryGetProperty("assetIndex", out var assetIndex) &&
                TryGetString(assetIndex, "id", out var assetIndexId))
            {
                assetsId = assetIndexId;
            }

            if (root.TryGetProperty("javaVersion", out var javaVersion) &&
                javaVersion.TryGetProperty("majorVersion", out var majorVersion) &&
                majorVersion.TryGetInt32(out var parsedMajorVersion))
            {
                javaMajorVersion = parsedMajorVersion;
            }

            if (root.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("client", out _))
            {
                clientJarVersionId = profileId;
            }

            if (TryGetString(root, "jar", out var explicitJarVersion))
            {
                clientJarVersionId = explicitJarVersion;
            }

            if (root.TryGetProperty("arguments", out var arguments))
            {
                AppendArray(arguments, "jvm", jvmArguments);
                AppendArray(arguments, "game", gameArguments);
            }

            if (!root.TryGetProperty("libraries", out var profileLibraries) ||
                profileLibraries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var library in profileLibraries.EnumerateArray())
            {
                var cloned = library.Clone();
                var key = GetLibraryKey(cloned);
                if (libraryIndexes.TryGetValue(key, out var existingIndex))
                {
                    libraries[existingIndex] = cloned;
                }
                else
                {
                    libraryIndexes[key] = libraries.Count;
                    libraries.Add(cloned);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(mainClass))
            throw new MinecraftLaunchException("版本配置缺少 mainClass。");
        if (string.IsNullOrWhiteSpace(clientJarVersionId))
            throw new MinecraftLaunchException("版本配置未指定可用的客户端 JAR。");

        return new MinecraftVersionProfile
        {
            Id = requestedVersionId,
            MainClass = mainClass,
            ClientJarVersionId = clientJarVersionId,
            AssetsId = assetsId ?? "legacy",
            VersionType = versionType ?? "release",
            RequiredJavaMajorVersion = javaMajorVersion,
            LegacyGameArguments = legacyArguments,
            JvmArguments = jvmArguments,
            GameArguments = gameArguments,
            Libraries = libraries
        };
    }

    private static void AppendArray(JsonElement parent, string propertyName, List<JsonElement> target)
    {
        if (!parent.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        target.AddRange(array.EnumerateArray().Select(element => element.Clone()));
    }

    private static string GetLibraryKey(JsonElement library)
    {
        if (!TryGetString(library, "name", out var name))
        {
            return Guid.NewGuid().ToString("N");
        }

        var parts = name.Split(':');
        return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : name;
    }

    private static string GetVersionJsonPath(string minecraftDirectory, string versionId) =>
        Path.Combine(minecraftDirectory, "versions", versionId, $"{versionId}.json");

    private static bool TryGetString(JsonElement parent, string propertyName, out string value)
    {
        value = string.Empty;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static void ValidateVersionId(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId) ||
            versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            versionId.Contains('/') ||
            versionId.Contains('\\') ||
            versionId is "." or "..")
        {
            throw new MinecraftLaunchException($"无效的版本 ID：{versionId}");
        }
    }
}
