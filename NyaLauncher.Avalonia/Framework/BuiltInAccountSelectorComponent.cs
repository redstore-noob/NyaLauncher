using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Launch.Auth;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInAccountSelectorComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/account-selector</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/account-selector";
    private const string AddAccountActionId = "add-account";
    private const string SelectAccountActionId = "select-account";
    private const string RefreshAvatarActionId = "refresh-avatar";
    private const string AccountKeyArgument = "accountKey";

    public static PolygonComponentRegistration Create(
        Action<string> navigate,
        MinecraftProfileService profileService)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        ArgumentNullException.ThrowIfNull(profileService);

        var definition = new PolygonComponentBuilder(ComponentId, "账号选择")
            .WithDescription("查看当前账号与登录模式，并快速添加或切换账号")
            .WithGlyph("material:AccountCircle")
            .WithSize(260, 72)
            .WithSizeLimits(220, 64, 360, 92)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.025, 0.24, 0.075, 0.52))
            .WithTheme(new PolygonComponentTheme())
            .AddAction(AddAccountActionId)
            .AddAction(SelectAccountActionId)
            .AddAction(RefreshAvatarActionId)
            .AddImage(
                "account-glyph",
                new ComponentRect(0.115, 0.25, 0.085, 0.5),
                fallbackText: "material:AccountCircle",
                stretch: ComponentImageStretch.Uniform,
                cornerRadius: 6,
                pixelated: true,
                // 皮肤贴图自动合成为双层头像（脸层 + 帽层）
                isSkinHead: true)
            .AddText(
                "account-name",
                new ComponentRect(0.215, 0.17, 0.59, 0.34),
                "Player_01",
                ComponentTextRole.Title,
                fontSize: 14)
            .AddText(
                "login-mode",
                new ComponentRect(0.215, 0.52, 0.59, 0.24),
                "离线登录",
                ComponentTextRole.Caption,
                fontSize: 10)
            .AddDropdown(
                "account-menu",
                new ComponentRect(0.84, 0.22, 0.115, 0.56),
                pinnedItems:
                [
                    new ComponentMenuItem
                    {
                        Id = "add-account",
                        Text = "账号管理",
                        SecondaryText = "点击此处进入账号管理界面。",
                        Glyph = "material:Cog",
                        ActionId = AddAccountActionId,
                        SeparatorAfter = true
                    },
                    new ComponentMenuItem
                    {
                        Id = "refresh-avatar",
                        Text = "刷新头像",
                        SecondaryText = "重新下载并缓存所有账号的皮肤头像。",
                        Glyph = "material:Refresh",
                        ActionId = RefreshAvatarActionId,
                        SeparatorAfter = true
                    }
                ])
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new AccountSelectorInstance(navigate, profileService))
        };
    }

    private sealed class AccountSelectorInstance : PolygonComponentInstanceBase
    {
        private readonly Action<string> _navigate;
        private MinecraftProfileService _profileService;
        private CancellationTokenSource? _avatarCancellation;
        private long _avatarRefreshVersion;
        private IReadOnlyDictionary<string, string>? _lastSkinByKey;

        public AccountSelectorInstance(
            Action<string> navigate,
            MinecraftProfileService profileService)
        {
            _navigate = navigate;
            _profileService = profileService;
            SetState(CreateState());
            AccountStore.Changed += OnAccountsChanged;
            StartAvatarLoad();
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("账号选择组件已释放。");

            cancellationToken.ThrowIfCancellationRequested();
            switch (invocation.ActionId)
            {
                case RefreshAvatarActionId:
                    RefreshAvatars();
                    return ComponentActionResult.Completed("正在刷新账号头像…");

                case AddAccountActionId:
                    await Dispatcher.UIThread.InvokeAsync(() => _navigate("account-login"));
                    return ComponentActionResult.Completed();

                case SelectAccountActionId:
                    if (invocation.Arguments is null ||
                        !invocation.Arguments.TryGetValue(AccountKeyArgument, out var accountKey))
                    {
                        return ComponentActionResult.Failed("账号菜单项缺少账号标识。");
                    }

                    var switched = await Dispatcher.UIThread.InvokeAsync(
                        () => AccountStore.SelectByStableKey(accountKey));
                    return switched
                        ? ComponentActionResult.Completed("已切换当前账号。")
                        : ComponentActionResult.Failed("该账号已不存在，请重新打开菜单。");

                default:
                    return ComponentActionResult.Failed($"未知账号组件动作：{invocation.ActionId}");
            }
        }

        public override ValueTask DisposeAsync()
        {
            AccountStore.Changed -= OnAccountsChanged;
            _avatarCancellation?.Cancel();
            _avatarCancellation?.Dispose();
            return base.DisposeAsync();
        }

        private void OnAccountsChanged()
        {
            if (IsDisposed)
                return;

            SetState(CreateState());
            StartAvatarLoad();
        }

        /// <summary>
        /// 后台逐个解析账号皮肤头像：离线账号来自 <see cref="OfflineSkinCatalog"/>，
        /// 正版账号来自 <see cref="MinecraftProfileService"/> 的档案皮肤（复用账户管理页逻辑）。
        /// </summary>
        private void StartAvatarLoad()
        {
            _avatarCancellation?.Cancel();
            _avatarCancellation?.Dispose();
            if (IsDisposed)
                return;

            var cancellation = new CancellationTokenSource();
            _avatarCancellation = cancellation;
            _ = EnrichAvatarsAsync(cancellation);
        }

        /// <summary>
        /// 手动刷新：清空已加载头像的本地缓存并强制重新解析、重新下载。
        /// </summary>
        private void RefreshAvatars()
        {
            if (_lastSkinByKey is { } skins)
            {
                foreach (var source in skins.Values)
                {
                    if (!string.IsNullOrWhiteSpace(source))
                        ComponentImageLoader.InvalidateRemoteCache(source);
                }
            }

            Interlocked.Increment(ref _avatarRefreshVersion);
            StartAvatarLoad();
        }

        private async Task EnrichAvatarsAsync(CancellationTokenSource cancellation)
        {
            try
            {
                var skinByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var account in AccountStore.Current)
                {
                    if (cancellation.IsCancellationRequested ||
                        IsDisposed)
                    {
                        return;
                    }

                    try
                    {
                        string? source;
                        if (account.Type == "offline")
                        {
                            source = await OfflineSkinCatalog.ResolveTextureSourceAsync(
                                    account.OfflineSkinId,
                                    cancellation.Token)
                                .ConfigureAwait(false);
                        }
                        else if (account.Type == "microsoft" && account.Microsoft is not null)
                        {
                            var profile = await _profileService.GetProfileAsync(
                                    account,
                                    cancellation.Token)
                                .ConfigureAwait(false);
                            source = profile.ActiveSkin?.Url;
                        }
                        else
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(source))
                        {
                            skinByKey[AccountStore.GetStableKey(account)] = source;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        // 单个账号皮肤头像加载失败不影响其它账号与菜单功能
                        System.Diagnostics.Debug.WriteLine($"账号皮肤头像加载失败：{exception}");
                    }
                }

                if (cancellation.IsCancellationRequested ||
                    IsDisposed)
                {
                    return;
                }

                _lastSkinByKey = skinByKey;
                SetState(CreateState(skinByKey));
            }
            catch (OperationCanceledException)
            {
            }
        }

        private ComponentStateSnapshot CreateState(
            IReadOnlyDictionary<string, string>? skinByKey = null)
        {
            var selected = AccountStore.Selected;
            var selectedKey = selected is not null
                ? AccountStore.GetStableKey(selected)
                : null;
            var selectedSkin = selectedKey is not null &&
                               skinByKey is not null &&
                               skinByKey.TryGetValue(selectedKey, out var skin)
                ? skin
                : null;
            var accountItems = AccountStore.Current
                .Select((account, index) =>
                {
                    var key = AccountStore.GetStableKey(account);
                    return new ComponentMenuItem
                    {
                        Id = $"account-{index}",
                        Text = account.DisplayName,
                        SecondaryText = account.LoginModeLabel,
                        // 玩家皮肤作为头像（异步解析后填充 IconSource），未加载到时回退登录类型字形
                        Glyph = account.Type switch
                        {
                            "microsoft" => "material:Diamond",
                            "offline" => "material:CircleOutline",
                            _ => "material:DiamondOutline"
                        },
                        IconSource = skinByKey is not null &&
                                     skinByKey.TryGetValue(key, out var skin)
                            ? skin
                            : null,
                        // 皮肤贴图只显示脸部头像区域
                        IsSkinHead = true,
                        ActionId = SelectAccountActionId,
                        Arguments = new Dictionary<string, string>
                        {
                            [AccountKeyArgument] = key
                        },
                        IsSelected = ReferenceEquals(account, selected)
                    };
                })
                .ToArray();

            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["account-glyph"] = new()
                    {
                        // 未加载到头像时回退账号图标
                        Text = "material:AccountCircle",
                        // 玩家皮肤作为头像（左上 1/8 脸部，Image 元素自带裁剪与像素风）
                        ImageSource = selectedSkin,
                        // 手动刷新时递增，强制重新下载头像
                        ImageRefreshToken = Interlocked.Read(ref _avatarRefreshVersion)
                    },
                    ["account-name"] = new()
                    {
                        Text = selected?.DisplayName ?? "未选择账号"
                    },
                    ["login-mode"] = new()
                    {
                        Text = selected?.LoginModeLabel ?? "请添加账号"
                    },
                    ["account-menu"] = new()
                    {
                        MenuItems = accountItems
                    }
                }
            };
        }
    }
}
