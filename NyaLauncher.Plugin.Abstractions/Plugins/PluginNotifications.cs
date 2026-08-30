using System;
using System.Threading.Tasks;

namespace NyaLauncher.Plugin.Abstractions.Plugins;

/// <summary>通知严重级别：决定警示条/提示框的图标与主题色。</summary>
public enum PluginNoticeSeverity
{
    /// <summary>普通信息。</summary>
    Info,

    /// <summary>操作成功。</summary>
    Success,

    /// <summary>警告（确认对话框的默认级别）。</summary>
    Warning,

    /// <summary>错误或危险操作。</summary>
    Error,
}

/// <summary>
/// 提示框上的一个动作按钮。
/// <para><paramref name="Id"/> 作为 <see cref="IPluginNotifications.PromptAsync"/> 的返回值；
/// 省略时以 <paramref name="Label"/> 文字作为返回值。<paramref name="IsDefault"/> 只影响视觉强调样式。</para>
/// </summary>
/// <param name="Label">按钮显示文字（必填）。</param>
/// <param name="Id">可选的稳定标识；省略时用 <paramref name="Label"/> 作为返回值。</param>
/// <param name="IsDefault">是否为默认按钮（视觉强调）。</param>
public sealed record PluginPromptButton(string Label, string? Id = null, bool IsDefault = false)
{
    /// <summary>实际用于返回的标识：优先 <see cref="Id"/>，未设置时回退到 <see cref="Label"/>。</summary>
    public string ResolvedId => Id ?? Label;
}

/// <summary>
/// 启动器托管的通知服务：警示条（NyaAlert，底部滑入自动收回）与提示框
/// （NyaPrompt，Material 风对话框）。经 <see cref="IPluginContext.GetService{TService}"/>
/// 获取，需要 <see cref="PluginCapabilities.NativeUi"/> 能力授权；
/// 未授权时返回 <see langword="null"/>。全部方法可在任意线程调用。
/// </summary>
public interface IPluginNotifications
{
    /// <summary>显示一条警示条，展示数秒后自动收回；新警示会顶掉旧警示。</summary>
    /// <param name="severity">严重级别，决定图标与主题色。</param>
    /// <param name="message">提示文字。</param>
    /// <param name="duration">停留时长；省略时使用宿主默认值（约 4 秒）。</param>
    void Alert(PluginNoticeSeverity severity, string message, TimeSpan? duration = null);

    /// <summary>显示提示框并等待用户点击的按钮 Id（未传 Id 时为按钮文字；提示被新提示顶掉时为 <c>null</c>）。</summary>
    /// <param name="title">标题文字。</param>
    /// <param name="message">正文内容。</param>
    /// <param name="severity">严重级别，决定图标与主题色。</param>
    /// <param name="buttons">按钮组；省略时宿主显示单个「好的」。</param>
    Task<string?> PromptAsync(
        string title,
        string message = "",
        PluginNoticeSeverity severity = PluginNoticeSeverity.Info,
        params PluginPromptButton[] buttons);

    /// <summary>确认对话框：返回用户是否点击了确认（点了取消、宿主缺失或提示被顶掉返回 <c>false</c>）。</summary>
    /// <param name="title">标题文字。</param>
    /// <param name="message">正文内容。</param>
    /// <param name="severity">严重级别，默认 <see cref="PluginNoticeSeverity.Warning"/>。</param>
    Task<bool> ConfirmAsync(
        string title,
        string message = "",
        PluginNoticeSeverity severity = PluginNoticeSeverity.Warning);
}
