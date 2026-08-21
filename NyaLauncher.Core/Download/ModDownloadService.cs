namespace NyaLauncher.Core.Download;

/// <summary>
/// Mod 文件下载服务。从 Modrinth 下载 mod JAR 到用户选择的路径。
/// </summary>
public static class ModDownloadService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    static ModDownloadService()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("NyaLauncher/1.0");
    }

    /// <summary>
    /// 下载 Mod 文件到指定路径。
    /// </summary>
    /// <param name="downloadUrl">Mod 文件的下载 URL。</param>
    /// <param name="targetPath">保存的完整文件路径。</param>
    /// <param name="progress">进度回调（已下载字节数，总字节数）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task DownloadAsync(
        string downloadUrl,
        string targetPath,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var response = await Client.GetAsync(
                downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            128 * 1024, useAsync: true);

        var buffer = new byte[128 * 1024];
        long downloaded = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            downloaded += read;
            progress?.Report((downloaded, totalBytes));
        }
    }
}
