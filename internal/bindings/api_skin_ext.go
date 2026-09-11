package bindings

// 皮肤/外观 API 扩展（对应 C# NyaLauncher.Avalonia 的 MinecraftProfileService、
// AuthlibProfileTextureService、OfflineSkinCatalog 的头像/皮肤解析语义）。
// 全部实现在 bindings 层，不改 internal/auth（离线皮肤仍存储为 OfflineSkinId 字符串：
// 内置目录 Id 或自定义皮肤 PNG 的本地路径，与 C# UpdateOfflineSkin 的字符串语义一致）。

import (
	"archive/zip"
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"image"
	"image/png"
	"io"
	"mime/multipart"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"nyalauncher/internal/auth"
	"nyalauncher/internal/config"
	"nyalauncher/internal/launch"
)

// ---- 常量与 HTTP 客户端 ----

const (
	minecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile"
	minecraftSkinEndpoint    = "https://api.minecraftservices.com/minecraft/profile/skins"
)

// skinHTTPClient 皮肤档案请求共用客户端（对应 C# 30 秒超时 / authlib 15 秒超时，统一取 20 秒）。
var skinHTTPClient = &http.Client{Timeout: 20 * time.Second}

// ---- 离线皮肤目录（对应 C# OfflineSkinCatalog.Choices） ----

// OfflineSkinChoice 离线账号可选的一件内置皮肤。
type OfflineSkinChoice struct {
	// Id 皮肤标识（如 "steve"）。
	Id string `json:"id"`
	// DisplayName 显示名称。
	DisplayName string `json:"displayName"`
	// Model 皮肤模型："classic"（宽手臂）或 "slim"（窄手臂）。
	Model string `json:"model"`
	// FallbackText 图片缺失时的占位文字。
	FallbackText string `json:"fallbackText"`
	// Source 解析好的贴图源：本地路径（已包装为 /localfile URL）或 data URI，
	// 前端可直接作为 <img src> 使用；解析失败为空串。
	Source string `json:"source"`
}

// offlineSkinChoices 内置皮肤清单（顺序即展示顺序，与 C# 一致）。
var offlineSkinChoices = []OfflineSkinChoice{
	{"steve", "Steve", "classic", "S", ""},
	{"alex", "Alex", "slim", "A", ""},
	{"noor", "Noor", "slim", "N", ""},
	{"sunny", "Sunny", "classic", "S", ""},
	{"ari", "Ari", "slim", "A", ""},
	{"zuri", "Zuri", "slim", "Z", ""},
	{"makena", "Makena", "classic", "M", ""},
	{"kai", "Kai", "slim", "K", ""},
	{"efe", "Efe", "slim", "E", ""},
}

// GetOfflineSkinCatalog 列出离线皮肤目录可用皮肤（对应 OfflineSkinPickerDialog 的数据源：
// OfflineSkinCatalog.Choices，9 款内置皮肤 + 已解析的贴图源）。
func (a *AccountAPI) GetOfflineSkinCatalog() []OfflineSkinChoice {
	result := make([]OfflineSkinChoice, len(offlineSkinChoices))
	copy(result, offlineSkinChoices)
	for i := range result {
		result[i].Source = resolveOfflineSkinSourceReady(result[i].Id)
	}
	return result
}

// GetOfflineSkinChoice 按 Id 查目录项（忽略大小写）；未知回退第一项（对应 OfflineSkinCatalog.Get）。
func GetOfflineSkinChoice(id string) OfflineSkinChoice {
	for _, choice := range offlineSkinChoices {
		if strings.EqualFold(choice.Id, id) {
			return choice
		}
	}
	return offlineSkinChoices[0]
}

// resolveOfflineSkinSourceReady 解析贴图源并包装为前端可直接使用的 URL。
func resolveOfflineSkinSourceReady(id string) string {
	source := ResolveOfflineSkinSource(id)
	if source == "" {
		return ""
	}
	if strings.HasPrefix(source, "data:") {
		return source
	}
	return LocalFileURL(source)
}

// LocalFilePathPrefix 前缀：标记 OfflineSkinId 中存放的是自定义皮肤文件路径。
// （C# 只支持内置目录 Id；Wails 版扩展支持本地 PNG 路径，复用同一持久化字段。）

// ---- 离线皮肤贴图解析（对应 OfflineSkinCatalog.ResolveTextureSourceAsync） ----

// resolveGate 串行化解压/生成，避免并发写同一缓存文件。
var resolveGate sync.Mutex

// ResolveOfflineSkinSource 解析离线皮肤贴图来源。优先级：
// 缓存 PNG → 客户端 jar 内置贴图 → 程序生成的占位皮肤（data URI）。
func ResolveOfflineSkinSource(id string) string {
	resolveGate.Lock()
	defer resolveGate.Unlock()
	return resolveOfflineSkinSourceLocked(GetOfflineSkinChoice(id))
}

func resolveOfflineSkinSourceLocked(choice OfflineSkinChoice) string {
	cacheDir := offlineSkinCacheDirectory()
	if cacheDir == "" {
		return generatedSkinDataURI(choice)
	}

	// 1) 命中已解压的缓存 PNG
	cachedPath := filepath.Join(cacheDir, choice.Id+".png")
	if fileExists(cachedPath) {
		return cachedPath
	}

	// 2) 从已安装的客户端 jar 中提取（按修改时间从新到旧尝试）
	if extracted := tryExtractFromClientJars(choice, cacheDir); extracted != "" {
		return extracted
	}

	// 3) 回退到程序生成的占位皮肤
	return generatedSkinDataURI(choice)
}

// offlineSkinCacheDirectory 存储目录/appearance-cache/default-skins（对应 C# 缓存子目录）。
func offlineSkinCacheDirectory() string {
	storage := strings.TrimSpace(config.StorageDirectory())
	if storage == "" {
		return ""
	}
	return filepath.Join(storage, "appearance-cache", "default-skins")
}

// clientJarRoots 可能装有客户端 jar 的根目录：先游戏目录，再默认目录（去重）。
func clientJarRoots() []string {
	var roots []string
	if game := strings.TrimSpace(config.GameDirectory()); game != "" {
		roots = append(roots, game)
	}
	if conventional := launch.GetDefaultMinecraftDirectory(); conventional != "" {
		dup := false
		for _, root := range roots {
			if strings.EqualFold(root, conventional) {
				dup = true
				break
			}
		}
		if !dup {
			roots = append(roots, conventional)
		}
	}
	return roots
}

// tryExtractFromClientJars 遍历 versions/**/*.jar（新到旧），
// 提取皮肤内置贴图到缓存目录；成功返回缓存文件完整路径。
func tryExtractFromClientJars(choice OfflineSkinChoice, cacheDir string) string {
	type jarFile struct{ path string; modTime time.Time }
	var jars []jarFile
	for _, root := range clientJarRoots() {
		versionsDir := filepath.Join(root, "versions")
		entries, err := os.Stat(versionsDir)
		if err != nil || !entries.IsDir() {
			continue
		}
		_ = filepath.Walk(versionsDir, func(path string, info os.FileInfo, err error) error {
			if err != nil || info.IsDir() || !strings.EqualFold(filepath.Ext(path), ".jar") {
				return nil
			}
			jars = append(jars, jarFile{path, info.ModTime()})
			return nil
		})
	}
	sort.Slice(jars, func(i, j int) bool { return jars[i].modTime.After(jars[j].modTime) })

	for _, jar := range jars {
		if extracted := tryExtractFromJar(choice, jar.path, cacheDir); extracted != "" {
			return extracted
		}
	}
	return ""
}

// tryExtractFromJar 从单个客户端 jar 提取皮肤贴图；找不到或读取失败返回空串。
func tryExtractFromJar(choice OfflineSkinChoice, jarPath, cacheDir string) string {
	archive, err := openSkinJarReader(jarPath)
	if err != nil {
		return ""
	}
	defer archive.Close()

	// 优先取皮肤对应模型的贴图，取不到再试另一个模型（jar 内固定路径：
	// assets/minecraft/textures/entity/player/<model>/<id>.png）
	preferred := "wide"
	if choice.Model == "slim" {
		preferred = "slim"
	}
	other := "wide"
	if preferred == "wide" {
		other = "slim"
	}
	entryPath := fmt.Sprintf("assets/minecraft/textures/entity/player/%s/%s.png", preferred, choice.Id)
	file, err := archive.Open(entryPath)
	if err != nil {
		entryPath = fmt.Sprintf("assets/minecraft/textures/entity/player/%s/%s.png", other, choice.Id)
		file, err = archive.Open(entryPath)
		if err != nil {
			return ""
		}
	}
	defer file.Close()

	targetPath := filepath.Join(cacheDir, choice.Id+".png")
	if err := os.MkdirAll(cacheDir, 0o755); err != nil {
		return ""
	}
	out, err := os.CreateTemp(cacheDir, choice.Id+"-*.tmp")
	if err != nil {
		return ""
	}
	tmpName := out.Name()
	if _, err := io.Copy(out, file); err != nil {
		out.Close()
		_ = os.Remove(tmpName)
		return ""
	}
	if err := out.Close(); err != nil {
		_ = os.Remove(tmpName)
		return ""
	}
	if err := os.Rename(tmpName, targetPath); err != nil {
		_ = os.Remove(tmpName)
		return ""
	}
	return targetPath
}

// openSkinJarReader 打开客户端 jar（zip 归档）。
func openSkinJarReader(jarPath string) (*zip.ReadCloser, error) {
	return zip.OpenReader(jarPath)
}

// ---- 占位皮肤生成（对应 C# CreateGeneratedTextureDataUri 的像素移植） ----

// generatedSkinDataURI 生成占位皮肤（64×64 PNG data URI）。
func generatedSkinDataURI(choice OfflineSkinChoice) string {
	const textureSize = 64
	img := image.NewNRGBA(image.Rect(0, 0, textureSize, textureSize))
	setPx := func(x, y int, color uint32) {
		index := img.PixOffset(x, y)
		img.Pix[index] = byte(color >> 24)
		img.Pix[index+1] = byte(color >> 16)
		img.Pix[index+2] = byte(color >> 8)
		img.Pix[index+3] = byte(color)
	}

	if strings.EqualFold(choice.Id, "steve") {
		for y, row := range steveHeadPixelRows {
			for x := 0; x < len(row); x += 6 {
				color, ok := parseHexColor(row[x : x+6])
				if !ok {
					continue
				}
				setPx(8+x/6, 8+y, color|0xFF000000)
			}
		}
	} else {
		drawGeneratedHead(setPx, skinPalette(choice.Id))
	}

	var buf bytes.Buffer
	if err := png.Encode(&buf, img); err != nil {
		return ""
	}
	return "data:image/png;base64," + base64.StdEncoding.EncodeToString(buf.Bytes())
}

// skinPalette 占位头像的四个颜色：皮肤底色 / 头发 / 虹膜 / 嘴部阴影。
type skinPalette4 struct{ skin, hair, eye, shadow uint32 }

func skinPalette(id string) skinPalette4 {
	switch strings.ToLower(id) {
	case "alex":
		return skinPalette4{0xD89B74, 0xB45B28, 0x4B792D, 0x9B4B2A}
	case "noor":
		return skinPalette4{0x9A6547, 0x241B1A, 0x4E3828, 0x6F4034}
	case "sunny":
		return skinPalette4{0xBD805B, 0x5A3425, 0x426D91, 0x8A4D3D}
	case "ari":
		return skinPalette4{0xD2A078, 0x32241F, 0x75543A, 0x965F4A}
	case "zuri":
		return skinPalette4{0x82513D, 0x201817, 0x6E4F31, 0x5C332D}
	case "makena":
		return skinPalette4{0x74432F, 0x181414, 0x47321F, 0x4C2927}
	case "kai":
		return skinPalette4{0xC18B64, 0x2A211E, 0x3F6E61, 0x814B3D}
	case "efe":
		return skinPalette4{0x69402F, 0x171313, 0x684D2E, 0x482725}
	default:
		return skinPalette4{0xB98463, 0x39251B, 0x4656A6, 0x875044}
	}
}

// drawGeneratedHead 按调色板绘制简化头部（脸 / 头发 / 眼睛 / 阴影）。
func drawGeneratedHead(setPx func(x, y int, color uint32), p skinPalette4) {
	// 脸部 8×8
	for y := 8; y < 16; y++ {
		for x := 8; x < 16; x++ {
			setPx(x, y, p.skin|0xFF000000)
		}
	}
	// 刘海与两侧鬓角
	for x := 8; x < 16; x++ {
		setPx(x, 8, p.hair|0xFF000000)
		setPx(x, 9, p.hair|0xFF000000)
	}
	for y := 10; y < 13; y++ {
		setPx(8, y, p.hair|0xFF000000)
		setPx(15, y, p.hair|0xFF000000)
	}
	// 眼睛：白底 + 虹膜
	setPx(10, 11, 0xFFF4F5F8)
	setPx(11, 11, p.eye|0xFF000000)
	setPx(12, 11, p.eye|0xFF000000)
	setPx(13, 11, 0xFFF4F5F8)
	// 嘴部阴影
	setPx(11, 13, p.shadow|0xFF000000)
	setPx(12, 13, p.shadow|0xFF000000)
	setPx(11, 14, p.shadow|0xFF000000)
	setPx(12, 14, p.shadow|0xFF000000)
	// 头部背面（与原实现保持一致）
	setPx(40, 8, p.hair|0xFF000000)
	setPx(47, 8, p.hair|0xFF000000)
	setPx(40, 9, p.hair|0xFF000000)
	setPx(47, 9, p.hair|0xFF000000)
}

// steveHeadPixelRows 经典 Steve 头部（脸层 8×8）像素，每 6 个字符一个 RGB 颜色，
// 逐格取自客户端贴图 wide/steve.png（与 C# OfflineSkinCatalog 一致）。
var steveHeadPixelRows = []string{
	"3324113324113F2A153F2A153F2A153F2A153324112B1E0D",
	"2418083324113324113F2A153F2A153324113F2A15332411",
	"2B1E0D9B6349B3795EB7836BB3795EAA72599B6349342512",
	"9B6349AA7259B3795EB3795EAA7259AA7259AA72599B6349",
	"AA7259FFFFFF523D89AA72599B6349523D89FFFFFFAA7259",
	"9B6349AA7259AA72596A40306A4030AA7259AA72599B6349",
	"90593F8F5E3E492510774235774235421D0A8F5E3E815339",
	"94603E815339421D0A492510421D0A4925108153398F5E3E",
}

func parseHexColor(value string) (uint32, bool) {
	var color uint32
	for _, c := range value {
		color <<= 4
		switch {
		case c >= '0' && c <= '9':
			color |= uint32(c - '0')
		case c >= 'A' && c <= 'F':
			color |= uint32(c-'A') + 10
		case c >= 'a' && c <= 'f':
			color |= uint32(c-'a') + 10
		default:
			return 0, false
		}
	}
	return color, true
}

// ---- 头像（8×8 头部裁剪） ----

// avatarCacheKey → data URI 缓存：同一贴图源不重复下载/解码。
type avatarCacheEntry struct {
	source   string
	modTime  time.Time
	avatarURI string
}

var (
	avatarCacheMu sync.Mutex
	avatarCache   = map[string]avatarCacheEntry{}
)

// GetAvatarUrl 账号头像（当前皮肤贴图的 8×8 头部裁剪，data URI），失败返回错误，
// 前端回退到首字母占位（对应 C# AccountManagePage 的 AvatarSource 解析语义）。
func (a *AccountAPI) GetAvatarUrl(accountID string) (string, error) {
	source, err := a.resolveSkinSource(accountID)
	if err != nil {
		return "", err
	}
	if source == "" {
		return "", errors.New("该账号没有可用皮肤贴图")
	}
	return avatarDataURI(source)
}

// GetSkinUrl 账号当前皮肤贴图源：正版/authlib 为远程 URL；离线为本地路径
// （包装为 /localfile URL）或 data URI。可直接作为 <img src> 使用。
func (a *AccountAPI) GetSkinUrl(accountID string) (string, error) {
	source, err := a.resolveSkinSource(accountID)
	if err != nil {
		return "", err
	}
	if source == "" {
		return "", errors.New("该账号没有可用皮肤贴图")
	}
	if strings.HasPrefix(source, "data:") {
		return source, nil
	}
	return LocalFileURL(source), nil
}

// resolveSkinSource 按账号类型解析皮肤贴图源（对应 C# SkinCapeEditorComponent.ResolveAvatarAsync）。
func (a *AccountAPI) resolveSkinSource(accountID string) (string, error) {
	account := auth.Shared.FindByStableKey(accountID)
	if account == nil {
		return "", fmt.Errorf("账号不存在：%s", accountID)
	}
	switch account.Type {
	case "offline":
		// OfflineSkinId：内置目录 Id，或自定义皮肤 PNG 的本地路径（扩展能力）。
		id := strings.TrimSpace(account.OfflineSkinId)
		if id == "" {
			id = "steve"
		}
		if isLocalPngPath(id) {
			if fileExists(id) {
				return id, nil
			}
			return "", errors.New("自定义皮肤文件不存在")
		}
		resolveGate.Lock()
		defer resolveGate.Unlock()
		return resolveOfflineSkinSourceLocked(GetOfflineSkinChoice(id)), nil
	case "authlib":
		if account.Authlib == nil {
			return "", errors.New("皮肤站账号缺少凭据")
		}
		return authlibSkinURL(callCtx(a.ctx), *account.Authlib)
	case "microsoft":
		if account.Microsoft == nil {
			return "", errors.New("正版账号缺少凭据")
		}
		profile, err := a.fetchMojangProfile(account, false)
		if err != nil {
			return "", err
		}
		if profile.ActiveSkinURL == "" {
			return "", nil
		}
		return profile.ActiveSkinURL, nil
	default:
		return "", fmt.Errorf("该账号类型不支持头像解析：%s", account.Type)
	}
}

// avatarDataURI 从皮肤贴图源（远程 URL 或本地路径）提取 8×8 头部，返回 data URI。
func avatarDataURI(source string) (string, error) {
	var modTime time.Time
	if !strings.HasPrefix(source, "data:") {
		if info, err := os.Stat(source); err == nil {
			modTime = info.ModTime()
		}
	}

	avatarCacheMu.Lock()
	cached, ok := avatarCache[source]
	if ok && cached.modTime.Equal(modTime) {
		avatarCacheMu.Unlock()
		return cached.avatarURI, nil
	}
	avatarCacheMu.Unlock()

	var skin image.Image
	if strings.HasPrefix(source, "data:") {
		comma := strings.Index(source, ",")
		if comma < 0 {
			return "", errors.New("无效的 data URI")
		}
		raw, err := base64.StdEncoding.DecodeString(source[comma+1:])
		if err != nil {
			return "", err
		}
		skin, err = png.Decode(bytes.NewReader(raw))
		if err != nil {
			return "", err
		}
	} else if strings.HasPrefix(source, "http://") || strings.HasPrefix(source, "https://") {
		resp, err := skinHTTPClient.Get(source)
		if err != nil {
			return "", err
		}
		defer resp.Body.Close()
		if resp.StatusCode != http.StatusOK {
			return "", fmt.Errorf("皮肤贴图下载返回 %d", resp.StatusCode)
		}
		skin, err = png.Decode(resp.Body)
		if err != nil {
			return "", err
		}
	} else {
		file, err := os.Open(source)
		if err != nil {
			return "", err
		}
		defer file.Close()
		skin, err = png.Decode(file)
		if err != nil {
			return "", err
		}
	}

	// 8×8 头部（贴图 (8,8)-(16,16)），放大后即经典 MC 头像
	head := image.NewNRGBA(image.Rect(0, 0, 8, 8))
	bounds := skin.Bounds()
	for y := 0; y < 8; y++ {
		for x := 0; x < 8; x++ {
			px := bounds.Min.X + 8 + x
			py := bounds.Min.Y + 8 + y
			if px >= bounds.Max.X || py >= bounds.Max.Y {
				continue
			}
			head.Set(x, y, skin.At(px, py))
		}
	}
	var buf bytes.Buffer
	if err := png.Encode(&buf, head); err != nil {
		return "", err
	}
	uri := "data:image/png;base64," + base64.StdEncoding.EncodeToString(buf.Bytes())

	avatarCacheMu.Lock()
	avatarCache[source] = avatarCacheEntry{source, modTime, uri}
	avatarCacheMu.Unlock()
	return uri, nil
}

// ---- 正版（Mojang）档案与皮肤上传（对应 MinecraftProfileService） ----

// mojangProfile 正版档案解析结果。
type mojangProfile struct {
	ActiveSkinURL string
	Capes         []mojangTexture
}

type mojangTexture struct {
	Id      string `json:"id"`
	State   string `json:"state"`
	URL     string `json:"url"`
	Variant string `json:"variant"`
	Alias   string `json:"alias"`
}

type mojangProfileDTO struct {
	Id    string          `json:"id"`
	Name  string          `json:"name"`
	Skins []mojangTexture `json:"skins"`
	Capes []mojangTexture `json:"capes"`
}

// normalizeTextureURL 贴图地址归一化：https 直接使用；http 仅当主机为
// textures.minecraft.net 时升级为 https（对应 C# NormalizeTextureUrl）。
func normalizeTextureURL(value string) string {
	parsed, err := url.Parse(value)
	if err != nil || parsed.Host == "" {
		return ""
	}
	switch parsed.Scheme {
	case "https":
		return parsed.String()
	case "http":
		if strings.EqualFold(parsed.Hostname(), "textures.minecraft.net") {
			parsed.Scheme = "https"
			parsed.Host = parsed.Hostname()
			return parsed.String()
		}
	}
	return ""
}

// fetchMojangProfile 拉取正版档案；令牌过期自动刷新，401/403 强制刷新重试一次
// （对应 C# SendWithFreshTokenAsync）。
func (a *AccountAPI) fetchMojangProfile(account *auth.LaunchAccount, forceRefresh bool) (*mojangProfile, error) {
	token, err := a.freshMicrosoftToken(account, forceRefresh)
	if err != nil {
		return nil, err
	}
	profile, status, err := requestMojangProfile(token)
	if err != nil {
		return nil, err
	}
	if (status == http.StatusUnauthorized || status == http.StatusForbidden) && !forceRefresh {
		return a.fetchMojangProfile(account, true)
	}
	if status != http.StatusOK {
		return nil, fmt.Errorf("Minecraft 档案服务返回 %d", status)
	}
	return profile, nil
}

func requestMojangProfile(token string) (*mojangProfile, int, error) {
	req, err := http.NewRequest(http.MethodGet, minecraftProfileEndpoint, nil)
	if err != nil {
		return nil, 0, err
	}
	req.Header.Set("Authorization", "Bearer "+token)
	resp, err := skinHTTPClient.Do(req)
	if err != nil {
		return nil, 0, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, resp.StatusCode, nil
	}
	var dto mojangProfileDTO
	if err := json.NewDecoder(resp.Body).Decode(&dto); err != nil {
		return nil, resp.StatusCode, err
	}
	active := ""
	for _, skin := range dto.Skins {
		if strings.EqualFold(skin.State, "ACTIVE") {
			active = normalizeTextureURL(skin.URL)
			break
		}
	}
	if active == "" && len(dto.Skins) > 0 {
		active = normalizeTextureURL(dto.Skins[0].URL)
	}
	return &mojangProfile{ActiveSkinURL: active, Capes: dto.Capes}, http.StatusOK, nil
}

// freshMicrosoftToken 取可用的正版访问令牌：按账号刷新锁串行化，
// 过期（或强制）时经认证器刷新并写回存储（对应 C# EnsureFreshAccountAsync）。
func (a *AccountAPI) freshMicrosoftToken(account *auth.LaunchAccount, forceRefresh bool) (string, error) {
	if !strings.EqualFold(account.Type, "microsoft") || account.Microsoft == nil {
		return "", errors.New("当前账号不是可编辑皮肤的正版账号。")
	}
	var token string
	err := auth.Shared.WithRefreshLock(account, func() error {
		ms := account.Microsoft
		if ms == nil {
			return errors.New("当前账号不是可编辑皮肤的正版账号。")
		}
		if !forceRefresh && !ms.IsExpired() {
			token = ms.AccessToken
			return nil
		}
		refreshed, err := a.microsoft.Refresh(callCtx(a.ctx), *ms)
		if err != nil {
			return err
		}
		auth.Shared.UpdateMicrosoftAccount(account, &refreshed)
		token = refreshed.AccessToken
		return nil
	})
	if err != nil {
		return "", err
	}
	return token, nil
}

// UploadSkin 为正版账号上传皮肤（对应 MinecraftProfileService.UploadSkinAsync + 
// MinecraftAppearanceEditor.ValidateSkinFile）：
// 校验 PNG（≤4 MiB，64×64 或 64×32）→ multipart 上传（variant: classic|slim）。
func (a *AccountAPI) UploadSkin(accountID, path, variant string) error {
	account := auth.Shared.FindByStableKey(accountID)
	if account == nil {
		return fmt.Errorf("账号不存在：%s", accountID)
	}
	if err := validateSkinFile(path); err != nil {
		return err
	}
	model := variant
	switch strings.ToLower(variant) {
	case "classic", "slim":
		model = strings.ToLower(variant)
	case "wide":
		model = "classic"
	default:
		return errors.New("皮肤模型必须是 classic（经典）或 slim（纤细）。")
	}

	return a.uploadSkinWithRetry(account, path, model, false)
}

func (a *AccountAPI) uploadSkinWithRetry(account *auth.LaunchAccount, path, model string, forceRefresh bool) error {
	token, err := a.freshMicrosoftToken(account, forceRefresh)
	if err != nil {
		return err
	}
	status, err := postMojangSkin(token, path, model)
	if err != nil {
		return err
	}
	if (status == http.StatusUnauthorized || status == http.StatusForbidden) && !forceRefresh {
		return a.uploadSkinWithRetry(account, path, model, true)
	}
	if status < 200 || status >= 300 {
		return fmt.Errorf("皮肤上传返回 %d", status)
	}
	return nil
}

// postMojangSkin multipart 上传：variant 字段 + file 字段（image/png）。
// 注意：C# 与 Mojang 实际 API 均为 POST（非 PUT）。
func postMojangSkin(token, path, model string) (int, error) {
	file, err := os.Open(path)
	if err != nil {
		return 0, err
	}
	defer file.Close()

	body := &bytes.Buffer{}
	form := multipart.NewWriter(body)
	if err := form.WriteField("variant", model); err != nil {
		return 0, err
	}
	part, err := form.CreateFormFile("file", filepath.Base(path))
	if err != nil {
		return 0, err
	}
	if _, err := io.Copy(part, file); err != nil {
		return 0, err
	}
	if err := form.Close(); err != nil {
		return 0, err
	}

	req, err := http.NewRequest(http.MethodPost, minecraftSkinEndpoint, body)
	if err != nil {
		return 0, err
	}
	req.Header.Set("Authorization", "Bearer "+token)
	req.Header.Set("Content-Type", form.FormDataContentType())
	resp, err := skinHTTPClient.Do(req)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()
	return resp.StatusCode, nil
}

// validateSkinFile 校验皮肤文件（对应 MinecraftAppearanceEditor.ValidateSkinFile）：
// 本地 PNG，大小 1 B–4 MiB，尺寸 64×64 或 64×32。
func validateSkinFile(path string) error {
	info, err := os.Stat(path)
	if err != nil || !strings.EqualFold(filepath.Ext(path), ".png") {
		return errors.New("皮肤文件必须是本地 PNG 图片。")
	}
	if info.Size() <= 0 || info.Size() > 4*1024*1024 {
		return errors.New("皮肤文件为空或超过 4 MiB 限制。")
	}
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()
	img, err := png.Decode(file)
	if err != nil {
		return errors.New("皮肤文件不是有效的 PNG 图片。")
	}
	bounds := img.Bounds()
	width := bounds.Dx()
	height := bounds.Dy()
	if width != 64 || (height != 32 && height != 64) {
		return errors.New("Minecraft Java 皮肤尺寸必须为 64×64，或兼容旧版的 64×32。")
	}
	return nil
}

// ---- 皮肤站（authlib / Yggdrasil）皮肤解析（对应 AuthlibProfileTextureService） ----

// authlibSkinURL 经 sessionserver 读取角色贴图属性，从 base64 textures 值中
// 提取皮肤 URL；无皮肤或解析失败返回空串与 nil（由调用方回退占位头像）。
func authlibSkinURL(ctx context.Context, credential auth.AuthlibCredential) (string, error) {
	requestURL := strings.TrimRight(credential.ApiRoot, "/") +
		"/sessionserver/session/minecraft/profile/" + credential.ProfileUuid
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, requestURL, nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("User-Agent", "NyaLauncher/1.0")
	resp, err := skinHTTPClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", nil
	}

	var profile struct {
		Properties []struct {
			Name  string `json:"name"`
			Value string `json:"value"`
		} `json:"properties"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&profile); err != nil {
		return "", nil
	}
	textureValue := ""
	for _, property := range profile.Properties {
		if property.Name == "textures" {
			textureValue = property.Value
			break
		}
	}
	if textureValue == "" {
		return "", nil
	}
	decoded, err := base64.StdEncoding.DecodeString(textureValue)
	if err != nil {
		return "", nil
	}
	var payload struct {
		Textures struct {
			Skin struct {
				URL string `json:"url"`
			} `json:"SKIN"`
			Cape struct {
				URL string `json:"url"`
			} `json:"CAPE"`
		} `json:"textures"`
	}
	if err := json.Unmarshal(decoded, &payload); err != nil {
		return "", nil
	}
	// 仅接受绝对 http(s) 地址（与 C# 语义一致）
	if payload.Textures.Skin.URL != "" &&
		(strings.HasPrefix(payload.Textures.Skin.URL, "http://") ||
			strings.HasPrefix(payload.Textures.Skin.URL, "https://")) {
		return payload.Textures.Skin.URL, nil
	}
	return "", nil
}

// ---- 离线皮肤设置（对应 AccountStore.UpdateOfflineSkin 的扩展） ----

// SetOfflineSkin 设置离线账号皮肤。
// path 为内置目录 Id（如 "steve"）时与 C# AccountStore.UpdateOfflineSkin 语义一致；
// 为本地 PNG 路径时为 Wails 版扩展：校验后复制到存储目录 appearance-cache/custom-skins，
// 并把复制后的路径写入 OfflineSkinId 持久化。
func (a *AccountAPI) SetOfflineSkin(accountID, path string) error {
	account := auth.Shared.FindByStableKey(accountID)
	if account == nil {
		return fmt.Errorf("账号不存在：%s", accountID)
	}
	if !strings.EqualFold(account.Type, "offline") {
		return errors.New("只能为离线账号设置离线皮肤。")
	}

	trimmed := strings.TrimSpace(path)
	if trimmed == "" {
		return errors.New("皮肤标识或路径不能为空。")
	}
	if !isLocalPngPath(trimmed) {
		// 内置目录 Id：直接更新
		auth.Shared.UpdateOfflineSkin(account, GetOfflineSkinChoice(trimmed).Id)
		return nil
	}

	if err := validateSkinFile(trimmed); err != nil {
		return err
	}
	storage := strings.TrimSpace(config.StorageDirectory())
	if storage == "" {
		return errors.New("存储目录不可用，无法保存自定义皮肤。")
	}
	targetDir := filepath.Join(storage, "appearance-cache", "custom-skins")
	if err := os.MkdirAll(targetDir, 0o755); err != nil {
		return err
	}
	// 以账号稳定键命名，避免多账号互相覆盖
	key := strings.NewReplacer("\\", "_", "/", "_", ":", "_", " ", "_").Replace(accountID)
	targetPath := filepath.Join(targetDir, key+".png")
	if err := copyFile(trimmed, targetPath); err != nil {
		return err
	}
	auth.Shared.UpdateOfflineSkin(account, targetPath)
	return nil
}

// isLocalPngPath 值是否为本地 PNG 路径（而非内置目录 Id）。
func isLocalPngPath(value string) bool {
	if strings.ContainsAny(value, "\\/") || filepath.IsAbs(value) {
		return strings.EqualFold(filepath.Ext(value), ".png")
	}
	return false
}

// copyFile 复制文件（不存在目标目录时由调用方保证）。
func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.CreateTemp(filepath.Dir(dst), filepath.Base(dst)+".tmp")
	if err != nil {
		return err
	}
	tmpName := out.Name()
	if _, err := io.Copy(out, in); err != nil {
		out.Close()
		_ = os.Remove(tmpName)
		return err
	}
	if err := out.Close(); err != nil {
		_ = os.Remove(tmpName)
		return err
	}
	if err := os.Rename(tmpName, dst); err != nil {
		_ = os.Remove(tmpName)
		return err
	}
	return nil
}

// ---- 小工具 ----

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

// LocalFileURL 把本地路径包装为 /localfile 流 URL（见 localfile_handler.go）。
func LocalFileURL(path string) string {
	return "/localfile?path=" + url.QueryEscape(path)
}
