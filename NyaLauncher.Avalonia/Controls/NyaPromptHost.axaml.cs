using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Material.Icons.Avalonia;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Material 风提示对话框宿主：遮罩 + 居中卡片，嵌入 MainWindow（ZIndex 950）。
/// 出入场动效复用 OverlayEffects.PopIn / PopOut（M3 令牌，尊重 AnimationGate）。
/// 调用入口是 <see cref="NyaPrompt"/> 静态门面，本类只做展示与结果回传。
/// </summary>
public partial class NyaPromptHost : UserControl
{
    private TaskCompletionSource<string?>? _pending;
    private bool _closing;

    /// <summary>
    /// 初始化宿主并把它注册到 <see cref="NyaPrompt"/> 门面。
    /// 一个窗口内应只存在一个实例：后注册的宿主会顶掉先前的注册。
    /// </summary>
    public NyaPromptHost()
    {
        InitializeComponent();
        NyaPrompt.Register(this);
    }

    /// <summary>
    /// 展示提示框并等待用户点击的按钮。仅限 UI 线程调用（<see cref="NyaPrompt"/> 门面已负责封送）。
    /// <para>同一时刻只支持一个提示：若上一个提示尚未完成，其等待会立即以 <c>null</c> 结束，
    /// 界面就地切换为新请求的内容。</para>
    /// </summary>
    /// <param name="request">标题、正文、级别与按钮组。</param>
    /// <returns>
    /// 被点击按钮的 <see cref="NyaPromptButton.ResolvedId"/>；
    /// 该提示被新提示顶掉时返回 <c>null</c>。
    /// </returns>
    public Task<string?> ShowAsync(NyaPromptRequest request)
    {
        // 被新提示顶掉：旧等待立即以 null 完成，避免泄漏
        _pending?.TrySetResult(null);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        _closing = false;

        TitleText.Text = request.Title;
        MessageText.Text = request.Message;

        var (kind, brushKey) = NyaNoticeSeverities.Map(request.Severity);
        IconGlyph.Kind = kind;
        IconGlyph[!MaterialIcon.ForegroundProperty] = new DynamicResourceExtension(brushKey);

        ActionsPanel.Children.Clear();
        foreach (var button in request.Buttons)
        {
            var btn = new Button
            {
                Content = button.Label,
                Tag = button.ResolvedId,
            };
            btn.Classes.Add("PromptAction");
            if (button.IsDefault)
                btn.Classes.Add("PromptDefault");
            btn.Click += OnActionClick;
            ActionsPanel.Children.Add(btn);
        }

        // 兜底：清除上次 PopOut 残留的整层透明，避免第二次弹不出
        Opacity = 1;
        IsVisible = true;
        return tcs.Task;
    }

    private void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            Complete(id);
    }

    private void Complete(string? result)
    {
        if (_closing)
            return;
        _closing = true;

        OverlayEffects.PopOut(this, () =>
        {
            IsVisible = false;
            _pending?.TrySetResult(result);
            _pending = null;
            _closing = false;
        });
    }
}
