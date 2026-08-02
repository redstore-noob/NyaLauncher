namespace NyaLauncher.Core.Launch.Auth;

/// <summary>
/// 正版（Microsoft 账号）认证器，负责完成完整的 Microsoft → Xbox → Minecraft 登录链路，
/// 以及令牌的刷新与有效性校验。
/// </summary>
public interface IMicrosoftAuthenticator
{
    /// <summary>
    /// 通过设备码流程完成正版登录。
    /// </summary>
    /// <param name="deviceCodeHandler">
    /// 可选回调，在获得设备码后调用，用于向用户展示验证码并等待用户完成授权。
    /// 为 null 时自动尝试打开系统浏览器跳转到验证页面。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含完整令牌的正版账号。</returns>
    Task<MicrosoftAccount> AuthenticateAsync(
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeHandler = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用刷新令牌无感刷新账号令牌（无需用户重新授权）。
    /// </summary>
    Task<MicrosoftAccount> RefreshAsync(
        MicrosoftAccount account,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验账号令牌是否仍然有效；已过期时自动尝试刷新。
    /// </summary>
    Task<MicrosoftAccount> ValidateAsync(
        MicrosoftAccount account,
        CancellationToken cancellationToken = default);
}
