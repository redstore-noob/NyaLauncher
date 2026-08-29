namespace NyaLauncher.Core.Download;

/// <summary>
/// Mod 文件下载服务。从 Modrinth CDN 下载 mod JAR / mrpack / 资源包等到指定路径。
/// 内置断点续传与自动重试：网络抖动中断后按 HTTP Range 从临时文件断点继续，
/// 最多重试 3 次；全部失败才向调用方抛出异常。
/// </summary>
public static class ModDownloadService
{
    /// <summary>单次尝试的最大时长（含读取流）；超时视为瞬时失败，重试时断点续传。</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(10);

    /// <summary>瞬时失败后的总尝试次数（首次 + 重试）。</summary>
    private const int MaxAttempts = 4;

    private static readonly HttpClient Client = new()
    {
        Timeout = AttemptTimeout
    };

    static ModDownloadService()
    {
        // 部分 CDN（Cloudflare 等）会拒绝缺失 User-Agent 的请求
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("NyaLauncher/1.0");
    }

    /// <summary>
    /// 下载文件到指定路径。
    /// 先写入临时文件（&lt;目标路径&gt;.nya-download），全部完成后再原子移动到目标路径；
    /// 瞬时网络失败自动断点续传重试；最终失败或用户取消时清理临时文件。
    /// </summary>
    /// <param name="downloadUrl">文件下载 URL。</param>
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

        var temporaryPath = $"{targetPath}.nya-download";
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadAttemptAsync(downloadUrl, temporaryPath, progress, cancellationToken)
                        .ConfigureAwait(false);

                    // 下载成功后才替换目标文件（原子移动）
                    File.Move(temporaryPath, targetPath, overwrite: true);
                    return;
                }
                catch (Exception exception) when (
                    attempt < MaxAttempts &&
                    IsTransientFailure(exception, cancellationToken))
                {
                    // 网络抖动 / 超时：退避后从断点续传重试（临时文件保留）
                    await Task.Delay(
                            TimeSpan.FromSeconds(Math.Min(attempt * 2, 6)),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    /// <summary>单次下载尝试：若临时文件已有部分内容则用 Range 断点续传。</summary>
    private static async Task DownloadAttemptAsync(
        string downloadUrl,
        string temporaryPath,
        IProgress<(long downloaded, long total)>? progress,
        CancellationToken cancellationToken)
    {
        var resumeFrom = 0L;
        if (File.Exists(temporaryPath))
            resumeFrom = new FileInfo(temporaryPath).Length;

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (resumeFrom > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);

        using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // 416：本地临时文件已完整（可能上次成功但移动前被打断），直接视为完成
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            return;

        response.EnsureSuccessStatusCode();

        long totalBytes;
        long downloadedBase;
        FileStream destination;
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
        {
            // 断点续传：Content-Range 携带完整长度
            totalBytes = response.Content.Headers.ContentRange?.Length ?? -1;
            downloadedBase = resumeFrom;
            destination = new FileStream(
                temporaryPath, FileMode.Append, FileAccess.Write, FileShare.None,
                128 * 1024, useAsync: true);
        }
        else
        {
            // 服务器不支持 Range（返回 200 全量）：从头覆盖
            totalBytes = response.Content.Headers.ContentLength ?? -1;
            downloadedBase = 0;
            destination = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, useAsync: true);
        }

        await using (destination)
        {
            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var buffer = new byte[128 * 1024];
            var downloaded = downloadedBase;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                downloaded += read;
                progress?.Report((downloaded, totalBytes));
            }
        }
        // 中断时由 FileStream 的 Dispose 自动冲刷已写入部分，
        // 保证临时文件始终是有效前缀，重试可从断点继续。
    }

    /// <summary>
    /// 判断异常是否为可重试的瞬时网络失败。
    /// 用户主动取消（外部令牌已触发）不算瞬时失败，直接抛出让上层显示"已取消"。
    /// </summary>
    private static bool IsTransientFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;
        return exception is HttpRequestException
            or IOException
            or System.Net.Sockets.SocketException
            or TaskCanceledException; // TaskCanceled 且外部令牌未触发 = HttpClient 超时
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 清理失败不影响主流程，残留的 .nya-download 文件可由下次下载覆盖。
        }
    }
}
