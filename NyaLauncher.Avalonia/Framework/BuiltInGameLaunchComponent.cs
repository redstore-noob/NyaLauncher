using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Launch.Auth;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInGameLaunchComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/game-launch</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/game-launch";
    private const string LaunchActionId = "launch-game";

    public static PolygonComponentRegistration Create(GameLaunchService launchService)
    {
        ArgumentNullException.ThrowIfNull(launchService);

        var definition = new PolygonComponentBuilder(ComponentId, "启动游戏")
            .WithDescription("点击直接使用当前账号启动已选择的 Minecraft 游戏实例")
            .WithGlyph("material:Play")
            .WithSize(250, 110)
            .WithSizeLimits(210, 92, 360, 150)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.04, 0.1, 0.09, 0.3))
            .WithTheme(new PolygonComponentTheme { Variant = ComponentThemeVariant.Launch })
            .AddAction(LaunchActionId)
            .UseSurfaceAction(LaunchActionId)
            .AddText(
                "launch-glyph",
                new ComponentRect(0.08, 0.2, 0.14, 0.42),
                "material:Play",
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

    private sealed class GameLaunchInstance : PolygonComponentInstanceBase
    {
        private readonly GameLaunchService _launchService;

        public GameLaunchInstance(GameLaunchService launchService)
        {
            _launchService = launchService;
            SetState(CreateState());
            AccountStore.Changed += OnSelectionChanged;
            GameInstanceStore.Changed += OnInstancesChanged;
            _launchService.Changed += OnLaunchChanged;
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
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

        public override ValueTask DisposeAsync()
        {
            AccountStore.Changed -= OnSelectionChanged;
            GameInstanceStore.Changed -= OnInstancesChanged;
            _launchService.Changed -= OnLaunchChanged;
            return base.DisposeAsync();
        }

        private void OnSelectionChanged() => Publish();

        private void OnInstancesChanged(GameInstanceSnapshot _) => Publish();

        private void OnLaunchChanged(GameLaunchSnapshot _) => Publish();

        private void Publish() => SetState(CreateState());

        private ComponentStateSnapshot CreateState()
        {
            var launch = _launchService.Current;
            var instance = GameInstanceStore.Current;
            var account = AccountStore.Selected;
            var title = instance.SelectedVersionId ?? "未选择游戏实例";
            var status = account is null
                ? "请先添加并选择账号"
                : $"{account.DisplayName} · 点击直接启动";
            var glyph = "material:Play";

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
                    glyph = "material:Stop";
                    break;
                case GameLaunchPhase.Failed:
                    title = launch.Title;
                    status = launch.Message;
                    glyph = "!";
                    break;
                case GameLaunchPhase.Exited:
                    title = instance.SelectedVersionId ?? launch.VersionId ?? "游戏已退出";
                    status = $"{launch.Message} · 点击可再次启动";
                    glyph = "material:Refresh";
                    break;
            }

            return new ComponentStateSnapshot
            {
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
