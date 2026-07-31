using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

internal sealed class BuiltInFeatureAreaProvider : IFeatureAreaProvider
{
    private readonly System.Action<string> _navigate;

    public BuiltInFeatureAreaProvider(System.Action<string> navigate)
    {
        _navigate = navigate;
    }

    public IEnumerable<FeatureAreaDefinition> GetFeatureAreas()
    {
        yield return new FeatureAreaDefinition
        {
            Id = "area-001",
            Title = "启动中心",
            Subtitle = "选择实例并进入游戏",
            Glyph = "▶",
            Actions =
            [
                new("select-instance", "选择游戏实例", "Minecraft 1.21.8 · Fabric", "▣",
                    () => _navigate("select-instance")),
                new("account", "游戏账号", "离线账号 Player_01", "☺",
                    () => _navigate("account")),
                new("launch", "启动游戏", "准备就绪", "▶",
                    () => _navigate("launch"), true)
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
                    () => _navigate("instances")),
                new("downloads", "下载资源", "游戏、模组、光影与材质", "↓",
                    () => _navigate("downloads")),
                new("tasks", "下载任务", "当前没有进行中的任务", "≡",
                    () => _navigate("tasks"))
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
                    () => _navigate("settings")),
                new("runtime", "Java 运行环境", "自动查找并管理 Java", "⌘",
                    () => _navigate("runtime")),
                new("plugins", "插件中心", "为 NyaLauncher 添加能力", "＋",
                    () => _navigate("plugins"))
            ]
        };
    }
}
