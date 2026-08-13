using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInGameInstanceSelectorComponent
{
    public const string ComponentId = "nyalauncher.builtin/game-instance-selector";
    private const string SelectInstanceActionId = "select-instance";
    private const string VersionIdArgument = "versionId";

    public static PolygonComponentRegistration Create()
    {
        var definition = new PolygonComponentBuilder(ComponentId, "游戏实例选择")
            .WithDescription("显示并切换当前 Minecraft 文件夹内已安装的游戏实例")
            .WithGlyph("▣")
            .WithSize(260, 72)
            .WithSizeLimits(220, 64, 380, 92)
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
            .AddAction(SelectInstanceActionId)
            .AddImage(
                "instance-icon",
                new ComponentRect(0.115, 0.25, 0.085, 0.5),
                stretch: ComponentImageStretch.Uniform,
                fallbackText: "▦",
                cornerRadius: 6)
            .AddText(
                "instance-name",
                new ComponentRect(0.215, 0.17, 0.59, 0.34),
                "未选择实例",
                ComponentTextRole.Title,
                fontSize: 14)
            .AddText(
                "instance-status",
                new ComponentRect(0.215, 0.52, 0.59, 0.24),
                "正在扫描 Minecraft 文件夹",
                ComponentTextRole.Caption,
                fontSize: 10)
            .AddDropdown(
                "instance-menu",
                new ComponentRect(0.84, 0.22, 0.115, 0.56))
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new GameInstanceSelectorInstance())
        };
    }

    private sealed class GameInstanceSelectorInstance : IPolygonComponentInstance
    {
        private ComponentStateSnapshot _currentState;
        private CancellationTokenSource? _visualCancellation;
        private long _revision;
        private int _isDisposed;

        public GameInstanceSelectorInstance()
        {
            _currentState = CreateState(
                GameInstanceStore.Current,
                Interlocked.Increment(ref _revision),
                null);
            GameInstanceStore.Changed += OnInstancesChanged;
            StartVisualLoad(GameInstanceStore.Current);
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
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

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                GameInstanceStore.Changed -= OnInstancesChanged;
                _visualCancellation?.Cancel();
                _visualCancellation?.Dispose();
                StateChanged = null;
            }

            return ValueTask.CompletedTask;
        }

        private void OnInstancesChanged(GameInstanceSnapshot snapshot)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;

            var next = CreateState(snapshot, Interlocked.Increment(ref _revision), null);
            Volatile.Write(ref _currentState, next);
            StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(next));
            StartVisualLoad(snapshot);
        }

        private static ComponentStateSnapshot CreateState(
            GameInstanceSnapshot snapshot,
            long revision,
            IReadOnlyDictionary<string, GameInstanceVisual>? visuals)
        {
            string name;
            string status;
            IReadOnlyList<ComponentMenuItem> items;

            if (snapshot.IsLoading)
            {
                name = "正在扫描游戏实例…";
                status = "请稍候";
                items = [CreatePlaceholder("正在扫描 Minecraft 文件夹…")];
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
                            : "▦",
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
                Revision = revision,
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
                    Volatile.Read(ref _isDisposed) != 0 ||
                    !ReferenceEquals(_visualCancellation, cancellation))
                {
                    return;
                }

                var next = CreateState(snapshot, Interlocked.Increment(ref _revision), visuals);
                Volatile.Write(ref _currentState, next);
                StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(next));
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
                return new ComponentElementState { Text = "▦" };
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
