using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Launch;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal sealed class BuiltInFeatureAreaProvider : IFeatureAreaProvider
{
    private readonly System.Action<string> _navigate;
    private readonly MinecraftProfileService _profileService;
    private readonly GameLaunchService _launchService;
    private readonly System.Action<ServerJoinRequest> _openServerJoin;

    public BuiltInFeatureAreaProvider(
        System.Action<string> navigate,
        MinecraftProfileService profileService,
        GameLaunchService launchService,
        System.Action<ServerJoinRequest> openServerJoin)
    {
        _navigate = navigate;
        _profileService = profileService;
        _launchService = launchService;
        _openServerJoin = openServerJoin;
    }

    public IEnumerable<FeatureAreaDefinition> GetFeatureAreas()
    {
        yield return new FeatureAreaDefinition
        {
            Id = "area-001",
            Title = "启动中心",
            Subtitle = "选择实例并进入游戏",
            Glyph = "material:Play",
            Actions =
            [
                CreateInstanceSelectorAction()
            ],
            PolygonComponents =
            [
                BuiltInAccountSelectorComponent.Create(_navigate, _profileService),
                BuiltInSkinCapeComponent.Create(_profileService, _navigate),
                BuiltInGameLaunchComponent.Create(_launchService),
                BuiltInWorldLaunchComponent.Create(_launchService),
                BuiltInMemoryUsageComponent.Create(),
                BuiltInServerJoinComponent.Create(_openServerJoin)
            ]
        };

        yield return new FeatureAreaDefinition
        {
            Id = "area-002",
            Title = "资源与实例",
            Subtitle = "管理游戏内容与版本",
            Glyph = "material:Diamond",
            Actions =
            [
                new("instances", "实例库", "查看、复制或创建实例", "material:Apps",
                    () => _navigate("instances")),
                new("downloads", "下载资源", "游戏、模组、光影与材质", "material:ArrowDown",
                    () => _navigate("downloads")),
                new("tasks", "下载任务", "当前没有进行中的任务", "material:FormatListBulleted",
                    () => _navigate("tasks"))
            ],
            PolygonComponents =
            [
                BuiltInVersionManagerComponent.Create(_navigate),
                CreateDownloadProgressComponent()
            ]
        };

        yield return new FeatureAreaDefinition
        {
            Id = "area-003",
            Title = "启动器工具",
            Subtitle = "配置与运行环境",
            Glyph = "material:Star",
            Actions =
            [
                new("settings", "个性化设置", "外观、语言与行为", "material:Cog",
                    () => _navigate("settings")),
                new("runtime", "Java 运行环境", "自动查找并管理 Java", "material:Coffee",
                    () => _navigate("runtime")),
                new("music-player", "音乐播放器", "播放音乐、管理播放列表", "material:MusicNote",
                    () => _navigate("music-player"))
            ],
            PolygonComponents =
            [
                BuiltInMusicPlayerComponent.Create(_navigate)
            ]
        };
    }

    /// <summary>
    /// 「选择游戏实例」功能区动作直接绑定游戏实例选择组件：
    /// 顶部按钮以组件卡片样式渲染，点击弹出实例下拉菜单，与组件库卡片样式完全融合。
    /// </summary>
    private static FeatureAreaAction CreateInstanceSelectorAction()
    {
        var registration = BuiltInGameInstanceSelectorComponent.Create();
        var definition = registration.Definition;
        return new FeatureAreaAction(
            "select-instance",
            definition.Title,
            definition.Description,
            definition.Glyph)
        {
            BaseWidth = definition.PreferredSize.Width,
            BaseHeight = definition.PreferredSize.Height,
            PolygonComponent = registration
        };
    }

    private static PolygonComponentRegistration CreateDownloadProgressComponent()
    {
        var definition = new PolygonComponentBuilder(
                "nyalauncher.builtin/download-task-progress",
                "下载任务进度")
            .WithDescription("展示文本、进度条、按钮、异步动作与实时状态更新")
            .WithGlyph("material:Hexagon")
            .WithSize(320, 180)
            .WithShape(PolygonShapeDefinition.RegularPolygon(6, rotationDegrees: 0))
            .WithDragHandle(new ComponentRect(0.45, 0.055, 0.1, 0.1))
            .WithTheme(new PolygonComponentTheme())
            .AddAction("run-demo")
            .AddText(
                "title",
                new ComponentRect(0.17, 0.15, 0.66, 0.14),
                "下载任务",
                ComponentTextRole.Title,
                fontSize: 15)
            .AddText(
                "status",
                new ComponentRect(0.17, 0.3, 0.66, 0.12),
                "资源索引等待继续",
                ComponentTextRole.Caption,
                fontSize: 11)
            .AddProgress(
                "task-progress",
                new ComponentRect(0.17, 0.46, 0.66, 0.16),
                "下载进度",
                value: 36)
            .AddButton(
                "advance-button",
                new ComponentRect(0.32, 0.69, 0.36, 0.16),
                "继续下载",
                "run-demo",
                glyph: "material:Play",
                isPrimary: true)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new BuiltInDownloadProgressInstance())
        };
    }

    private sealed class BuiltInDownloadProgressInstance : PolygonComponentInstanceBase
    {
        private const string RunDemoActionId = "run-demo";
        private double _progress = 36;
        private int _isRunning;

        public BuiltInDownloadProgressInstance()
        {
            SetState(CreateState(
                _progress,
                "资源索引等待继续",
                "继续下载",
                buttonEnabled: true));
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("组件实例已释放。");
            if (!string.Equals(invocation.ActionId, RunDemoActionId, StringComparison.OrdinalIgnoreCase))
                return ComponentActionResult.Failed($"未知动作：{invocation.ActionId}");
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
                return ComponentActionResult.Failed("下载演示正在运行。");

            try
            {
                var start = _progress >= 100 ? 0 : _progress;
                _progress = start;
                Publish(start, "正在准备下载资源…", "正在运行…", buttonEnabled: false);

                const int stepCount = 8;
                for (var step = 1; step <= stepCount; step++)
                {
                    await Task.Delay(90, cancellationToken).ConfigureAwait(false);
                    _progress = start + (100 - start) * step / stepCount;
                    Publish(
                        _progress,
                        _progress >= 100
                            ? "资源下载完成"
                            : $"正在下载资源… {_progress:0}%",
                        _progress >= 100 ? "重新演示" : "正在运行…",
                        buttonEnabled: _progress >= 100);
                }

                return ComponentActionResult.Completed("下载任务演示已完成。");
            }
            catch (OperationCanceledException)
            {
                Publish(_progress, "下载任务已取消", "继续下载", buttonEnabled: true);
                throw;
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
            }
        }

        private void Publish(
            double progress,
            string status,
            string buttonText,
            bool buttonEnabled)
        {
            if (IsDisposed)
                return;

            SetState(CreateState(
                progress,
                status,
                buttonText,
                buttonEnabled));
        }

        private static ComponentStateSnapshot CreateState(
            double progress,
            string status,
            string buttonText,
            bool buttonEnabled)
        {
            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = new ComponentElementState { Text = status },
                    ["task-progress"] = new ComponentElementState
                    {
                        ProgressValue = progress,
                        Text = "下载进度"
                    },
                    ["advance-button"] = new ComponentElementState
                    {
                        Text = buttonText,
                        IsEnabled = buttonEnabled
                    }
                }
            };
        }
    }
}
