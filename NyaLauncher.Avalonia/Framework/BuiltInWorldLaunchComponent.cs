using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 「最近的世界」卡片：扫描全部实例（含版本隔离目录）saves 目录中的世界，
/// 通过下拉菜单选择世界（显示世界名 + 所属实例），顶部展示世界预览图（存档 icon.png），
/// 下方显示名称与上次游玩时间，右下角一键启动。
/// 启动前会自动切换到该世界所属的游戏实例，再执行启动。
/// 实例变化时自动重新扫描。
/// </summary>
internal static class BuiltInWorldLaunchComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/world-launch</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/world-launch";
    private const string LaunchActionId = "launch-world";
    private const string SelectWorldActionId = "select-world";
    private const string WorldIdArgument = "worldId";

    public static PolygonComponentRegistration Create(GameLaunchService launchService)
    {
        ArgumentNullException.ThrowIfNull(launchService);

        var definition = new PolygonComponentBuilder(ComponentId, "最近的世界")
            .WithDescription("选择任意实例的 Minecraft 世界，一键启动到对应实例继续冒险")
            .WithGlyph("material:Earth")
            .WithSize(360, 260)
            .WithSizeLimits(260, 200, 640, 460)
            .WithShape(PolygonShapeDefinition.Rectangle())
            // 拖拽把手放在信息带左侧空白区，避免遮挡预览图与启动按钮
            .WithDragHandle(new ComponentRect(0.02, 0.64, 0.045, 0.30))
            .WithTheme(new PolygonComponentTheme())
            .AddAction(LaunchActionId)
            .AddAction(SelectWorldActionId)
            // 顶部 52% 为世界预览图，铺满并保持圆角裁切
            .AddImage(
                "world-preview",
                new ComponentRect(0.04, 0.04, 0.92, 0.52),
                source: "",
                stretch: ComponentImageStretch.UniformToFill,
                fallbackText: "material:Earth",
                cornerRadius: 14)
            // 信息带左列：世界名 + 紧随其后的下拉 chevron（点击弹世界列表）
            .AddText(
                "world-name",
                new ComponentRect(0.07, 0.61, 0.44, 0.15),
                "全新世界",
                ComponentTextRole.Title,
                fontSize: 16)
            .AddDropdown(
                "world-menu",
                new ComponentRect(0.52, 0.615, 0.11, 0.14),
                glyph: "material:ChevronDown")
            // 第二行：上次游玩时间
            .AddText(
                "last-played",
                new ComponentRect(0.07, 0.80, 0.56, 0.11),
                "暂无游玩记录",
                ComponentTextRole.Caption,
                fontSize: 11)
            // 右侧大号主按钮：垂直占满信息带，醒目且好点
            .AddButton(
                "launch-btn",
                new ComponentRect(0.71, 0.60, 0.25, 0.30),
                "启动",
                LaunchActionId,
                glyph: "material:Play",
                isPrimary: true)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new WorldLaunchInstance(launchService))
        };
    }

    private sealed record WorldInfo(
        string DirectoryName,
        string VersionId,
        DateTime LastPlayed,
        string? IconPath)
    {
        /// <summary>跨实例的唯一标识：实例 id + 世界目录名（Windows 目录名不会包含 /）。</summary>
        public string Key => $"{VersionId}/{DirectoryName}";
    }

    private sealed class WorldLaunchInstance : PolygonComponentInstanceBase
    {
        private readonly GameLaunchService _launchService;
        private readonly object _gate = new();
        private List<WorldInfo> _worlds = [];
        private string? _selectedWorldId;

        public WorldLaunchInstance(GameLaunchService launchService)
        {
            _launchService = launchService;
            SetState(CreateState([], null));
            GameInstanceStore.Changed += OnInstancesChanged;
            RefreshAsync();
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("世界组件已释放。");

            if (string.Equals(
                    invocation.ActionId,
                    SelectWorldActionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (invocation.Arguments is null ||
                    !invocation.Arguments.TryGetValue(WorldIdArgument, out var worldId) ||
                    string.IsNullOrWhiteSpace(worldId))
                {
                    return ComponentActionResult.Failed("世界菜单项缺少世界标识。");
                }

                WorldInfo? selected;
                lock (_gate)
                {
                    _selectedWorldId = worldId;
                    selected = ResolveSelectedWorldLocked();
                }

                if (selected is null)
                    return ComponentActionResult.Failed("该世界已不存在，请重新打开菜单。");

                PublishFromCache();
                return ComponentActionResult.Completed($"已选择世界 {selected.DirectoryName}。");
            }

            if (string.Equals(
                    invocation.ActionId,
                    LaunchActionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                WorldInfo? world;
                lock (_gate)
                {
                    world = ResolveSelectedWorldLocked();
                }

                if (world is null)
                    return ComponentActionResult.Failed("没有可启动的世界，请先选择一个世界。");

                // 启动前先切换到世界所属的实例（与「游戏实例选择」组件同一通道），
                // 保证 LaunchSelectedAsync 启动的正是该世界所在的版本。
                var switched = await Dispatcher.UIThread.InvokeAsync(
                    () => GameInstanceStore.Select(world.VersionId));
                if (!switched)
                    return ComponentActionResult.Failed($"世界所属实例 {world.VersionId} 已不存在。");

                return await _launchService
                    .LaunchSelectedAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return ComponentActionResult.Failed($"未知世界组件动作：{invocation.ActionId}");
        }

        public override ValueTask DisposeAsync()
        {
            GameInstanceStore.Changed -= OnInstancesChanged;
            return base.DisposeAsync();
        }

        private void OnInstancesChanged(GameInstanceSnapshot snapshot) => RefreshAsync();

        private void RefreshAsync()
        {
            if (IsDisposed)
                return;

            _ = Task.Run(() =>
            {
                var worlds = ScanWorlds(GameInstanceStore.Current);
                Publish(worlds);
            });
        }

        /// <summary>
        /// 扫描全部实例的存档：每个实例按版本隔离设置解析出各自的游戏目录，
        /// 收集 saves 下的世界（以 level.dat 修改时间近似「上次游玩」），按时间倒序排列。
        /// </summary>
        private static List<WorldInfo> ScanWorlds(GameInstanceSnapshot snapshot)
        {
            var worlds = new List<WorldInfo>();
            try
            {
                if (snapshot.IsLoading ||
                    !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) ||
                    string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
                {
                    return worlds;
                }

                foreach (var versionId in snapshot.VersionIds)
                {
                    string? isolatedGameDirectory = null;
                    try
                    {
                        isolatedGameDirectory = GameVersionIsolation.GetGameDirectory(snapshot, versionId);
                    }
                    catch
                    {
                        // 隔离目录解析失败时回退共享目录
                    }

                    var gameDirectory = string.IsNullOrWhiteSpace(isolatedGameDirectory)
                        ? snapshot.MinecraftDirectory
                        : isolatedGameDirectory;
                    var savesDirectory = Path.Combine(gameDirectory, "saves");
                    if (!Directory.Exists(savesDirectory))
                        continue;

                    foreach (var world in new DirectoryInfo(savesDirectory).EnumerateDirectories())
                    {
                        var levelFile = Path.Combine(world.FullName, "level.dat");
                        var lastPlayed = File.Exists(levelFile)
                            ? File.GetLastWriteTime(levelFile)
                            : world.LastWriteTime;
                        var iconPath = Path.Combine(world.FullName, "icon.png");

                        worlds.Add(new WorldInfo(
                            world.Name,
                            versionId,
                            lastPlayed,
                            File.Exists(iconPath) ? iconPath : null));
                    }
                }
            }
            catch
            {
                // 扫描失败不应影响组件渲染，返回已收集到的部分
            }

            return worlds
                .OrderByDescending(w => w.LastPlayed)
                .ThenBy(w => w.DirectoryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>在持锁前提下解析当前应展示/启动的世界：选中项优先，缺失时回退最新世界。</summary>
        private WorldInfo? ResolveSelectedWorldLocked()
        {
            return _worlds.FirstOrDefault(w => string.Equals(w.Key, _selectedWorldId, StringComparison.Ordinal))
                   ?? _worlds.FirstOrDefault();
        }

        private void PublishFromCache()
        {
            List<WorldInfo> worlds;
            lock (_gate)
            {
                worlds = _worlds;
            }

            Publish(worlds);
        }

        private void Publish(List<WorldInfo> worlds)
        {
            if (IsDisposed)
                return;

            string? selectedId;
            lock (_gate)
            {
                _worlds = worlds;
                selectedId = _selectedWorldId;
            }

            SetState(CreateState(worlds, selectedId));
        }

        private static ComponentStateSnapshot CreateState(
            List<WorldInfo> worlds,
            string? selectedId)
        {
            var selected = worlds.FirstOrDefault(w => string.Equals(w.Key, selectedId, StringComparison.Ordinal))
                           ?? worlds.FirstOrDefault();

            var items = worlds.Count == 0
                ? [CreatePlaceholder("没有找到任何世界")]
                : worlds.Select((world, index) => new ComponentMenuItem
                {
                    Id = $"world-{index}",
                    Text = world.DirectoryName,
                    SecondaryText = $"实例 {world.VersionId}",
                    Glyph = "material:Earth",
                    ActionId = SelectWorldActionId,
                    Arguments = new Dictionary<string, string>
                    {
                        [WorldIdArgument] = world.Key
                    },
                    IsSelected = string.Equals(world.Key, selectedId, StringComparison.Ordinal)
                }).ToArray();

            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["world-preview"] = new ComponentElementState { ImageSource = selected?.IconPath },
                    ["world-name"] = new ComponentElementState
                    {
                        Text = selected?.DirectoryName ?? "全新世界"
                    },
                    ["world-menu"] = new ComponentElementState
                    {
                        IsEnabled = worlds.Count > 0,
                        MenuItems = items
                    },
                    ["last-played"] = new ComponentElementState
                    {
                        Text = selected is null
                            ? "暂无游玩记录"
                            : $"上次游玩：{selected.LastPlayed:yyyy/MM/dd HH:mm} · 实例 {selected.VersionId}"
                    },
                    ["launch-btn"] = new ComponentElementState
                    {
                        Text = "启动",
                        IsEnabled = selected is not null
                    }
                }
            };
        }

        private static ComponentMenuItem CreatePlaceholder(string text) =>
            new()
            {
                Id = "placeholder",
                Text = text,
                ActionId = SelectWorldActionId,
                IsEnabled = false
            };
    }
}
