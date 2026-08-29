using Avalonia;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Themes;

/// <summary>
/// 多边形组件主题桥接层：从 Application.Current.Resources 读取标准主题颜色，
/// 为 PolygonComponentView 和内置组件提供主题感知的画笔与颜色值。
///
/// 组件卡片不再自带颜色：画刷一律绑定标准主题资源（ComponentBgBrush / PrimaryTextBrush 等），
/// 新增主题时无需为 Polygon 组件单独定义颜色。
/// </summary>
internal static class ThemePolygonHelper
{
    private static IBrush GetBrush(string key, string fallback)
    {
        var app = Application.Current;
        if (app?.Resources.TryGetValue(key, out var value) == true && value is IBrush brush)
            return brush;
        return Brush.Parse(fallback);
    }

    public static IBrush CardBackground => GetBrush("CardBgBrush", "#141F1A");
    public static IBrush CardBorder => GetBrush("CardBorderBrush", "#273830");
    public static IBrush IconBoxBg => GetBrush("IconBoxBgBrush", "#283830");
    public static IBrush DragGlyph => GetBrush("DragHandleGlyphBrush", "#98C0AA");
    public static IBrush Muted => GetBrush("MutedTextBrush", "#96B8A6");
    public static IBrush EditorSurface => GetBrush("SurfaceBgBrush", "#192520");
    public static IBrush EditorBorder => GetBrush("MediumBorderBrush", "#2C3E35");
    public static IBrush CardBg => GetBrush("CardBg2Brush", "#121C17");
    public static IBrush CardBrd => GetBrush("CardBorderBrush", "#2A3E34");
    public static IBrush DeleteBg => GetBrush("ErrorDarkBrush", "#2A1A20");
    public static IBrush DeleteBorder => GetBrush("ErrorBrush", "#D94B64");
    public static IBrush DeleteFg => GetBrush("WhiteBrush", "#FFFFFF");
    public static IBrush PresetBg => GetBrush("ComponentBgBrush", "#1E2E27");
    public static IBrush PresetBorder => GetBrush("DefaultBorderBrush", "#2C3E35");
    public static IBrush SkinButtonBg => GetBrush("ComponentBgBrush", "#1B2822");
    public static IBrush SkinButtonBgCurrent => GetBrush("ComponentHoverBgBrush", "#243830");
    public static IBrush SkinButtonBorder => GetBrush("DefaultBorderBrush", "#344A40");
    public static IBrush SkinButtonBorderCurrent => GetBrush("AccentBrightBrush", "#80D4B0");
    public static IBrush SkinAvatarBg => GetBrush("ComponentBgBrush", "#1E2E27");
    public static IBrush AccentGlyph => GetBrush("AccentBrush", "#3EC9A0");
    public static IBrush DisabledText => GetBrush("DisabledTextBrush", "#5A7A6C");
    public static IBrush TertiaryText => GetBrush("TertiaryTextBrush", "#D6E6DE");
    public static IBrush DragPreviewBg => GetBrush("DropPreviewBgBrush", "#3848D4A0");
    public static IBrush TaskDownloadingBg => GetBrush("SuccessBrush", "#1E8868");
    public static IBrush TaskDownloadingBorder => GetBrush("SuccessBrush", "#5CDCBA");
    public static IBrush TaskLaunchingBg => GetBrush("AccentBrush", "#2CA482");
    public static IBrush TaskLaunchingBorder => GetBrush("AccentLightBrush", "#60E2C0");
}
