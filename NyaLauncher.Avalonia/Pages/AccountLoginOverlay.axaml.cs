using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>
/// 可复用的"新建账户"遮罩：正版（设备码登录）或离线（输入名字）。
/// 添加成功后写入 <see cref="AccountStore"/> 并触发 <see cref="AccountAdded"/> 事件。
/// </summary>
public partial class AccountLoginOverlay : UserControl
{
    private readonly IMicrosoftAuthenticator _authenticator = new MicrosoftDeviceCodeAuthenticator();
    private CancellationTokenSource? _deviceCodeCancellation;

    /// <summary>账号添加成功后触发（此时已持久化）。</summary>
    public event EventHandler<LaunchAccount>? AccountAdded;

    public AccountLoginOverlay()
    {
        InitializeComponent();
    }

    public void Show()
    {
        ResetToMainView();
        OverlayRoot.IsVisible = true;
    }

    public void Hide() => OverlayRoot.IsVisible = false;

    private void ResetToMainView()
    {
        MainView.IsVisible = true;
        DeviceCodeView.IsVisible = false;
        OfflineAddPanel.IsVisible = false;
        HintText.Text = string.Empty;
        DeviceCodeStatusText.Text = string.Empty;
        NewOfflineNameBox.Text = "Player_01";
    }

    private void OnAddMicrosoftClick(object? sender, RoutedEventArgs e)
    {
        MainView.IsVisible = false;
        DeviceCodeView.IsVisible = true;
        _ = BeginMicrosoftLoginAsync();
    }

    private void OnAddOfflineClick(object? sender, RoutedEventArgs e)
    {
        HintText.Text = string.Empty;
        OfflineAddPanel.IsVisible = true;
        NewOfflineNameBox.Focus();
    }

    private void OnConfirmOfflineClick(object? sender, RoutedEventArgs e)
    {
        var name = NewOfflineNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            HintText.Text = "请输入离线用户名。";
            return;
        }

        if (AccountStore.HasOfflineName(name))
        {
            HintText.Text = $"已存在同名离线账号：{name}";
            return;
        }

        var account = new LaunchAccount
        {
            Type = "offline",
            DisplayName = name,
            OfflineName = name
        };
        AccountStore.Add(account);
        Hide();
        AccountAdded?.Invoke(this, account);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Hide();

    private void OnBackToMainClick(object? sender, RoutedEventArgs e)
    {
        _deviceCodeCancellation?.Cancel();
        ResetToMainView();
    }

    private void OnCancelDeviceCodeClick(object? sender, RoutedEventArgs e)
    {
        _deviceCodeCancellation?.Cancel();
        ResetToMainView();
    }

    private async Task BeginMicrosoftLoginAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _deviceCodeCancellation = cancellation;

        try
        {
            var account = await _authenticator.AuthenticateAsync(
                async (info, _) =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DeviceCodeHintText.Text =
                            "请在浏览器中打开以下地址，然后输入验证码";
                        DeviceCodeText.Text = info.UserCode;
                        DeviceCodeUrlText.Text = info.VerificationUri;
                        DeviceCodeStatusText.Text = string.Empty;
                    });

                    try
                    {
                        Process.Start(new ProcessStartInfo(
                            info.VerificationUriFull.ToString())
                        {
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // 自动打开浏览器失败时，用户仍可点击"打开浏览器"。
                    }
                },
                cancellation.Token);

            var entry = new LaunchAccount
            {
                Type = "microsoft",
                DisplayName = account.Username,
                Microsoft = account
            };
            AccountStore.Add(entry);
            Hide();
            AccountAdded?.Invoke(this, entry);
        }
        catch (Exception ex) when (
            ex is MicrosoftAuthenticationException or OperationCanceledException)
        {
            ResetToMainView();
            HintText.Text = ex is OperationCanceledException
                ? "已取消微软账号登录。"
                : $"微软账号登录失败：{ex.Message}";
        }
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs e)
    {
        var codeText = DeviceCodeText.Text;
        if (string.IsNullOrWhiteSpace(codeText))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(
                $"https://www.microsoft.com/link?user_code={codeText}")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            DeviceCodeStatusText.Text = "打开浏览器失败，请手动访问。";
        }
    }
}
