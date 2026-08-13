using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInGameLaunchComponent
{
    public const string ComponentId = "nyalauncher.builtin/game-launch";
    private const string LaunchActionId = "launch-game";

    public static PolygonComponentRegistration Create(GameLaunchService launchService)
    {
        ArgumentNullException.ThrowIfNull(launchService);

        var definition = new PolygonComponentBuilder(ComponentId, "启动游戏")
            .WithDescription("点击直接使用当前账号启动已选择的 Minecraft 游戏实例")
            .WithGlyph("▶")
            .WithSize(250, 110)
            .WithSizeLimits(210, 92, 360, 150)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.04, 0.1, 0.09, 0.3))
            .WithTheme(new PolygonComponentTheme
            {
                Surface = "#5968E8",
                SurfaceHover = "#6C7BFF",
                Border = "#8793FF",
                BorderHover = "#C3C9FF",
                TextPrimary = "#FFFFFF",
                TextSecondary = "#E1E4FF",
                Accent = "#FFFFFF",
                AccentForeground = "#5968E8",
                ProgressTrack = "#4A57C7",
                BorderThickness = 1.5
            })
            .AddAction(LaunchActionId)
            .UseSurfaceAction(LaunchActionId)
            .AddText(
                "launch-glyph",
                new ComponentRect(0.08, 0.2, 0.14, 0.42),
                "▶",
                ComponentTextRole.Emphasis,
                fontSize: 25)
            .AddText(
                "launch-title",
                new ComponentRect(0.26, 0.18, 0.66, 0.25),
                "选择实例后启动",
                ComponentTextRole.Title,
                fontSize: 16)
            .AddText(
                "launch-status",
                new ComponentRect(0.26, 0.48, 0.66, 0.18),
                "点击直接启动游戏",
                ComponentTextRole.Caption,
                fontSize: 10)
            .AddProgress(
                "launch-progress",
                new ComponentRect(0.26, 0.72, 0.66, 0.11),
                "正在启动",
                value: 0)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new GameLaunchInstance(launchService))
        };
    }

    private sealed class GameLaunchInstance : IPolygonComponentInstance
    {
        private readonly GameLaunchService _launchService;
        private ComponentStateSnapshot _currentState;
        private long _revision;
        private int _isDisposed;

        public GameLaunchInstance(GameLaunchService launchService)
        {
            _launchService = launchService;
            _currentState = CreateState(Interlocked.Increment(ref _revision));
            AccountStore.Changed += OnSelectionChanged;
            GameInstanceStore.Changed += OnInstancesChanged;
            _launchService.Changed += OnLaunchChanged;
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return ComponentActionResult.Failed("启动游戏组件已释放。");
            if (!string.Equals(
                    invocation.ActionId,
                    LaunchActionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ComponentActionResult.Failed($"未知启动组件动作：{invocation.ActionId}");
            }

            return await _launchService
                .LaunchSelectedAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                AccountStore.Changed -= OnSelectionChanged;
                GameInstanceStore.Changed -= OnInstancesChanged;
                _launchService.Changed -= OnLaunchChanged;
                StateChanged = null;
            }

            return ValueTask.CompletedTask;
        }

        private void OnSelectionChanged() => Publish();

        private void OnInstancesChanged(GameInstanceSnapshot _) => Publish();

        private void OnLaunchChanged(GameLaunchSnapshot _) => Publish();

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
            var launch = _launchService.Current;
            var instance = GameInstanceStore.Current;
            var account = AccountStore.Selected;
            var title = instance.SelectedVersionId ?? "未选择游戏实例";
            var status = account is null
                ? "请先添加并选择账号"
                : $"{account.DisplayName} · 点击直接启动";
            var glyph = "▶";

            switch (launch.Phase)
            {
                case GameLaunchPhase.Preparing:
                    title = launch.Title;
                    status = launch.Message;
                    glyph = "…";
                    break;
                case GameLaunchPhase.Running:
                    title = launch.Title;
                    status = launch.Message;
                    glyph = "■";
                    break;
                case GameLaunchPhase.Failed:
                    title = launch.Title;
                    status = launch.Message;
                    glyph = "!";
                    break;
                case GameLaunchPhase.Exited:
                    title = instance.SelectedVersionId ?? launch.VersionId ?? "游戏已退出";
                    status = $"{launch.Message} · 点击可再次启动";
                    glyph = "↻";
                    break;
            }

            return new ComponentStateSnapshot
            {
                Revision = revision,
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["launch-glyph"] = new() { Text = glyph },
                    ["launch-title"] = new() { Text = title },
                    ["launch-status"] = new() { Text = status },
                    ["launch-progress"] = new()
                    {
                        IsVisible = launch.Phase == GameLaunchPhase.Preparing,
                        IsIndeterminate = launch.Phase == GameLaunchPhase.Preparing,
                        Text = "正在启动"
                    }
                }
            };
        }
    }
}
