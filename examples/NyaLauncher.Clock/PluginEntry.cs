using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Clock;

public sealed class PluginEntry : PluginBase
{
    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var definition = ClockComponentDefinition.Create(Context.Manifest.Id);
        Context.Registrar.AddComponentArea(new PluginComponentArea
        {
            Id = $"{Context.Manifest.Id}.area",
            Title = "电子时钟",
            Subtitle = "当前电脑时间",
            Glyph = "◷",
            Components =
            [
                new PolygonComponentRegistration
                {
                    Definition = definition,
                    Factory = new ClockComponentFactory(Context.Settings)
                }
            ]
        });

        return ValueTask.CompletedTask;
    }
}

internal sealed class ClockComponentFactory(IPluginSettings settings) : IPolygonComponentFactory
{
    public IPolygonComponentInstance Create(ComponentInstanceContext context) =>
        new ClockComponentInstance(settings);
}
