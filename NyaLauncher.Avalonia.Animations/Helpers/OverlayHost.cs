using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 叠加层注入助手：把"水波纹 / 光影流转"等覆盖视觉塞进目标控件之上，
/// 且不破坏原布局。仅支持 Border / ContentControl / Grid（这三类足以覆盖卡片与按钮）；
/// 其它容器类型返回 null（调用方安全跳过）。同一控件多次调用复用同一个 wrapper，
/// 多个叠加层（如 ripple + shimmer 共存）会叠在同一 wrapper 里。
/// 逻辑只在本模块，主工程只负责触发。
/// </summary>
internal static class OverlayHost
{
    private static readonly ConditionalWeakTable<Control, Grid> Wrappers = new();

    public static Grid? GetOrCreateOverlay(Control target)
    {
        if (Wrappers.TryGetValue(target, out var existing))
            return existing;

        Grid wrapper;
        switch (target)
        {
            case Grid grid:
                // Grid 子元素默认重叠，本身就适合当叠加容器，直接复用。
                wrapper = grid;
                break;

            case Border border:
                wrapper = new Grid();
                var bc = border.Child;
                border.Child = wrapper;
                if (bc is Control child) wrapper.Children.Add(child);
                break;

            case ContentControl cc:
                wrapper = new Grid();
                var orig = cc.Content;
                cc.Content = wrapper;
                if (orig is Control c) wrapper.Children.Add(c);
                else if (orig != null) wrapper.Children.Add(new ContentPresenter { Content = orig });
                break;

            default:
                return null;
        }

        if (wrapper != target)
            Wrappers.Add(target, wrapper);
        return wrapper;
    }
}
