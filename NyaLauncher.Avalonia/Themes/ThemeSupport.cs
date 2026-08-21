using Avalonia;
using Avalonia.Media;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Themes;

internal static class ThemeBrushes
{
    public static IBrush CardBackground => Get("CardBgBrush", "#20283D");
    public static IBrush HeaderBackground => Get("PanelBgBrush", "#1B2132");
    public static IBrush CardBorder => Get("DefaultBorderBrush", "#38435F");
    public static IBrush Accent => Get("AccentBrush", "#7C8CFF");
    public static IBrush Muted => Get("MutedTextBrush", "#7E88A4");
    public static IBrush SeamIdle => Get("SubtleBorderBrush", "#303A53");
    public static IBrush IconBoxBg => Get("IconBoxBgBrush", "#303A55");
    public static IBrush DragHandleBg => Get("ControlBgBrush", "#171D2C");
    public static IBrush DragHandleGlyph => Get("TertiaryTextBrush", "#8993AD");
    public static IBrush DragHandleActive => Get("AccentDeepBrush", "#38447A");
    public static IBrush ComponentPrimaryBg => Get("AccentDeepBrush", "#38447A");
    public static IBrush ComponentPrimaryBorder => Get("AccentBrush", "#7C8CFF");
    public static IBrush ComponentPrimaryHoverBg => Get("AccentDarkerBrush", "#4D59B7");
    public static IBrush ComponentBg => Get("ControlBgBrush", "#171D2C");
    public static IBrush ComponentBorder => Get("DefaultBorderBrush", "#38435F");
    public static IBrush ComponentHoverBg => Get("HighlightBgBrush", "#313B58");
    public static IBrush SidebarBorder => Get("MediumBorderBrush", "#465372");
    public static IBrush ButtonBg => Get("ButtonBgBrush", "#2A334B");
    public static IBrush SurfaceBg => Get("SurfaceBgBrush", "#222A3E");

    internal static IBrush Get(string key, string fallback)
    {
        if (Application.Current?.TryGetResource(key, null, out var value) == true)
        {
            if (value is IBrush brush)
                return brush;
            if (value is Color color)
                return new SolidColorBrush(color);
        }
        return Brush.Parse(fallback);
    }
}

internal static class ThemePolygonHelper
{
    public static IBrush Muted => ThemeBrushes.Muted;
    public static IBrush AccentGlyph => ThemeBrushes.Accent;
    public static IBrush TertiaryText => ThemeBrushes.Get("TertiaryTextBrush", "#8993AD");
    public static IBrush DisabledText => ThemeBrushes.Get("DisabledTextBrush", "#566079");
    public static IBrush CardBackground => ThemeBrushes.CardBackground;
    public static IBrush CardBg => ThemeBrushes.CardBackground;
    public static IBrush CardBorder => ThemeBrushes.CardBorder;
    public static IBrush CardBrd => ThemeBrushes.CardBorder;
    public static IBrush IconBoxBg => ThemeBrushes.IconBoxBg;
    public static IBrush EditorSurface => ThemeBrushes.Get("ControlBgBrush", "#171D2C");
    public static IBrush EditorBorder => ThemeBrushes.Get("DefaultBorderBrush", "#38435F");
    public static IBrush PresetBg => ThemeBrushes.Get("ButtonBgBrush", "#2A334B");
    public static IBrush PresetBorder => ThemeBrushes.Get("MediumBorderBrush", "#465372");
    public static IBrush DeleteBg => Brush.Parse("#3B282B");
    public static IBrush DeleteBorder => Brush.Parse("#75434B");
    public static IBrush DeleteFg => Brush.Parse("#FFD2CE");
    public static IBrush SkinButtonBg => ThemeBrushes.Get("ControlBgBrush", "#171D2C");
    public static IBrush SkinButtonBgCurrent => ThemeBrushes.Get("AccentDeepBrush", "#38447A");
    public static IBrush SkinButtonBorder => ThemeBrushes.Get("DefaultBorderBrush", "#38435F");
    public static IBrush SkinButtonBorderCurrent => ThemeBrushes.Accent;
    public static IBrush SkinAvatarBg => ThemeBrushes.Get("IconBoxBgBrush", "#303A55");
    public static IBrush DragPreviewBg => ThemeBrushes.Get("DropPreviewBgBrush", "#556C7BFF");
    public static IBrush DragGlyph => ThemeBrushes.Get("TertiaryTextBrush", "#8993AD");
    public static IBrush TaskDownloadingBg => Brush.Parse("#2C3858");
    public static IBrush TaskDownloadingBorder => Brush.Parse("#6688D8");
    public static IBrush TaskLaunchingBg => Brush.Parse("#334A43");
    public static IBrush TaskLaunchingBorder => Brush.Parse("#68B596");

    public static PolygonComponentTheme CreateDefaultTheme() => new()
    {
        Surface = "#20283D",
        SurfaceHover = "#293552",
        Border = "#53658F",
        BorderHover = "#8C9DFF",
        TextPrimary = "#F6F7FF",
        TextSecondary = "#A5AEC7",
        Accent = "#7C8CFF",
        AccentForeground = "#FFFFFF",
        ProgressTrack = "#303B56"
    };

    public static PolygonComponentTheme CreateLaunchTheme() => new()
    {
        Surface = "#263A39",
        SurfaceHover = "#304A47",
        Border = "#4C7770",
        BorderHover = "#7CD8BC",
        TextPrimary = "#F4FFF9",
        TextSecondary = "#A8C8BD",
        Accent = "#75D6A3",
        AccentForeground = "#10251D",
        ProgressTrack = "#30483F"
    };
}
