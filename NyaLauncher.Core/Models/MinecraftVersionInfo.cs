using System.Text.Json.Serialization;

namespace NyaLauncher.Core.Models;

/// <summary>
/// Minecraft 版本清单根对象（来自 Mojang API）
/// </summary>
public class VersionManifest
{
    [JsonPropertyName("latest")]
    [JsonRequired]
    public LatestVersions Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    [JsonRequired]
    public List<MinecraftVersion> Versions { get; set; } = [];
}

public class LatestVersions
{
    [JsonPropertyName("release")]
    [JsonRequired]
    public string Release { get; set; } = string.Empty;

    [JsonPropertyName("snapshot")]
    [JsonRequired]
    public string Snapshot { get; set; } = string.Empty;
}

public class MinecraftVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTime ReleaseTime { get; set; }

    // --- 辅助属性 ---

    /// <summary>版本类型的中文显示名</summary>
    [JsonIgnore]
    public string TypeDisplay => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照版",
        "old_beta" => "经典 Beta",
        "old_alpha" => "经典 Alpha",
        _ => Type
    };

    /// <summary>版本类型对应的图标</summary>
    [JsonIgnore]
    public string TypeIcon => Type switch
    {
        "release" => "📦",
        "snapshot" => "🧪",
        "old_beta" => "🔶",
        "old_alpha" => "🔷",
        _ => "📄"
    };

    /// <summary>格式化后的发布日期</summary>
    [JsonIgnore]
    public string ReleaseDateDisplay => ReleaseTime.ToString("yyyy年M月");

    /// <summary>版本显示名</summary>
    [JsonIgnore]
    public string DisplayName => $"Minecraft {Id}";

    /// <summary>是否是最新正式版</summary>
    [JsonIgnore]
    public bool IsLatestRelease { get; set; }

    /// <summary>是否是最新快照</summary>
    [JsonIgnore]
    public bool IsLatestSnapshot { get; set; }
}
