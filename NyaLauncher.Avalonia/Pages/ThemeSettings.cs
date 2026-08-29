using System;
using Avalonia;
using Avalonia.Styling;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia.Pages;

internal static class ThemeSettings
{
    private const string ThemeFamilyKey = "themeFamily";
    private const string ThemeModeKey = "themeMode";
    private const string LegacyThemeKey = "theme";
    private const string AmbientGradientKey = "ambientGradient";
    private const string SparkleTrailKey = "sparkleTrail";
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

    /// <summary>
    /// 把存储的模式解析为具体明暗：「System」读取操作系统当前偏好，其余原样返回。
    /// </summary>
    public static string ResolveThemeMode()
    {
        var mode = LoadThemeMode();
        if (!string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            return mode;

        var values = Application.Current?.PlatformSettings?.GetColorValues();
        // PlatformThemeVariant.Light.ToString() == "Light"，避免引入额外命名空间比较
        return string.Equals(values?.ThemeVariant.ToString(), "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
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

    /// <summary>读取「彩虹背景」开关（默认开）。</summary>
    public static bool LoadAmbientGradient()
    {
        return LauncherConfig.GetValue(AmbientGradientKey) != "false";
    }

    /// <summary>保存「彩虹背景」开关。</summary>
    public static void SaveAmbientGradient(bool enabled)
    {
        LauncherConfig.SetValue(AmbientGradientKey, enabled ? "true" : "false");
    }

    /// <summary>读取「星尘特效」开关（默认开）。</summary>
    public static bool LoadSparkleTrail()
    {
        return LauncherConfig.GetValue(SparkleTrailKey) != "false";
    }

    /// <summary>保存「星尘特效」开关。</summary>
    public static void SaveSparkleTrail(bool enabled)
    {
        LauncherConfig.SetValue(SparkleTrailKey, enabled ? "true" : "false");
    }
}
