using System.Net.Http.Json;
using System.Text.Json;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 获取 Minecraft 版本清单的服务
/// </summary>
public static class ManifestGet
{
    private const string DefaultManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// 从 Mojang 官方源获取 Minecraft 版本清单。
    /// </summary>
    /// <returns>获取的Minecraft版本列表，按发布时间降序排列</returns>
    public static Task<List<MinecraftVersion>> GetVersionsAsync() =>
        GetVersionsAsync(DefaultManifestUrl);

    /// <summary>
    /// 从自定义地址获取 Minecraft 版本清单。
    /// </summary>
    /// <param name="url">版本清单的绝对地址。</param>
    /// <returns>获取的 Minecraft 版本列表，按发布时间降序排列。</returns>
    public static async Task<List<MinecraftVersion>> GetVersionsAsync(
        string url = DefaultManifestUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var manifest = await HttpClient.GetFromJsonAsync<VersionManifest>(url);
        if (manifest is null)
            throw new JsonException("版本清单响应为空。");
        if (manifest.Latest is null)
            throw new JsonException("版本清单缺少 latest 节点。");
        if (manifest.Versions is null)
            throw new JsonException("版本清单缺少 versions 节点。");

        var versions = manifest.Versions.OfType<MinecraftVersion>().ToList();
        if (versions.Count == 0)
            return [];

        // 标记最新版本
        foreach (var version in versions)
        {
            if (version.Id == manifest.Latest.Release)
                version.IsLatestRelease = true;
            if (version.Id == manifest.Latest.Snapshot)
                version.IsLatestSnapshot = true;
        }

        // 按发布时间降序排列
        return [.. versions.OrderByDescending(version => version.ReleaseTime)];
    }
}
