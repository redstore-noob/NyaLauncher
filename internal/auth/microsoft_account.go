package auth

import "time"

// MicrosoftAccount 通过 Microsoft 账号（正版）登录后获得的 Minecraft 账号，
// 包含启动所需的全部令牌。该对象不包含任何私钥信息，但其中的令牌应妥善保存
// （结合 AccountSecretProtector 加密持久化）。
type MicrosoftAccount struct {
	// Username 游戏内玩家名（auth_player_name）。
	Username string `json:"Username"`
	// Uuid 32 位无连字符的玩家档案 UUID（Minecraft profile API 原生格式），
	// 用于 auth_uuid；与官方启动器及主流启动器的传参格式保持一致。
	Uuid string `json:"Uuid"`
	// AccessToken Minecraft 服务访问令牌，用于 auth_access_token。
	AccessToken string `json:"AccessToken"`
	// RefreshToken Microsoft 账号刷新令牌，用于令牌过期后的无感刷新。
	RefreshToken string `json:"RefreshToken"`
	// XboxUserId Xbox 用户 ID（xuid），用于 auth_xuid；部分旧版本可能缺失。
	XboxUserId string `json:"XboxUserId"`
	// ClientId 启动器用于 session 验证的客户端 ID（对应 --clientId 参数）。
	ClientId string `json:"ClientId"`
	// ExpiresAt Minecraft 访问令牌的过期时间（UTC）。
	ExpiresAt time.Time `json:"ExpiresAt"`
}

func (a MicrosoftAccount) AccountKind() string        { return "microsoft" }
func (a MicrosoftAccount) AccountUsername() string    { return a.Username }
func (a MicrosoftAccount) AccountUuid() string        { return a.Uuid }
func (a MicrosoftAccount) AccountAccessToken() string { return a.AccessToken }
func (a MicrosoftAccount) AccountUserType() string    { return "msa" }
func (a MicrosoftAccount) AccountXboxUserId() string  { return a.XboxUserId }

// IsExpired 访问令牌是否已过期（含 5 分钟提前量，避免临界时间启动失败）。
func (a MicrosoftAccount) IsExpired() bool {
	return time.Now().UTC().After(a.ExpiresAt.Add(-5 * time.Minute))
}

// AuthlibCredential 皮肤站（authlib-injector / Yggdrasil 规范）账号的持久化凭据。
// 访问令牌不随时间过期，但可能被服务端主动失效，启动前需 validate/refresh。
type AuthlibCredential struct {
	// Username 登录用户名（皮肤站邮箱或账号名），仅用于重新登录提示，不参与启动。
	Username string `json:"Username"`
	// ProfileName 游戏角色名（auth_player_name）。
	ProfileName string `json:"ProfileName"`
	// ProfileUuid 角色档案 UUID（Yggdrasil 原生带连字符格式）。
	ProfileUuid string `json:"ProfileUuid"`
	// AccessToken Yggdrasil 访问令牌，用于 auth_access_token 与启动前校验。
	AccessToken string `json:"AccessToken"`
	// ApiRoot 皮肤站 Yggdrasil API 根地址（已按 X-Authlib-Injector-API-Location 归一化）。
	ApiRoot string `json:"ApiRoot"`
	// ServerName 皮肤站显示名（来自元数据 meta.serverName），可为空。
	ServerName string `json:"ServerName"`
}

// AuthlibAccount 通过皮肤站登录获得的启动账号：以皮肤站角色身份启动游戏，
// 启动时由 launch 包的 AuthlibInjectorRuntime 注入 authlib-injector javaagent
// 把游戏会话服务重定向到对应皮肤站。
type AuthlibAccount struct {
	// ProfileName 游戏角色名。
	ProfileName string
	// ProfileUuid 角色档案 UUID（带连字符；参数构建时统一压缩为无连字符）。
	ProfileUuid string
	// AccessToken Yggdrasil 访问令牌。
	AccessToken string
	// ApiRoot 皮肤站 Yggdrasil API 根地址（javaagent 注入目标）。
	ApiRoot string
}

func (a AuthlibAccount) AccountKind() string        { return "authlib" }
func (a AuthlibAccount) AccountUsername() string    { return a.ProfileName }
func (a AuthlibAccount) AccountUuid() string        { return a.ProfileUuid }
func (a AuthlibAccount) AccountAccessToken() string { return a.AccessToken }
func (a AuthlibAccount) AccountUserType() string    { return "msa" }
func (a AuthlibAccount) AccountXboxUserId() string  { return "" }
