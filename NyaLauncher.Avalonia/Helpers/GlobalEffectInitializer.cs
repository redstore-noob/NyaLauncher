using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Helpers;

/// <summary>
/// 全局动效初始化器 — 遍历可视树，自动给所有交互控件附加Q弹 + 水波纹
/// </summary>
public static class GlobalEffectInitializer
{
    /// <summary>
    /// 记录已附加动效的控件，防止重复订阅导致抽搐
    /// </summary>
    private static readonly HashSet<object> _initialized = new();

    /// <summary>
    /// 对容器内的所有交互控件附加动效（幂等：重复调用不会重复订阅）
    /// </summary>
    public static void AttachAll(Visual root, Canvas? rippleLayer = null)
    {
        rippleLayer ??= RippleBehavior.GlobalRippleLayer;
        var controls = new List<Control>();
        CollectInteractiveControls(root, controls);

        foreach (var c in controls)
        {
            // ★ 跳过已附加的控件，防止重复订阅
            if (!_initialized.Add(c)) continue;

            // 所有交互控件附上悬停缩放
            BounceBehavior.AttachHoverScale(c);

            // 按钮类附加完整 Q 弹（点击回弹）
            if (c is Button button)
            {
                BounceBehavior.AttachBounce(button);
            }

            // 点击弹跳（输入框/下拉框不附加）
            if (c is not TextBox and not ComboBox)
            {
                BounceBehavior.AttachClickBounce(c);
            }

            // 水波纹（所有交互控件）
            if (rippleLayer != null)
            {
                RippleBehavior.AttachRipple(c, rippleLayer);
            }

            // ComboBox 额外附加下拉弹出动效
            if (c is ComboBox comboBox)
            {
                BounceBehavior.AttachDropDownAnimation(comboBox);
            }
        }
    }

    /// <summary>
    /// 递归收集所有交互控件
    /// </summary>
    private static void CollectInteractiveControls(Visual parent, List<Control> results)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control control && IsInteractiveType(control))
            {
                results.Add(control);
            }

            // 递归（但跳过页面内部 — 页面构造时自己会调）
            if (child is not Window and not UserControl)
            {
                CollectInteractiveControls(child, results);
            }
        }
    }

    /// <summary>
    /// 判断是否是需要附加动效的交互控件类型
    /// </summary>
    private static bool IsInteractiveType(Control c) => c switch
    {
        TabStripItem => true,
        ComboBoxItem => true,
        ListBoxItem => true,
        RadioButton => true,
        CheckBox => true,
        ToggleButton => true,
        Button => true,
        TextBox => true,
        ComboBox => true,
        Slider => true,
        _ => false
    };
}
