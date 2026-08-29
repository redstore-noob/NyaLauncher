using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Launch;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 「内存使用」紧凑信息卡：已用 / 物理内存 + 强调色进度条，
/// 附带当前 JVM 上限配置（自动模式下为启动时的解析值）。
/// 定时轮询系统内存快照，可在未来无缝接入实际 JVM 进程占用。
/// </summary>
internal static class BuiltInMemoryUsageComponent
{
    /// <summary>组件 Id：<c>nyalauncher.builtin/memory-usage</c>。全局唯一且必须保持稳定，用户的工作区布局与个性化配置靠它引用本组件。</summary>
    public const string ComponentId = "nyalauncher.builtin/memory-usage";
    private const int RefreshPeriodMs = 4000;

    public static PolygonComponentRegistration Create()
    {
        var definition = new PolygonComponentBuilder(ComponentId, "内存使用")
            .WithDescription("物理内存占用与 JVM 内存上限一览")
            .WithGlyph("material:Memory")
            .WithSize(240, 130)
            .WithSizeLimits(180, 100, 420, 220)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.015, 0.32, 0.045, 0.38))
            .WithTheme(new PolygonComponentTheme())
            .AddText(
                "mem-title",
                new ComponentRect(0.07, 0.10, 0.50, 0.16),
                "内存使用",
                ComponentTextRole.Caption,
                fontSize: 12)
            .AddText(
                "mem-value",
                new ComponentRect(0.07, 0.27, 0.86, 0.17),
                "采样中…",
                ComponentTextRole.Emphasis,
                fontSize: 13)
            // 进度条高度 0.08 × 130px ≈ 10px，两端圆角由宿主渲染层处理
            .AddProgress(
                "mem-bar",
                new ComponentRect(0.07, 0.50, 0.86, 0.08),
                label: "",
                value: 0)
            .AddText(
                "mem-percent",
                new ComponentRect(0.07, 0.64, 0.86, 0.13),
                "",
                ComponentTextRole.Caption,
                fontSize: 10)
            .AddText(
                "mem-jvm",
                new ComponentRect(0.07, 0.82, 0.86, 0.13),
                "",
                ComponentTextRole.Caption,
                fontSize: 10)
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new MemoryUsageInstance())
        };
    }

    private sealed class MemoryUsageInstance : PolygonComponentInstanceBase
    {
        private readonly Timer _refreshTimer;

        public MemoryUsageInstance()
        {
            SetState(CreateState(null, 0, null));
            _refreshTimer = new Timer(
                _ => Refresh(),
                null,
                dueTime: 0,
                RefreshPeriodMs);
        }

        public override ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ComponentActionResult.Failed(
                $"内存组件为纯展示组件，未知动作：{invocation.ActionId}"));

        public override ValueTask DisposeAsync()
        {
            _refreshTimer.Dispose();
            return base.DisposeAsync();
        }

        private void Refresh()
        {
            if (IsDisposed)
                return;

            try
            {
                var memory = GameMemorySettings.GetSystemMemory();
                var usedMb = Math.Clamp(
                    memory.TotalMemoryMb - memory.AvailableMemoryMb,
                    0,
                    memory.TotalMemoryMb);
                var jvmMb = ReadJvmMaximumMemoryMb();

                Publish(
                    memory.TotalMemoryMb,
                    usedMb,
                    jvmMb);
            }
            catch
            {
                // 内存采样失败时保留上一次快照，等待下一轮重试
            }
        }

        /// <summary>当前启动策略下的 JVM -Xmx 上限（自动模式取解析值，手动模式取配置值）。</summary>
        private static int ReadJvmMaximumMemoryMb()
        {
            try
            {
                return GameMemorySettings.IsAutomaticAdjustmentEnabled
                    ? GameMemorySettings.ResolveForLaunch().MaximumMemoryMb
                    : GameMemorySettings.GetManualMaximumMemoryMb();
            }
            catch
            {
                return 0;
            }
        }

        private void Publish(int totalMb, int usedMb, int jvmMb)
        {
            SetState(CreateState(totalMb, usedMb, jvmMb));
        }

        private ComponentStateSnapshot CreateState(int? totalMb, int usedMb, int? jvmMb)
        {
            string? valueText = null;
            double? progressValue = null;
            string? percentText = null;
            string? jvmText = null;

            if (totalMb is { } total && total > 0)
            {
                valueText = $"{FormatGb(usedMb)} GB / {FormatGb(total)} GB";
                progressValue = 100.0 * usedMb / total;
                percentText = $"{(int)Math.Round(progressValue.Value)}%";
            }

            if (jvmMb is { } jvm && jvm > 0)
                jvmText = GameMemorySettings.IsAutomaticAdjustmentEnabled
                    ? $"JVM 自动上限 {FormatGb(jvm)} GB"
                    : $"JVM 配置 {FormatGb(jvm)} GB";

            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["mem-value"] = new ComponentElementState { Text = valueText },
                    ["mem-bar"] = new ComponentElementState { ProgressValue = progressValue },
                    ["mem-percent"] = new ComponentElementState { Text = percentText },
                    ["mem-jvm"] = new ComponentElementState { Text = jvmText }
                }
            };
        }

        private static string FormatGb(int megabytes) =>
            (megabytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
