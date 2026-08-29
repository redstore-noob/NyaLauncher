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

internal static class BuiltInSkinCapeComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/skin-cape-editor</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/skin-cape-editor";
    private const string ChangeSkinActionId = "change-skin";
    private const string ChangeCapeActionId = "change-cape";
    private const string SelectOfflineSkinActionId = "select-offline-skin";
    private const string RefreshAvatarActionId = "refresh-avatar";
    private const string SelectDisplayAccountActionId = "select-display-account";
    private const string ResetDisplayAccountActionId = "reset-display-account";
    private const string AccountKeyArgument = "accountKey";
    private const string SkinIdArgument = "skinId";

    /// <summary>
    /// 正版账号的换肤/换披风涉及原生文件选择对话框与模态窗口，
    /// 在多边形组件的后台线程中直接弹出容易卡死，因此统一跳转到账户管理页面，
    /// 由页面在 UI 线程上完成操作。离线默认皮肤的选择仍在此组件内联完成。
    /// </summary>
    public static PolygonComponentRegistration Create(
        MinecraftProfileService profileService,
        Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(profileService);
        ArgumentNullException.ThrowIfNull(navigate);

        var definition = new PolygonComponentBuilder(ComponentId, "皮肤与披风")
            .WithDescription("显示当前皮肤头像，并编辑正版皮肤、正版披风或离线默认皮肤")
            .WithGlyph("material:TshirtCrew")
            .WithSize(96, 96)
            .WithSizeLimits(72, 72, 160, 160)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.04, 0.04, 0.24, 0.24))
            .WithTheme(new PolygonComponentTheme())
            .AddAction(ChangeSkinActionId)
            .AddAction(ChangeCapeActionId)
            .AddAction(SelectOfflineSkinActionId)
            .AddAction(RefreshAvatarActionId)
            .AddAction(SelectDisplayAccountActionId)
            .AddAction(ResetDisplayAccountActionId)
            .AddImage(
                "skin-face",
                new ComponentRect(0.08, 0.08, 0.84, 0.84),
                sourcePixelRect: new ComponentPixelRect(8, 8, 8, 8),
                fallbackText: "?",
                cornerRadius: 11,
                pixelated: true)
            .AddImage(
                "skin-hat",
                new ComponentRect(0.08, 0.08, 0.84, 0.84),
                sourcePixelRect: new ComponentPixelRect(40, 8, 8, 8),
                fallbackText: string.Empty,
                cornerRadius: 11,
                pixelated: true)
            .AddDropdown(
                "appearance-menu",
                new ComponentRect(0.03, 0.03, 0.94, 0.94),
                glyph: string.Empty)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new SkinCapeInstance(profileService, navigate))
        };
    }

    private sealed class SkinCapeInstance : PolygonComponentInstanceBase
    {
        private readonly MinecraftProfileService _profileService;
        private readonly Action<string> _navigate;
        private readonly object _stateGate = new();
        private CancellationTokenSource? _appearanceRefresh;
        private string? _appearanceAccountKey;
        private string? _appearanceSkinId;
        private string? _skinSource;
        private long _avatarRefreshVersion;

        public SkinCapeInstance(
            MinecraftProfileService profileService,
            Action<string> navigate)
        {
            _profileService = profileService;
            _navigate = navigate;
            SetState(CreateState());
            AccountStore.Changed += OnAccountsChanged;
            _profileService.AppearanceChanged += OnAppearanceChanged;
            BeginAppearanceRefresh();
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("皮肤与披风组件已释放。");

            // 组件可独立选择展示的账号（未设置时跟随全局当前账号）
            var selected = ComponentDisplayAccount.Resolve(ComponentId);
            if (selected is null)
                return ComponentActionResult.Failed("请先添加并选择一个账号。");
            var accountKey = AccountStore.GetStableKey(selected);

            switch (invocation.ActionId)
            {
                // 手动刷新头像：失效本地缓存并强制重新下载
                case RefreshAvatarActionId when selected.Type == "microsoft":
                    RefreshAvatar();
                    return ComponentActionResult.Completed("正在重新下载并缓存皮肤头像…");

                // 更换本组件展示的账号（可独立于全局当前账号）
                case SelectDisplayAccountActionId:
                    if (invocation.Arguments is null ||
                        !invocation.Arguments.TryGetValue(AccountKeyArgument, out var displayAccountKey))
                    {
                        return ComponentActionResult.Failed("账号菜单项缺少账号标识。");
                    }

                    ComponentDisplayAccount.SetKey(ComponentId, displayAccountKey);
                    Publish();
                    BeginAppearanceRefresh();
                    return ComponentActionResult.Completed("已切换展示账号。");

                case ResetDisplayAccountActionId:
                    ComponentDisplayAccount.SetKey(ComponentId, null);
                    Publish();
                    BeginAppearanceRefresh();
                    return ComponentActionResult.Completed("已恢复展示全局当前账号。");

                // 正版换肤 / 换披风：跳转到账户管理页面完成（避免在组件后台线程弹原生对话框卡死）
                case ChangeSkinActionId when selected.Type == "microsoft":
                case ChangeCapeActionId when selected.Type == "microsoft":
                    await Dispatcher.UIThread.InvokeAsync(() => _navigate("account"));
                    return ComponentActionResult.Completed(
                        "已打开账户管理页面，请在那里完成皮肤或披风操作。");

                case SelectOfflineSkinActionId when selected.Type == "offline":
                    if (invocation.Arguments is null ||
                        !invocation.Arguments.TryGetValue(AccountKeyArgument, out var requestedAccountKey) ||
                        !invocation.Arguments.TryGetValue(SkinIdArgument, out var skinId) ||
                        !string.Equals(requestedAccountKey, accountKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return ComponentActionResult.Failed("离线皮肤菜单已经失效，请重新打开。");
                    }

                    var choice = OfflineSkinCatalog.Get(skinId);
                    AccountStore.UpdateOfflineSkin(selected, choice.Id);
                    return ComponentActionResult.Completed($"已选择离线默认皮肤 {choice.DisplayName}。");

                default:
                    return ComponentActionResult.Failed("当前账号不支持该外观操作。");
            }
        }

        public override ValueTask DisposeAsync()
        {
            AccountStore.Changed -= OnAccountsChanged;
            _profileService.AppearanceChanged -= OnAppearanceChanged;
            lock (_stateGate)
            {
                _appearanceRefresh?.Cancel();
                _appearanceRefresh?.Dispose();
                _appearanceRefresh = null;
            }

            return base.DisposeAsync();
        }

        private void OnAccountsChanged()
        {
            if (IsDisposed)
                return;

            // 账号列表变化后以「展示账号」为准；覆盖账号被删除时自动回退全局当前账号
            var selected = ComponentDisplayAccount.Resolve(ComponentId);
            var selectedKey = selected is not null
                ? AccountStore.GetStableKey(selected)
                : null;
            var selectedSkinId = selected?.Type == "offline"
                ? OfflineSkinCatalog.Get(selected.OfflineSkinId).Id
                : null;
            lock (_stateGate)
            {
                if (!string.Equals(
                        _appearanceAccountKey,
                        selectedKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        _appearanceSkinId,
                        selectedSkinId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _appearanceAccountKey = null;
                    _appearanceSkinId = null;
                    _skinSource = null;
                }
            }

            Publish();
            BeginAppearanceRefresh();
        }

        private void OnAppearanceChanged(string accountKey)
        {
            // 仅当变化的是本组件正在展示的账号时才刷新
            var selected = ComponentDisplayAccount.Resolve(ComponentId);
            if (selected is not null && string.Equals(
                    AccountStore.GetStableKey(selected),
                    accountKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                BeginAppearanceRefresh();
            }
        }

        private void RefreshAvatar()
        {
            string? currentSource;
            lock (_stateGate)
            {
                currentSource = _skinSource;
                _avatarRefreshVersion++;
            }

            if (!string.IsNullOrWhiteSpace(currentSource))
                ComponentImageLoader.InvalidateRemoteCache(currentSource);

            BeginAppearanceRefresh();
        }

        private void BeginAppearanceRefresh()
        {
            if (IsDisposed)
                return;

            // 组件可独立选择展示的账号（未设置时跟随全局当前账号）
            var account = ComponentDisplayAccount.Resolve(ComponentId);
            var canRefresh = account is not null &&
                             ((account.Type == "microsoft" && account.Microsoft is not null) ||
                              account.Type == "offline");
            var refresh = canRefresh ? new CancellationTokenSource() : null;
            CancellationTokenSource? previousRefresh;
            lock (_stateGate)
            {
                if (IsDisposed)
                {
                    refresh?.Dispose();
                    return;
                }

                previousRefresh = _appearanceRefresh;
                _appearanceRefresh = refresh;
            }

            previousRefresh?.Cancel();
            previousRefresh?.Dispose();
            if (account is null || refresh is null)
                return;

            var accountKey = AccountStore.GetStableKey(account);
            if (account.Type == "microsoft")
            {
                _ = RefreshProfileAsync(account, accountKey, refresh);
                return;
            }

            var skinId = OfflineSkinCatalog.Get(account.OfflineSkinId).Id;
            _ = RefreshOfflineSkinAsync(accountKey, skinId, refresh);
        }

        private async Task RefreshProfileAsync(
            LaunchAccount account,
            string accountKey,
            CancellationTokenSource refresh)
        {
            var cancellationToken = refresh.Token;
            try
            {
                var profile = await _profileService.GetProfileAsync(account, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDisposed ||
                    ComponentDisplayAccount.Resolve(ComponentId) is not { } selected ||
                    selected.Type != "microsoft" ||
                    !string.Equals(
                        AccountStore.GetStableKey(selected),
                        accountKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                lock (_stateGate)
                {
                    if (!ReferenceEquals(_appearanceRefresh, refresh) ||
                        cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _appearanceAccountKey = accountKey;
                    _appearanceSkinId = null;
                    _skinSource = profile.ActiveSkin?.Url;
                }

                Publish();
            }
            catch (OperationCanceledException)
            {
                // Account changes and disposal cancel stale profile requests.
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"读取正版皮肤档案失败：{exception}");
            }
        }

        private async Task RefreshOfflineSkinAsync(
            string accountKey,
            string skinId,
            CancellationTokenSource refresh)
        {
            var cancellationToken = refresh.Token;
            try
            {
                var source = await OfflineSkinCatalog.ResolveTextureSourceAsync(
                        skinId,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDisposed ||
                    ComponentDisplayAccount.Resolve(ComponentId) is not { } selected ||
                    selected.Type != "offline" ||
                    !string.Equals(
                        AccountStore.GetStableKey(selected),
                        accountKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        OfflineSkinCatalog.Get(selected.OfflineSkinId).Id,
                        skinId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                lock (_stateGate)
                {
                    if (!ReferenceEquals(_appearanceRefresh, refresh) ||
                        cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _appearanceAccountKey = accountKey;
                    _appearanceSkinId = skinId;
                    _skinSource = source;
                }

                Publish();
            }
            catch (OperationCanceledException)
            {
                // Account/skin changes and disposal cancel stale catalog work.
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to resolve an offline skin: {exception}");
            }
        }

        private void Publish()
        {
            SetState(CreateState());
        }

        private ComponentStateSnapshot CreateState()
        {
            // 组件可独立选择展示的账号（未设置时跟随全局当前账号）
            var selected = ComponentDisplayAccount.Resolve(ComponentId);
            var accountKey = selected is null ? string.Empty : AccountStore.GetStableKey(selected);
            string? source;
            string fallback;
            IReadOnlyList<ComponentMenuItem> menuItems;
            long refreshToken = 0;

            if (selected?.Type == "microsoft")
            {
                lock (_stateGate)
                {
                    source = string.Equals(
                        _appearanceAccountKey,
                        accountKey,
                        StringComparison.OrdinalIgnoreCase)
                        ? _skinSource
                        : null;
                    refreshToken = _avatarRefreshVersion;
                }

                fallback = GetFallbackText(selected.DisplayName);
                var microsoftItems = new List<ComponentMenuItem>();
                // 展示账号分组：组件可独立于全局当前账号展示指定正版账号
                microsoftItems.AddRange(CreateDisplayAccountItems());
                microsoftItems.Add(CreateActionItem("refresh-avatar", "刷新头像", "重新下载并缓存当前皮肤头像", "material:Refresh", RefreshAvatarActionId));
                microsoftItems.Add(CreateActionItem("change-skin", "更换皮肤", "选择 PNG 后设置 Steve 或 Alex 模型", "material:ViewDashboard", ChangeSkinActionId));
                microsoftItems.Add(CreateActionItem("change-cape", "更换披风", "从该正版账号已有披风中选择", "material:DiamondOutline", ChangeCapeActionId));
                menuItems = microsoftItems;
            }
            else if (selected?.Type == "offline")
            {
                var choice = OfflineSkinCatalog.Get(selected.OfflineSkinId);
                lock (_stateGate)
                {
                    source = string.Equals(
                                 _appearanceAccountKey,
                                 accountKey,
                                 StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(
                                 _appearanceSkinId,
                                 choice.Id,
                                 StringComparison.OrdinalIgnoreCase)
                        ? _skinSource
                        : null;
                }

                fallback = choice.FallbackText;
                menuItems = OfflineSkinCatalog.Choices.Select(skin => new ComponentMenuItem
                {
                    Id = $"offline-{skin.Id}",
                    Text = skin.DisplayName,
                    SecondaryText = skin.Model == MinecraftSkinModel.Slim
                        ? "离线默认皮肤 · 纤细模型"
                        : "离线默认皮肤 · 经典模型",
                    Glyph = "material:CheckboxBlank",
                    ActionId = SelectOfflineSkinActionId,
                    Arguments = new Dictionary<string, string>
                    {
                        [AccountKeyArgument] = accountKey,
                        [SkinIdArgument] = skin.Id
                    },
                    IsSelected = string.Equals(choice.Id, skin.Id, StringComparison.OrdinalIgnoreCase)
                }).ToArray();
            }
            else
            {
                source = null;
                fallback = selected is null ? "?" : GetFallbackText(selected.DisplayName);
                menuItems =
                [
                    new ComponentMenuItem
                    {
                        Id = "unsupported-provider",
                        Text = selected is null ? "请先添加账号" : "此登录提供方暂不支持外观编辑",
                        Glyph = "material:Minus",
                        ActionId = ChangeSkinActionId,
                        IsEnabled = false
                    }
                ];
            }

            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["skin-face"] = new()
                    {
                        ImageSource = source,
                        ImageRefreshToken = refreshToken,
                        Text = fallback
                    },
                    ["skin-hat"] = new()
                    {
                        ImageSource = source,
                        ImageRefreshToken = refreshToken,
                        Text = string.Empty
                    },
                    ["appearance-menu"] = new() { MenuItems = menuItems }
                }
            };
        }

        /// <summary>
        /// 构建「展示账号」菜单分组：跟随全局当前账号 + 所有正版账号（当前展示项打勾），
        /// 分组末尾带分隔线。选择后本组件展示对应账号的皮肤头像，不再跟随全局切换。
        /// </summary>
        private static List<ComponentMenuItem> CreateDisplayAccountItems()
        {
            var displayKey = ComponentDisplayAccount.GetKey(ComponentId);
            var items = new List<ComponentMenuItem>
            {
                new()
                {
                    Id = "display-follow",
                    Text = "跟随当前账号",
                    SecondaryText = "展示全局当前选择的账号",
                    Glyph = "material:AccountCircle",
                    ActionId = ResetDisplayAccountActionId,
                    IsSelected = string.IsNullOrEmpty(displayKey)
                }
            };

            var index = 0;
            foreach (var account in AccountStore.Current)
            {
                if (account.Type != "microsoft")
                    continue;

                items.Add(new ComponentMenuItem
                {
                    Id = $"display-{index++}",
                    Text = account.DisplayName,
                    SecondaryText = "展示该正版账号的皮肤头像",
                    Glyph = "material:Diamond",
                    ActionId = SelectDisplayAccountActionId,
                    Arguments = new Dictionary<string, string>
                    {
                        [AccountKeyArgument] = AccountStore.GetStableKey(account)
                    },
                    IsSelected = string.Equals(
                        displayKey,
                        AccountStore.GetStableKey(account),
                        StringComparison.OrdinalIgnoreCase)
                });
            }

            // 分组末尾加分隔线，与外观操作区分（record 属性 init-only，用 with 替换）
            var lastIndex = items.Count - 1;
            items[lastIndex] = items[lastIndex] with { SeparatorAfter = true };
            return items;
        }

        private static ComponentMenuItem CreateActionItem(
            string id,
            string text,
            string secondaryText,
            string glyph,
            string actionId) => new()
            {
                Id = id,
                Text = text,
                SecondaryText = secondaryText,
                Glyph = glyph,
                ActionId = actionId
            };

        private static string GetFallbackText(string value) =>
            string.IsNullOrWhiteSpace(value) ? "?" : value.Trim()[..1].ToUpperInvariant();
    }
}
