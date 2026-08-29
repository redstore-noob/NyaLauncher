using System;
using Avalonia.Animation.Easings;

namespace NyaLauncher.Avalonia.Animations.Helpers;

/// <summary>
/// Material Design 3 motion 令牌：emphasized 缓动曲线族与过渡时长。
/// 参考规范 https://m3.material.io/styles/motion/easing-and-duration/tokens-specs
/// </summary>
public static class MaterialMotion
{
    /// <summary>大型转场（布局重排、容器变换）建议时长。</summary>
    public const int LargeTransitionMs = 400;

    /// <summary>中等转场（页面、对话框出入场）建议时长。</summary>
    public const int MediumTransitionMs = 300;

    /// <summary>进入元素的不透明度应在该比例时长的匀速内先于位移完成。</summary>
    public const double FadeEndFraction = 0.4;

    /// <summary>退出元素的不透明度应在该比例时长的匀速内先于位移消失。</summary>
    public const double FadeEndFractionExit = 0.3;

    /// <summary>
    /// 列表错峰入场的总延迟上限（M3 编排规范：开场编排应在数百毫秒内完成）。
    /// 项数很多时逐项压缩间隔，而不是无限排队让尾项迟迟不出现。
    /// </summary>
    public const int MaxStaggerTotalDelayMs = 360;

    /// <summary>emphasized：cubic-bezier(0.2, 0.0, 0.0, 1.0)，容器位移的标准曲线。</summary>
    public static double Emphasized(double t) => Solve(t, 0.2, 0.0, 0.0, 1.0);

    /// <summary>emphasized-decelerate：cubic-bezier(0.05, 0.7, 0.1, 1.0)，进入/落座元素。</summary>
    public static double EmphasizedDecelerate(double t) => Solve(t, 0.05, 0.7, 0.1, 1.0);

    /// <summary>emphasized-accelerate：cubic-bezier(0.3, 0.0, 0.8, 0.15)，退出元素。</summary>
    public static double EmphasizedAccelerate(double t) => Solve(t, 0.3, 0.0, 0.8, 0.15);

    /// <summary>匀速（M3 fade 专用）：不透明度过渡不做缓动。</summary>
    public static readonly Easing LinearEasing = new LinearEasing();

    /// <summary>emphasized 的 Transitions 实例：悬浮/位移类微交互的标准曲线。</summary>
    public static readonly Easing EmphasizedEasing = new SplineEasing(0.2, 0.0, 0.0, 1.0);

    /// <summary>emphasized-decelerate 的 Transitions 实例：进入/落座。</summary>
    public static readonly Easing EmphasizedDecelerateEasing = new SplineEasing(0.05, 0.7, 0.1, 1.0);

    /// <summary>emphasized-accelerate 的 Transitions 实例：退出/收缩。</summary>
    public static readonly Easing EmphasizedAccelerateEasing = new SplineEasing(0.3, 0.0, 0.8, 0.15);

    /// <summary>cubic-bezier(x1,y1,x2,y2) 求值：二分法解 x 分量后取对应 y。
    /// M3 曲线的控制点 x 均处于 (0,1) 且曲线单调，可用二分。</summary>
    private static double Solve(double t, double x1, double y1, double x2, double y2)
    {
        switch (t)
        {
            case <= 0:
                return 0;
            case >= 1:
                return 1;
        }

        var lo = 0d;
        var hi = 1d;
        var time = t;
        for (var i = 0; i < 24; i++)
        {
            var x = Curve(time, x1, x2);
            if (Math.Abs(x - t) < 1e-6)
                break;

            if (x < t)
                lo = time;
            else
                hi = time;
            time = (lo + hi) / 2;
        }

        return Curve(time, y1, y2);
    }

    private static double Curve(double time, double p1, double p2)
    {
        var u = 1 - time;
        return 3 * u * u * time * p1 + 3 * u * time * time * p2 + time * time * time;
    }
}
