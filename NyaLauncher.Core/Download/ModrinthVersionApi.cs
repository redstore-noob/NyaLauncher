using System.Text.Json;
using System.Text.Json.Serialization;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Core.Download;

/// <summary>
/// Modrinth API v2 的 Mod 版本查询与下载服务。
/// </summary>
public static class ModrinthVersionApi
{
    private const string BaseUrl = "https://api.modrinth.com/v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// 获取指定项目的版本列表，可按 MC 版本和 Loader 过滤。
    /// </summary>
    public static async Task<List<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        string[]? gameVersions = null,
        string[]? loaders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var url = $"{BaseUrl}/project/{projectId}/version";
        var queryParams = new List<string>();
        if (gameVersions is { Length: > 0 })
            queryParams.Add($"game_versions=[{string.Join(",", gameVersions.Select(v => $"\"{v}\""))}]");
        if (loaders is { Length: > 0 })
            queryParams.Add($"loaders=[{string.Join(",", loaders.Select(l => $"\"{l}\""))}]");
        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        try
        {
            var json = await PathUtil.SharedHttpClient
                .GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false);

            return JsonSerializer.Deserialize<List<ModrinthVersion>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// 获取指定项目支持的 MC 版本列表（去重、降序）。
    /// </summary>
    public static async Task<List<string>> GetSupportedGameVersionsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetVersionsAsync(projectId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return versions
            .SelectMany(v => v.GameVersions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取指定项目在指定 MC 版本下支持的 Loader 列表（去重）。
    /// </summary>
    public static async Task<List<string>> GetSupportedLoadersAsync(
        string projectId,
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetVersionsAsync(projectId, gameVersions: [gameVersion], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return versions
            .SelectMany(v => v.Loaders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取指定项目在指定 MC 版本 + Loader 下的可用 Mod 版本列表。
    /// </summary>
    public static async Task<List<ModrinthVersion>> GetVersionsForComboAsync(
        string projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        return await GetVersionsAsync(projectId, [gameVersion], [loader], cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Modrinth API 返回的 Mod 版本条目。
/// </summary>
public sealed class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("date_published")]
    public string DatePublishedRaw { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<ModrinthVersionFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<ModrinthDependency> Dependencies { get; set; } = [];

    // ---- 计算属性 ----

    [JsonIgnore]
    public ModrinthVersionFile? PrimaryFile =>
        Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? VersionNumber : Name;

    [JsonIgnore]
    public string DateDisplay
    {
        get
        {
            if (DateTime.TryParse(DatePublishedRaw, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return DatePublishedRaw;
        }
    }

    [JsonIgnore]
    public string GameVersionsDisplay =>
        GameVersions.Count > 0 ? string.Join(", ", GameVersions.Take(3)) : "";

    [JsonIgnore]
    public string LoaderDisplay => string.Join(", ", Loaders.Select(l => l switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => l
    }));

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LoaderDisplay))
                parts.Add(LoaderDisplay);
            if (!string.IsNullOrWhiteSpace(DateDisplay))
                parts.Add(DateDisplay);
            if (PrimaryFile is { } f)
                parts.Add(f.SizeDisplay);
            return string.Join(" · ", parts);
        }
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Modrinth 版本中的单个文件。
/// </summary>
public sealed class ModrinthVersionFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonIgnore]
    public string SizeDisplay => Size switch
    {
        >= 1048576 => $"{Size / 1048576.0:0.1} MB",
        >= 1024 => $"{Size / 1024.0:0.0} KB",
        _ => $"{Size} B"
    };
}

/// <summary>
/// Modrinth 版本的依赖条目。
/// </summary>
public sealed class ModrinthDependency
{
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = "required";

    [JsonIgnore]
    public bool IsRequired => DependencyType == "required";

    [JsonIgnore]
    public string DependencyTypeDisplay => DependencyType switch
    {
        "required" => "必需",
        "optional" => "可选",
        "incompatible" => "不兼容",
        "embedded" => "内嵌",
        _ => DependencyType
    };
}
