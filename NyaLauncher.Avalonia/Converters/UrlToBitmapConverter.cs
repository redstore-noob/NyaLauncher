using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 异步加载远程或本地图片的 Image 控件（不阻塞 UI 线程）。
/// 字节获取与裁剪钳制复用 <see cref="ComponentImageLoader"/> 的现有实现
/// （该加载器自带进程级字节缓存，安全无共享位图）。
/// <para>
/// 位图生命周期由控件自身管理：每个 AsyncImage 只持有自己创建的 Bitmap，
/// 换图或控件脱离视觉树时释放，杜绝"共享 Bitmap 被缓存淘汰 Dispose 后
/// 仍在渲染"导致的 ObjectDisposedException 崩溃。
/// </para>
/// </summary>
public class AsyncImage : Image
{
    private Bitmap? _ownedBitmap;
    private int _loadVersion;

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

    /// <summary>
    /// 是否为 Minecraft 皮肤贴图：自动合成为双层头像（脸层 + 帽层叠加），
    /// 兼容 64×64 经典 / 128×128 高清皮肤。
    /// </summary>
    public static readonly StyledProperty<bool> IsSkinHeadProperty =
        AvaloniaProperty.Register<AsyncImage, bool>(nameof(IsSkinHead));

    public bool IsSkinHead
    {
        get => GetValue(IsSkinHeadProperty);
        set => SetValue(IsSkinHeadProperty, value);
    }

    static AsyncImage()
    {
        SourceUrlProperty.Changed.AddClassHandler<AsyncImage>(OnSourceUrlChanged);
        CropRectProperty.Changed.AddClassHandler<AsyncImage>(OnCropRectChanged);
        IsSkinHeadProperty.Changed.AddClassHandler<AsyncImage>(OnCropRectChanged);
    }

    private static void OnCropRectChanged(AsyncImage sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 裁剪区域变化时，用已加载的位图重新设置（重建 CroppedBitmap）
        if (sender._ownedBitmap is { } owned)
            sender.SetSource(owned);
    }

    private static void OnSourceUrlChanged(AsyncImage sender, AvaloniaPropertyChangedEventArgs e)
    {
        var source = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(source))
        {
            sender._loadVersion++;
            sender.SetSource(null);
            return;
        }

        sender.StartLoad(source);
    }

    private void StartLoad(string source)
    {
        var version = ++_loadVersion;
        _ = LoadAsync(source, version);
    }

    private async Task LoadAsync(string source, int version)
    {
        try
        {
            // 复用 ComponentImageLoader：支持 HTTPS、本地文件与 data:image/png 三种来源，
            // 并自带进程级字节缓存（安全：字节不可变，无共享位图问题）。
            var data = await ComponentImageLoader.LoadBytesAsync(source, CancellationToken.None)
                .ConfigureAwait(false);
            using var ms = new MemoryStream(data);
            var bitmap = await Task.Run(() => new Bitmap(ms)).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _loadVersion)
                {
                    // 加载期间 URL 已变更或控件已卸载：结果过期，直接释放避免泄漏
                    bitmap.Dispose();
                    return;
                }

                // 位图所有权移交给控件（SetSource 内部会释放旧图）
                SetSource(bitmap);
            });
        }
        catch
        {
            // 失败不缓存：网络恢复后重新加载仍有机会成功
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == _loadVersion)
                    SetSource(null);
            });
        }
    }

    /// <summary>
    /// 应用位图并接管其生命周期；必要时用 <see cref="CroppedBitmap"/> 裁剪到 <see cref="CropRect"/>。
    /// 先切换 <see cref="Source"/> 再释放旧位图，避免渲染线程访问已销毁对象。
    /// </summary>
    private void SetSource(Bitmap? bitmap)
    {
        var previous = _ownedBitmap;
        _ownedBitmap = null;

        if (bitmap is null)
        {
            Source = null;
        }
        else if (IsSkinHead)
        {
            // 双层皮肤头像：脸层 + 帽层叠加合成
            Source = SkinHeadComposer.Compose(bitmap) is { } composed ? composed : bitmap;
        }
        else if (CropRect is { } crop)
        {
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
        else
        {
            Source = bitmap;
        }

        // Source 已指向新图，旧位图不再被渲染，可安全释放
        if (previous is not null && !ReferenceEquals(previous, bitmap))
            previous.Dispose();
        _ownedBitmap = bitmap;
    }

    /// <summary>释放本控件持有的位图资源。</summary>
    private void ReleaseOwnedBitmap()
    {
        var owned = _ownedBitmap;
        _ownedBitmap = null;
        if (owned is null)
            return;

        // 先切断渲染引用，再释放底层位图
        Source = null;
        owned.Dispose();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loadVersion++; // 使在途加载结果失效，避免卸载后重新持有位图
        ReleaseOwnedBitmap();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 弹窗重开 / 页面重新挂载时，若位图已被释放则重新加载
        if (_ownedBitmap is null && !string.IsNullOrWhiteSpace(SourceUrl))
            StartLoad(SourceUrl);
    }
}
