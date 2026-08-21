using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 获取 Minecraft 版本清单的服务。通过 <see cref="DownloadSourceProvider"/> 自动选择下载源。
/// </summary>
public static class ManifestGet
{
    /// <summary>
    /// 获取 Minecraft 版本清单（使用当前活跃下载源，失败自动回退）。
    /// </summary>
    public static async Task<List<MinecraftVersion>> GetVersionsAsync()
    {
        var json = await DownloadSourceProvider.GetStringAsync(
                DownloadSources.Official.LauncherMeta)
            .ConfigureAwait(false);

        var manifest = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json);
        if (manifest is null)
            throw new System.Text.Json.JsonException("版本清单响应为空。");
        if (manifest.Latest is null)
            throw new System.Text.Json.JsonException("版本清单缺少 latest 节点。");
        if (manifest.Versions is null)
            throw new System.Text.Json.JsonException("版本清单缺少 versions 节点。");

        var versions = manifest.Versions.OfType<MinecraftVersion>().ToList();
        if (versions.Count == 0)
            return [];

        foreach (var version in versions)
        {
            if (version.Id == manifest.Latest.Release)
                version.IsLatestRelease = true;
            if (version.Id == manifest.Latest.Snapshot)
                version.IsLatestSnapshot = true;
        }

        return [.. versions.OrderByDescending(version => version.ReleaseTime)];
    }
}
