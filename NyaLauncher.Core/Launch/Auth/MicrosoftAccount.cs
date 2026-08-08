namespace NyaLauncher.Core.Launch.Auth;

/// <summary>
/// 通过 Microsoft 账号（正版）登录后获得的 Minecraft 账号，包含启动所需的全部令牌。
/// 该对象不包含任何私钥信息，但其中的令牌应妥善保存（建议结合 TokenCrypto 加密持久化）。
/// </summary>
public sealed record MicrosoftAccount : IMinecraftAccount
{
    /// <summary>游戏内玩家名（auth_player_name）。</summary>
    public required string Username { get; init; }

    /// <summary>
    /// 32 位无连字符的玩家档案 UUID（Minecraft profile API 原生格式），
    /// 用于 auth_uuid；与官方启动器及主流启动器的传参格式保持一致。
    /// </summary>
    public required string Uuid { get; init; }

    /// <summary>Minecraft 服务访问令牌，用于 auth_access_token。</summary>
    public required string AccessToken { get; init; }

    /// <summary>Microsoft 账号刷新令牌，用于令牌过期后的无感刷新。</summary>
    public required string RefreshToken { get; init; }

    /// <summary>Xbox 用户 ID（xuid），用于 auth_xuid；部分旧版本可能缺失。</summary>
    public string XboxUserId { get; init; } = string.Empty;

    /// <summary>启动器用于 session 验证的客户端 ID（对应 --clientId 参数）。</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Minecraft 访问令牌的过期时间（UTC）。</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>启动参数 user_type：正版为 "msa"。</summary>
    public string UserType => "msa";

    /// <summary>访问令牌是否已过期（含 5 分钟提前量，避免临界时间启动失败）。</summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-5);
}
