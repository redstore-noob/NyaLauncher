using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 下载设置的持久化入口。通过 <see cref="LauncherConfig"/> 读写。
/// </summary>
public static class DownloadSettings
{
    /// <summary>默认并行下载线程数。</summary>
    public const int DefaultParallelDownloads = 8;

    /// <summary>最小并行下载线程数。</summary>
    public const int MinParallelDownloads = 1;

    /// <summary>最大并行下载线程数。</summary>
    public const int MaxParallelDownloads = 32;

    /// <summary>
    /// 并行下载线程数（同时下载的文件块数）。
    /// </summary>
    public static int ParallelDownloads
    {
        get
        {
            var value = LauncherConfig.GetValue("downloadParallelDownloads");
            return int.TryParse(value, out var result) &&
                   result >= MinParallelDownloads &&
                   result <= MaxParallelDownloads
                ? result
                : DefaultParallelDownloads;
        }
    }

    /// <summary>保存并行下载线程数。</summary>
    public static void SaveParallelDownloads(int count)
    {
        var clamped = Math.Clamp(count, MinParallelDownloads, MaxParallelDownloads);
        LauncherConfig.SetValue("downloadParallelDownloads", clamped.ToString());
    }

    /// <summary>
    /// 当前活跃下载源名称。启动时从配置恢复 <see cref="DownloadSourceProvider.Active"/>。
    /// </summary>
    public static string ActiveSourceName
    {
        get => LauncherConfig.GetValue("downloadActiveSource") ?? DownloadSources.Official.Name;
    }

    /// <summary>保存活跃下载源。</summary>
    public static void SaveActiveSource(DownloadSource source)
    {
        LauncherConfig.SetValue("downloadActiveSource", source.Name);
        DownloadSourceProvider.Active = source;
    }

    /// <summary>
    /// 自动回退源名称。首次启动默认 BMCL；用户显式禁用时返回 null。
    /// </summary>
    public static string? FallbackSourceName
    {
        get
        {
            var value = LauncherConfig.GetValue("downloadFallbackSource");
            // null = 从未设置过 → 使用默认值 BMCL
            // "disabled" = 用户显式禁用 → 无回退
            // 其他非空 = 用户选择的源名称
            if (value is null)
                return DownloadSources.Bmcl.Name;
            if (string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
                return null;
            return value;
        }
    }

    /// <summary>保存自动回退源。传 null 禁用回退。</summary>
    public static void SaveFallbackSource(DownloadSource? source)
    {
        // "disabled" = 用户显式禁用回退
        // "BMCL" 等 = 用户选择的源
        // 不存在 = 首次启动默认 BMCL
        LauncherConfig.SetValue("downloadFallbackSource",
            source is null ? "disabled" : source.Name);
        DownloadSourceProvider.Fallback = source;
    }

    /// <summary>
    /// 应用已保存的设置到 <see cref="DownloadSourceProvider"/>。
    /// 应在启动时调用一次。
    /// </summary>
    public static void ApplySavedSettings()
    {
        var activeName = ActiveSourceName;
        DownloadSourceProvider.Active = DownloadSources.All
            .FirstOrDefault(s => string.Equals(s.Name, activeName, StringComparison.OrdinalIgnoreCase))
            ?? DownloadSources.Official;

        var fallbackName = FallbackSourceName;
        if (string.IsNullOrWhiteSpace(fallbackName))
        {
            DownloadSourceProvider.Fallback = null;
        }
        else
        {
            DownloadSourceProvider.Fallback = DownloadSources.All
                .FirstOrDefault(s => string.Equals(s.Name, fallbackName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
