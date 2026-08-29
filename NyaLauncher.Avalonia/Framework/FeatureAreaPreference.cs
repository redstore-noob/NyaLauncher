using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 单个功能区的用户个性化设置，会被序列化进 <c>workspace.json</c>。
/// <para>
/// <see cref="ActionIds"/> 引用的是<b>全局动作目录</b>里的 Id，而不是区域自带的按钮列表，
/// 因此同一个动作可以出现在多个区域，也不会把动作本身的委托或状态写进配置。
/// </para>
/// </summary>
public sealed class FeatureAreaPreference
{
    /// <summary>对应的功能区 Id；为空的条目在应用偏好时会被跳过。</summary>
    public string AreaId { get; set; } = string.Empty;

    /// <summary>用户自定义的显示名称；为空时回退到区域定义的 <c>Title</c>。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>用户自定义的副标题；为空时回退到区域定义的 <c>Subtitle</c>。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>用户选择的图标预设字符；为空时回退到区域定义的 <c>Glyph</c>。</summary>
    public string IconGlyph { get; set; } = "material:Apps";

    /// <summary>用户选择的本地图标图片路径；图片失效时自动回退到 <see cref="IconGlyph"/>。</summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// 该区域要显示哪些动作，按 Id 引用全局目录。
    /// 引用了不存在 Id 的项会被忽略，不会中断其它项的应用。
    /// </summary>
    public List<string> ActionIds { get; set; } = [];
}
