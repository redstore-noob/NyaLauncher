using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Material.Icons;
using NyaLauncher.Avalonia.Controls;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>通知严重级别：决定提示/警示的图标与主题色。</summary>
public enum NyaNoticeSeverity
{
    /// <summary>普通信息：Info 图标 + <c>InfoBrush</c>。</summary>
    Info,

    /// <summary>操作成功：CheckCircle 图标 + <c>SuccessBrush</c>。</summary>
    Success,

    /// <summary>警告（默认用于确认对话框）：Warning 图标 + <c>WarningBrush</c>。</summary>
    Warning,

    /// <summary>错误或危险操作：Error 图标 + <c>ErrorBrush</c>。</summary>
    Error,
}

internal static class NyaNoticeSeverities
{
    /// <summary>级别 → Material 图标 + 主题画刷键（画刷经 DynamicResource 绑定，主题切换实时跟随）。</summary>
    public static (MaterialIconKind Kind, string BrushKey) Map(NyaNoticeSeverity severity) => severity switch
    {
        NyaNoticeSeverity.Success => (MaterialIconKind.CheckCircle, "SuccessBrush"),
        NyaNoticeSeverity.Warning => (MaterialIconKind.Warning, "WarningBrush"),
        NyaNoticeSeverity.Error => (MaterialIconKind.Error, "ErrorBrush"),
        _ => (MaterialIconKind.Info, "InfoBrush"),
    };
}

/// <summary>
/// 提示框上的一个动作按钮。
/// <para><paramref name="Id"/> 作为 <see cref="NyaPrompt.ShowAsync"/> 的返回值；省略时以
/// <see cref="Label"/> 文字作为返回值。<paramref name="IsDefault"/> 只影响视觉强调样式。</para>
/// </summary>
/// <param name="Label">按钮显示文字（必填）。</param>
/// <param name="Id">可选的稳定标识；省略时用 <paramref name="Label"/> 作为返回值。</param>
/// <param name="IsDefault">是否为默认按钮（视觉强调）。</param>
public sealed record NyaPromptButton(string Label, string? Id = null, bool IsDefault = false)
{
    /// <summary>实际用于返回的标识：优先 <see cref="Id"/>，未设置时回退到 <see cref="Label"/>。</summary>
    public string ResolvedId => Id ?? Label;
}

/// <summary>提示框的一次完整请求（标题、正文、级别与按钮组），由门面交给宿主渲染。</summary>
/// <param name="Title">标题文字。</param>
/// <param name="Message">正文内容。</param>
/// <param name="Severity">严重级别，决定图标与主题色。</param>
/// <param name="Buttons">按钮组；为空时宿主会补一个默认的「好的」按钮。</param>
public sealed record NyaPromptRequest(
    string Title,
    string Message,
    NyaNoticeSeverity Severity,
    NyaPromptButton[] Buttons);

/// <summary>
/// 全局提示框门面：Material 风对话框，函数触发、嵌入主界面（见 MainWindow 的 NyaPromptHost）。
/// <code>
/// NyaPrompt.Show("已保存", "配置已写入 config.json");                     // 单按钮提示
/// var ok = await NyaPrompt.ConfirmAsync("删除实例", "该操作不可撤销");      // true / false
/// var id = await NyaPrompt.ShowAsync("选择", "选一个", NyaNoticeSeverity.Info,
///                                     new("甲"), new("乙", IsDefault: true));
/// </code>
/// 可在任意线程调用（内部自动封送 UI 线程）。
/// </summary>
public static class NyaPrompt
{
    private static NyaPromptHost? _host;

    /// <summary>
    /// 绑定提示框宿主。由 <see cref="NyaPromptHost"/> 构造函数自动调用，
    /// 业务代码<b>不要</b>手动注册——重复注册会让先前的宿主永远收不到请求。
    /// </summary>
    /// <param name="host">宿提示框宿主实例。</param>
    internal static void Register(NyaPromptHost host) => _host = host;

    /// <summary>展示提示框（不等待结果）。不传按钮时显示单个「好的」。</summary>
    /// <param name="title">标题文字。</param>
    /// <param name="message">正文内容。</param>
    /// <param name="severity">严重级别，决定图标与主题色。</param>
    /// <param name="buttons">按钮组；省略时显示单个「好的」。</param>
    public static void Show(
        string title,
        string message,
        NyaNoticeSeverity severity = NyaNoticeSeverity.Info,
        params NyaPromptButton[] buttons)
        => _ = ShowAsync(title, message, severity, buttons);

    /// <summary>展示提示框并等待用户点击的按钮 Id（未传 Id 时为按钮文字；宿主缺失/被顶掉时为 null）。</summary>
    /// <param name="title">标题文字。</param>
    /// <param name="message">正文内容。</param>
    /// <param name="severity">严重级别，决定图标与主题色。</param>
    /// <param name="buttons">按钮组；省略时显示单个「好的」。</param>
    /// <returns>
    /// 被点击按钮的 <see cref="NyaPromptButton.ResolvedId"/>；
    /// 宿主未注册<b>或</b>该提示被新提示顶掉时返回 <c>null</c>（旧等待立即完成，避免泄漏）。
    /// </returns>
    public static Task<string?> ShowAsync(
        string title,
        string message,
        NyaNoticeSeverity severity = NyaNoticeSeverity.Info,
        params NyaPromptButton[] buttons)
    {
        var host = _host;
        if (host is null)
            return Task.FromResult<string?>(null);

        var request = new NyaPromptRequest(title, message, severity, Normalize(buttons));
        if (Dispatcher.UIThread.CheckAccess())
            return host.ShowAsync(request);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                tcs.TrySetResult(await host.ShowAsync(request));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// 确认对话框：内部把按钮 Id 固定为 <c>cancel</c> / <c>confirm</c>，
    /// 返回用户是否点击了确认。可在任意线程调用。
    /// </summary>
    /// <param name="title">标题文字。</param>
    /// <param name="message">正文内容。</param>
    /// <param name="confirm">确认按钮文字。</param>
    /// <param name="cancel">取消按钮文字。</param>
    /// <param name="severity">严重级别，默认 <see cref="NyaNoticeSeverity.Warning"/>。</param>
    /// <returns>点了确认返回 <c>true</c>，点了取消或宿主缺失返回 <c>false</c>。</returns>
    public static Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirm = "确定",
        string cancel = "取消",
        NyaNoticeSeverity severity = NyaNoticeSeverity.Warning)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ShowAsync(title, message, severity,
                new NyaPromptButton(cancel, "cancel"),
                new NyaPromptButton(confirm, "confirm", IsDefault: true))
            .ContinueWith(
                t => tcs.TrySetResult(string.Equals(t.Result, "confirm", StringComparison.Ordinal)),
                TaskContinuationOptions.ExecuteSynchronously);
        return tcs.Task;
    }

    private static NyaPromptButton[] Normalize(NyaPromptButton[] buttons)
        => buttons is { Length: > 0 }
            ? buttons
            : [new NyaPromptButton("好的", IsDefault: true)];
}

/// <summary>
/// 全局警示条门面：窗口底部左侧滑入的小滑条，展示数秒后自动收回（见 MainWindow 的 NyaAlertHost）。
/// <code>
/// NyaAlert.Success("实例创建完成");
/// NyaAlert.Error("网络请求失败", TimeSpan.FromSeconds(8));   // 自定义停留时长
/// </code>
/// 可在任意线程调用；新警示会顶掉旧警示（就地换文案，不重播动画）。
/// </summary>
public static class NyaAlert
{
    /// <summary>默认停留时长：4 秒。</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    private static NyaAlertHost? _host;

    /// <summary>
    /// 绑定警示条宿主。由 <see cref="NyaAlertHost"/> 构造函数自动调用，
    /// 业务代码<b>不要</b>手动注册。
    /// </summary>
    /// <param name="host">警示条宿主实例。</param>
    internal static void Register(NyaAlertHost host) => _host = host;

    /// <summary>显示一条信息级警示条。</summary>
    /// <param name="message">提示文字。</param>
    /// <param name="duration">停留时长；省略时使用 <see cref="DefaultDuration"/>。</param>
    public static void Info(string message, TimeSpan? duration = null) =>
        Show(message, NyaNoticeSeverity.Info, duration);

    /// <summary>显示一条成功级警示条（绿色对勾）。</summary>
    /// <param name="message">提示文字。</param>
    /// <param name="duration">停留时长；省略时使用 <see cref="DefaultDuration"/>。</param>
    public static void Success(string message, TimeSpan? duration = null) =>
        Show(message, NyaNoticeSeverity.Success, duration);

    /// <summary>显示一条警告级警示条（黄色感叹号）。</summary>
    /// <param name="message">提示文字。</param>
    /// <param name="duration">停留时长；省略时使用 <see cref="DefaultDuration"/>。</param>
    public static void Warning(string message, TimeSpan? duration = null) =>
        Show(message, NyaNoticeSeverity.Warning, duration);

    /// <summary>显示一条错误级警示条（红色错误图标）。</summary>
    /// <param name="message">提示文字。</param>
    /// <param name="duration">停留时长；省略时使用 <see cref="DefaultDuration"/>。</param>
    public static void Error(string message, TimeSpan? duration = null) =>
        Show(message, NyaNoticeSeverity.Error, duration);

    /// <summary>
    /// 显示一条自定义级别的警示条。宿主未注册时静默返回。
    /// 新警示会<b>顶掉</b>旧警示：就地换文案并重置倒计时，不重播滑入动画。
    /// </summary>
    /// <param name="message">提示文字。</param>
    /// <param name="severity">严重级别，决定图标与主题色。</param>
    /// <param name="duration">停留时长；省略时使用 <see cref="DefaultDuration"/>。</param>
    public static void Show(
        string message,
        NyaNoticeSeverity severity = NyaNoticeSeverity.Info,
        TimeSpan? duration = null)
    {
        var host = _host;
        if (host is null)
            return;
        Dispatcher.UIThread.Post(() => host.Show(message, severity, duration ?? DefaultDuration));
    }
}
