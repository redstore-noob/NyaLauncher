using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Acquires bounded image bytes for polygon components and owns the shared
/// remote cache. Avalonia decoding, cropping and bitmap lifetime stay in the view.
/// </summary>
internal static class ComponentImageLoader
{
    private const int MaximumSourceLength = 4096;
    private const int MaximumImageBytes = 8 * 1024 * 1024;
    private const int MaximumCachedImageBytes = 1024 * 1024;
    private const int MaximumRemoteCacheEntries = 64;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly ConcurrentDictionary<string, Task<byte[]>> RemoteCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 把裁剪区域钳制在位图范围内：起点不小于 0、不超过右下角，
    /// 宽高不小于 1 且不超过位图剩余空间。Minecraft 皮肤组件与
    /// <see cref="AsyncImage"/> 的头像显示都复用这一实现。
    /// </summary>
    internal static PixelRect ClampCropRect(PixelRect crop, PixelSize pixelSize)
    {
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));

        var x = Math.Clamp(crop.X, 0, pixelSize.Width - 1);
        var y = Math.Clamp(crop.Y, 0, pixelSize.Height - 1);
        var width = Math.Clamp(crop.Width, 1, pixelSize.Width - x);
        var height = Math.Clamp(crop.Height, 1, pixelSize.Height - y);
        return new PixelRect(x, y, width, height);
    }

    internal static string SnapshotSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumSourceLength)
            return string.Empty;

        return source.Trim();
    }

    internal static async Task<byte[]> LoadBytesAsync(
        string source,
        CancellationToken cancellationToken)
    {
        const string pngDataPrefix = "data:image/png;base64,";
        if (source.StartsWith(pngDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var encoded = source[pngDataPrefix.Length..];
            if (encoded.Length == 0 || encoded.Length > MaximumSourceLength)
                throw new InvalidDataException("内嵌 PNG 图片为空或超出大小限制。");

            var decoded = Convert.FromBase64String(encoded);
            if (decoded.Length == 0 || decoded.Length > MaximumImageBytes)
                throw new InvalidDataException("内嵌 PNG 图片为空或超出大小限制。");

            return decoded;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            if (RemoteCache.Count >= MaximumRemoteCacheEntries &&
                !RemoteCache.ContainsKey(source))
            {
                return await DownloadBytesAsync(source).WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var load = RemoteCache.GetOrAdd(source, StartCachedDownload);
            // Cancelling one view must not cancel the shared cache population.
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!Path.IsPathFullyQualified(source))
            throw new InvalidDataException("图片来源必须是本地绝对路径或 HTTPS 地址。");

        var info = new FileInfo(source);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumImageBytes)
            throw new InvalidDataException("图片文件不存在、为空或超出大小限制。");

        return await File.ReadAllBytesAsync(info.FullName, cancellationToken).ConfigureAwait(false);
    }

    private static Task<byte[]> StartCachedDownload(string source)
    {
        var load = DownloadBytesAsync(source);
        _ = ObserveCachedDownloadAsync(source, load);
        return load;
    }

    private static async Task ObserveCachedDownloadAsync(
        string source,
        Task<byte[]> load)
    {
        try
        {
            var bytes = await load.ConfigureAwait(false);
            if (bytes.Length <= MaximumCachedImageBytes)
                return;
        }
        catch
        {
            // Failed and timed-out downloads must remain retriable.
        }

        RemoteCache.TryRemove(new KeyValuePair<string, Task<byte[]>>(source, load));
    }

    private static async Task<byte[]> DownloadBytesAsync(string source)
    {
        using var timeoutCancellation = new CancellationTokenSource(HttpClient.Timeout);
        var cancellationToken = timeoutCancellation.Token;
        using var response = await HttpClient.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumImageBytes)
            throw new InvalidDataException("远程图片超出大小限制。");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (output.Length + read > MaximumImageBytes)
                throw new InvalidDataException("远程图片超出大小限制。");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
