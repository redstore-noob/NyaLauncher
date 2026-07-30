using System.Net.Http.Json;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 获取 Minecraft 版本清单的服务
/// </summary>
public static class ManifestGet
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// 获取所有 Minecraft 版本列表（按发布时间降序排列）
    /// </summary>
    public static async Task<List<MinecraftVersion>> GetVersionsAsync()
    {
        var manifest = await HttpClient.GetFromJsonAsync<VersionManifest>(ManifestUrl);

        if (manifest?.Versions is null || manifest.Versions.Count == 0)
            return [];

        // 标记最新版本
        foreach (var version in manifest.Versions)
        {
            if (version.Id == manifest.Latest.Release)
                version.IsLatestRelease = true;
            if (version.Id == manifest.Latest.Snapshot)
                version.IsLatestSnapshot = true;
        }

        // 按发布时间降序排列
        return [.. manifest.Versions.OrderByDescending(v => v.ReleaseTime)];
    }

    /// <summary>
    /// 获取指定类型的版本列表
    /// </summary>
    public static async Task<List<MinecraftVersion>> GetVersionsByTypeAsync(string type)
    {
        var versions = await GetVersionsAsync();
        return [.. versions.Where(v => v.Type == type)];
    }
}
