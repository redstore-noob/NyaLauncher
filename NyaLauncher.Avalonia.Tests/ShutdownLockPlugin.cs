using NyaLauncher.Plugin.Abstractions.Plugins;

namespace Tests;

public sealed class PluginEntry : INyaLauncherPlugin
{
    public ValueTask StartAsync(
        IPluginContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
