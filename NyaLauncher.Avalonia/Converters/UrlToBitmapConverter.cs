using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 异步加载远程或本地图片的 Image 控件（不阻塞 UI 线程）。
/// </summary>
public class AsyncImage : Image
{
    private static readonly ConcurrentDictionary<string, Bitmap?> ImageCache = new();
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<AsyncImage, string?>(nameof(SourceUrl));

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    static AsyncImage()
    {
        SourceUrlProperty.Changed.AddClassHandler<AsyncImage>(OnSourceUrlChanged);
    }

    private static async void OnSourceUrlChanged(AsyncImage sender, AvaloniaPropertyChangedEventArgs e)
    {
        var source = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(source))
        {
            sender.Source = null;
            return;
        }

        // 缓存命中 → 直接设置
        if (ImageCache.TryGetValue(source, out var cached))
        {
            sender.Source = cached;
            return;
        }

        try
        {
            var data = await LoadBytesAsync(source).ConfigureAwait(false);
            using var ms = new MemoryStream(data);
            var bitmap = await Task.Run(() => new Bitmap(ms)).ConfigureAwait(false);
            ImageCache[source] = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.Equals(sender.SourceUrl, source, StringComparison.Ordinal))
                    sender.Source = bitmap;
            });
        }
        catch
        {
            ImageCache[source] = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.Equals(sender.SourceUrl, source, StringComparison.Ordinal))
                    sender.Source = null;
            });
        }
    }

    private static Task<byte[]> LoadBytesAsync(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps)
        {
            return HttpClient.GetByteArrayAsync(uri);
        }

        return Task.Run(() =>
        {
            var path = Path.GetFullPath(source);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 8 * 1024 * 1024)
                throw new IOException("Local image is missing or exceeds 8 MiB.");
            return File.ReadAllBytes(path);
        });
    }
}
