namespace NyaLauncher.Core.Launch;

public interface IOfflineMinecraftLauncher
{
    Task<MinecraftLaunchResult> LaunchAsync(
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default);
}
