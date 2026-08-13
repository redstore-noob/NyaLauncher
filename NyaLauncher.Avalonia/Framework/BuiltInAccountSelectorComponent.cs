using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInAccountSelectorComponent
{
    public const string ComponentId = "nyalauncher.builtin/account-selector";
    private const string AddAccountActionId = "add-account";
    private const string SelectAccountActionId = "select-account";
    private const string AccountKeyArgument = "accountKey";

    public static PolygonComponentRegistration Create(Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);

        var definition = new PolygonComponentBuilder(ComponentId, "账号选择")
            .WithDescription("查看当前账号与登录模式，并快速添加或切换账号")
            .WithGlyph("☺")
            .WithSize(260, 72)
            .WithSizeLimits(220, 64, 360, 92)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.025, 0.24, 0.075, 0.52))
            .WithTheme(new PolygonComponentTheme
            {
                Surface = "#20263A",
                SurfaceHover = "#29314A",
                Border = "#3A4563",
                BorderHover = "#7C8CFF",
                Accent = "#7C8CFF",
                ProgressTrack = "#30384F"
            })
            .AddAction(AddAccountActionId)
            .AddAction(SelectAccountActionId)
            .AddText(
                "account-glyph",
                new ComponentRect(0.115, 0.25, 0.085, 0.5),
                "☺",
                ComponentTextRole.Emphasis,
                fontSize: 17)
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
                        Text = "添加账号",
                        SecondaryText = "正版登录或离线登录",
                        Glyph = "＋",
                        ActionId = AddAccountActionId,
                        SeparatorAfter = true
                    }
                ])
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new AccountSelectorInstance(navigate))
        };
    }

    private sealed class AccountSelectorInstance : IPolygonComponentInstance
    {
        private readonly Action<string> _navigate;
        private ComponentStateSnapshot _currentState;
        private long _revision;
        private int _isDisposed;

        public AccountSelectorInstance(Action<string> navigate)
        {
            _navigate = navigate;
            _currentState = CreateState(Interlocked.Increment(ref _revision));
            AccountStore.Changed += OnAccountsChanged;
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return ComponentActionResult.Failed("账号选择组件已释放。");

            cancellationToken.ThrowIfCancellationRequested();
            switch (invocation.ActionId)
            {
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

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                AccountStore.Changed -= OnAccountsChanged;
                StateChanged = null;
            }

            return ValueTask.CompletedTask;
        }

        private void OnAccountsChanged()
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
            var accountItems = AccountStore.Current
                .Select((account, index) => new ComponentMenuItem
                {
                    Id = $"account-{index}",
                    Text = account.DisplayName,
                    SecondaryText = account.LoginModeLabel,
                    Glyph = account.Type switch
                    {
                        "microsoft" => "◆",
                        "offline" => "○",
                        _ => "◇"
                    },
                    ActionId = SelectAccountActionId,
                    Arguments = new Dictionary<string, string>
                    {
                        [AccountKeyArgument] = AccountStore.GetStableKey(account)
                    },
                    IsSelected = ReferenceEquals(account, selected)
                })
                .ToArray();

            return new ComponentStateSnapshot
            {
                Revision = revision,
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
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
