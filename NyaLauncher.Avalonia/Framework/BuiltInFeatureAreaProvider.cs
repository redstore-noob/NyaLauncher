using System.Collections.Generic;
using NyaLauncher.Core.Launch;
using NyaLauncher.Avalonia.Plugins;

namespace NyaLauncher.Avalonia.Framework;

internal sealed class BuiltInFeatureAreaProvider : IFeatureAreaProvider
{
    private readonly System.Action<string> _navigate;
    private readonly MinecraftProfileService _profileService;
    private readonly GameLaunchService _launchService;
    private readonly PluginManager? _pluginManager;

    public BuiltInFeatureAreaProvider(
        System.Action<string> navigate,
        MinecraftProfileService profileService,
        GameLaunchService launchService,
        PluginManager? pluginManager = null)
    {
        _navigate = navigate;
        _profileService = profileService;
        _launchService = launchService;
        _pluginManager = pluginManager;
    }

    public IEnumerable<FeatureAreaDefinition> GetFeatureAreas()
    {
        yield return new FeatureAreaDefinition
        {
            Id = "area-001",
            Title = "启动中心",
            Subtitle = "选择实例并进入游戏",
            Glyph = "▶",
            Actions = [],
            PolygonComponents =
            [
                BuiltInAccountSelectorComponent.Create(_navigate),
                BuiltInGameInstanceSelectorComponent.Create(),
                BuiltInSkinCapeComponent.Create(_profileService, _navigate),
                BuiltInGameLaunchComponent.Create(_launchService)
            ]
        };

        yield return new FeatureAreaDefinition
        {
            Id = "area-002",
            Title = "资源与实例",
            Subtitle = "管理游戏内容与版本",
            Glyph = "◆",
            Actions =
            [
                new("instances", "实例库", "查看、复制或创建实例", "▦",
                    () => _navigate("instances"))
            ],
            PolygonComponents =
            [
                BuiltInVersionManagerComponent.Create(_navigate)
            ]
        };

        yield return new FeatureAreaDefinition
        {
            Id = "area-003",
            Title = "启动器工具",
            Subtitle = "配置、插件与运行环境",
            Glyph = "✦",
            Actions =
            [
                new("settings", "启动器设置", "外观、语言与行为", "⚙",
                    () => _navigate("settings"))
            ],
            PolygonComponents =
            [
                BuiltInPluginListComponent.Create(_navigate, _pluginManager)
            ]
        };
    }
}
