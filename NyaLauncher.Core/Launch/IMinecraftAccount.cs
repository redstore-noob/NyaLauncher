namespace NyaLauncher.Core.Launch;

/// <summary>
/// 可供 Minecraft 启动使用的统一账号抽象。
/// 离线账号与正版（Microsoft）账号都实现此接口。
/// </summary>
public interface IMinecraftAccount
{
    /// <summary>游戏内玩家名（auth_player_name）。</summary>
    string Username { get; }

    /// <summary>
    /// 用于 auth_uuid 的 UUID 字符串。
    /// 正版账号为带连字符的档案 UUID，离线账号为无连字符的离线 UUID。
    /// </summary>
    string Uuid { get; }

    /// <summary>用于 auth_access_token 的访问令牌；离线账号固定为 "0"。</summary>
    string AccessToken { get; }

    /// <summary>用于 user_type 的启动参数：离线为 "legacy"，正版为 "msa"。</summary>
    string UserType { get; }

    /// <summary>用于 auth_xuid 的 Xbox 用户 ID；离线账号为空字符串。</summary>
    string XboxUserId { get; }
}
