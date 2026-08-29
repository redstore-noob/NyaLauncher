using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Content;
using NyaLauncher.Core.Launch;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInGameInstanceSelectorComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/game-instance-selector</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/game-instance-selector";
    private const string SelectInstanceActionId = "select-instance";
    private const string VersionIdArgument = "versionId";

    public static PolygonComponentRegistration Create()
    {
        var definition = new PolygonComponentBuilder(ComponentId, "游戏实例选择")
            .WithDescription("点击卡片任意位置，从下拉列表中选择游戏实例")
            .WithGlyph("material:ViewDashboard")
            .WithSize(300, 96)
            .WithSizeLimits(240, 72, 480, 120)
            .WithShape(PolygonShapeDefinition.Rectangle())
            // 左侧细条为拖拽把手，其余整卡都是下拉热区（点击任意位置弹出实例列表）
            .WithDragHandle(new ComponentRect(0.02, 0.20, 0.05, 0.60))
            .WithTheme(new PolygonComponentTheme())
            .AddAction(SelectInstanceActionId)
            .AddImage(
                "instance-icon",
                new ComponentRect(0.15, 0.24, 0.10, 0.52),
                stretch: ComponentImageStretch.Uniform,
                fallbackText: "material:Apps",
                cornerRadius: 8)
            .AddText(
                "instance-name",
                new ComponentRect(0.28, 0.18, 0.56, 0.32),
                "未选择实例",
                ComponentTextRole.Title,
                fontSize: 15)
            .AddText(
                "instance-status",
                new ComponentRect(0.28, 0.55, 0.56, 0.28),
                string.Empty,
                ComponentTextRole.Caption,
                fontSize: 11)
            .AddDropdown(
                "instance-menu",
                new ComponentRect(0.09, 0.05, 0.89, 0.90),
                glyph: "material:ChevronDown",
                alignRight: true)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new GameInstanceSelectorInstance())
        };
    }

    private sealed class GameInstanceSelectorInstance : PolygonComponentInstanceBase
    {
        private CancellationTokenSource? _visualCancellation;

        public GameInstanceSelectorInstance()
        {
            SetState(CreateState(GameInstanceStore.Current));
            GameInstanceStore.Changed += OnInstancesChanged;
            StartVisualLoad(GameInstanceStore.Current);
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("游戏实例选择组件已释放。");
            if (!string.Equals(
                    invocation.ActionId,
                    SelectInstanceActionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ComponentActionResult.Failed($"未知游戏实例组件动作：{invocation.ActionId}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (invocation.Arguments is null ||
                !invocation.Arguments.TryGetValue(VersionIdArgument, out var versionId))
            {
                return ComponentActionResult.Failed("游戏实例菜单项缺少版本标识。");
            }

            var selected = await Dispatcher.UIThread.InvokeAsync(
                () => GameInstanceStore.Select(versionId));
            return selected
                ? ComponentActionResult.Completed($"已选择游戏实例 {versionId}。")
                : ComponentActionResult.Failed("该游戏实例已不存在，请重新打开菜单。");
        }

        public override ValueTask DisposeAsync()
        {
            GameInstanceStore.Changed -= OnInstancesChanged;
            _visualCancellation?.Cancel();
            _visualCancellation?.Dispose();
            return base.DisposeAsync();
        }

        private void OnInstancesChanged(GameInstanceSnapshot snapshot)
        {
            if (IsDisposed)
                return;

            SetState(CreateState(snapshot));
            StartVisualLoad(snapshot);
        }

        private static ComponentStateSnapshot CreateState(
            GameInstanceSnapshot snapshot,
            IReadOnlyDictionary<string, GameInstanceVisual>? visuals = null)
        {
            string name;
            string status;
            IReadOnlyList<ComponentMenuItem> items;

            if (snapshot.IsLoading)
            {
                name = "实例列表";
                status = string.Empty;
                items = [CreatePlaceholder("实例列表加载中")];
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                name = "Minecraft 目录无效";
                status = "请在启动页检查游戏目录";
                items = [CreatePlaceholder("目录扫描失败", snapshot.ErrorMessage)];
            }
            else if (snapshot.VersionIds.Count == 0)
            {
                name = "未找到游戏实例";
                status = "当前 Minecraft 文件夹中没有版本";
                items = [CreatePlaceholder("没有可选择的游戏实例")];
            }
            else
            {
                name = snapshot.SelectedVersionId ?? "未选择实例";
                var selectedIsolated = snapshot.SelectedVersionId is { } selectedId &&
                                       GameVersionIsolation.IsEnabled(snapshot, selectedId);
                status = $"已安装 {snapshot.VersionIds.Count} 个实例 · " +
                         (selectedIsolated ? "版本隔离" : "共享目录");
                items = snapshot.VersionIds
                    .Select((versionId, index) => new ComponentMenuItem
                    {
                        Id = $"instance-{index}",
                        Text = versionId,
                        SecondaryText = GameVersionIsolation.IsEnabled(snapshot, versionId)
                            ? "版本隔离 · 独立内容目录"
                            : "共享 Minecraft 内容目录",
                        Glyph = visuals is not null && visuals.TryGetValue(versionId, out var visual)
                            ? visual.FallbackGlyph
                            : "material:Apps",
                        IconSource = visuals is not null && visuals.TryGetValue(versionId, out visual)
                            ? visual.IconPath
                            : null,
                        ActionId = SelectInstanceActionId,
                        Arguments = new Dictionary<string, string>
                        {
                            [VersionIdArgument] = versionId
                        },
                        IsSelected = string.Equals(
                            versionId,
                            snapshot.SelectedVersionId,
                            StringComparison.OrdinalIgnoreCase)
                    })
                    .ToArray();
            }

            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["instance-icon"] = CreateIconState(snapshot, visuals),
                    ["instance-name"] = new() { Text = name },
                    ["instance-status"] = new() { Text = status },
                    ["instance-menu"] = new()
                    {
                        IsEnabled = snapshot.VersionIds.Count > 0 &&
                                    !snapshot.IsLoading &&
                                    snapshot.ErrorMessage is null,
                        MenuItems = items
                    }
                }
            };
        }

        private void StartVisualLoad(GameInstanceSnapshot snapshot)
        {
            _visualCancellation?.Cancel();
            _visualCancellation?.Dispose();
            if (snapshot.IsLoading || snapshot.ErrorMessage is not null || snapshot.VersionIds.Count == 0)
                return;

            var cancellation = new CancellationTokenSource();
            _visualCancellation = cancellation;
            _ = LoadVisualsAsync(snapshot, cancellation);
        }

        private async Task LoadVisualsAsync(
            GameInstanceSnapshot snapshot,
            CancellationTokenSource cancellation)
        {
            try
            {
                var visuals = await Task.Run(() => snapshot.VersionIds.ToDictionary(
                    versionId => versionId,
                    versionId =>
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        return GameContentMetadataService.ResolveInstanceVisual(snapshot, versionId);
                    },
                    StringComparer.OrdinalIgnoreCase), cancellation.Token).ConfigureAwait(false);
                if (cancellation.IsCancellationRequested ||
                    IsDisposed ||
                    !ReferenceEquals(_visualCancellation, cancellation))
                {
                    return;
                }

                SetState(CreateState(snapshot, visuals));
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static ComponentElementState CreateIconState(
            GameInstanceSnapshot snapshot,
            IReadOnlyDictionary<string, GameInstanceVisual>? visuals)
        {
            if (snapshot.SelectedVersionId is not { } selected ||
                visuals is null ||
                !visuals.TryGetValue(selected, out var visual))
            {
                return new ComponentElementState { Text = "material:Apps" };
            }
            return new ComponentElementState
            {
                ImageSource = visual.IconPath,
                Text = visual.FallbackGlyph
            };
        }

        private static ComponentMenuItem CreatePlaceholder(
            string text,
            string secondaryText = "") =>
            new()
            {
                Id = "placeholder",
                Text = text,
                SecondaryText = secondaryText,
                ActionId = SelectInstanceActionId,
                IsEnabled = false
            };
    }
}
