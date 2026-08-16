using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 异步加载远程或本地图片的 Image 控件（不阻塞 UI 线程）。
/// 字节获取与裁剪钳制复用 <see cref="ComponentImageLoader"/> 的现有实现，
/// 头像显示与多边形组件的皮肤贴图走同一套逻辑。
/// </summary>
public class AsyncImage : Image
{
    private static readonly ConcurrentDictionary<string, Bitmap?> ImageCache = new();

    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<AsyncImage, string?>(nameof(SourceUrl));

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    /// <summary>
    /// 可选裁剪区域（像素坐标）。设置后只显示原图中的这一块，
    /// 例如 Minecraft 皮肤里的脸部 8×8 像素：8,8,8,8。
    /// </summary>
    public static readonly StyledProperty<PixelRect?> CropRectProperty =
        AvaloniaProperty.Register<AsyncImage, PixelRect?>(nameof(CropRect));

    public PixelRect? CropRect
    {
        get => GetValue(CropRectProperty);
        set => SetValue(CropRectProperty, value);
    }

    static AsyncImage()
    {
        SourceUrlProperty.Changed.AddClassHandler<AsyncImage>(OnSourceUrlChanged);
        CropRectProperty.Changed.AddClassHandler<AsyncImage>(OnCropRectChanged);
    }

    private static void OnCropRectChanged(AsyncImage sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 裁剪区域变化时，直接用缓存位图重设 Source，无需重新加载
        var source = sender.SourceUrl;
        if (string.IsNullOrWhiteSpace(source))
            return;
        if (ImageCache.TryGetValue(source, out var cached))
            sender.SetSource(cached);
    }

    private static async void OnSourceUrlChanged(AsyncImage sender, AvaloniaPropertyChangedEventArgs e)
    {
        var source = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(source))
        {
            sender.SetSource(null);
            return;
        }

        // 缓存命中 → 直接设置
        if (ImageCache.TryGetValue(source, out var cached))
        {
            sender.SetSource(cached);
            return;
        }

        try
        {
            // 复用 ComponentImageLoader：支持 HTTPS、本地文件与 data:image/png 三种来源
            var data = await ComponentImageLoader.LoadBytesAsync(source, CancellationToken.None)
                .ConfigureAwait(false);
            using var ms = new MemoryStream(data);
            var bitmap = await Task.Run(() => new Bitmap(ms)).ConfigureAwait(false);
            ImageCache[source] = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.Equals(sender.SourceUrl, source, StringComparison.Ordinal))
                    sender.SetSource(bitmap);
            });
        }
        catch
        {
            ImageCache[source] = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.Equals(sender.SourceUrl, source, StringComparison.Ordinal))
                    sender.SetSource(null);
            });
        }
    }

    /// <summary>
    /// 应用位图，必要时用 <see cref="CroppedBitmap"/> 裁剪到 <see cref="CropRect"/>。
    /// 裁剪区域先经 <see cref="ComponentImageLoader.ClampCropRect"/> 钳制到位图范围内。
    /// </summary>
    private void SetSource(Bitmap? bitmap)
    {
        if (bitmap is null || CropRect is not { } crop)
        {
            Source = bitmap;
            return;
        }

        try
        {
            var clamped = ComponentImageLoader.ClampCropRect(crop, bitmap.PixelSize);
            Source = new CroppedBitmap(bitmap, clamped);
        }
        catch (ArgumentException)
        {
            // 裁剪失败时回退为整图，避免显示异常
            Source = bitmap;
        }
    }
}
