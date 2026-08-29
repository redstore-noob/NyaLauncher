using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 全局动效开关（附加属性）。
/// 在 App.axaml 中通过全局 Style 给 Button / ComboBox 设置
/// <c>GlobalAnimation.Enable="True"</c>，让所有控件（含动态创建、列表项、代码构建）
/// 自动附加对应动画，无需逐个手动挂载。
/// </summary>
public static class GlobalAnimation
{
    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "Enable", typeof(GlobalAnimation), false);

    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    static GlobalAnimation()
    {
        EnableProperty.Changed.AddClassHandler<Control>(OnEnableChanged);
    }

    public static void SetEnable(AvaloniaObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(AvaloniaObject element) =>
        element.GetValue(EnableProperty);

    private static void OnEnableChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;
        // 动画总开关关闭时不挂载 hover/点击动画
        if (!AnimationGate.Enabled)
            return;
        if (Attached.TryGetValue(control, out _))
            return;

        try
        {
            switch (control)
            {
                case Button button:
                    // hover 微放大（流光感反馈）+ 点击弹性回弹（Q 弹）
                    // 注：ToggleButton / CheckBox / RadioButton 均继承自 Button，
                    // 此分支已覆盖开关类控件的 hover 动画。
                    BounceBehavior.AttachHoverScale(button);
                    // CheckBox / RadioButton / ToggleSwitch 不挂点击回弹：
                    // 按下缩小时命中区域变小，快速点击时抬起事件可能落在控件外，
                    // 导致开关状态偶发不切换；且 Material 自带 ripple 反馈，无需回弹。
                    if (button is not ToggleButton)
                        BounceBehavior.AttachClickBounce(button);
                    break;
                case ComboBox comboBox:
                    // 下拉弹出线性动画
                    BounceBehavior.AttachDropDownAnimation(comboBox);
                    break;
                default:
                    return;
            }
        }
        catch
        {
            // 动画挂载失败不影响控件功能
            return;
        }

        Attached.Add(control, new object());
    }
}
