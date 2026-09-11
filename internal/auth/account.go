package auth

// MinecraftAccount 可供 Minecraft 启动使用的统一账号抽象。
// 离线账号与正版（Microsoft）账号都实现此接口。
// 注意：C# 中该接口定义在 Launch 命名空间（IMinecraftAccount）；Go 版为避免
// internal/launch 与 internal/auth 的循环依赖，接口定义移到 auth 包，
// launch 侧以类型别名引用（见 launch.MinecraftAccount）。
type MinecraftAccount interface {
	// AccountKind 账号类型："offline" | "microsoft" | "authlib"。
	// C# 通过类型模式匹配区分，Go 改用显式类型标记。
	AccountKind() string
	// AccountUsername 游戏内玩家名（auth_player_name）。
	AccountUsername() string
	// AccountUuid 用于 auth_uuid 的 UUID 字符串。
	// 正版账号为 32 位无连字符的档案 UUID，离线账号为无连字符的离线 UUID。
	AccountUuid() string
	// AccountAccessToken 用于 auth_access_token 的访问令牌；离线账号固定为 "0"。
	AccountAccessToken() string
	// AccountUserType 用于 user_type 的启动参数：离线为 "legacy"，正版为 "msa"。
	AccountUserType() string
	// AccountXboxUserId 用于 auth_xuid 的 Xbox 用户 ID；离线账号为空字符串。
	AccountXboxUserId() string
}
