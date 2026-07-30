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
/// 异步加载远程图片的 Image 控件（不阻塞 UI 线程）
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
        var url = e.NewValue as string;
        if (string.IsNullOrEmpty(url))
        {
            sender.Source = null;
            return;
        }

        // 缓存命中 → 直接设置
        if (ImageCache.TryGetValue(url, out var cached))
        {
            sender.Source = cached;
            return;
        }

        try
        {
            var data = await HttpClient.GetByteArrayAsync(url).ConfigureAwait(false);
            using var ms = new MemoryStream(data);
            var bitmap = await Task.Run(() => new Bitmap(ms)).ConfigureAwait(false);
            ImageCache[url] = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() => { sender.Source = bitmap; });
        }
        catch
        {
            ImageCache[url] = null;
            sender.Source = null;
        }
    }
}
