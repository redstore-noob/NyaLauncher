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
    /// <summary>组件 Id：<c>nyalauncher.builtin/version-manager</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/version-manager";
    private const string OpenActionId = "open-version-manager";

    public static PolygonComponentRegistration Create(Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        var definition = new PolygonComponentBuilder(ComponentId, "版本选择与管理")
            .WithDescription("进入版本管理页面，管理文件夹、实例、内容与启动设置")
            .WithGlyph("material:Layers")
            .WithSize(280, 100)
            .WithSizeLimits(230, 84, 390, 138)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.035, 0.16, 0.08, 0.32))
            .WithTheme(new PolygonComponentTheme())
            .AddAction(OpenActionId)
            .UseSurfaceAction(OpenActionId)
            .AddText(
                "manager-glyph",
                new ComponentRect(0.08, 0.24, 0.13, 0.5),
                "material:Layers",
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

    private sealed class VersionManagerInstance : PolygonComponentInstanceBase
    {
        private readonly Action<string> _navigate;

        public VersionManagerInstance(Action<string> navigate)
        {
            _navigate = navigate;
            SetState(CreateState());
            GameInstanceStore.Changed += OnInstancesChanged;
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("版本管理组件已释放。");
            if (!string.Equals(invocation.ActionId, OpenActionId, StringComparison.OrdinalIgnoreCase))
                return ComponentActionResult.Failed($"未知版本管理动作：{invocation.ActionId}");

            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => _navigate("version-manager"));
            return ComponentActionResult.Completed();
        }

        public override ValueTask DisposeAsync()
        {
            GameInstanceStore.Changed -= OnInstancesChanged;
            return base.DisposeAsync();
        }

        private void OnInstancesChanged(GameInstanceSnapshot snapshot)
        {
            if (IsDisposed)
                return;
            SetState(CreateState());
        }

        private static ComponentStateSnapshot CreateState()
        {
            var snapshot = GameInstanceStore.Current;
            var status = snapshot.IsLoading
                ? string.Empty
                : snapshot.ErrorMessage is not null
                    ? "文件夹无效，点击进入检查"
                    : snapshot.VersionIds.Count == 0
                        ? "还没有实例，点击添加并选择版本"
                        : $"共 {snapshot.VersionIds.Count} 个实例";
            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["manager-status"] = new() { Text = status }
                }
            };
        }
    }
}
