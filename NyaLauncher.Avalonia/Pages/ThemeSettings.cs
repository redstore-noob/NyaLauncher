using System;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia.Pages;

internal static class ThemeSettings
{
    private const string ThemeFamilyKey = "themeFamily";
    private const string ThemeModeKey = "themeMode";
    private const string LegacyThemeKey = "theme";
    private const string DefaultFamily = "HatsuneMiku";
    private const string DefaultMode = "Dark";

    public static string LoadTheme()
    {
        return $"{LoadThemeFamily()}_{LoadThemeMode()}";
    }

    public static string LoadThemeFamily()
    {
        var family = LauncherConfig.GetValue(ThemeFamilyKey);
        if (!string.IsNullOrWhiteSpace(family))
            return family;

        var legacy = LauncherConfig.GetValue(LegacyThemeKey);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var parts = legacy.Split('_', 2);
            if (parts.Length == 2)
            {
                SaveThemeFamily(parts[0]);
                SaveThemeMode(parts[1]);
                return parts[0];
            }
        }

        return DefaultFamily;
    }

    public static string LoadThemeMode()
    {
        var mode = LauncherConfig.GetValue(ThemeModeKey);
        if (!string.IsNullOrWhiteSpace(mode))
            return mode;

        var legacy = LauncherConfig.GetValue(LegacyThemeKey);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var parts = legacy.Split('_', 2);
            if (parts.Length == 2)
            {
                SaveThemeMode(parts[1]);
                return parts[1];
            }
        }

        return DefaultMode;
    }

    public static void SaveThemeFamily(string family)
    {
        if (!string.IsNullOrWhiteSpace(family))
            LauncherConfig.SetValue(ThemeFamilyKey, family.Trim());
    }

    public static void SaveThemeMode(string mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
            LauncherConfig.SetValue(ThemeModeKey, mode.Trim());
    }
}
