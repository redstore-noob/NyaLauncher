using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Minecraft 皮肤双层头像合成：脸层（纹理左上 (1/8,1/8)）铺底，
/// 帽层（纹理 (5/8,1/8)）以透明像素叠加其上，显示带帽子/头发的完整头像。
/// 兼容 64×64 经典 / 128×128 高清皮肤；合成尺寸为纹理宽度的 8 倍，
/// 放大插值由宿主控件的 RenderOptions 控制（设为 None 保持清晰像素风）。
/// </summary>
public static class SkinHeadComposer
{
    public static IImage? Compose(Bitmap skin)
    {
        var head = skin.PixelSize.Width / 8;
        if (head <= 0)
            return null;

        var avatar = head * 8;
        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing
        {
            ImageSource = new CroppedBitmap(skin, new PixelRect(head, head, head, head)),
            Rect = new Rect(0, 0, avatar, avatar)
        });
        group.Children.Add(new ImageDrawing
        {
            ImageSource = new CroppedBitmap(skin, new PixelRect(head * 5, head, head, head)),
            Rect = new Rect(0, 0, avatar, avatar)
        });
        return new DrawingImage(group);
    }
}
