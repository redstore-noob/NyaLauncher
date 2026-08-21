using System;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia.Pages;

internal static class ThemeSettings
{
    private const string FamilyKey = "themeFamily";
    private const string ModeKey = "themeMode";

    public static string LoadThemeFamily() =>
        LauncherConfig.GetValue(FamilyKey) is { Length: > 0 } family
            ? family
            : "HatsuneMiku";

    public static string LoadThemeMode() =>
        string.Equals(LauncherConfig.GetValue(ModeKey), "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";

    public static string LoadTheme() => $"{LoadThemeFamily()} {LoadThemeMode()}";

    public static void SaveThemeFamily(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        LauncherConfig.SetValue(FamilyKey, family.Trim());
    }

    public static void SaveThemeMode(string mode)
    {
        var normalized = string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
        LauncherConfig.SetValue(ModeKey, normalized);
    }
}
