using System.Net.Http.Json;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 获取 Minecraft 版本清单的服务
/// </summary>
public static class ManifestGet
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    
    /// <summary>
    /// 获取Minecraft版本清单方法(Remake)
    /// </summary>
    /// <param name="url">自定义获取Minecraft版本的地址,默认情况不填时为mojang官方源</param>
    /// <returns>获取的Minecraft版本列表，按发布时间降序排列</returns>
    public static async Task<List<MinecraftVersion>> GetVersionsAsync(string url="https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
    {
        var manifest = await HttpClient.GetFromJsonAsync<VersionManifest>(url);

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
    /// <param name="url">自定义获取Minecraft版本的地址，默认为mojang官方源</param>
    /// </summary>
    public static async Task<List<MinecraftVersion>> GetVersionsByTypeAsync(string type, string url="https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
    {
        var versions = await GetVersionsAsync(url);
        return [.. versions.Where(v => v.Type == type)];
    }
}
