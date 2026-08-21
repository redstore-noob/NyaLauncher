using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Launch;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

internal static class BuiltInVersionManagerComponent
{
    public const string ComponentId = "nyalauncher.builtin/version-manager";
    private const string OpenActionId = "open-version-manager";

    public static PolygonComponentRegistration Create(Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        var definition = new PolygonComponentBuilder(ComponentId, "版本选择与管理")
            .WithDescription("进入版本管理页面，管理文件夹、实例、内容与启动设置")
            .WithGlyph("▦")
            .WithSize(280, 100)
            .WithSizeLimits(230, 84, 390, 138)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.035, 0.16, 0.08, 0.32))
            .WithTheme(ThemePolygonHelper.CreateDefaultTheme())
            .AddAction(OpenActionId)
            .UseSurfaceAction(OpenActionId)
            .AddText(
                "manager-glyph",
                new ComponentRect(0.08, 0.24, 0.13, 0.5),
                "▦",
                ComponentTextRole.Emphasis,
                fontSize: 24)
            .AddText(
                "manager-title",
                new ComponentRect(0.25, 0.2, 0.67, 0.28),
                "版本选择与管理",
                ComponentTextRole.Title,
                fontSize: 15)
            .AddText(
                "manager-status",
                new ComponentRect(0.25, 0.52, 0.67, 0.22),
                "点击进入版本管理",
                ComponentTextRole.Caption,
                fontSize: 10)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new VersionManagerInstance(navigate))
        };
    }

    private sealed class VersionManagerInstance : IPolygonComponentInstance
    {
        private readonly Action<string> _navigate;
        private ComponentStateSnapshot _currentState;
        private long _revision;
        private int _isDisposed;

        public VersionManagerInstance(Action<string> navigate)
        {
            _navigate = navigate;
            _currentState = CreateState(Interlocked.Increment(ref _revision));
            GameInstanceStore.Changed += OnInstancesChanged;
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return ComponentActionResult.Failed("版本管理组件已释放。");
            if (!string.Equals(invocation.ActionId, OpenActionId, StringComparison.OrdinalIgnoreCase))
                return ComponentActionResult.Failed($"未知版本管理动作：{invocation.ActionId}");

            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => _navigate("version-manager"));
            return ComponentActionResult.Completed();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                GameInstanceStore.Changed -= OnInstancesChanged;
                StateChanged = null;
            }
            return ValueTask.CompletedTask;
        }

        private void OnInstancesChanged(GameInstanceSnapshot snapshot)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;
            var state = CreateState(Interlocked.Increment(ref _revision));
            Volatile.Write(ref _currentState, state);
            StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(state));
        }

        private static ComponentStateSnapshot CreateState(long revision)
        {
            var snapshot = GameInstanceStore.Current;
            var status = snapshot.IsLoading
                ? "正在扫描版本文件夹…"
                : snapshot.ErrorMessage is not null
                    ? "文件夹无效，点击进入检查"
                    : snapshot.SelectedVersionId is { Length: > 0 } versionId
                        ? $"当前：{versionId}"
                        : "点击添加并选择版本";
            return new ComponentStateSnapshot
            {
                Revision = revision,
                Elements = new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["manager-status"] = new() { Text = status }
                }
            };
        }
    }
}
