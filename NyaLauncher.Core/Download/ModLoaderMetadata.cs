using System.Text.Json;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 支持的 Mod Loader 类型。
/// </summary>
public enum ModLoaderType
{
    Vanilla,
    Fabric,
    NeoForge,
    Forge
}

/// <summary>
/// 单个 Mod Loader 版本条目（从 Loader API 获取）。
/// </summary>
public sealed record ModLoaderVersion
{
    /// <summary>Loader 类型。</summary>
    public ModLoaderType Type { get; init; }

    /// <summary>Loader 版本号，如 "0.16.14"（Fabric）或 "21.8.1"（NeoForge）。</summary>
    public string LoaderVersion { get; init; } = string.Empty;

    /// <summary>是否为推荐/稳定版本。</summary>
    public bool IsStable { get; init; }

    /// <summary>构建号（部分 Loader 使用）。</summary>
    public int BuildNumber { get; init; }

    /// <summary>安装此 Loader 所需的版本 JSON 元数据 URL。</summary>
    public string MetadataUrl { get; init; } = string.Empty;

    /// <summary>
    /// MetadataUrl 指向的是安装器 JAR 而非版本 JSON。
    /// 需要下载 JAR 后从中提取 version.json 或 install_profile.json。
    /// </summary>
    public bool RequiresInstallerExtraction { get; init; }

    /// <summary>用于 UI 展示的格式化名称。</summary>
    public string DisplayName => Type switch
    {
        ModLoaderType.Fabric => $"Fabric Loader {LoaderVersion}",
        ModLoaderType.NeoForge => $"NeoForge {LoaderVersion}",
        ModLoaderType.Forge => $"Forge {LoaderVersion}",
        _ => LoaderVersion
    };
}

/// <summary>
/// Mod Loader 元数据获取服务。从各 Loader 官方 API 获取可用版本列表和安装元数据。
/// </summary>
public static class ModLoaderMetadata
{
    // ------------------------------------------------------------------
    // Fabric
    // ------------------------------------------------------------------

    private const string FabricLoaderVersionsUrl =
        "https://meta.fabricmc.net/v2/versions/loader/";

    /// <summary>
    /// 获取指定 Minecraft 版本可用的 Fabric Loader 版本列表。
    /// </summary>
    public static async Task<List<ModLoaderVersion>> GetFabricVersionsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var url = $"{FabricLoaderVersionsUrl}{minecraftVersion}";
        var json = await DownloadSourceProvider.GetStringAsync(url, TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        var response = System.Text.Json.JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json);

        if (response is null)
            return [];

        return response
            .Where(entry => entry.Loader is not null)
            .Select(entry => new ModLoaderVersion
            {
                Type = ModLoaderType.Fabric,
                LoaderVersion = entry.Loader!.Version,
                IsStable = entry.Loader.Stable,
                MetadataUrl = $"{FabricLoaderVersionsUrl}{minecraftVersion}/{entry.Loader.Version}/profile/json"
            })
            .ToList();
    }

    // ------------------------------------------------------------------
    // NeoForge
    // ------------------------------------------------------------------

    private const string NeoForgeVersionsUrl =
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    /// <summary>
    /// 获取指定 Minecraft 版本可用的 NeoForge 版本列表。
    /// NeoForge 版本格式为 "{mcMajor}.{mcMinor}.{patch}"，如 "21.8.1"。
    /// </summary>
    public static async Task<List<ModLoaderVersion>> GetNeoForgeVersionsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var xml = await DownloadSourceProvider.GetStringAsync(
                NeoForgeVersionsUrl, TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        var versions = ParseMavenVersions(xml);
        var prefix = ToNeoForgePrefix(minecraftVersion);

        return versions
            .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v)
            .Select(v => new ModLoaderVersion
            {
                Type = ModLoaderType.NeoForge,
                LoaderVersion = v,
                MetadataUrl =
                    $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{v}/neoforge-{v}-installer.jar",
                RequiresInstallerExtraction = true
            })
            .ToList();
    }

    // ------------------------------------------------------------------
    // Forge
    // ------------------------------------------------------------------

    private const string ForgePromotionsUrl =
        "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

    /// <summary>
    /// 获取指定 Minecraft 版本推荐的 Forge 版本。
    /// Forge 的版本分发方式不同于 Fabric/NeoForge，只提供推荐版本。
    /// </summary>
    public static async Task<List<ModLoaderVersion>> GetForgeVersionsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var json = await DownloadSourceProvider.GetStringAsync(
                ForgePromotionsUrl, TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("promos", out var promos))
            return [];

        var results = new List<ModLoaderVersion>();
        foreach (var property in promos.EnumerateObject())
        {
            // key 格式: "{mcVersion}-latest" 或 "{mcVersion}-recommended"
            if (!property.Name.StartsWith(minecraftVersion, StringComparison.OrdinalIgnoreCase))
                continue;

            var forgeVersion = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(forgeVersion))
                continue;

            var isRecommended = property.Name.EndsWith("-recommended", StringComparison.OrdinalIgnoreCase);
            var fullVersion = $"{minecraftVersion}-{forgeVersion}";

            results.Add(new ModLoaderVersion
            {
                Type = ModLoaderType.Forge,
                LoaderVersion = fullVersion,
                IsStable = isRecommended,
                MetadataUrl =
                    $"https://maven.minecraftforge.net/net/minecraftforge/forge/{fullVersion}/forge-{fullVersion}-installer.jar",
                RequiresInstallerExtraction = true
            });
        }

        return results
            .OrderByDescending(v => v.IsStable)
            .ThenByDescending(v => v.LoaderVersion)
            .ToList();
    }

    // ------------------------------------------------------------------
    // 通用入口
    // ------------------------------------------------------------------

    /// <summary>
    /// 获取指定 Loader 类型在指定 Minecraft 版本下的可用版本列表。
    /// </summary>
    public static Task<List<ModLoaderVersion>> GetVersionsAsync(
        ModLoaderType type,
        string minecraftVersion,
        CancellationToken cancellationToken = default) => type switch
    {
        ModLoaderType.Fabric => GetFabricVersionsAsync(minecraftVersion, cancellationToken),
        ModLoaderType.NeoForge => GetNeoForgeVersionsAsync(minecraftVersion, cancellationToken),
        ModLoaderType.Forge => GetForgeVersionsAsync(minecraftVersion, cancellationToken),
        _ => Task.FromResult<List<ModLoaderVersion>>([])
    };

    // ------------------------------------------------------------------
    // 辅助方法
    // ------------------------------------------------------------------

    /// <summary>
    /// 将 Minecraft 版本号转换为 NeoForge 的 Maven 版本前缀。
    /// 如 "1.21.8" → "21.8"，"1.20.4" → "20.4"。
    /// </summary>
    private static string ToNeoForgePrefix(string minecraftVersion)
    {
        // NeoForge 版本格式: {mcMajor}.{mcMinor}.{patch}
        // MC 1.21.8 → NeoForge 21.8.x
        var parts = minecraftVersion.Split('.');
        if (parts.Length >= 3)
            return $"{parts[1]}.{parts[2]}";
        if (parts.Length == 2)
            return parts[1];
        return minecraftVersion;
    }

    /// <summary>
    /// 解析 Maven maven-metadata.xml 中的 version 列表。
    /// </summary>
    private static List<string> ParseMavenVersions(string xml)
    {
        var versions = new List<string>();
        const string startTag = "<version>";
        const string endTag = "</version>";

        var span = xml.AsSpan();
        while (true)
        {
            var startIndex = span.IndexOf(startTag.AsSpan());
            if (startIndex < 0)
                break;

            var valueStart = startIndex + startTag.Length;
            var endIndex = span[valueStart..].IndexOf(endTag.AsSpan());
            if (endIndex < 0)
                break;

            versions.Add(span.Slice(valueStart, endIndex).ToString());
            span = span[(valueStart + endIndex + endTag.Length)..];
        }

        return versions;
    }

    // ------------------------------------------------------------------
    // JSON 反序列化模型
    // ------------------------------------------------------------------

    private sealed class FabricLoaderEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("loader")]
        public FabricLoaderInfo? Loader { get; set; }
    }

    private sealed class FabricLoaderInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }
}
