using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Themes;

/// <summary>
/// 主题热重载管理器：切换主题家族或明暗模式时无需重启应用。
/// <para>
/// 原理：1) 更新 <see cref="Application.RequestedThemeVariant"/>（标准控件明暗）；
/// 2) 通过 <see cref="StyleAlter"/> 更新 Application 资源字典；
/// 3) 广播 <see cref="ThemeChanged"/>，宿主（MainWindow）重挂载根元素，
/// 使所有 StaticResource 引用重新解析到新资源值。
/// </para>
/// </summary>
public static class ThemeManager
{
    /// <summary>主题已切换，宿主应刷新整个界面。</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// 热应用主题。设置标准控件明暗 + 更新资源字典 + 广播刷新。
    /// <paramref name="themeMode"/> 支持「System」（自动解析操作系统当前偏好，
    /// 且运行期间监听系统明暗变化实时跟随）。
    /// </summary>
    public static void ApplyTheme(string themeFamily, string themeMode)
    {
        var app = Application.Current;
        if (app is null)
            return;

        // 0. 跟随系统：把 System 解析为具体明暗
        if (string.Equals(themeMode, "System", StringComparison.OrdinalIgnoreCase))
            themeMode = ResolveSystemMode(app);

        // 1. 标准控件（ComboBox / TextBox / 对话框等）的明暗变体
        var mode = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        if (app.RequestedThemeVariant != mode)
            app.RequestedThemeVariant = mode;

        // 2. 主题资源字典（家族资源文件的明暗变体条目复制到 Application.Resources）
        StyleAlter.ApplyTheme(themeFamily, themeMode);

        // 3. 广播热重载
        ThemeChanged?.Invoke();
    }

    private static bool _systemThemeHooked;

    /// <summary>读取操作系统当前明暗偏好，并确保已挂钩系统变化监听。</summary>
    private static string ResolveSystemMode(Application app)
    {
        var settings = app.PlatformSettings;
        if (settings is null)
            return "Dark";

        if (!_systemThemeHooked)
        {
            _systemThemeHooked = true;
            settings.ColorValuesChanged += (_, _) =>
            {
                // 仅当用户选择「跟随系统」时响应系统明暗变化
                if (!string.Equals(Pages.ThemeSettings.LoadThemeMode(), "System", StringComparison.OrdinalIgnoreCase))
                    return;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ApplyTheme(Pages.ThemeSettings.LoadThemeFamily(), "System");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ThemeManager] 跟随系统切换失败：{ex}");
                    }
                });
            };
        }

        var values = settings.GetColorValues();
        // PlatformThemeVariant.Light.ToString() == "Light"
        return string.Equals(values?.ThemeVariant.ToString(), "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
    }

    /// <summary>
    /// 平滑重挂载窗口根元素：先淡出、再重挂载、再淡入。
    /// 自动保存并恢复 ScrollViewer 偏移等状态。
    /// </summary>
    public static async Task RemountRootAsync(
        Window window,
        TimeSpan? fadeOut = null,
        TimeSpan? fadeIn = null)
    {
        if (window?.Content is not Control root)
            return;

        fadeOut ??= TimeSpan.FromMilliseconds(120);
        fadeIn ??= TimeSpan.FromMilliseconds(200);

        // 1. 保存视觉状态（ScrollViewer 偏移等）
        var state = CaptureVisualState(root);

        // 2. 淡出
        await AnimateOpacityAsync(root, 1.0, 0.0, fadeOut.Value);

        // 3. 重挂载
        window.Content = null;
        window.Content = root;

        // 4. 恢复视觉状态（延迟一帧确保布局完成）
        await Task.Delay(1);
        RestoreVisualState(root, state);

        // 5. 淡入
        await AnimateOpacityAsync(root, 0.0, 1.0, fadeIn.Value);
    }

    private static async Task AnimateOpacityAsync(Control target, double from, double to, TimeSpan duration)
    {
        var totalMs = duration.TotalMilliseconds;
        const double frameMs = 16.0;
        var steps = Math.Max(1, (int)(totalMs / frameMs));
        var diff = to - from;

        for (int i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            // Quadratic ease-out: 先快后慢，视觉更丝滑
            t = 1 - (1 - t) * (1 - t);
            target.Opacity = from + diff * t;
            if (i < steps)
                await Task.Delay((int)frameMs);
        }
        target.Opacity = to;
    }

    private static VisualState CaptureVisualState(Control root)
    {
        var state = new VisualState();
        foreach (var sv in FindVisualChildren<ScrollViewer>(root))
        {
            var key = !string.IsNullOrEmpty(sv.Name) ? sv.Name : sv.GetHashCode().ToString();
            state.ScrollOffsets[key] = sv.Offset;
        }
        return state;
    }

    private static void RestoreVisualState(Control root, VisualState state)
    {
        foreach (var sv in FindVisualChildren<ScrollViewer>(root))
        {
            var key = !string.IsNullOrEmpty(sv.Name) ? sv.Name : sv.GetHashCode().ToString();
            if (state.ScrollOffsets.TryGetValue(key, out var offset))
                sv.Offset = offset;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(Control parent) where T : Control
    {
        if (parent is T t)
            yield return t;

        foreach (var child in parent.GetVisualChildren().OfType<Control>())
        {
            foreach (var result in FindVisualChildren<T>(child))
                yield return result;
        }
    }

    private class VisualState
    {
        public Dictionary<string, Vector> ScrollOffsets { get; } = new();
    }
}
