using System;
using System.Collections.Generic;
using Avalonia.Controls;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// A self-contained area that can be placed in the launcher workspace.
/// </summary>
public sealed class FeatureAreaDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>区域副标题。用户可在个性化窗口里改写，改写值优先于本属性。</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// 区域图标字符。支持 Material 图标前缀（如 <c>material:Apps</c>）与 Emoji。
    /// 用户可在个性化窗口里改用内置预设或本地图片。
    /// </summary>
    public string Glyph { get; init; } = "material:Apps";

    /// <summary>可选的本地图片路径；存在时优先于 <see cref="Glyph"/> 显示。</summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// 自定义内容工厂，返回任意 Avalonia 控件。
    /// 未设置时宿主改用内置动作视图渲染 <see cref="Actions"/>。
    /// </summary>
    public Func<Control>? ContentFactory { get; init; }

    /// <summary>
    /// 本区域提供的按钮型动作。这些动作会进入全局目录，
    /// 用户可以在个性化窗口里把它们挑到任意区域显示。
    /// </summary>
    public IReadOnlyList<FeatureAreaAction> Actions { get; init; } = [];

    /// <summary>
    /// 声明式多边形组件。注册时会被逐个校验并转成动作，
    /// 进入与 <see cref="Actions"/> 相同的全局目录，随后由宿主渲染。
    /// </summary>
    public IReadOnlyList<PolygonComponentRegistration> PolygonComponents { get; init; } = [];
}
