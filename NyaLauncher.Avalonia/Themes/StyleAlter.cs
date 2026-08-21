using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace NyaLauncher.Avalonia.Themes;

public class StyleAlter
{
    public static event Action? ThemeChanged;

    /// <summary>
    /// 加载指定主题家族（{family}_Resources.axaml）中的明暗变体，
    /// 并把当前激活变体的条目复制到 Application.Current.Resources。
    /// 家族文件缺失时自动降级到 HatsuneMiku。
    /// </summary>
    public static void ApplyTheme(string themeFamily, string themeMode)
    {
        if (string.IsNullOrWhiteSpace(themeFamily))
            return;

        var app = Application.Current;
        if (app == null)
            return;

        var variant = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        var uri = new Uri($"avares://NyaLauncher.Avalonia/Themes/{themeFamily}_Resources.axaml");
        try
        {
            ApplyVariant(uri, variant);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StyleAlter] Failed to load theme family '{themeFamily}' ({variant}): {ex}");

            if (!string.Equals(themeFamily, "HatsuneMiku", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackUri = new Uri("avares://NyaLauncher.Avalonia/Themes/HatsuneMiku_Resources.axaml");
                try
                {
                    ApplyVariant(fallbackUri, variant);
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[StyleAlter] Fallback theme also failed: {fallbackEx}");
                    throw;
                }
            }
            else
            {
                throw;
            }
        }

        ThemeChanged?.Invoke();
    }

    private static void ApplyVariant(Uri uri, ThemeVariant variant)
    {
        var app = Application.Current;
        if (app == null)
            return;
        var obj = AvaloniaXamlLoader.Load(uri);
        if (obj is not ResourceDictionary dict)
            return;

        if (dict.ThemeDictionaries.TryGetValue(variant, out var provider) &&
            provider is ResourceDictionary variantDict)
        {
            foreach (var entry in variantDict)
                app.Resources[entry.Key] = entry.Value;
        }
        else
        {
            foreach (var entry in dict)
                app.Resources[entry.Key] = entry.Value;
        }
    }
}
