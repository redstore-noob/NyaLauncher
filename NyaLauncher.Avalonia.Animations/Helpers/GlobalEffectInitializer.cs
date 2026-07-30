using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace NyaLauncher.Avalonia.Animations.Helpers;

public static class GlobalEffectInitializer
{
    private static readonly HashSet<object> _initialized = new();

    public static void AttachAll(Visual root, Canvas? rippleLayer = null)
    {
        rippleLayer ??= RippleBehavior.GlobalRippleLayer;
        var controls = new List<Control>();
        CollectInteractiveControls(root, controls);

        foreach (var c in controls)
        {
            if (!_initialized.Add(c)) continue;
            BounceBehavior.AttachHoverScale(c);
            if (c is Button button)
                BounceBehavior.AttachBounce(button);
            if (c is not TextBox and not ComboBox)
                BounceBehavior.AttachClickBounce(c);
            if (rippleLayer != null)
                RippleBehavior.AttachRipple(c, rippleLayer);
            if (c is ComboBox comboBox)
                BounceBehavior.AttachDropDownAnimation(comboBox);
        }
    }

    private static void CollectInteractiveControls(Visual parent, List<Control> results)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control control && IsInteractiveType(control))
                results.Add(control);
            if (child is not Window and not UserControl)
                CollectInteractiveControls(child, results);
        }
    }

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
