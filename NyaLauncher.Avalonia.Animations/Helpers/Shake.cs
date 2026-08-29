using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 错误抖动：操作失败/下载出错时让控件左右快速抖动几下提示。
/// 主工程在出错回调里调用 <see cref="Trigger"/>（如下载失败面板），逻辑全部只在本模块。
/// </summary>
public static class Shake
{
    private static readonly ConditionalWeakTable<Control, object> Active = new();

    /// <summary>
    /// 触发一次左右抖动（约 5 下，幅度递减）。重复触发会重新开始；动画结束复位 RenderTransform。
    /// </summary>
    public static void Trigger(Control control, int intensity = 7)
    {
        if (control is null) return;
        // 动画总开关关闭时不播抖动
        if (!AnimationGate.Enabled) return;
        _ = ShakeAsync(control, intensity);
    }

    private static async Task ShakeAsync(Control control, int intensity)
    {
        // 防重入：正在抖时不重新开始（避免叠加）
        if (Active.TryGetValue(control, out _)) return;
        Active.Add(control, new object());

        var translate = new TranslateTransform();
        control.RenderTransform = translate;

        try
        {
            const int shakes = 5;
            for (var i = 0; i < shakes; i++)
            {
                var amp = intensity * (1 - i / (double)shakes);
                translate.X = (i % 2 == 0 ? 1 : -1) * amp;
                await Task.Delay(30);
            }
            translate.X = 0;
            await Task.Delay(20);
        }
        catch (Exception)
        {
            // 抖动失败也复位
        }
        finally
        {
            control.RenderTransform = null;
            Active.Remove(control);
        }
    }
}
