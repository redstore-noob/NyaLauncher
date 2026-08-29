using Avalonia;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Themes;

/// <summary>
/// 主题画笔桥接层：从 Application.Current.Resources 读取当前主题的画笔值。
/// 每次访问属性时实时读取，确保主题切换后立即生效。
/// fallback 值仅在资源键缺失时使用（正常情况下不会触发）。
/// <para>
/// 使用约定：本层返回的是瞬时快照。持久视觉元素请在
/// <see cref="ThemeManager.ThemeChanged"/> 时重新读取并应用
/// （DockWorkspace.Rebuild 即此模式）；一次性的交互态着色
/// （拖拽高亮等）直接读取即可。XAML 中不要通过本层取色，
/// 应直接使用 {DynamicResource xxxBrush}。
/// </para>
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

    public static IBrush CardBackground => GetBrush("CardBg2Brush", Brush.Parse("#121C17"));
    public static IBrush HeaderBackground => GetBrush("HeaderBgBrush", Brush.Parse("#192520"));
    public static IBrush CardBorder => GetBrush("CardBorderBrush", Brush.Parse("#2A3E34"));
    public static IBrush Accent => GetBrush("AccentBrush", Brush.Parse("#3EC9A0"));
    public static IBrush Muted => GetBrush("MutedTextBrush", Brush.Parse("#96B8A6"));
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
