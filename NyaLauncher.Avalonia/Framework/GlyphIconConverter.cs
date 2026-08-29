using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 字形 → 图标控件转换器（供 XAML 绑定使用）：
/// "material:Kind" 渲染为 MaterialIcon，其余回退为 TextBlock。
/// </summary>
public class GlyphIconConverter : IValueConverter
{
    /// <summary>
    /// 把字形字符串转成图标控件：固定字号 14，前景色留空（由控件自身继承主题前景）。
    /// </summary>
    /// <param name="value">字形字符串，形如 <c>material:Play</c>；其它值回退为普通文字。</param>
    /// <param name="targetType">目标类型（未使用）。</param>
    /// <param name="parameter">转换器参数（未使用）。</param>
    /// <param name="culture">区域信息（未使用）。</param>
    /// <returns>Material 图标或 <c>TextBlock</c>。</returns>
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        FeatureIconFactory.CreateGlyph(value as string, 14, (IBrush?)null);

    /// <summary>
    /// 不支持反向转换：图标控件无法还原成字形字符串。
    /// </summary>
    /// <param name="value">（未使用）。</param>
    /// <param name="targetType">（未使用）。</param>
    /// <param name="parameter">（未使用）。</param>
    /// <param name="culture">（未使用）。</param>
    /// <returns>恒为 <see cref="BindingOperations.DoNothing"/>。</returns>
    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        BindingOperations.DoNothing;
}
