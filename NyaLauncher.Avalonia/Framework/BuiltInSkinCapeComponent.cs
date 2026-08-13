using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInSkinCapeComponent
{
    public const string ComponentId = "nyalauncher.builtin/skin-cape-editor";
    private const string ChangeSkinActionId = "change-skin";
    private const string ChangeCapeActionId = "change-cape";
    private const string SelectOfflineSkinActionId = "select-offline-skin";
    private const string AccountKeyArgument = "accountKey";
    private const string SkinIdArgument = "skinId";

    public static PolygonComponentRegistration Create(
        MinecraftProfileService profileService,
        Func<PlayerAppearanceRequest, CancellationToken, Task<ComponentActionResult>> editor)
    {
        ArgumentNullException.ThrowIfNull(profileService);
        ArgumentNullException.ThrowIfNull(editor);

        var definition = new PolygonComponentBuilder(ComponentId, "皮肤与披风")
            .WithDescription("显示当前皮肤头像，并编辑正版皮肤、正版披风或离线默认皮肤")
            .WithGlyph("▦")
            .WithSize(96, 96)
            .WithSizeLimits(72, 72, 160, 160)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.04, 0.04, 0.24, 0.24))
            .WithTheme(new PolygonComponentTheme
            {
                Surface = "#1B2132",
                SurfaceHover = "#252E45",
                Border = "#53658F",
                BorderHover = "#9AA8FF",
                Accent = "#8C9DFF",
                ProgressTrack = "#252C40",
                BorderThickness = 2
            })
            .AddAction(ChangeSkinActionId)
            .AddAction(ChangeCapeActionId)
            .AddAction(SelectOfflineSkinActionId)
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
                _ => new SkinCapeInstance(profileService, editor))
        };
    }

    private sealed class SkinCapeInstance : IPolygonComponentInstance
    {
        private readonly MinecraftProfileService _profileService;
        private readonly Func<PlayerAppearanceRequest, CancellationToken, Task<ComponentActionResult>> _editor;
        private readonly object _stateGate = new();
        private ComponentStateSnapshot _currentState;
        private CancellationTokenSource? _appearanceRefresh;
        private string? _appearanceAccountKey;
        private string? _appearanceSkinId;
        private string? _skinSource;
        private long _revision;
        private int _isDisposed;

        public SkinCapeInstance(
            MinecraftProfileService profileService,
            Func<PlayerAppearanceRequest, CancellationToken, Task<ComponentActionResult>> editor)
        {
            _profileService = profileService;
            _editor = editor;
            _currentState = CreateState(Interlocked.Increment(ref _revision));
            AccountStore.Changed += OnAccountsChanged;
            _profileService.AppearanceChanged += OnAppearanceChanged;
            BeginAppearanceRefresh();
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return ComponentActionResult.Failed("皮肤与披风组件已释放。");

            var selected = AccountStore.Selected;
            if (selected is null)
                return ComponentActionResult.Failed("请先添加并选择一个账号。");
            var accountKey = AccountStore.GetStableKey(selected);

            switch (invocation.ActionId)
            {
                case ChangeSkinActionId when selected.Type == "microsoft":
                    return await _editor(
                        new PlayerAppearanceRequest(PlayerAppearanceCommand.ChangeSkin, accountKey),
                        cancellationToken).ConfigureAwait(false);

                case ChangeCapeActionId when selected.Type == "microsoft":
                    return await _editor(
                        new PlayerAppearanceRequest(PlayerAppearanceCommand.ChangeCape, accountKey),
                        cancellationToken).ConfigureAwait(false);

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

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                AccountStore.Changed -= OnAccountsChanged;
                _profileService.AppearanceChanged -= OnAppearanceChanged;
                lock (_stateGate)
                {
                    _appearanceRefresh?.Cancel();
                    _appearanceRefresh?.Dispose();
                    _appearanceRefresh = null;
                }

                StateChanged = null;
            }

            return ValueTask.CompletedTask;
        }

        private void OnAccountsChanged()
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;

            var selected = AccountStore.Selected;
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
            var selected = AccountStore.Selected;
            if (selected is not null && string.Equals(
                    AccountStore.GetStableKey(selected),
                    accountKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                BeginAppearanceRefresh();
            }
        }

        private void BeginAppearanceRefresh()
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;

            var account = AccountStore.Selected;
            var canRefresh = account is not null &&
                             ((account.Type == "microsoft" && account.Microsoft is not null) ||
                              account.Type == "offline");
            var refresh = canRefresh ? new CancellationTokenSource() : null;
            CancellationTokenSource? previousRefresh;
            lock (_stateGate)
            {
                if (Volatile.Read(ref _isDisposed) != 0)
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
                if (Volatile.Read(ref _isDisposed) != 0 ||
                    AccountStore.Selected is not { } selected ||
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
                if (Volatile.Read(ref _isDisposed) != 0 ||
                    AccountStore.Selected is not { } selected ||
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
            if (Volatile.Read(ref _isDisposed) != 0)
                return;
            var next = CreateState(Interlocked.Increment(ref _revision));
            Volatile.Write(ref _currentState, next);
            StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(next));
        }

        private ComponentStateSnapshot CreateState(long revision)
        {
            var selected = AccountStore.Selected;
            var accountKey = selected is null ? string.Empty : AccountStore.GetStableKey(selected);
            string? source;
            string fallback;
            IReadOnlyList<ComponentMenuItem> menuItems;

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
                }

                fallback = GetFallbackText(selected.DisplayName);
                menuItems =
                [
                    CreateActionItem("change-skin", "更换皮肤", "选择 PNG 后设置 Steve 或 Alex 模型", "▣", ChangeSkinActionId),
                    CreateActionItem("change-cape", "更换披风", "从该正版账号已有披风中选择", "◇", ChangeCapeActionId)
                ];
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
                    Glyph = "□",
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
                        Glyph = "·",
                        ActionId = ChangeSkinActionId,
                        IsEnabled = false
                    }
                ];
            }

            return new ComponentStateSnapshot
            {
                Revision = revision,
                Elements = new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["skin-face"] = new() { ImageSource = source, Text = fallback },
                    ["skin-hat"] = new() { ImageSource = source, Text = string.Empty },
                    ["appearance-menu"] = new() { MenuItems = menuItems }
                }
            };
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

internal enum PlayerAppearanceCommand
{
    ChangeSkin,
    ChangeCape
}

internal sealed record PlayerAppearanceRequest(
    PlayerAppearanceCommand Command,
    string AccountKey);
