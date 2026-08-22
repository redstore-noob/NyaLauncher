using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Dialogs;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>
/// 账户管理页面：集中管理正版（微软）与离线账号。
/// 列表、头像、默认账号均与 <see cref="AccountStore"/> 双向同步，
/// 添加账号复用 <see cref="AccountLoginOverlay"/>，外观编辑复用
/// <see cref="MinecraftAppearanceEditor"/> 与 <see cref="OfflineSkinCatalog"/>。
/// </summary>
public partial class AccountManagePage : UserControl
{
    private readonly MinecraftProfileService _profileService;
    private CancellationTokenSource? _avatarCancellation;

    public AccountManagePage() : this(new MinecraftProfileService())
    {
    }

    internal AccountManagePage(MinecraftProfileService profileService)
    {
        ArgumentNullException.ThrowIfNull(profileService);
        _profileService = profileService;
        InitializeComponent();
        AccountStore.Changed += OnAccountsChanged;
        AccountLoginOverlay.AccountAdded += OnAccountAdded;
        RefreshAccountList();
    }

    /// <summary>供外部（如工作区组件）直接打开登录遮罩。</summary>
    public void ShowAccountLogin() => AccountLoginOverlay.Show();

    // ------------------------------------------------------------------
    // 列表构建与同步
    // ------------------------------------------------------------------

    private void OnAccountAdded(object? sender, LaunchAccount account)
    {
        // AccountStore.Add 会先触发 Changed 刷新列表，这里只补充提示文案。
        StatusText.Text = $"已添加账号：{account.DisplayName}";
    }

    private void OnAccountsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnAccountsChanged);
            return;
        }

        RefreshAccountList();
    }

    /// <summary>
    /// 以当前内存中的账号列表重建 ListBox。保留用户正在查看的选中项；
    /// 没有选中时自动选中默认账号（列表首项）。
    /// </summary>
    private void RefreshAccountList()
    {
        var selectedKey = (AccountList.SelectedItem as AccountListItem)?.StableKey;
        var items = AccountStore.Current.Select(CreateItem).ToList();

        AccountList.ItemsSource = items;
        AccountList.SelectedItem =
            (selectedKey is not null
                ? items.FirstOrDefault(item => string.Equals(
                    item.StableKey,
                    selectedKey,
                    StringComparison.OrdinalIgnoreCase))
                : null)
            ?? items.FirstOrDefault(item => item.IsDefault)
            ?? items.FirstOrDefault();

        EmptyHintPanel.IsVisible = items.Count == 0;
        UpdateToolbar();

        // 后台逐个加载头像贴图（离线默认皮肤 / 正版档案皮肤）
        _ = EnrichAvatarsAsync(items);
    }

    private static AccountListItem CreateItem(LaunchAccount account) => new()
    {
        Account = account,
        AvatarFallback = GetFallbackText(account.DisplayName),
        Detail = CreateDetail(account),
        IsDefault = ReferenceEquals(AccountStore.Selected, account)
    };

    private static string CreateDetail(LaunchAccount account) => account.Type switch
    {
        "microsoft" when account.Microsoft is { } microsoft =>
            microsoft.IsExpired
                ? "正版账户 · 令牌已过期，启动游戏时会自动刷新"
                : $"正版账户 · 令牌有效期至 {microsoft.ExpiresAt:yyyy-MM-dd HH:mm}",
        "offline" =>
            $"离线账户 · 默认皮肤：{OfflineSkinCatalog.Get(account.OfflineSkinId).DisplayName}",
        _ => account.TypeLabel
    };

    /// <summary>
    /// 逐个解析账号头像源：离线账号来自 <see cref="OfflineSkinCatalog"/>，
    /// 正版账号来自 <see cref="MinecraftProfileService"/> 的档案皮肤。
    /// 解析结果写入 <see cref="AccountListItem.AvatarSource"/>，DataTemplate 绑定自动刷新。
    /// </summary>
    private async Task EnrichAvatarsAsync(IReadOnlyList<AccountListItem> items)
    {
        _avatarCancellation?.Cancel();
        _avatarCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _avatarCancellation = cancellation;

        foreach (var item in items)
        {
            if (cancellation.IsCancellationRequested)
                return;

            try
            {
                string? source;
                if (item.Account.Type == "offline")
                {
                    source = await OfflineSkinCatalog.ResolveTextureSourceAsync(
                            item.Account.OfflineSkinId,
                            cancellation.Token)
                        .ConfigureAwait(false);
                }
                else if (item.Account.Type == "microsoft" && item.Account.Microsoft is not null)
                {
                    var profile = await _profileService.GetProfileAsync(
                            item.Account,
                            cancellation.Token)
                        .ConfigureAwait(false);
                    source = profile.ActiveSkin?.Url;
                }
                else
                {
                    continue;
                }

                if (cancellation.IsCancellationRequested)
                    return;

                // 回到 UI 线程更新可观察属性
                await Dispatcher.UIThread.InvokeAsync(() => item.AvatarSource = source);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // 单个账号头像加载失败不影响其它账号与页面功能
                System.Diagnostics.Debug.WriteLine($"账号头像加载失败：{exception}");
            }
        }
    }

    // ------------------------------------------------------------------
    // 工具栏操作
    // ------------------------------------------------------------------

    private void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        AccountLoginOverlay.Show();
    }

    private void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedAccount();
        if (selected is null)
        {
            StatusText.Text = "请先选择一个账号。";
            return;
        }

        AccountStore.MoveToTop(selected);
        StatusText.Text = $"已设为默认：{selected.DisplayName}";
    }

    private void OnDeleteAccountClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedAccount();
        if (selected is null)
        {
            StatusText.Text = "请先选择一个账号。";
            return;
        }

        AccountStore.Remove(selected);
        StatusText.Text = AccountStore.Current.Count == 0
            ? $"已删除账号：{selected.DisplayName}（账号列表已清空）"
            : $"已删除账号：{selected.DisplayName}";
    }

    private async void OnChangeSkinClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedAccount();
        if (selected is null)
        {
            StatusText.Text = "请先选择一个账号。";
            return;
        }

        try
        {
            if (selected.Type == "offline")
                await ChangeOfflineSkinAsync(selected);
            else if (selected.Type == "microsoft")
                await ChangeMicrosoftSkinAsync(selected);
            else
                StatusText.Text = "该账号暂不支持编辑皮肤。";
        }
        catch (Exception exception)
        {
            // async void 中未捕获的异常会直接中断进程，这里统一转成提示
            StatusText.Text = $"更换皮肤失败：{exception.Message}";
        }
    }

    private async void OnChangeCapeClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedAccount();
        if (selected?.Type != "microsoft" || selected.Microsoft is null)
        {
            StatusText.Text = "更换披风仅支持正版账号。";
            return;
        }

        if (GetOwnerWindow() is not { } owner)
            return;

        try
        {
            StatusText.Text = "正在读取披风列表…";
            var result = await MinecraftAppearanceEditor.ChangeCapeAsync(
                owner,
                _profileService,
                selected);
            StatusText.Text = result.Message;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"更换披风失败：{exception.Message}";
        }
    }

    private void OnAccountSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateToolbar();
    }

    private async Task ChangeOfflineSkinAsync(LaunchAccount account)
    {
        if (GetOwnerWindow() is not { } owner)
            return;

        // 离线默认皮肤选择对话框（复用 OfflineSkinCatalog 的 9 款内置皮肤）
        var dialog = new OfflineSkinPickerDialog(account.OfflineSkinId);
        var choice = await dialog.ShowDialog<OfflineSkinChoice?>(owner);
        if (choice is null)
            return;

        AccountStore.UpdateOfflineSkin(account, choice.Id);
        StatusText.Text = $"已选择离线默认皮肤：{choice.DisplayName}";
    }

    private async Task ChangeMicrosoftSkinAsync(LaunchAccount account)
    {
        if (GetOwnerWindow() is not { } owner)
            return;

        StatusText.Text = "正在准备上传皮肤…";
        var result = await MinecraftAppearanceEditor.ChangeSkinAsync(
            owner,
            _profileService,
            account);
        StatusText.Text = result.Message;
        if (result.Success)
            RefreshAccountList();
    }

    /// <summary>同步工具栏与底部状态：当前选中账号、默认标记、披风按钮可用性。</summary>
    private void UpdateToolbar()
    {
        var selected = GetSelectedAccount();
        if (selected is null)
        {
            SelectedAccountText.Text = "未选择账号";
            SelectedAccountDetailText.Text = "请选择一个账号，再使用右侧工具按钮";
            SelectedDefaultBadge.IsVisible = false;
            ChangeCapeButton.IsEnabled = false;
            return;
        }

        SelectedAccountText.Text = selected.DisplayName;
        SelectedAccountDetailText.Text = CreateDetail(selected);
        SelectedDefaultBadge.IsVisible = ReferenceEquals(AccountStore.Selected, selected);
        ChangeCapeButton.IsEnabled = selected.Type == "microsoft";
    }

    private LaunchAccount? GetSelectedAccount() =>
        (AccountList.SelectedItem as AccountListItem)?.Account;

    private Window? GetOwnerWindow() => TopLevel.GetTopLevel(this) as Window;

    private static string GetFallbackText(string value) =>
        string.IsNullOrWhiteSpace(value) ? "?" : value.Trim()[..1].ToUpperInvariant();

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = AccountStore.Current.Count == 0
            ? "还没有账号，点击「＋ 添加账户」开始。"
            : $"共 {AccountStore.Current.Count} 个账号，列表首项为默认账号。";
        RefreshAccountList();
    }
}

/// <summary>
/// 账号列表项：包装 <see cref="LaunchAccount"/> 并暴露 DataTemplate 所需字段。
/// <see cref="AvatarSource"/> 为可观察属性，后台解析到头像贴图后 UI 自动更新。
/// </summary>
internal sealed class AccountListItem : INotifyPropertyChanged
{
    private string? _avatarSource;

    /// <summary>对应的账号对象，供操作按钮回读。</summary>
    public required LaunchAccount Account { get; init; }

    /// <summary>持久身份键，用于列表重建时保持选中项。</summary>
    public string StableKey => AccountStore.GetStableKey(Account);

    public string DisplayName => Account.DisplayName;

    public string TypeLabel => Account.TypeLabel;

    public string AvatarFallback { get; init; } = "?";

    public string Detail { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    /// <summary>头像贴图源（本地文件路径或远程 URL）；设置后触发 UI 刷新。</summary>
    public string? AvatarSource
    {
        get => _avatarSource;
        set
        {
            if (_avatarSource == value)
                return;
            _avatarSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarSource)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
