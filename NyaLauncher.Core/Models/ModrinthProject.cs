using System.Text.Json.Serialization;

namespace NyaLauncher.Core.Models;

/// <summary>
/// Modrinth API v2 search 返回结果
/// </summary>
public class ModrinthSearchResult
{
    [JsonPropertyName("hits")]
    public List<ModrinthProject> Hits { get; set; } = [];
}

/// <summary>
/// Modrinth 项目数据（来自 Modrinth API）
/// </summary>
public class ModrinthProject
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; } = string.Empty;

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("follows")]
    public int Follows { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("date_created")]
    public DateTime DateCreated { get; set; }

    // --- 辅助属性 ---

    /// <summary>格式化下载量</summary>
    [JsonIgnore]
    public string DownloadsDisplay => Downloads >= 1_000_000
        ? $"{Downloads / 1_000_000.0:F1}M 下载"
        : Downloads >= 1_000
            ? $"{Downloads / 1_000.0:F1}K 下载"
            : $"{Downloads} 下载";

    /// <summary>格式化关注数</summary>
    [JsonIgnore]
    public string FollowsDisplay => Follows >= 1_000
        ? $"{Follows / 1_000.0:F1}K ⭐"
        : $"{Follows} ⭐";

    /// <summary>项目类型的中文显示名</summary>
    [JsonIgnore]
    public string TypeDisplay => ProjectType switch
    {
        "mod" => "Mod",
        "modpack" => "整合包",
        "shader" => "光影包",
        "resourcepack" => "材质包",
        _ => ProjectType
    };

    /// <summary>项目类型对应图标</summary>
    [JsonIgnore]
    public string TypeIcon => ProjectType switch
    {
        "mod" => "⬜",
        "modpack" => "📦",
        "shader" => "☀️",
        "resourcepack" => "🎨",
        _ => "📄"
    };
}
