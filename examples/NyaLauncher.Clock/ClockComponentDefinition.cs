using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Clock;

internal static class ClockComponentDefinition
{
    public const string ComponentLocalId = "digital-clock";
    public const string TimeElementId = "time";
    public const string TimeZoneElementId = "time-zone";
    public const string PeriodElementId = "period";
    public const string SecondsElementId = "seconds";

    public static PolygonComponentDefinition Create(string pluginId) =>
        new PolygonComponentBuilder($"{pluginId}/{ComponentLocalId}", "电子时钟")
            .WithDescription("显示当前电脑时间，可在插件设置中切换时间制和辅助信息。")
            .WithGlyph("◷")
            .WithShape(PolygonShapeDefinition.CutCorner(0.065))
            .WithSize(360, 210)
            .WithSizeLimits(300, 170, 540, 315)
            .WithDragHandle(new ComponentRect(0.44, 0.02, 0.12, 0.07))
            .WithTheme(new PolygonComponentTheme
            {
                Surface = "#111719",
                SurfaceHover = "#172124",
                Border = "#244047",
                BorderHover = "#55E6C1",
                TextPrimary = "#8DFFE2",
                TextSecondary = "#76AFA2",
                Accent = "#55E6C1",
                AccentForeground = "#07110F",
                ProgressTrack = "#1A2B2E",
                BorderThickness = 1.5
            })
            .AddText(
                TimeZoneElementId,
                new ComponentRect(0.07, 0.055, 0.86, 0.09),
                "UTC+00:00",
                ComponentTextRole.Caption,
                10)
            .AddText(
                TimeElementId,
                new ComponentRect(0.045, 0.16, 0.91, 0.64),
                "00:00",
                ComponentTextRole.Title,
                72)
            .AddText(
                PeriodElementId,
                new ComponentRect(0.07, 0.82, 0.18, 0.10),
                "AM",
                ComponentTextRole.Caption,
                13)
            .AddText(
                SecondsElementId,
                new ComponentRect(0.75, 0.82, 0.18, 0.10),
                "00",
                ComponentTextRole.Emphasis,
                15)
            .Build();
}
