using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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

        // FluentTheme 的标准控件与自定义资源使用同一套明暗模式。
        // 放在资源合并完成后切换，避免界面短暂显示新模式配旧配色。
        app.RequestedThemeVariant = variant;
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
            MergeResources(app.Resources, variantDict);
        }
        else
        {
            MergeResources(app.Resources, dict);
        }
    }

    /// <summary>
    /// 将新主题合并到应用资源。已有画笔会原位改色，让通过 StaticResource
    /// 或代码持有该画笔的现有控件也能立即刷新；其他资源则正常替换，供
    /// DynamicResource 与后续创建的控件使用。
    /// </summary>
    private static void MergeResources(IResourceDictionary target, ResourceDictionary source)
    {
        foreach (var entry in source)
        {
            if (entry.Value is ISolidColorBrush incomingBrush)
            {
                if (target.TryGetValue(entry.Key, out var current) &&
                    current is SolidColorBrush currentBrush)
                {
                    currentBrush.Color = incomingBrush.Color;
                    currentBrush.Opacity = incomingBrush.Opacity;
                    // 重新写入同一对象以广播资源变更，同时保留被代码或旧
                    // StaticResource 持有的画笔身份。
                    target[entry.Key] = currentBrush;
                }
                else
                {
                    target[entry.Key] = new SolidColorBrush(incomingBrush.Color)
                    {
                        Opacity = incomingBrush.Opacity
                    };
                }

                continue;
            }

            target[entry.Key] = entry.Value;
        }
    }
}
