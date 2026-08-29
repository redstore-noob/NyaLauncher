namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// 全局动画总开关（默认开）。由设置页「动画效果」开关切换并持久化；
/// 关闭时所有装饰性动画一律跳过：出入场（WindowEffects）、弹入弹出（OverlayEffects）、
/// hover/点击回弹（GlobalAnimation/AnimationHelper）、涟漪（RippleBehavior）、
/// 级联入场（Stagger）、流光/星尘（AmbientGradient/SparkleTrail）、
/// 旋转/脉冲/跑马灯/翻转/磁吸/打字机/闪烁/抖动/Emoji 粒子/换页过渡等。
/// 本开关由设置页写入，Animations 模块内部只读。
/// </summary>
public static class AnimationGate
{
    private static bool _enabled = true;

    /// <summary>
    /// 动画总开关。切换时同步刷新「彩虹背景」与「星尘特效」（关闭即移除已注入的层，打开即重新注入），
    /// 让开关立即生效，无需重启。
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            // 立即生效：关闭时移除已注入的流光/星尘层，打开时重新注入
            AmbientGradient.RefreshGlobal();
            SparkleTrail.RefreshGlobal();
        }
    }
}
