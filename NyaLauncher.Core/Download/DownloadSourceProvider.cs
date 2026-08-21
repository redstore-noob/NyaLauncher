namespace NyaLauncher.Core.Download;

/// <summary>
/// 一个完整的下载源定义，包含所有需要替换的基础 URL。
/// </summary>
public sealed record DownloadSource
{
    /// <summary>显示名称，如 "Official" 或 "BMCL"。</summary>
    public required string Name { get; init; }

    /// <summary>版本清单完整 URL（version_manifest_v2.json）。</summary>
    public required string LauncherMeta { get; init; }

    /// <summary>piston-meta / launchermeta 基础域名（无尾部斜杠）。</summary>
    public required string Meta { get; init; }

    /// <summary>libraries 基础域名（无尾部斜杠）。</summary>
    public required string Libraries { get; init; }

    /// <summary>资源文件基础域名（无尾部斜杠）。</summary>
    public required string Resources { get; init; }

    /// <summary>Maven 仓库基础域名（用于 Forge/NeoForge 的 Maven 坐标解析，无尾部斜杠）。</summary>
    public required string Maven { get; init; }
}

/// <summary>
/// 内置下载源定义。
/// </summary>
public static class DownloadSources
{
    public static DownloadSource Official { get; } = new()
    {
        Name = "Official",
        LauncherMeta = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
        Meta = "https://piston-meta.mojang.com",
        Libraries = "https://libraries.minecraft.net",
        Resources = "https://resources.download.minecraft.net",
        Maven = "https://libraries.minecraft.net"
    };

    public static DownloadSource Bmcl { get; } = new()
    {
        Name = "BMCL",
        LauncherMeta = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
        Meta = "https://bmclapi2.bangbang93.com",
        Libraries = "https://bmclapi2.bangbang93.com/maven",
        Resources = "https://bmclapi2.bangbang93.com/assets",
        Maven = "https://bmclapi2.bangbang93.com/maven"
    };

    /// <summary>所有可用下载源。</summary>
    public static IReadOnlyList<DownloadSource> All { get; } = [Official, Bmcl];
}

/// <summary>
/// 下载源提供器。Core 侧的全局单例，前端通过 <see cref="Active"/> 和
/// <see cref="Fallback"/> 控制源选择，所有下载逻辑通过本类获取 URL。
/// </summary>
public static class DownloadSourceProvider
{
    /// <summary>
    /// 当前活跃下载源。默认 Official。
    /// 前端可在启动时或设置页切换。
    /// </summary>
    public static DownloadSource Active { get; set; } = DownloadSources.Official;

    /// <summary>
    /// 自动回退源。当 <see cref="Active"/> 请求失败时自动尝试。
    /// 设为 null 则不回退。默认 BMCL。
    /// </summary>
    public static DownloadSource? Fallback { get; set; } = DownloadSources.Bmcl;

    // ------------------------------------------------------------------
    // URL 替换
    // ------------------------------------------------------------------

    /// <summary>
    /// 将 Official 源的 URL 替换为 <see cref="Active"/> 源对应地址。
    /// 如果 Active 就是 Official 则原样返回。
    /// </summary>
    public static string Resolve(string officialUrl)
    {
        if (string.IsNullOrWhiteSpace(officialUrl) || Active == DownloadSources.Official)
            return officialUrl;

        return ReplaceBaseUrl(officialUrl, Active);
    }

    /// <summary>
    /// 获取 Fallback 源对应的 URL。无 Fallback 时返回 null。
    /// </summary>
    public static string? ResolveFallback(string officialUrl)
    {
        if (Fallback is null || string.IsNullOrWhiteSpace(officialUrl))
            return null;
        return ReplaceBaseUrl(officialUrl, Fallback);
    }

    // ------------------------------------------------------------------
    // 带回退的 HTTP 请求
    // ------------------------------------------------------------------

    /// <summary>
    /// GET 请求，自动应用 Active 源；失败时回退到 Fallback 源。
    /// </summary>
    public static async Task<string> GetStringAsync(
        string officialUrl,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var primaryUrl = Resolve(officialUrl);
        var fallbackUrl = ResolveFallback(officialUrl);

        using var client = CreateClient(timeout);

        try
        {
            return await client.GetStringAsync(primaryUrl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && fallbackUrl is not null)
        {
            return await client.GetStringAsync(fallbackUrl, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// GET 请求（字节），自动应用 Active 源；失败时回退到 Fallback 源。
    /// </summary>
    public static async Task<byte[]> GetBytesAsync(
        string officialUrl,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var primaryUrl = Resolve(officialUrl);
        var fallbackUrl = ResolveFallback(officialUrl);

        using var client = CreateClient(timeout);

        try
        {
            return await client.GetByteArrayAsync(primaryUrl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && fallbackUrl is not null)
        {
            return await client.GetByteArrayAsync(fallbackUrl, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static HttpClient CreateClient(TimeSpan? timeout)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NyaLauncher/1.0");
        return client;
    }

    /// <summary>
    /// 将一个指向 Official 各域名的 URL 替换为目标源对应域名。
    /// 识别的域名：piston-meta.mojang.com, piston-data.mojang.com,
    ///   launchermeta.mojang.com, libraries.minecraft.net,
    ///   resources.download.minecraft.net, maven.minecraftforge.net,
    ///   maven.neoforged.net。
    /// </summary>
    private static string ReplaceBaseUrl(string url, DownloadSource target)
    {
        // 先处理有特殊路径映射的域名
        if (url.Contains("libraries.minecraft.net", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://libraries.minecraft.net", target.Libraries, StringComparison.OrdinalIgnoreCase);

        if (url.Contains("resources.download.minecraft.net", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://resources.download.minecraft.net", target.Resources, StringComparison.OrdinalIgnoreCase);

        // Forge Maven
        if (url.Contains("maven.minecraftforge.net", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://maven.minecraftforge.net", target.Maven, StringComparison.OrdinalIgnoreCase);

        // NeoForge Maven
        if (url.Contains("maven.neoforged.net", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://maven.neoforged.net", target.Maven, StringComparison.OrdinalIgnoreCase);

        // 通用 Meta 域名
        if (url.Contains("piston-meta.mojang.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://piston-meta.mojang.com", target.Meta, StringComparison.OrdinalIgnoreCase);

        if (url.Contains("piston-data.mojang.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://piston-data.mojang.com", target.Meta, StringComparison.OrdinalIgnoreCase);

        if (url.Contains("launchermeta.mojang.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://launchermeta.mojang.com", target.Meta, StringComparison.OrdinalIgnoreCase);

        return url;
    }
}
