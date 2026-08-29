using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Material.Icons;
using Material.Icons.Avalonia;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 功能区与组件图标工厂：把「字形字符串」或「本地图片路径」变成 Avalonia 控件。
/// <para>
/// 取值优先级：本地图片（存在且能解码）→ Material 图标（形如 <c>material:Play</c>）
/// → 普通文字（Emoji 或用户自定义字形）。本地图片失效时自动回退到字形，
/// 因此用户换过图标后即使图片被删掉，界面也不会开天窗。
/// </para>
/// </summary>
public static class FeatureIconFactory
{
    /// <summary>Material 图标字形前缀：字形字符串形如 <c>material:Play</c> 时渲染为 MaterialIcon。</summary>
    public const string MaterialPrefix = "material:";

    /// <summary>默认占位字形（Material 图标）。字形为空或解析失败时使用。</summary>
    public const string DefaultGlyph = "material:Apps";

    /// <summary>
    /// 创建图标控件：优先使用本地图片，否则按 <paramref name="glyph"/> 生成字形控件。
    /// </summary>
    /// <param name="glyph">字形字符串；为空时回退到 <see cref="DefaultGlyph"/>。</param>
    /// <param name="iconPath">本地图片绝对路径；文件不存在或解码失败时回退到字形。</param>
    /// <param name="fontSize">字号；Material 图标会按「字号 + 2」设置控件宽高。</param>
    /// <returns><c>Image</c>、<c>MaterialIcon</c> 或 <c>TextBlock</c>。</returns>
    public static Control Create(
        string glyph,
        string? iconPath,
        double fontSize = 18)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            try
            {
                return new Image
                {
                    Source = new Bitmap(iconPath),
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
            }
            catch
            {
                // 图片被移动、被占用或格式非法时，回退到该区域预设的字形图标
            }
        }

        return CreateGlyph(glyph, fontSize, ThemePolygonHelper.AccentGlyph);
    }

    /// <summary>
    /// 按字形字符串创建图标控件：<c>material:Kind</c> 渲染为 MaterialIcon（宽高 ≈ 字号），
    /// 其余字符串回退为 TextBlock（兼容旧 emoji 字形与用户自定义字形）。
    /// </summary>
    /// <param name="glyph">字形字符串；为空时回退到 <see cref="DefaultGlyph"/>。</param>
    /// <param name="fontSize">字号；Material 图标按「字号 + 2」设置宽高。</param>
    /// <param name="foreground">前景画刷；为 <c>null</c> 时使用主题强调字形色。</param>
    /// <param name="bold">文字回退时是否加粗。</param>
    /// <returns><c>MaterialIcon</c> 或 <c>TextBlock</c>。</returns>
    public static Control CreateGlyph(
        string? glyph,
        double fontSize,
        IBrush? foreground = null,
        bool bold = true)
    {
        if (string.IsNullOrWhiteSpace(glyph))
            glyph = DefaultGlyph; // 空字形回退到默认 Material 图标

        if (TryParseMaterialKind(glyph, out var kind))
        {
            var size = fontSize + 2;
            return new MaterialIcon
            {
                Kind = kind,
                Width = size,
                Height = size,
                Foreground = foreground ?? ThemePolygonHelper.AccentGlyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = glyph,
            FontSize = fontSize,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Foreground = foreground ?? ThemePolygonHelper.AccentGlyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>
    /// 资源键重载：先按字形创建控件，再把前景色绑定到主题资源键（<c>DynamicResource</c>），
    /// 主题热重载时颜色实时跟随。
    /// </summary>
    /// <param name="glyph">字形字符串；为空时回退到 <see cref="DefaultGlyph"/>。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="foregroundResourceKey">主题资源键，例如 <c>AccentBrush</c>。</param>
    /// <param name="bold">文字回退时是否加粗。</param>
    /// <returns>前景色已绑定到主题资源的图标控件。</returns>
    public static Control CreateGlyph(
        string? glyph,
        double fontSize,
        string foregroundResourceKey,
        bool bold = true)
    {
        var control = CreateGlyph(glyph, fontSize, (IBrush?)null, bold);
        switch (control)
        {
            case TextBlock textBlock:
                textBlock[!TextBlock.ForegroundProperty] =
                    new DynamicResourceExtension(foregroundResourceKey);
                break;
            case MaterialIcon icon:
                icon[!MaterialIcon.ForegroundProperty] =
                    new DynamicResourceExtension(foregroundResourceKey);
                break;
        }

        return control;
    }

    /// <summary>
    /// 解析 <c>material:Kind</c> 字形。前缀不匹配的<b>或</b>枚举名无效的都返回 <c>false</c>，
    /// 由调用方走 TextBlock 回退。
    /// </summary>
    /// <param name="glyph">待解析的字形字符串。</param>
    /// <param name="kind">解析出的 Material 图标种类；失败时为默认值。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    public static bool TryParseMaterialKind(string? glyph, out MaterialIconKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(glyph) ||
            !glyph.StartsWith(MaterialPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Enum.TryParse(
            glyph.AsSpan(MaterialPrefix.Length),
            ignoreCase: true,
            out kind);
    }
}
