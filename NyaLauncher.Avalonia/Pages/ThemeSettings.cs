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
    private const string CodexBlueUnlockedKey = "codexBlueUnlocked";
    private const string AmbientGradientKey = "ambientGradient";
    private const string SparkleTrailKey = "sparkleTrail";
    private const string ClickRingKey = "clickRing";
    private const string CustomBackgroundKey = "customBackgroundImage";
    private const string CustomBackgroundOpacityKey = "customBackgroundOpacity";
    private const string CustomBackgroundBlurKey = "customBackgroundBlur";
    private const string DefaultFamily = "HatsuneMiku";
    private const string DefaultMode = "Dark";

    public static event Action? ThemeAvailabilityChanged;

    public static bool IsCodexBlueUnlocked() =>
        string.Equals(LauncherConfig.GetValue(CodexBlueUnlockedKey), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsThemeFamilyAvailable(string family) =>
        !string.Equals(family.Trim(), "CodexBlue", StringComparison.OrdinalIgnoreCase) || IsCodexBlueUnlocked();

    /// <summary>彩蛋解锁随启动器配置保存；保存失败时保持锁定。</summary>
    public static bool UnlockCodexBlue()
    {
        if (IsCodexBlueUnlocked())
            return true;
        if (!LauncherConfig.SetValue(CodexBlueUnlockedKey, "true"))
            return false;

        ThemeAvailabilityChanged?.Invoke();
        return true;
    }

    private static string AvailableFamily(string family) =>
        IsThemeFamilyAvailable(family) ? family.Trim() : DefaultFamily;

    public static string LoadTheme()
    {
        return $"{LoadThemeFamily()}_{LoadThemeMode()}";
    }

    public static string LoadThemeFamily()
    {
        var family = LauncherConfig.GetValue(ThemeFamilyKey);
        if (!string.IsNullOrWhiteSpace(family))
        {
            var availableFamily = AvailableFamily(family);
            // 旧版本选中过隐藏主题时，恢复到可用主题，等待用户完成彩蛋。
            if (family != availableFamily)
                SaveThemeFamily(availableFamily);
            return availableFamily;
        }

        var legacy = LauncherConfig.GetValue(LegacyThemeKey);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var parts = legacy.Split('_', 2);
            if (parts.Length == 2)
            {
                var availableFamily = AvailableFamily(parts[0]);
                SaveThemeFamily(availableFamily);
                SaveThemeMode(parts[1]);
                return availableFamily;
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
            LauncherConfig.SetValue(ThemeFamilyKey, AvailableFamily(family));
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

    /// <summary>读取「点击圆环」开关（默认开）。</summary>
    public static bool LoadClickRing()
    {
        return LauncherConfig.GetValue(ClickRingKey) != "false";
    }

    /// <summary>保存「点击圆环」开关。</summary>
    public static void SaveClickRing(bool enabled)
    {
        LauncherConfig.SetValue(ClickRingKey, enabled ? "true" : "false");
    }

    /// <summary>读取「自定义背景图」路径（未设置或为空返回 null）。</summary>
    public static string? LoadCustomBackground()
    {
        var path = LauncherConfig.GetValue(CustomBackgroundKey);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>保存「自定义背景图」路径（null/空 = 关闭）。</summary>
    public static void SaveCustomBackground(string? path)
    {
        LauncherConfig.SetValue(CustomBackgroundKey,
            string.IsNullOrWhiteSpace(path) ? string.Empty : path!.Trim());
    }

    /// <summary>读取「自定义背景图」不透明度（默认 0.35，钳到 0.05–0.85）。</summary>
    public static double LoadCustomBackgroundOpacity()
    {
        return double.TryParse(LauncherConfig.GetValue(CustomBackgroundOpacityKey), out var v) && v > 0
            ? Math.Clamp(v, 0.05, 0.85)
            : 0.35;
    }

    /// <summary>保存「自定义背景图」不透明度。</summary>
    public static void SaveCustomBackgroundOpacity(double value)
    {
        LauncherConfig.SetValue(CustomBackgroundOpacityKey,
            Math.Clamp(value, 0.05, 0.85).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>读取「自定义背景图」高斯模糊半径（默认 0 = 关闭，钳到 0–30）。</summary>
    public static double LoadCustomBackgroundBlur()
    {
        return double.TryParse(LauncherConfig.GetValue(CustomBackgroundBlurKey), out var v) && v > 0
            ? Math.Clamp(v, 0, 30)
            : 0;
    }

    /// <summary>保存「自定义背景图」高斯模糊半径。</summary>
    public static void SaveCustomBackgroundBlur(double value)
    {
        LauncherConfig.SetValue(CustomBackgroundBlurKey,
            Math.Clamp(value, 0, 30).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
