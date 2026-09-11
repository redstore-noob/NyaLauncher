package auth

import (
	"encoding/json"
	"fmt"
	"strings"
	"sync"
	"time"

	"nyalauncher/internal/config"
	"nyalauncher/internal/logs"
)

// accountDto 账号持久化 DTO；JSON 字段名与 C# 默认序列化名（PascalCase）一致，
// 兼容读取既有 config.json。
type accountDto struct {
	Type          string     `json:"Type,omitempty"`
	Username      string     `json:"Username,omitempty"`
	Uuid          string     `json:"Uuid,omitempty"`
	AccessToken   string     `json:"AccessToken,omitempty"`
	RefreshToken  string     `json:"RefreshToken,omitempty"`
	XboxUserId    string     `json:"XboxUserId,omitempty"`
	ClientId      string     `json:"ClientId,omitempty"`
	ExpiresAt     *time.Time `json:"ExpiresAt,omitempty"`
	ProfileName   string     `json:"ProfileName,omitempty"`
	ApiRoot       string     `json:"ApiRoot,omitempty"`
	ServerName    string     `json:"ServerName,omitempty"`
	OfflineName   string     `json:"OfflineName,omitempty"`
	OfflineSkinId string     `json:"OfflineSkinId,omitempty"`
}

// AccountsConfigKey 账号列表在 config.json 中的键。
const AccountsConfigKey = "accounts"

// AccountStoreService 账号存储的可实例化实现：所有页面共享 AccountStore 门面持有的
// 默认实例；需要隔离状态的场景（单元测试、多存储目录）可直接构造本类。
type AccountStoreService struct {
	gate sync.RWMutex
	// current 内存中的权威账号列表（对应 C# ObservableCollection）。
	current []*LaunchAccount
	// refreshGates 以账号稳定键为粒度的刷新锁。
	refreshGates sync.Map
	// OnChanged 账号列表发生变化（增/删/排序）时触发。
	// C# 为多订阅者事件，Go 移植为单回调字段（语义偏离见 PORTING_NOTES.md）。
	OnChanged func()
}

// NewAccountStoreService 构造账号存储；loadFromDisk 为 true 时立即从 config.json 加载。
func NewAccountStoreService(loadFromDisk bool) *AccountStoreService {
	service := &AccountStoreService{}
	if loadFromDisk {
		service.current = service.loadFromDisk()
	}
	return service
}

// Selected 列表首项是所有页面和组件共享的当前账号。
func (s *AccountStoreService) Selected() *LaunchAccount {
	s.gate.RLock()
	defer s.gate.RUnlock()
	if len(s.current) == 0 {
		return nil
	}
	return s.current[0]
}

// Current 返回账号列表快照。
func (s *AccountStoreService) Current() []*LaunchAccount {
	s.gate.RLock()
	defer s.gate.RUnlock()
	out := make([]*LaunchAccount, len(s.current))
	copy(out, s.current)
	return out
}

// contains 判断列表中是否存在同一账号对象（引用相等，与 C# 一致）。
func (s *AccountStoreService) contains(account *LaunchAccount) bool {
	for _, candidate := range s.current {
		if candidate == account {
			return true
		}
	}
	return false
}

// Add 新增账号并置于列表首项。
func (s *AccountStoreService) Add(account *LaunchAccount) {
	if account == nil {
		panic("account 不能为空")
	}
	s.gate.Lock()
	s.current = append([]*LaunchAccount{account}, s.current...)
	s.gate.Unlock()
	s.Save()
	s.raiseChanged()
}

// Remove 移除账号。注意：删除后允许列表为空，不再自动补充默认账号，
// 否则用户删除最后一个账号时"删了又出现"，看起来像删除失败。
func (s *AccountStoreService) Remove(account *LaunchAccount) {
	s.gate.Lock()
	for index, candidate := range s.current {
		if candidate == account {
			s.current = append(s.current[:index], s.current[index+1:]...)
			break
		}
	}
	s.gate.Unlock()
	s.Save()
	s.raiseChanged()
}

// MoveToTop 把指定账号设为默认（移到列表顶部）。
func (s *AccountStoreService) MoveToTop(account *LaunchAccount) {
	if account == nil {
		panic("account 不能为空")
	}
	s.gate.Lock()
	if len(s.current) > 0 && s.current[0] == account {
		s.gate.Unlock()
		return
	}
	found := false
	for index, candidate := range s.current {
		if candidate == account {
			s.current = append(s.current[:index], s.current[index+1:]...)
			found = true
			break
		}
	}
	if found {
		s.current = append([]*LaunchAccount{account}, s.current...)
	}
	s.gate.Unlock()
	if found {
		s.Save()
		s.raiseChanged()
	}
}

// GetStableKey 账号的持久身份键。
func (s *AccountStoreService) GetStableKey(account *LaunchAccount) string {
	if account == nil {
		panic("account 不能为空")
	}
	var identity string
	switch account.Type {
	case "microsoft":
		if account.Microsoft != nil {
			identity = account.Microsoft.Uuid
		}
	case "authlib":
		// 皮肤站身份含 API 根：同一角色 UUID 在不同皮肤站是不同账号
		if account.Authlib != nil {
			identity = fmt.Sprintf("%s@%s", account.Authlib.ProfileUuid,
				strings.TrimSuffix(account.Authlib.ApiRoot, "/"))
		}
	case "offline":
		identity = account.OfflineName
	default:
		identity = account.DisplayName
	}
	if strings.TrimSpace(identity) == "" {
		identity = account.DisplayName
	}
	return account.Type + ":" + identity
}

// SelectByStableKey 通过持久身份切换当前账号；成功后该账号会移动到列表首项。
func (s *AccountStoreService) SelectByStableKey(key string) bool {
	if strings.TrimSpace(key) == "" {
		return false
	}
	account := s.FindByStableKey(key)
	if account == nil {
		return false
	}
	s.MoveToTop(account)
	return true
}

// FindByStableKey 通过持久身份查找账号（忽略大小写）。
func (s *AccountStoreService) FindByStableKey(key string) *LaunchAccount {
	if strings.TrimSpace(key) == "" {
		return nil
	}
	for _, candidate := range s.Current() {
		if strings.EqualFold(s.GetStableKey(candidate), key) {
			return candidate
		}
	}
	return nil
}

// Reload 在配置存储目录切换后，从新的 config.json 重新载入账号。
func (s *AccountStoreService) Reload() {
	loaded := s.loadFromDisk()
	s.gate.Lock()
	s.current = loaded
	s.gate.Unlock()
	s.raiseChanged()
}

// UpdateMicrosoftAccount 更新正版账号凭据（经由存储以保证落盘与 Changed 通知）。
func (s *AccountStoreService) UpdateMicrosoftAccount(account *LaunchAccount, microsoft *MicrosoftAccount) {
	if account == nil || microsoft == nil {
		panic("参数不能为空")
	}
	s.gate.Lock()
	if !s.contains(account) || !strings.EqualFold(account.Type, "microsoft") {
		s.gate.Unlock()
		panic("只能更新账号存储中的正版账号。")
	}
	account.Microsoft = microsoft
	s.gate.Unlock()
	s.Save()
	s.raiseChanged()
}

// UpdateAuthlibAccount 更新皮肤站账号凭据；显示名跟随角色名，
// 保证重新登录换角色后列表同步更新。
func (s *AccountStoreService) UpdateAuthlibAccount(account *LaunchAccount, credential *AuthlibCredential) {
	if account == nil || credential == nil {
		panic("参数不能为空")
	}
	s.gate.Lock()
	if !s.contains(account) || !strings.EqualFold(account.Type, "authlib") {
		s.gate.Unlock()
		panic("只能更新账号存储中的皮肤站账号。")
	}
	account.Authlib = credential
	if strings.TrimSpace(credential.ProfileName) != "" {
		account.DisplayName = credential.ProfileName
	}
	s.gate.Unlock()
	s.Save()
	s.raiseChanged()
}

// UpdateOfflineSkin 更新离线账号皮肤。
func (s *AccountStoreService) UpdateOfflineSkin(account *LaunchAccount, skinId string) {
	if account == nil || strings.TrimSpace(skinId) == "" {
		panic("参数不能为空")
	}
	s.gate.Lock()
	if !s.contains(account) || !strings.EqualFold(account.Type, "offline") {
		s.gate.Unlock()
		panic("只能更新账号存储中的离线账号。")
	}
	account.OfflineSkinId = skinId
	s.gate.Unlock()
	s.Save()
	s.raiseChanged()
}

// HasOfflineName 是否已存在同名离线账号（忽略大小写）。
func (s *AccountStoreService) HasOfflineName(name string) bool {
	for _, candidate := range s.Current() {
		if candidate.Type == "offline" &&
			strings.EqualFold(candidate.OfflineName, name) {
			return true
		}
	}
	return false
}

// WithRefreshLock 以账号稳定键为粒度串行化「刷新凭据」操作。微软 OAuth 轮换策略下，
// 同一 refresh_token 被并发使用两次时第二次必得 invalid_grant（账号被强制下线）。
// 启动校验与皮肤/档案服务等不同组件都会触发刷新，必须使用同一实例的这把锁；
// 进入锁后回调内应重新读取 account.Microsoft 判断过期，
// 因为等待锁的期间可能已有并发的刷新完成并写入了轮换后的令牌。
func (s *AccountStoreService) WithRefreshLock(account *LaunchAccount, action func() error) error {
	if account == nil || action == nil {
		panic("参数不能为空")
	}
	key := s.GetStableKey(account)
	gateAny, _ := s.refreshGates.LoadOrStore(key, &sync.Mutex{})
	gate := gateAny.(*sync.Mutex)
	gate.Lock()
	defer gate.Unlock()
	return action()
}

// Save 把当前列表写回 config.json。
func (s *AccountStoreService) Save() {
	snapshot := s.Current()
	dtos := make([]accountDto, 0, len(snapshot))
	for _, account := range snapshot {
		switch {
		case account.Type == "microsoft" && account.Microsoft != nil:
			ms := account.Microsoft
			expiresAt := ms.ExpiresAt
			dtos = append(dtos, accountDto{
				Type:         "microsoft",
				Username:     ms.Username,
				Uuid:         ms.Uuid,
				AccessToken:  ms.AccessToken,
				RefreshToken: ms.RefreshToken,
				XboxUserId:   ms.XboxUserId,
				ClientId:     ms.ClientId,
				ExpiresAt:    &expiresAt,
			})
		case account.Type == "authlib" && account.Authlib != nil:
			authlib := account.Authlib
			dtos = append(dtos, accountDto{
				Type:        "authlib",
				Username:    authlib.Username,
				ProfileName: authlib.ProfileName,
				Uuid:        authlib.ProfileUuid,
				AccessToken: authlib.AccessToken,
				ApiRoot:     authlib.ApiRoot,
				ServerName:  authlib.ServerName,
			})
		case account.Type == "offline" && account.OfflineName != "":
			dtos = append(dtos, accountDto{
				Type:          "offline",
				OfflineName:   account.OfflineName,
				OfflineSkinId: account.OfflineSkinId,
			})
		default:
			// 凭据对象缺失的账号（内存中的异常状态）不落盘：
			// 宁可丢掉这条，也不降级成错误类型污染配置
		}
	}

	data, err := json.Marshal(dtos)
	if err != nil || len(strings.TrimSpace(string(data))) == 0 {
		return
	}
	// Windows 上以 DPAPI 加密落盘；加密失败（极罕见）回落明文——宁可明文也不能丢账号
	stored := Protect(string(data))
	if stored == "" {
		stored = string(data)
	}
	config.SetValue(AccountsConfigKey, stored)
}

// raiseChanged 触发 OnChanged 回调；回调异常通过 recover 隔离，
// 防止一个页面出错中断其它页面。
func (s *AccountStoreService) raiseChanged() {
	handler := s.OnChanged
	if handler == nil {
		return
	}
	func() {
		defer func() {
			if err := recover(); err != nil {
				logs.Write("ERROR", fmt.Sprintf("AccountStore OnChanged 订阅者异常：%v", err))
			}
		}()
		handler()
	}()
}

// loadFromDisk 从 config.json 加载账号列表。
func (s *AccountStoreService) loadFromDisk() []*LaunchAccount {
	accounts := make([]*LaunchAccount, 0)
	stored := config.GetValue(AccountsConfigKey)
	jsonBody := stored
	encrypted := strings.HasPrefix(stored, EncryptedPrefix)
	if encrypted {
		jsonBody = Unprotect(stored)
		if jsonBody == "" {
			// 解密失败（换机/换 Windows 用户等）：凭据已不可恢复。
			// 直接返回空列表，让用户重新登录；不回落默认账号，避免掩盖问题。
			logs.Write("ERROR", "账号数据解密失败，已跳过加载，需要重新登录账号")
			return accounts
		}
	}

	// 只要保存过 accounts（包括空数组 []），就按内容加载，保持用户的选择（允许空列表）。
	if strings.TrimSpace(jsonBody) != "" {
		var dtos []accountDto
		if err := json.Unmarshal([]byte(jsonBody), &dtos); err == nil {
			accounts = s.buildAccounts(dtos)

			// 明文遗留数据一次性升级为加密存储（直接写配置，不经过 Save 的重新序列化）
			if !encrypted && IsAvailable() {
				if upgraded := Protect(jsonBody); upgraded != "" {
					config.SetValue(AccountsConfigKey, upgraded)
				}
			}
			return accounts
		}

		// 旧版启动器（前缀引入前）以「裸 DPAPI blob」存储账号；
		// 不是有效 JSON 时先尝试按旧格式解密，成功则按解密内容加载并升级为带前缀存储。
		if !encrypted {
			if legacyBody := platformUnprotect(stored); legacyBody != "" {
				var legacyDtos []accountDto
				if err := json.Unmarshal([]byte(legacyBody), &legacyDtos); err == nil {
					accounts = s.buildAccounts(legacyDtos)
					if IsAvailable() {
						if upgraded := Protect(legacyBody); upgraded != "" {
							config.SetValue(AccountsConfigKey, upgraded)
						}
					}
					return accounts
				}
			}
		}

		// 配置损坏时忽略，走下面的默认账号逻辑；留下日志便于排查。
		// 对照 C#（Console.WriteLine）：这只是一条诊断信息，按未配置处理是预期降级，
		// 不应以 ERROR 级别写入日志（空文件 / 首次启动等场景同样走不到这里）。
		logs.Write("WARN", "账号配置解析失败，按未配置处理")
	}

	// 从未配置过账号（或配置损坏）：兼容旧版 offlineUsername。
	legacy := config.GetValue("offlineUsername")
	if strings.TrimSpace(legacy) != "" {
		accounts = append(accounts, &LaunchAccount{
			Type:        "offline",
			DisplayName: legacy,
			OfflineName: legacy,
		})
		return accounts
	}

	// 全新安装：提供一个默认离线账号，保证首次打开即可启动。
	accounts = append(accounts, &LaunchAccount{
		Type:          "offline",
		DisplayName:   "Player_01",
		OfflineName:   "Player_01",
		OfflineSkinId: "steve",
	})
	return accounts
}

// buildAccounts 把 DTO 列表转换为账号对象；凭据字段缺失的条目按对应类型的要求丢弃。
// 抽取自 loadFromDisk 主体，供正常解析与旧版裸 DPAPI 格式解密两条路径复用。
func (s *AccountStoreService) buildAccounts(dtos []accountDto) []*LaunchAccount {
	accounts := make([]*LaunchAccount, 0, len(dtos))
	for index := range dtos {
		dto := &dtos[index]
		switch {
		case dto.Type == "microsoft" && strings.TrimSpace(dto.Username) != "":
			expiresAt := time.Time{}
			if dto.ExpiresAt != nil {
				expiresAt = *dto.ExpiresAt
			}
			accounts = append(accounts, &LaunchAccount{
				Type:        "microsoft",
				DisplayName: dto.Username,
				Microsoft: &MicrosoftAccount{
					Username:     dto.Username,
					Uuid:         fallbackString(dto.Uuid),
					AccessToken:  fallbackString(dto.AccessToken),
					RefreshToken: fallbackString(dto.RefreshToken),
					XboxUserId:   fallbackString(dto.XboxUserId),
					ClientId:     fallbackString(dto.ClientId),
					ExpiresAt:    expiresAt,
				},
			})
		case dto.Type == "authlib" &&
			strings.TrimSpace(dto.Uuid) != "" &&
			strings.TrimSpace(dto.ApiRoot) != "":
			displayName := dto.ProfileName
			if displayName == "" {
				displayName = dto.Username
			}
			accounts = append(accounts, &LaunchAccount{
				Type:        "authlib",
				DisplayName: fallbackString(displayName),
				Authlib: &AuthlibCredential{
					Username:    fallbackString(dto.Username),
					ProfileName: fallbackString(dto.ProfileName),
					ProfileUuid: dto.Uuid,
					AccessToken: fallbackString(dto.AccessToken),
					ApiRoot:     dto.ApiRoot,
					ServerName:  fallbackString(dto.ServerName),
				},
			})
		case dto.Type == "offline" && strings.TrimSpace(dto.OfflineName) != "":
			skinId := dto.OfflineSkinId
			if strings.TrimSpace(skinId) == "" {
				skinId = "steve"
			}
			accounts = append(accounts, &LaunchAccount{
				Type:          "offline",
				DisplayName:   dto.OfflineName,
				OfflineName:   dto.OfflineName,
				OfflineSkinId: skinId,
			})
		}
	}
	return accounts
}

func fallbackString(value string) string {
	if value == "" {
		return ""
	}
	return value
}

// ---------------------------------------------------------------------------
// AccountStore 静态门面：Shared 是全进程共享的默认实例。
// ---------------------------------------------------------------------------

// Shared 全进程共享的默认实例；首个访问者触发一次从 config.json 的加载。
var Shared = NewAccountStoreService(true)

// AccountStore 门面函数集（对应 C# 静态类 AccountStore）。

func AccountStoreCurrent() []*LaunchAccount                 { return Shared.Current() }
func AccountStoreSelected() *LaunchAccount                  { return Shared.Selected() }
func AccountStoreAdd(account *LaunchAccount)                { Shared.Add(account) }
func AccountStoreRemove(account *LaunchAccount)             { Shared.Remove(account) }
func AccountStoreMoveToTop(account *LaunchAccount)          { Shared.MoveToTop(account) }
func AccountStoreGetStableKey(a *LaunchAccount) string      { return Shared.GetStableKey(a) }
func AccountStoreSelectByStableKey(key string) bool         { return Shared.SelectByStableKey(key) }
func AccountStoreFindByStableKey(key string) *LaunchAccount { return Shared.FindByStableKey(key) }
func AccountStoreReload()                                   { Shared.Reload() }
func AccountStoreSave()                                     { Shared.Save() }
func AccountStoreHasOfflineName(name string) bool           { return Shared.HasOfflineName(name) }
func AccountStoreUpdateMicrosoftAccount(account *LaunchAccount, ms *MicrosoftAccount) {
	Shared.UpdateMicrosoftAccount(account, ms)
}
func AccountStoreUpdateAuthlibAccount(account *LaunchAccount, credential *AuthlibCredential) {
	Shared.UpdateAuthlibAccount(account, credential)
}
func AccountStoreUpdateOfflineSkin(account *LaunchAccount, skinId string) {
	Shared.UpdateOfflineSkin(account, skinId)
}
