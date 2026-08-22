using Avalonia;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Themes;

/// <summary>
/// 主题画笔桥接层：从 Application.Current.Resources 读取当前主题的画笔值。
/// 每次访问属性时实时读取，确保主题切换后立即生效。
/// fallback 值仅在资源键缺失时使用（正常情况下不会触发）。
/// </summary>
public static class ThemeBrushes
{
    private static IBrush GetBrush(string key, IBrush fallback)
    {
        var app = Application.Current;
        if (app?.Resources.TryGetValue(key, out var value) == true && value is IBrush brush)
            return brush;
        return fallback;
    }

    public static IBrush WindowBackground => GetBrush("WindowBgBrush", Brush.Parse("#101812"));
    public static IBrush BaseBackground => GetBrush("BaseBgBrush", Brush.Parse("#121B15"));
    public static IBrush CardBackground => GetBrush("CardBg2Brush", Brush.Parse("#121C17"));
    public static IBrush PanelBackground => GetBrush("PanelBgBrush", Brush.Parse("#16221C"));
    public static IBrush SurfaceBackground => GetBrush("SurfaceBgBrush", Brush.Parse("#192520"));
    public static IBrush HighlightBackground => GetBrush("HighlightBgBrush", Brush.Parse("#243830"));
    public static IBrush ControlBackground => GetBrush("ControlBgBrush", Brush.Parse("#1E2E27"));
    public static IBrush BadgeBackground => GetBrush("BadgeBgBrush", Brush.Parse("#26372F"));
    public static IBrush DialogBackground => GetBrush("DialogBgBrush", Brush.Parse("#111A15"));
    public static IBrush DialogAltBackground => GetBrush("DialogAltBgBrush", Brush.Parse("#17241D"));
    public static IBrush HeaderBackground => GetBrush("HeaderBgBrush", Brush.Parse("#192520"));
    public static IBrush CardBorder => GetBrush("CardBorderBrush", Brush.Parse("#2A3E34"));
    public static IBrush SubtleBorder => GetBrush("SubtleBorderBrush", Brush.Parse("#223129"));
    public static IBrush DefaultBorder => GetBrush("DefaultBorderBrush", Brush.Parse("#2C3E35"));
    public static IBrush MediumBorder => GetBrush("MediumBorderBrush", Brush.Parse("#344A40"));
    public static IBrush StrongBorder => GetBrush("StrongBorderBrush", Brush.Parse("#3A5048"));
    public static IBrush Accent => GetBrush("AccentBrush", Brush.Parse("#3EC9A0"));
    public static IBrush AccentDark => GetBrush("AccentDarkBrush", Brush.Parse("#2CA482"));
    public static IBrush AccentText => GetBrush("AccentTextBrush", Brush.Parse("#A9F0D8"));
    public static IBrush PrimaryText => GetBrush("PrimaryTextBrush", Brush.Parse("#F6F7FF"));
    public static IBrush SecondaryText => GetBrush("SecondaryTextBrush", Brush.Parse("#D6E6DE"));
    public static IBrush TertiaryText => GetBrush("TertiaryTextBrush", Brush.Parse("#A5AEC7"));
    public static IBrush BodyText => GetBrush("BodyTextBrush", Brush.Parse("#DDE2F4"));
    public static IBrush Muted => GetBrush("MutedTextBrush", Brush.Parse("#96B8A6"));
    public static IBrush Subtext => GetBrush("SubtextTextBrush", Brush.Parse("#8FA99C"));
    public static IBrush HintText => GetBrush("HintTextBrush", Brush.Parse("#7E9187"));
    public static IBrush DisabledText => GetBrush("DisabledTextBrush", Brush.Parse("#61736A"));
    public static IBrush White => GetBrush("WhiteBrush", Brushes.White);
    public static IBrush Success => GetBrush("SuccessBrush", Brush.Parse("#5CDCBA"));
    public static IBrush Warning => GetBrush("WarningBrush", Brush.Parse("#E7BD68"));
    public static IBrush Error => GetBrush("ErrorBrush", Brush.Parse("#E46B7E"));
    public static IBrush ErrorDark => GetBrush("ErrorDarkBrush", Brush.Parse("#3A2028"));
    public static IBrush Info => GetBrush("InfoBrush", Brush.Parse("#78A9E8"));
    public static IBrush SeamIdle => GetBrush("SeamIdleBrush", Brush.Parse("#2C3E35"));
    public static IBrush DragHandleBg => GetBrush("DragHandleBgBrush", Brush.Parse("#223028"));
    public static IBrush DragHandleActive => GetBrush("DragHandleActiveBrush", Brush.Parse("#345040"));
    public static IBrush DragHandleGlyph => GetBrush("DragHandleGlyphBrush", Brush.Parse("#98C0AA"));
    public static IBrush IconBoxBg => GetBrush("IconBoxBgBrush", Brush.Parse("#283830"));
    public static IBrush ComponentBg => GetBrush("ComponentBgBrush", Brush.Parse("#1B2822"));
    public static IBrush ComponentBorder => GetBrush("ComponentBorderBrush", Brush.Parse("#2A3C34"));
    public static IBrush ComponentHoverBg => GetBrush("ComponentHoverBgBrush", Brush.Parse("#243830"));
    public static IBrush ComponentPrimaryBg => GetBrush("ComponentPrimaryBgBrush", Brush.Parse("#2CA482"));
    public static IBrush ComponentPrimaryBorder => GetBrush("ComponentPrimaryBorderBrush", Brush.Parse("#40C09A"));
    public static IBrush ComponentPrimaryHoverBg => GetBrush("ComponentPrimaryHoverBgBrush", Brush.Parse("#38B892"));
    public static IBrush SidebarBorder => GetBrush("SidebarBorderBrush", Brush.Parse("#3A5048"));
    public static IBrush ButtonBg => GetBrush("ButtonBgBrush", Brush.Parse("#1E2E27"));
    public static IBrush SurfaceBg => GetBrush("SurfaceBgBrush", Brush.Parse("#192520"));
}
