using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Core.Launch;

/// <summary>
/// 正版（Microsoft 账号）Minecraft 启动器。
/// </summary>
public sealed class MicrosoftMinecraftLauncher : IMicrosoftMinecraftLauncher
{
    private readonly IOfflineMinecraftLauncher _launcher;

    public MicrosoftMinecraftLauncher(IOfflineMinecraftLauncher? launcher = null)
    {
        _launcher = launcher ?? new OfflineMinecraftLauncher();
    }

    /// <inheritdoc />
    public async Task<MinecraftLaunchResult> LaunchAsync(
        MicrosoftAccount account,
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            throw new MinecraftLaunchException("正版账号缺少访问令牌，请先完成登录。");
        }

        if (account.IsExpired)
        {
            throw new MinecraftLaunchException(
                "正版账号的访问令牌已过期，请先通过 IMicrosoftAuthenticator 刷新或重新登录。");
        }

        return await _launcher.LaunchAsync(
            options.WithAccount(account),
            cancellationToken).ConfigureAwait(false);
    }
}
