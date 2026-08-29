using System;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 功能区对外暴露的一条命令（按钮型动作）。
/// <para>
/// 当 <see cref="PolygonComponent"/> 为 <c>null</c> 时，宿主用传统矩形按钮渲染；
/// 否则用多边形组件渲染器渲染，此时 <see cref="Execute"/> 通常为 <c>null</c>，
/// 交互由组件实例的 <c>InvokeAsync</c> 处理。
/// </para>
/// </summary>
/// <param name="Id">
/// 动作标识，在全局范围内唯一（按忽略大小写比较）。
/// 个性化配置靠它引用按钮，<b>必须保持稳定</b>；第三方建议使用
/// <c>publisher.plugin/name</c> 形式。
/// </param>
/// <param name="Title">按钮显示标题。</param>
/// <param name="Description">按钮描述文字（副标题或 ToolTip）。</param>
/// <param name="Glyph">图标字符，支持 Material 前缀与 Emoji。</param>
/// <param name="Execute">点击回调；多边形组件动作留空。</param>
/// <param name="IsPrimary">是否使用强调色视觉样式。</param>
public sealed record FeatureAreaAction(
    string Id,
    string Title,
    string Description,
    string Glyph,
    Action? Execute = null,
    bool IsPrimary = false)
{
    /// <summary>
    /// 稳定的所有者标识：注册表用它挂起并热替换某插件贡献的全部动作，
    /// 以区别于用户的手动移除。内置组件此值为 <c>null</c>。
    /// </summary>
    public string? OwnerPluginId { get; init; }

    /// <summary>
    /// 休眠动作：插件组件当前不可用时，由启动器生成的占位动作。
    /// 刻意保留原 Id 与占位尺寸，使工作区成员与摆放位置在插件禁用期间得以保留。
    /// </summary>
    public bool IsDormant { get; init; }

    /// <summary>首选宽度（设备无关像素）。插件可在不改工作区布局契约的前提下覆盖。</summary>
    public double BaseWidth { get; init; } = 220;

    /// <summary>首选高度（设备无关像素）。</summary>
    public double BaseHeight { get; init; } = 82;

    /// <summary>
    /// 可选的声明式多边形组件。为 <c>null</c> 时沿用传统矩形按钮渲染路径。
    /// </summary>
    public PolygonComponentRegistration? PolygonComponent { get; init; }

    /// <summary>实际生效的首选宽度：有组件定义时取其 <c>PreferredSize.Width</c>，否则用 <see cref="BaseWidth"/>。</summary>
    public double EffectiveBaseWidth =>
        PolygonComponent?.Definition.PreferredSize.Width ?? BaseWidth;

    /// <summary>实际生效的首选高度：有组件定义时取其 <c>PreferredSize.Height</c>，否则用 <see cref="BaseHeight"/>。</summary>
    public double EffectiveBaseHeight =>
        PolygonComponent?.Definition.PreferredSize.Height ?? BaseHeight;
}
