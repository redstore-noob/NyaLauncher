using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Network;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>一次「选择版本进服」请求：由组件发起，宿主（主窗口）弹出遮罩层处理。</summary>
public sealed record ServerJoinRequest(
    string Address,
    string Host,
    int Port,
    string Motd,
    string? VersionName,
    int OnlinePlayers,
    int MaxPlayers);

/// <summary>
/// 「服务器快连」长条组件，两个阶段：
/// 1. 输入态 —— 地址输入框 + 确定按钮；
/// 2. 锁定态 —— 点击确定后销毁输入框与确定按钮，地址锁死（直到组件被丢弃），
///    展示 MOTD、游戏版本与在线人数，每 30 秒自动刷新，可手动重查或进服。
/// </summary>
internal static class BuiltInServerJoinComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/server-join</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/server-join";
    private const string ConfirmActionId = "confirm-server";
    private const string RetryActionId = "retry-server";
    private const string JoinActionId = "join-server";
    private const string AddressElementId = "server-address";
    private const string ConfirmElementId = "confirm-btn";
    private const string MotdElementId = "server-motd";
    private const string InfoElementId = "server-info";
    private const string RetryElementId = "retry-btn";
    private const string JoinElementId = "join-btn";
    private const string IconElementId = "server-icon";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    public static PolygonComponentRegistration Create(Action<ServerJoinRequest> openServerJoin)
    {
        ArgumentNullException.ThrowIfNull(openServerJoin);

        var definition = new PolygonComponentBuilder(ComponentId, "服务器快连")
            .WithDescription("填入服务器地址，确定后锁定并实时显示状态")
            .WithGlyph("material:SatelliteVariant")
            .WithSize(460, 110)
            .WithSizeLimits(360, 96, 800, 160)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.015, 0.30, 0.038, 0.40))
            .WithTheme(new PolygonComponentTheme())
            .AddAction(ConfirmActionId)
            .AddAction(RetryActionId)
            .AddAction(JoinActionId)
            .UseSurfaceAction(JoinActionId)
            .AddTextInput(
                AddressElementId,
                new ComponentRect(0.07, 0.30, 0.54, 0.40),
                ConfirmActionId,
                placeholder: "服务器地址，如 play.hypixel.net",
                maximumLength: 120)
            .AddButton(
                ConfirmElementId,
                new ComponentRect(0.64, 0.30, 0.30, 0.40),
                "确定",
                ConfirmActionId,
                isPrimary: true)
            .AddText(
                MotdElementId,
                new ComponentRect(0.21, 0.10, 0.44, 0.38),
                string.Empty,
                ComponentTextRole.Title,
                fontSize: 13)
            .AddText(
                InfoElementId,
                new ComponentRect(0.21, 0.54, 0.44, 0.32),
                string.Empty,
                ComponentTextRole.Caption,
                fontSize: 10)
            // 左侧服务器图标：锁定态可见；favicon 未加载时回退 Dns 字形
            .AddImage(
                IconElementId,
                new ComponentRect(0.045, 0.17, 0.13, 0.66),
                source: string.Empty,
                stretch: ComponentImageStretch.Uniform,
                fallbackText: "material:Dns",
                cornerRadius: 10,
                pixelated: true)
            .AddButton(
                RetryElementId,
                new ComponentRect(0.68, 0.30, 0.14, 0.40),
                "material:Refresh",
                RetryActionId)
            .AddButton(
                JoinElementId,
                new ComponentRect(0.84, 0.30, 0.13, 0.40),
                "material:Play",
                JoinActionId,
                isPrimary: true)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new ServerJoinInstance(openServerJoin))
        };
    }

    private sealed class ServerJoinInstance : PolygonComponentInstanceBase
    {
        private readonly Action<ServerJoinRequest> _openServerJoin;
        private int _pingGeneration;
        private CancellationTokenSource? _refreshCts;

        private bool _locked;
        private string _addressInput = string.Empty;
        private string _host = string.Empty;
        private int _port;
        private string _lastMotd = string.Empty;
        private string? _lastVersionName;
        private string? _lastIconPath;
        private int _lastOnline;
        private int _lastMax;

        public ServerJoinInstance(Action<ServerJoinRequest> openServerJoin)
        {
            _openServerJoin = openServerJoin;
            SetState(CreateState(
                addressInput: string.Empty,
                locked: false,
                motd: string.Empty,
                info: string.Empty,
                joinEnabled: false,
                iconPath: null));
        }

        public override ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ValueTask.FromResult(ComponentActionResult.Failed("服务器组件已释放。"));

            switch (invocation.ActionId)
            {
                case ConfirmActionId:
                    return ValueTask.FromResult(Confirm(invocation));

                case RetryActionId:
                {
                    if (!_locked)
                        return ValueTask.FromResult(ComponentActionResult.Failed(
                            "请先确定服务器地址。"));

                    StartPingLoop();
                    return ValueTask.FromResult(ComponentActionResult.Completed(
                        $"正在重新查询 {_addressInput}…"));
                }

                case JoinActionId:
                    return ValueTask.FromResult(RequestServerJoin());

                default:
                    return ValueTask.FromResult(ComponentActionResult.Failed(
                        $"未知服务器组件动作：{invocation.ActionId}"));
            }
        }

        public override ValueTask DisposeAsync()
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
            return base.DisposeAsync();
        }

        private ComponentActionResult Confirm(ComponentActionInvocation invocation)
        {
            if (_locked)
                return ComponentActionResult.Failed(
                    "地址已锁定；丢弃该组件后重新添加即可填写新地址。");

            var address = invocation.Arguments is not null &&
                          invocation.Arguments.TryGetValue(AddressElementId, out var value)
                ? (value ?? string.Empty).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(address))
                return ComponentActionResult.Failed(
                    "请先填入服务器地址，例如 play.hypixel.net。");

            _locked = true;
            _addressInput = address;
            StartPingLoop();
            return ComponentActionResult.Completed($"地址已锁定，正在查询 {address}…");
        }

        private ComponentActionResult RequestServerJoin()
        {
            if (!_locked || string.IsNullOrWhiteSpace(_host))
                return ComponentActionResult.Failed(
                    "请先查询到在线服务器后再选择版本进服。");

            _openServerJoin(new ServerJoinRequest(
                _addressInput,
                _host,
                _port,
                _lastMotd,
                _lastVersionName,
                _lastOnline,
                _lastMax));
            return ComponentActionResult.Completed();
        }

        /// <summary>
        /// 启动（或重启）查询循环：立即查询一次，之后每 30 秒自动刷新在线人数。
        /// 旧循环通过取消令牌与代数计数双重失效。
        /// </summary>
        private void StartPingLoop()
        {
            if (IsDisposed)
                return;

            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            var cts = new CancellationTokenSource();
            _refreshCts = cts;
            var generation = Interlocked.Increment(ref _pingGeneration);
            Publish("正在查询服务器…", _addressInput, joinEnabled: false, iconPath: null);

            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        var (host, port) = MinecraftServerPinger.ParseAddress(_addressInput);
                        var status = await MinecraftServerPinger
                            .PingAsync(host, port, cts.Token)
                            .ConfigureAwait(false);
                        if (IsStale(generation, cts))
                            return;

                        _host = host;
                        _port = port;
                        _lastMotd = status.Motd;
                        _lastVersionName = status.VersionName;
                        _lastIconPath = status.IconPath;
                        _lastOnline = status.OnlinePlayers;
                        _lastMax = status.MaxPlayers;
                        Publish(
                            status.Motd,
                            $"{status.VersionName ?? "未知版本"} · " +
                            $"{status.OnlinePlayers}/{status.MaxPlayers} 在线 · {host}:{port}",
                            joinEnabled: true,
                            iconPath: status.IconPath);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        if (IsStale(generation, cts))
                            return;

                        _host = string.Empty;
                        _lastMotd = "无法连接服务器";
                        _lastVersionName = null;
                        _lastIconPath = null;
                        _lastOnline = 0;
                        _lastMax = 0;
                        Publish(
                            "无法连接服务器",
                            $"{exception.Message}",
                            joinEnabled: false,
                            iconPath: null);
                    }

                    try
                    {
                        await Task.Delay(RefreshInterval, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }, cts.Token);
        }

        private bool IsStale(int generation, CancellationTokenSource cts) =>
            IsDisposed ||
            Volatile.Read(ref _pingGeneration) != generation ||
            cts.IsCancellationRequested;

        private void Publish(string motd, string info, bool joinEnabled, string? iconPath)
        {
            if (IsDisposed)
                return;

            SetState(CreateState(_addressInput, _locked, motd, info, joinEnabled, iconPath));
        }

        private static ComponentStateSnapshot CreateState(
            string addressInput,
            bool locked,
            string motd,
            string info,
            bool joinEnabled,
            string? iconPath)
        {
            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    // 锁定后输入框与确定按钮从界面上“销毁”
                    [AddressElementId] = new ComponentElementState
                    {
                        Value = addressInput,
                        IsVisible = !locked
                    },
                    [ConfirmElementId] = new ComponentElementState
                    {
                        Text = "确定",
                        IsVisible = !locked
                    },
                    [MotdElementId] = new ComponentElementState
                    {
                        Text = motd,
                        IsVisible = locked
                    },
                    [InfoElementId] = new ComponentElementState
                    {
                        Text = info,
                        IsVisible = locked
                    },
                    [RetryElementId] = new ComponentElementState
                    {
                        Text = "material:Refresh",
                        IsVisible = locked
                    },
                    [JoinElementId] = new ComponentElementState
                    {
                        Text = "material:Play",
                        IsVisible = locked,
                        IsEnabled = joinEnabled
                    },
                    [IconElementId] = new ComponentElementState
                    {
                        ImageSource = iconPath,
                        IsVisible = locked
                    }
                }
            };
        }
    }
}
