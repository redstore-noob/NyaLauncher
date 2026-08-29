using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>BMCLAPI（bangbang93 镜像）按 Minecraft 版本查询 NeoForge 列表。</summary>
    private const string NeoForgeBmclListUrl =
        "https://bmclapi2.bangbang93.com/neoforge/list/{0}";

    /// <summary>BMCLAPI 下载 NeoForge 安装器 JAR（302 重定向到实际文件）。</summary>
    private const string NeoForgeBmclInstallerUrl =
        "https://bmclapi2.bangbang93.com/neoforge/version/{0}/download/installer.jar";

    /// <summary>
    /// 获取指定 Minecraft 版本可用的 NeoForge 版本列表。
    /// 优先使用 BMCLAPI 镜像（国内网络可达性好），失败时回退官方 Maven。
    /// NeoForge 版本格式为 "{mcMajor}.{mcMinor}.{patch}"，如 "21.8.1"。
    /// </summary>
    public static async Task<List<ModLoaderVersion>> GetNeoForgeVersionsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        // 1) 优先 BMCLAPI：直接返回该 MC 版本下的 NeoForge 版本，无需前缀推导
        try
        {
            var bmclJson = await DownloadSourceProvider.GetStringAsync(
                    string.Format(NeoForgeBmclListUrl, minecraftVersion),
                    TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<NeoForgeBmclEntry>>(bmclJson);

            if (entries is { Count: > 0 })
            {
                return entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Version))
                    .Select(e => NormalizeBmclNeoForgeVersion(e.Version))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(v => v, Comparer<string>.Create(ModrinthVersionApi.CompareVersionStrings))
                    .Select(v => new ModLoaderVersion
                    {
                        Type = ModLoaderType.NeoForge,
                        LoaderVersion = v,
                        MetadataUrl = string.Format(NeoForgeBmclInstallerUrl, v),
                        RequiresInstallerExtraction = true
                    })
                    .ToList();
            }
        }
        catch
        {
            // BMCL 不可用（网络/格式异常）时回退官方源，避免版本列表为空
        }

        // 2) 回退：官方 maven-metadata.xml（按前缀匹配）
        var xml = await DownloadSourceProvider.GetStringAsync(
                NeoForgeVersionsUrl, TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        var versions = ParseMavenVersions(xml);
        var prefix = ToNeoForgePrefix(minecraftVersion);

        return versions
            // 带版本边界匹配：仅匹配 "21.8.x" 或 "21.0.x"（基础版 1.21 → 21.0.x），
            // 防止 "21.1" 误匹配 "21.11.x"、防止 "21" 误匹配全部 21.x 系列。
            .Where(v => MatchesPrefix(v, prefix))
            // 按版本号数字比较降序（字典序会让 21.8.9 排在 21.8.54 前面）
            .OrderByDescending(v => v, Comparer<string>.Create(ModrinthVersionApi.CompareVersionStrings))
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

    /// <summary>
    /// 归一化 BMCL 返回的 NeoForge 版本号：
    /// 纯版本号（如 "21.8.54"）原样返回；
    /// 带 "mc-版本" 前缀的（如 "1.20.1-47.1.12"）截取纯版本号。
    /// </summary>
    internal static string NormalizeBmclNeoForgeVersion(string version)
    {
        var v = version.Trim();
        if (v.Length == 0 || !char.IsDigit(v[0]))
            return v;

        var dash = v.IndexOf('-');
        if (dash > 0 && dash < v.Length - 1)
        {
            var tail = v[(dash + 1)..];
            if (char.IsDigit(tail[0]))
                return tail;
        }
        return v;
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
            // 必须与所选 MC 版本精确匹配：防止 "1.21" 误匹配 "1.21.1-latest"
            if (!MatchesForgePromoKey(property.Name, minecraftVersion))
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
    /// 如 "1.21.8" → "21.8"，"1.21"（基础版）→ "21.0"。
    /// </summary>
    internal static string ToNeoForgePrefix(string minecraftVersion)
    {
        // NeoForge 版本格式: {mcMinor}.{mcPatch}
        // MC 1.21.8 → NeoForge 21.8.x；MC 1.21（无补丁号）→ NeoForge 21.0.x
        var parts = minecraftVersion.Split('.');
        if (parts.Length >= 3)
            return $"{parts[1]}.{parts[2]}";
        if (parts.Length == 2)
            return $"{parts[1]}.0";
        return minecraftVersion;
    }

    /// <summary>
    /// 带版本边界的精确前缀匹配：仅当 value 等于 prefix 或以 "prefix." 开头时匹配，
    /// 防止 "21.1" 误匹配 "21.11.x" 这类错误。
    /// </summary>
    internal static bool MatchesPrefix(string value, string prefix) =>
        value.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Forge promos 键匹配：键格式为 "{mcVersion}-latest" / "{mcVersion}-recommended"，
    /// 要求去后缀后与所选 MC 版本精确相等，防止 "1.21" 误匹配 "1.21.1-latest"。
    /// </summary>
    internal static bool MatchesForgePromoKey(string promoKey, string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(promoKey) || string.IsNullOrWhiteSpace(minecraftVersion))
            return false;
        foreach (var suffix in new[] { "-latest", "-recommended" })
        {
            if (!promoKey.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            return string.Equals(
                promoKey[..^suffix.Length],
                minecraftVersion,
                StringComparison.OrdinalIgnoreCase);
        }
        return false;
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
        [JsonPropertyName("loader")]
        public FabricLoaderInfo? Loader { get; set; }
    }

    private sealed class FabricLoaderInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }

    /// <summary>BMCLAPI /neoforge/list/:mcversion 返回条目。</summary>
    private sealed class NeoForgeBmclEntry
    {
        [JsonPropertyName("rawVersion")]
        public string RawVersion { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("mcversion")]
        public string McVersion { get; set; } = string.Empty;
    }
}
