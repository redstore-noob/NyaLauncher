using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Plugins;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Stable launcher-owned entry to the plugin manager. It stays available even
/// when discovery fails, so users can always reach diagnostics and the package
/// directory without relying on third-party code.
/// </summary>
internal static class BuiltInPluginListComponent
{
    public const string ComponentId = "nyalauncher.builtin/plugin-list";
    private const string OpenActionId = "open-plugin-list";

    public static PolygonComponentRegistration Create(
        Action<string> navigate,
        PluginManager? pluginManager = null)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        var definition = new PolygonComponentBuilder(ComponentId, "插件列表")
            .WithDescription("查看、启用和配置已安装的第三方插件")
            .WithGlyph("＋")
            .WithSize(220, 82)
            .WithSizeLimits(180, 68, 310, 112)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.025, 0.24, 0.075, 0.52))
            .WithTheme(new PolygonComponentTheme
            {
                Surface = "#252D47",
                SurfaceHover = "#303A59",
                Border = "#3A4563",
                BorderHover = "#91A0FF",
                Accent = "#91A0FF",
                ProgressTrack = "#30384F"
            })
            .AddAction(OpenActionId)
            .UseSurfaceAction(OpenActionId)
            .AddText(
                "plugin-glyph",
                new ComponentRect(0.1, 0.23, 0.12, 0.5),
                "＋",
                ComponentTextRole.Emphasis,
                fontSize: 19)
            .AddText(
                "plugin-title",
                new ComponentRect(0.26, 0.18, 0.67, 0.28),
                "插件列表",
                ComponentTextRole.Title,
                fontSize: 14)
            .AddText(
                "plugin-status",
                new ComponentRect(0.26, 0.52, 0.67, 0.22),
                "点击管理第三方插件",
                ComponentTextRole.Caption,
                fontSize: 10)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new PluginListInstance(navigate, pluginManager))
        };
    }

    private sealed class PluginListInstance : IPolygonComponentInstance
    {
        private readonly Action<string> _navigate;
        private readonly PluginManager? _pluginManager;
        private ComponentStateSnapshot _currentState;
        private long _revision;
        private int _isDisposed;

        public PluginListInstance(Action<string> navigate, PluginManager? pluginManager)
        {
            _navigate = navigate;
            _pluginManager = pluginManager;
            _currentState = CreateState(
                pluginManager?.Current,
                Interlocked.Increment(ref _revision));
            if (_pluginManager is not null)
                _pluginManager.Changed += OnCatalogChanged;
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return ComponentActionResult.Failed("插件列表组件已释放。");
            if (!string.Equals(invocation.ActionId, OpenActionId, StringComparison.OrdinalIgnoreCase))
                return ComponentActionResult.Failed($"未知插件列表动作：{invocation.ActionId}");

            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => _navigate("plugins"));
            return ComponentActionResult.Completed();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                if (_pluginManager is not null)
                    _pluginManager.Changed -= OnCatalogChanged;
                StateChanged = null;
            }

            return ValueTask.CompletedTask;
        }

        private void OnCatalogChanged(object? sender, EventArgs e)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;

            var next = CreateState(
                _pluginManager?.Current,
                Interlocked.Increment(ref _revision));
            Volatile.Write(ref _currentState, next);
            StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(next));
        }

        private static ComponentStateSnapshot CreateState(
            PluginCatalogSnapshot? snapshot,
            long revision)
        {
            var status = snapshot is null
                ? "插件框架尚未初始化"
                : snapshot.IsScanning
                    ? "正在扫描插件目录…"
                    : !string.IsNullOrWhiteSpace(snapshot.Error)
                        ? "插件目录读取失败"
                        : CreateSummary(snapshot);
            return new ComponentStateSnapshot
            {
                Revision = revision,
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["plugin-status"] = new() { Text = status }
                }
            };
        }

        private static string CreateSummary(PluginCatalogSnapshot snapshot)
        {
            if (snapshot.Plugins.Count == 0)
                return "未安装插件 · 点击打开目录";

            var enabled = snapshot.Plugins.Count(plugin => plugin.IsEnabled);
            var failures = snapshot.Plugins.Count(plugin =>
                !string.IsNullOrWhiteSpace(plugin.Error) ||
                plugin.Status is PluginStatus.Invalid or PluginStatus.Incompatible or PluginStatus.Failed);
            return failures > 0
                ? $"{snapshot.Plugins.Count} 个插件 · {failures} 个需处理"
                : $"{snapshot.Plugins.Count} 个插件 · {enabled} 个已启用";
        }
    }
}
