using Avalonia;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 遮罩层主题画笔查找的统一实现：从当前主题资源中取画刷，
/// 兼容值为 <see cref="Color"/> 的主题键（自动包装为 <see cref="SolidColorBrush"/>）。
/// </summary>
internal static class OverlayTheme
{
    public static IBrush FindBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, null, out var value) == true)
        {
            if (value is IBrush brush)
                return brush;
            if (value is Color color)
                return new SolidColorBrush(color);
        }
        return Brushes.Gray;
    }
}
