using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Core.Launch;

/// <summary>
/// 使用正版（Microsoft）账号启动 Minecraft 的入口。
/// 登录与令牌刷新由 <see cref="Auth.IMicrosoftAuthenticator"/> 完成，
/// 本类负责校验令牌并复用现有的离线启动管线（进程构造、参数构建、资源解析）。
/// </summary>
public interface IMicrosoftMinecraftLauncher
{
    Task<MinecraftLaunchResult> LaunchAsync(
        MicrosoftAccount account,
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default);
}
