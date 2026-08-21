using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Download;

public sealed record DownloadSource(string Name, string LauncherMeta, string? MirrorRoot = null)
{
    internal string Resolve(string url)
    {
        if (MirrorRoot is null)
            return url;

        var root = MirrorRoot.TrimEnd('/');
        return url switch
        {
            var value when value.StartsWith(
                "https://piston-meta.mojang.com/",
                StringComparison.OrdinalIgnoreCase) =>
                $"{root}/{value["https://piston-meta.mojang.com/".Length..]}",
            var value when value.StartsWith(
                "https://launchermeta.mojang.com/",
                StringComparison.OrdinalIgnoreCase) =>
                $"{root}/{value["https://launchermeta.mojang.com/".Length..]}",
            var value when value.StartsWith(
                "https://launcher.mojang.com/",
                StringComparison.OrdinalIgnoreCase) =>
                $"{root}/{value["https://launcher.mojang.com/".Length..]}",
            var value when value.StartsWith(
                "https://libraries.minecraft.net/",
                StringComparison.OrdinalIgnoreCase) =>
                $"{root}/maven/{value["https://libraries.minecraft.net/".Length..]}",
            var value when value.StartsWith(
                "https://resources.download.minecraft.net/",
                StringComparison.OrdinalIgnoreCase) =>
                $"{root}/assets/{value["https://resources.download.minecraft.net/".Length..]}",
            _ => url
        };
    }
}

public static class DownloadSources
{
    public static DownloadSource Official { get; } = new(
        "Mojang 官方源",
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    public static DownloadSource BmclApi { get; } = new(
        "BMCLAPI 镜像",
        Official.LauncherMeta,
        "https://bmclapi2.bangbang93.com");

    public static IReadOnlyList<DownloadSource> All { get; } =
        [Official, BmclApi];
}

public static class DownloadSourceProvider
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static DownloadSource _active = DownloadSources.Official;

    public static DownloadSource Active => Volatile.Read(ref _active);

    public static DownloadSource? Fallback =>
        ReferenceEquals(Active, DownloadSources.Official)
            ? DownloadSources.BmclApi
            : DownloadSources.Official;

    public static void SetActive(DownloadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var canonical = DownloadSources.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, source.Name, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException("未知下载源。", nameof(source));
        Volatile.Write(ref _active, canonical);
    }

    public static string Resolve(string url)
    {
        ValidateHttpsUrl(url);
        return Active.Resolve(url);
    }

    public static string? ResolveFallback(string url)
    {
        ValidateHttpsUrl(url);
        return Fallback?.Resolve(url);
    }

    public static async Task<string> GetStringAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var primary = Resolve(url);
        try
        {
            return await HttpClient.GetStringAsync(primary, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            var fallback = ResolveFallback(url);
            if (fallback is null || string.Equals(primary, fallback, StringComparison.OrdinalIgnoreCase))
                throw;
            return await HttpClient.GetStringAsync(fallback, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void ValidateHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"下载地址必须使用 HTTPS：{url}");
        }
    }
}

public static class DownloadSettings
{
    private const string SourceKey = "downloadSource";
    private const string ParallelDownloadsKey = "parallelDownloads";
    private const int DefaultParallelDownloads = 8;
    private static int _parallelDownloads = DefaultParallelDownloads;

    public static int ParallelDownloads => Volatile.Read(ref _parallelDownloads);

    public static void ApplySavedSettings()
    {
        var sourceName = LauncherConfig.GetValue(SourceKey);
        var source = DownloadSources.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        DownloadSourceProvider.SetActive(source ?? DownloadSources.Official);

        var configuredParallelism = LauncherConfig.GetValue(ParallelDownloadsKey);
        Volatile.Write(
            ref _parallelDownloads,
            int.TryParse(configuredParallelism, out var value)
                ? Math.Clamp(value, 1, 32)
                : DefaultParallelDownloads);
    }

    public static void SaveActiveSource(DownloadSource source)
    {
        DownloadSourceProvider.SetActive(source);
        LauncherConfig.SetValue(SourceKey, DownloadSourceProvider.Active.Name);
    }

    public static void SaveParallelDownloads(int value)
    {
        var normalized = Math.Clamp(value, 1, 32);
        Volatile.Write(ref _parallelDownloads, normalized);
        LauncherConfig.SetValue(ParallelDownloadsKey, normalized.ToString());
    }
}
