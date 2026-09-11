// 从 JAR、ZIP 和 level.dat 中读取 Mod、资源包、光影和存档的元数据。
// 移植自 NyaLauncher.Core/Content/GameContentMetadataService.cs。
// 所有读取均为流式：zip 按需解压单个条目，元数据读取限制字节数，
// 不会把整个压缩包载入内存。解析结果按（文件路径, 大小, 修改时间）做轻量缓存。
package content

import (
	"archive/zip"
	"bytes"
	crand "crypto/rand"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strings"
	"sync"
	"time"

	"nyalauncher/internal/config"
)

// GameContentEntry 内容页里一个条目（Mod/资源包/光影/存档）的展示信息。
type GameContentEntry struct {
	Name          string
	MetadataLine  string
	Description   string
	IconPath      string // 空串表示无图标文件
	FallbackGlyph string
	SourcePath    string
	IsDisabled    bool
}

// GameInstanceVisual 实例图标解析结果：图标路径（或内置图标符号）+ 兜底字形。
type GameInstanceVisual struct {
	IconPath      string
	FallbackGlyph string
}

// InstanceContext 内容扫描所需的实例上下文（C# 直接消费 GameInstanceSnapshot；
// 为避免 instance ↔ content 循环依赖，这里收敛为两个必需字段，见 PORTING_NOTES.md）。
type InstanceContext struct {
	// SourcePath 实例的目录来源（根目录或外部实例目录）。
	SourcePath string
	// MinecraftDirectory Minecraft 根目录。
	MinecraftDirectory string
}

// ExternalInstanceLayout 外部实例信息的最小投影（对应 ExternalGameInstanceLayout 子集）。
type ExternalInstanceLayout struct {
	InstanceId        string
	InstanceDirectory string
	LauncherRoot      string
}

// ExternalInstanceResolver 外部实例识别钩子：由 internal/instance 在包初始化时注入
// instance.TryResolveExternalInstance 的适配器；未注入时外部实例识别跳过。
var ExternalInstanceResolver func(sourcePath string) (ExternalInstanceLayout, bool)

const (
	maximumMetadataBytes = 2 * 1024 * 1024
	// maximumZipIconBytes 压缩包内图标条目大小上限（自定义图标上限见 customiconstore.go）。
	maximumZipIconBytes = 8 * 1024 * 1024
	// maximumIconCacheFiles 内容图标缓存文件数上限；触发时清理到一半。
	maximumIconCacheFiles = 600
	// maximumEntryCacheCount 缓存条数上限：超过后整体清空（轻量策略，避免长期驻留冷数据）。
	maximumEntryCacheCount = 4096
)

// ---------------------------------------------------------------------------
// 公开入口
// ---------------------------------------------------------------------------

// ReadMods 读取目录下全部 mod（*.jar 与 *.jar.disabled），按名称排序。
func ReadMods(ctx context.Context, directory string) []GameContentEntry {
	paths := append(
		enumerateFiles(directory, ".jar"),
		enumerateFiles(directory, ".jar.disabled")...)
	entries := make([]GameContentEntry, 0, len(paths))
	for _, path := range paths {
		entries = append(entries, readCached(path, func() GameContentEntry {
			return readMod(ctx, path)
		}))
	}
	sortEntries(entries)
	return entries
}

// ReadResourcePacks 读取资源包（zip 或目录），按名称排序。
func ReadResourcePacks(ctx context.Context, directory string) []GameContentEntry {
	entries := make([]GameContentEntry, 0)
	for _, path := range enumerateArchivesAndDirectories(directory) {
		entries = append(entries, readCached(path, func() GameContentEntry {
			return readPack(ctx, path, "▣")
		}))
	}
	sortEntries(entries)
	return entries
}

// ReadShaders 读取光影包（zip 或目录），按名称排序。
func ReadShaders(ctx context.Context, directory string) []GameContentEntry {
	entries := make([]GameContentEntry, 0)
	for _, path := range enumerateArchivesAndDirectories(directory) {
		entries = append(entries, readCached(path, func() GameContentEntry {
			return readPack(ctx, path, "✦")
		}))
	}
	sortEntries(entries)
	return entries
}

// ReadSaves 读取存档目录列表（每个子目录一个条目），按名称排序。
func ReadSaves(ctx context.Context, directory string) []GameContentEntry {
	entries, err := os.ReadDir(directory)
	if err != nil {
		return []GameContentEntry{}
	}
	result := make([]GameContentEntry, 0)
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		if err := ctx.Err(); err != nil {
			break
		}
		result = append(result, readSave(filepath.Join(directory, entry.Name())))
	}
	sortEntries(result)
	return result
}

// ResolveInstanceVisual 解析实例的展示图标。优先级：显式内置图标偏好 →
// profile 自定义图标偏好 → 实例目录自带图标 → 按加载器默认。
// 前置判断 gameicon: 直接返回，无需触碰磁盘。
func ResolveInstanceVisual(snapshot InstanceContext, versionID, loaderName string) GameInstanceVisual {
	instanceDirectory := filepath.Join(snapshot.MinecraftDirectory, "versions", versionID)
	launcherRoot := ""
	if ExternalInstanceResolver != nil {
		if external, ok := ExternalInstanceResolver(snapshot.SourcePath); ok &&
			strings.EqualFold(external.InstanceId, versionID) {
			instanceDirectory = external.InstanceDirectory
			launcherRoot = external.LauncherRoot
		}
	}

	profileOverride := config.GetInstanceIconOverride(snapshot.MinecraftDirectory, versionID)
	if profileOverride != nil && strings.HasPrefix(*profileOverride, "gameicon:") {
		return GameInstanceVisual{*profileOverride, loaderGlyph(loaderName)}
	}

	// 自定义图标偏好（"custom"）优先读取用户手动设置的图标文件，其次实例目录自带图标
	useCustom := *configGetInstanceIconOverrideText(snapshot.MinecraftDirectory, versionID) == "custom"
	icon := ""
	if useCustom {
		icon = GetCustomIconPath(snapshot.MinecraftDirectory, versionID)
	}
	if icon == "" {
		icon = findInstanceIcon(instanceDirectory, launcherRoot)
	}
	if loaderName == "" {
		loaderName = detectLoaderFromMetadata(snapshot.MinecraftDirectory, instanceDirectory, versionID)
	}
	if icon == "" {
		icon = defaultInstanceIconPath(loaderName)
	}
	return GameInstanceVisual{icon, loaderGlyph(loaderName)}
}

// configGetInstanceIconOverrideText 返回图标偏好文本（nil 时返回指向空串的指针）。
func configGetInstanceIconOverrideText(minecraftDirectory, versionID string) *string {
	if value := config.GetInstanceIconOverride(minecraftDirectory, versionID); value != nil {
		return value
	}
	empty := ""
	return &empty
}

// ---------------------------------------------------------------------------
// 结果缓存
// ---------------------------------------------------------------------------

var (
	// entryCache 条目解析结果缓存：键含文件大小与修改时间，文件更新后自动失效；
	// 目录包的键由调用方构造，不含时间戳（目录 mtime 不反映内容变化，宁可重新解析）。
	entryCacheMu sync.Mutex
	entryCache   = map[string]GameContentEntry{}
)

// readCached 按（路径, 大小, 修改时间）缓存条目解析结果。
func readCached(path string, read func() GameContentEntry) GameContentEntry {
	key := buildCacheKey(path)
	if key == "" {
		return read()
	}
	entryCacheMu.Lock()
	if cached, ok := entryCache[key]; ok {
		entryCacheMu.Unlock()
		return cached
	}
	entryCacheMu.Unlock()

	entry := read()
	entryCacheMu.Lock()
	if len(entryCache) >= maximumEntryCacheCount {
		entryCache = map[string]GameContentEntry{}
	}
	entryCache[key] = entry
	entryCacheMu.Unlock()
	return entry
}

// buildCacheKey 构造（路径, 大小, 修改时间）缓存键；文件不可读时返回空串（不缓存）。
func buildCacheKey(path string) string {
	info, err := os.Stat(path)
	if err != nil {
		return ""
	}
	return fmt.Sprintf("%s|%d|%d", path, info.Size(), info.ModTime().UnixNano())
}

// ---------------------------------------------------------------------------
// Mod 解析（fabric / quilt / toml / 旧版 mcmod.info）
// ---------------------------------------------------------------------------

// readMod 解析单个 mod 压缩包；解析失败降级为未知条目（PCL/HMCL 对此类损坏同样宽容）。
func readMod(ctx context.Context, path string) GameContentEntry {
	if err := ctx.Err(); err != nil {
		return unknownEntry(filepath.Base(path), "◆", path)
	}
	fallbackName := strings.TrimSuffix(filepath.Base(path), filepath.Ext(path))

	archive, err := zip.OpenReader(path)
	if err != nil {
		return unknownEntry(fallbackName, "◆", path)
	}
	defer archive.Close()

	// 按加载器元数据文件的优先级逐个探测，找到即解析
	if entry := findEntry(&archive.Reader, "fabric.mod.json"); entry != nil {
		return readFabricMod(path, &archive.Reader, entry, fallbackName)
	}
	if entry := findEntry(&archive.Reader, "quilt.mod.json"); entry != nil {
		return readQuiltMod(path, &archive.Reader, entry, fallbackName)
	}
	toml := findEntry(&archive.Reader, "META-INF/neoforge.mods.toml")
	if toml == nil {
		toml = findEntry(&archive.Reader, "META-INF/mods.toml")
	}
	if toml != nil {
		return readTomlMod(path, &archive.Reader, toml, fallbackName)
	}
	if entry := findEntry(&archive.Reader, "mcmod.info"); entry != nil {
		return readLegacyMod(path, &archive.Reader, entry, fallbackName)
	}
	return unknownEntry(fallbackName, "◆", path)
}

// readFabricMod 解析 fabric.mod.json。
func readFabricMod(sourcePath string, archive *zip.Reader, metadata *zip.File, fallbackName string) GameContentEntry {
	root, err := readEntryObject(metadata)
	if err != nil {
		return unknownEntry(fallbackName, "◆", sourcePath)
	}
	name := firstNonEmpty(readJSONString(root, "name"), readJSONString(root, "id"), fallbackName)
	version := firstNonEmpty(readJSONString(root, "version"), "未提供")
	authors := readPeople(root, "authors")
	description := readDescription(root, "description")
	iconEntry := readIconProperty(root, "icon")
	return createEntry(sourcePath, archive, name, authors, version, description, iconEntry, "◆")
}

// readQuiltMod 解析 quilt.mod.json（结构比 fabric 多两层包装：quilt_loader → metadata）。
func readQuiltMod(sourcePath string, archive *zip.Reader, metadata *zip.File, fallbackName string) GameContentEntry {
	root, err := readEntryObject(metadata)
	if err != nil {
		return unknownEntry(fallbackName, "◆", sourcePath)
	}
	loader, ok := root["quilt_loader"].(map[string]any)
	if !ok {
		loader = root
	}
	metadataRoot, ok := loader["metadata"].(map[string]any)
	if !ok {
		metadataRoot = loader
	}
	name := firstNonEmpty(readJSONString(metadataRoot, "name"), readJSONString(loader, "id"), fallbackName)
	version := firstNonEmpty(readJSONString(loader, "version"), "未提供")
	authors := readPeople(metadataRoot, "contributors")
	description := readDescription(metadataRoot, "description")
	iconEntry := readIconProperty(metadataRoot, "icon")
	return createEntry(sourcePath, archive, name, authors, version, description, iconEntry, "◆")
}

// readTomlMod 解析 META-INF/(neoforge.)mods.toml。
func readTomlMod(sourcePath string, archive *zip.Reader, metadata *zip.File, fallbackName string) GameContentEntry {
	text, err := readEntryText(metadata)
	if err != nil {
		return unknownEntry(fallbackName, "◆", sourcePath)
	}
	modID := readTomlValue(text, "modId")
	if isTemplateValue(modID) {
		modID = ""
	}
	name := readTomlValue(text, "displayName")
	if isTemplateValue(name) {
		name = modID
	}
	if strings.TrimSpace(name) == "" {
		name = fallbackName
	}
	// 版本是模板占位符（未构建时原样保留 ${version}）时回退 MANIFEST 里的实现版本
	version := readTomlValue(text, "version")
	if isTemplateValue(version) {
		version = firstNonEmpty(
			readManifestValue(archive, "Implementation-Version"),
			readManifestValue(archive, "Specification-Version"))
	}
	if strings.TrimSpace(version) == "" {
		version = "未提供"
	}
	authors := readTomlValue(text, "authors")
	if isTemplateValue(authors) {
		authors = ""
	}
	if strings.TrimSpace(authors) == "" {
		authors = "未提供"
	}
	description := readTomlValue(text, "description")
	if isTemplateValue(description) {
		description = ""
	}
	logo := readTomlValue(text, "logoFile")
	return createEntry(sourcePath, archive, name, authors, version, description, logo, "◆")
}

// readLegacyMod 解析旧版 mcmod.info。
func readLegacyMod(sourcePath string, archive *zip.Reader, metadata *zip.File, fallbackName string) GameContentEntry {
	document, err := readEntryJSON(metadata)
	if err != nil {
		return unknownEntry(fallbackName, "◆", sourcePath)
	}
	// mcmod.info 顶层是数组（单 Mod 也是数组包一个对象）
	var obj map[string]any
	switch root := document.(type) {
	case []any:
		if len(root) > 0 {
			obj, _ = root[0].(map[string]any)
		}
	case map[string]any:
		obj = root
	}
	if obj == nil {
		obj = map[string]any{}
	}
	name := firstNonEmpty(readJSONString(obj, "name"), readJSONString(obj, "modid"), fallbackName)
	version := firstNonEmpty(readJSONString(obj, "version"), "未提供")
	authors := readPeople(obj, "authorList")
	description := readDescription(obj, "description")
	logo := readJSONString(obj, "logoFile")
	return createEntry(sourcePath, archive, name, authors, version, description, logo, "◆")
}

// ---------------------------------------------------------------------------
// 资源包 / 光影解析（zip 或目录，均以 pack.mcmeta 为准）
// ---------------------------------------------------------------------------

// readPack 解析资源包/光影条目；解析失败降级为未知条目。
func readPack(ctx context.Context, path, fallbackGlyph string) GameContentEntry {
	if err := ctx.Err(); err != nil {
		return unknownEntry(filepath.Base(path), fallbackGlyph, path)
	}
	fallbackName := strings.TrimSuffix(filepath.Base(path), filepath.Ext(path))

	info, err := os.Stat(path)
	if err != nil {
		return unknownEntry(fallbackName, fallbackGlyph, path)
	}
	if info.IsDir() {
		return readDirectoryPack(path, fallbackName, fallbackGlyph)
	}
	return readZipPack(path, fallbackName, fallbackGlyph)
}

// readDirectoryPack 目录形态的资源包/光影：读 pack.mcmeta，图标取包内第一个存在的图片文件。
func readDirectoryPack(path, fallbackName, fallbackGlyph string) GameContentEntry {
	metadataPath := filepath.Join(path, "pack.mcmeta")
	directoryIcon := findFirstExisting(path, "pack.png", "icon.png", "preview.png")
	if !fileExists(metadataPath) {
		return GameContentEntry{
			Name:          fallbackName,
			MetadataLine:  "作者 未提供 · 版本 未提供",
			Description:   path,
			IconPath:      directoryIcon,
			FallbackGlyph: fallbackGlyph,
			SourcePath:    path,
		}
	}
	data, err := os.ReadFile(metadataPath)
	if err != nil {
		return unknownEntry(fallbackName, fallbackGlyph, path)
	}
	var document any
	if err := json.Unmarshal(data, &document); err != nil {
		return unknownEntry(fallbackName, fallbackGlyph, path)
	}
	root, _ := document.(map[string]any)
	if root == nil {
		root = map[string]any{}
	}
	return readPackDocument(root, fallbackName, path, directoryIcon, fallbackGlyph, path)
}

// readZipPack zip 形态的资源包/光影：从压缩包内提取 pack.mcmeta 与图标。
func readZipPack(path, fallbackName, fallbackGlyph string) GameContentEntry {
	archive, err := zip.OpenReader(path)
	if err != nil {
		return unknownEntry(fallbackName, fallbackGlyph, path)
	}
	defer archive.Close()

	metadata := findEntry(&archive.Reader, "pack.mcmeta")
	var iconEntry *zip.File
	if iconEntry = findEntry(&archive.Reader, "pack.png"); iconEntry == nil {
		if iconEntry = findEntry(&archive.Reader, "icon.png"); iconEntry == nil {
			iconEntry = findEntry(&archive.Reader, "preview.png")
		}
	}
	icon := ""
	if iconEntry != nil {
		icon = extractIcon(path, iconEntry)
	}
	if metadata == nil {
		return GameContentEntry{
			Name:          fallbackName,
			MetadataLine:  "作者 未提供 · 版本 未提供",
			Description:   filepath.Base(path),
			IconPath:      icon,
			FallbackGlyph: fallbackGlyph,
			SourcePath:    path,
		}
	}
	root, err := readEntryJSON(metadata)
	if err != nil {
		return unknownEntry(fallbackName, fallbackGlyph, path)
	}
	obj, _ := root.(map[string]any)
	if obj == nil {
		obj = map[string]any{}
	}
	return readPackDocument(obj, fallbackName, filepath.Base(path), icon, fallbackGlyph, path)
}

// readPackDocument 从 pack.mcmeta JSON 中提取展示信息。
func readPackDocument(root map[string]any, fallbackName, sourceLabel, icon, fallbackGlyph, sourcePath string) GameContentEntry {
	// pack.mcmeta 顶层是 { "pack": {...} }，但手写的可能直接展开
	pack, ok := root["pack"].(map[string]any)
	if !ok {
		pack = root
	}
	name := firstNonEmpty(readJSONString(root, "name"), readJSONString(pack, "name"), fallbackName)
	author := firstNonEmpty(readJSONString(root, "author"), readJSONString(pack, "author"), "未提供")
	version := firstNonEmpty(readJSONString(root, "version"), readJSONString(pack, "version"), "未提供")
	description := readDescription(pack, "description")
	if strings.TrimSpace(description) == "" {
		description = sourceLabel
	}
	return GameContentEntry{
		Name:          name,
		MetadataLine:  fmt.Sprintf("作者 %s · 版本 %s", author, version),
		Description:   description,
		IconPath:      icon,
		FallbackGlyph: fallbackGlyph,
		SourcePath:    sourcePath,
	}
}

// ---------------------------------------------------------------------------
// 存档解析（level.dat = gzip + NBT）
// ---------------------------------------------------------------------------

// readSave 解析单个存档目录的展示信息。
func readSave(path string) GameContentEntry {
	folderName := filepath.Base(path)
	name := folderName
	gameVersion := "未提供"
	var lastPlayed *time.Time

	levelDat := filepath.Join(path, "level.dat")
	if fileExists(levelDat) {
		if values, err := readLevelDat(levelDat); err == nil {
			if strings.TrimSpace(values.LevelName) != "" {
				name = values.LevelName
			}
			if strings.TrimSpace(values.GameVersion) != "" {
				gameVersion = values.GameVersion
			}
			lastPlayed = values.LastPlayed
		}
	}

	created := time.Time{}
	if info, err := os.Stat(path); err == nil {
		// 目录被删除/权限不足时用占位时间，避免整个存档扫描中断
		created = info.ModTime()
	}
	icon := findFirstExisting(path, "icon.png")

	description := fmt.Sprintf("存档文件夹：%s", folderName)
	if lastPlayed != nil {
		description = fmt.Sprintf("最后游玩：%s · 存档文件夹：%s",
			lastPlayed.Format("2006-01-02 15:04"), folderName)
	}
	return GameContentEntry{
		Name:          name,
		MetadataLine:  fmt.Sprintf("创建日期 %s · Minecraft %s", created.Format("2006-01-02 15:04"), gameVersion),
		Description:   description,
		IconPath:      icon,
		FallbackGlyph: "material:Apps",
		SourcePath:    path,
	}
}

// ---------------------------------------------------------------------------
// 条目组装
// ---------------------------------------------------------------------------

// createEntry 组装 mod 条目并提取图标。
func createEntry(sourcePath string, archive *zip.Reader, name, authors, version, description, iconEntryName, fallbackGlyph string) GameContentEntry {
	icon := ""
	if strings.TrimSpace(iconEntryName) != "" {
		if entry := findEntry(archive, iconEntryName); entry != nil {
			icon = extractIcon(sourcePath, entry)
		}
	}
	descriptionText := strings.TrimSpace(description)
	if descriptionText == "" {
		descriptionText = filepath.Base(sourcePath)
	}
	return GameContentEntry{
		Name:          name,
		MetadataLine:  fmt.Sprintf("作者 %s · 版本 %s", normalizeDisplay(authors), normalizeDisplay(version)),
		Description:   descriptionText,
		IconPath:      icon,
		FallbackGlyph: fallbackGlyph,
		SourcePath:    sourcePath,
		IsDisabled:    isDisabledFile(sourcePath),
	}
}

// unknownEntry 解析失败时的降级条目。
func unknownEntry(name, glyph, sourcePath string) GameContentEntry {
	return GameContentEntry{
		Name:          name,
		MetadataLine:  "作者 未提供 · 版本 未提供",
		Description:   filepath.Base(sourcePath),
		FallbackGlyph: glyph,
		SourcePath:    sourcePath,
		IsDisabled:    isDisabledFile(sourcePath),
	}
}

func normalizeDisplay(value string) string {
	if strings.TrimSpace(value) == "" {
		return "未提供"
	}
	return strings.TrimSpace(value)
}

func isDisabledFile(path string) bool {
	return strings.HasSuffix(strings.ToLower(path), ".disabled")
}

// ---------------------------------------------------------------------------
// JSON 字段读取
// ---------------------------------------------------------------------------

// readPeople authors / contributors / authorList：字符串、对象（键为作者名）与对象数组三种形态都兼容。
func readPeople(root map[string]any, propertyName string) string {
	authors, ok := root[propertyName]
	if !ok {
		return "未提供"
	}
	switch value := authors.(type) {
	case string:
		if value == "" {
			return "未提供"
		}
		return value
	case map[string]any:
		names := make([]string, 0, len(value))
		for key := range value {
			names = append(names, key)
		}
		sort.Strings(names)
		return strings.Join(names, "、")
	case []any:
		var names []string
		for _, item := range value {
			switch entry := item.(type) {
			case string:
				if strings.TrimSpace(entry) != "" {
					names = append(names, entry)
				}
			case map[string]any:
				if name := readJSONString(entry, "name"); strings.TrimSpace(name) != "" {
					names = append(names, name)
				}
			}
		}
		if len(names) == 0 {
			return "未提供"
		}
		return strings.Join(names, "、")
	}
	return "未提供"
}

// readDescription description：字符串、组件对象（text/translate）或其他 JSON 值的原始文本。
func readDescription(root map[string]any, propertyName string) string {
	description, ok := root[propertyName]
	if !ok {
		return ""
	}
	switch value := description.(type) {
	case string:
		return value
	case map[string]any:
		if text := readJSONString(value, "text"); text != "" {
			return text
		}
		if translate := readJSONString(value, "translate"); translate != "" {
			return translate
		}
		serialized, _ := json.Marshal(value)
		return string(serialized)
	default:
		serialized, _ := json.Marshal(value)
		return string(serialized)
	}
}

// readIconProperty icon 字段：字符串或 { "尺寸": "路径" } 映射（取数值最大的尺寸）。
func readIconProperty(root map[string]any, propertyName string) string {
	icon, ok := root[propertyName]
	if !ok {
		return ""
	}
	switch value := icon.(type) {
	case string:
		return value
	case map[string]any:
		type sizePath struct {
			size int
			path string
		}
		var candidates []sizePath
		for key, entry := range value {
			size := 0
			if parsed, err := atoiSafe(key); err == nil {
				size = parsed
			}
			if text, ok := entry.(string); ok && strings.TrimSpace(text) != "" {
				candidates = append(candidates, sizePath{size, text})
			}
		}
		if len(candidates) == 0 {
			return ""
		}
		sort.Slice(candidates, func(i, j int) bool { return candidates[i].size > candidates[j].size })
		return candidates[0].path
	}
	return ""
}

func atoiSafe(value string) (int, error) {
	result := 0
	for _, ch := range value {
		if ch < '0' || ch > '9' {
			return 0, fmt.Errorf("not a number")
		}
		result = result*10 + int(ch-'0')
	}
	return result, nil
}

// readJSONString 读取 JSON 对象中字符串类型的字段，缺失或类型不符返回空串。
func readJSONString(root map[string]any, propertyName string) string {
	if root == nil {
		return ""
	}
	if value, ok := root[propertyName].(string); ok {
		return value
	}
	return ""
}

// ---------------------------------------------------------------------------
// 压缩包条目读取
// ---------------------------------------------------------------------------

// readEntryJSON 流式读取压缩包内的 JSON 条目。
// 限制实际读取字节数，避免恶意构造的条目撑爆内存。
func readEntryJSON(entry *zip.File) (any, error) {
	if entry.UncompressedSize64 > maximumMetadataBytes {
		return nil, fmt.Errorf("Metadata entry is too large.")
	}
	stream, err := entry.Open()
	if err != nil {
		return nil, err
	}
	defer stream.Close()
	limited, err := copyWithLimit(stream, maximumMetadataBytes)
	if err != nil {
		return nil, err
	}
	var document any
	if err := json.Unmarshal(limited, &document); err != nil {
		return nil, err
	}
	return document, nil
}

// readEntryObject 读取压缩包内的 JSON 条目并要求顶层为对象。
func readEntryObject(entry *zip.File) (map[string]any, error) {
	document, err := readEntryJSON(entry)
	if err != nil {
		return nil, err
	}
	obj, ok := document.(map[string]any)
	if !ok {
		return nil, fmt.Errorf("metadata entry is not an object")
	}
	return obj, nil
}

// copyWithLimit 复制最多 limit 字节；超出即报错（而不是静默截断导致解析出错）。
func copyWithLimit(source io.Reader, limit int64) ([]byte, error) {
	var buffer bytes.Buffer
	if _, err := io.CopyN(&buffer, source, limit+1); err != nil && err != io.EOF {
		return nil, err
	}
	if int64(buffer.Len()) > limit {
		return nil, fmt.Errorf("Metadata entry is too large.")
	}
	return buffer.Bytes(), nil
}

// readEntryText 读取压缩包条目的 UTF-8 文本（带大小上限）。
func readEntryText(entry *zip.File) (string, error) {
	if entry.UncompressedSize64 > maximumMetadataBytes {
		return "", fmt.Errorf("Metadata entry is too large.")
	}
	stream, err := entry.Open()
	if err != nil {
		return "", err
	}
	defer stream.Close()
	limited, err := copyWithLimit(stream, maximumMetadataBytes)
	if err != nil {
		return "", err
	}
	return string(limited), nil
}

// tomlValuePattern 用正则从 mods.toml 文本中提取顶层 key = "value"
// （含三引号与单双引号字面量）。使用时按 key 转义后动态编译。

// readTomlValue 提取 mods.toml 顶层字符串值。
func readTomlValue(text, key string) string {
	pattern := regexp.MustCompile(fmt.Sprintf(`(?im)^\s*%s\s*=\s*(?:"""([\s\S]*?)"""|'''([\s\S]*?)'''|"([^"]*)"|'([^']*)')`, regexp.QuoteMeta(key)))
	match := pattern.FindStringSubmatch(text)
	if match == nil {
		return ""
	}
	for _, group := range match[1:] {
		if group != "" {
			return strings.TrimSpace(group)
		}
	}
	return ""
}

// readManifestValue 读取 JAR 内 MANIFEST.MF 的指定键（处理续行："\r\n " 前缀拼接）。
func readManifestValue(archive *zip.Reader, key string) string {
	manifest := findEntry(archive, "META-INF/MANIFEST.MF")
	if manifest == nil {
		return ""
	}
	text, err := readEntryText(manifest)
	if err != nil {
		return ""
	}
	lines := strings.Split(strings.ReplaceAll(text, "\r\n ", ""), "\n")
	prefix := key + ":"
	for _, line := range lines {
		if len(line) >= len(prefix) && strings.EqualFold(line[:len(prefix)], prefix) {
			return strings.TrimSpace(line[len(prefix):])
		}
	}
	return ""
}

func isTemplateValue(value string) bool {
	return strings.TrimSpace(value) != "" && strings.Contains(value, "${")
}

// findEntry 按忽略大小写的完整路径查找压缩包条目。
func findEntry(archive *zip.Reader, entryPath string) *zip.File {
	normalized := strings.TrimPrefix(strings.ReplaceAll(entryPath, "\\", "/"), "/")
	for i := range archive.File {
		if strings.EqualFold(archive.File[i].Name, normalized) {
			return archive.File[i]
		}
	}
	return nil
}

// ---------------------------------------------------------------------------
// 图标提取与磁盘缓存
// ---------------------------------------------------------------------------

// extractIcon 把压缩包内的图标条目提取到 content-icons 磁盘缓存并返回路径。
// 缓存按（文件路径, 大小, mtime, 条目, 条目大小）哈希命名，文件更新后自然产生新键。
func extractIcon(sourcePath string, entry *zip.File) string {
	if entry.UncompressedSize64 <= 0 || entry.UncompressedSize64 > maximumZipIconBytes {
		return ""
	}
	extension := strings.ToLower(filepath.Ext(entry.Name))
	switch extension {
	case ".png", ".jpg", ".jpeg", ".webp":
	default:
		return ""
	}

	info, err := os.Stat(sourcePath)
	if err != nil {
		return ""
	}
	cacheKey := fmt.Sprintf("%s|%d|%d|%s|%d",
		sourcePath, info.Size(), info.ModTime().UnixNano(), entry.Name, entry.UncompressedSize64)
	sum := sha256.Sum256([]byte(cacheKey))
	hash := hex.EncodeToString(sum[:])
	cacheDirectory := filepath.Join(config.StorageDirectory(), "content-icons")
	output := filepath.Join(cacheDirectory, hash+extension)
	if fileExists(output) {
		return output
	}

	if err := os.MkdirAll(cacheDirectory, 0o755); err != nil {
		return ""
	}
	pruneIconCache(cacheDirectory)
	// 先写临时文件再改名：避免并发请求/中途失败留下半个图标文件
	temporary := fmt.Sprintf("%s.%s.tmp", output, newContentGUID())
	if err := writeEntryToFile(entry, temporary); err != nil {
		_ = os.Remove(temporary)
		return ""
	}
	if _, err := os.Stat(output); err == nil {
		// 并发竞争：别的线程/进程刚写出同一图标
		_ = os.Remove(temporary)
		return output
	}
	if err := os.Rename(temporary, output); err != nil {
		_ = os.Remove(temporary)
		return ""
	}
	return output
}

// writeEntryToFile 将压缩包条目内容写到目标文件（临时文件，独占创建）。
func writeEntryToFile(entry *zip.File, destination string) error {
	stream, err := entry.Open()
	if err != nil {
		return err
	}
	defer stream.Close()
	target, err := os.OpenFile(destination, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	defer target.Close()
	_, err = io.Copy(target, stream)
	return err
}

// pruneIconCache 图标缓存按 (路径, 大小, mtime, 条目) 哈希命名，重下/改名的模组会不断
// 产生新键；超过上限时删除最旧的一批，避免缓存无限增长。
func pruneIconCache(cacheDirectory string) {
	entries, err := os.ReadDir(cacheDirectory)
	if err != nil || len(entries) <= maximumIconCacheFiles {
		return
	}
	type fileInfo struct {
		path    string
		modTime time.Time
	}
	files := make([]fileInfo, 0, len(entries))
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}
		info, err := entry.Info()
		if err != nil {
			continue
		}
		files = append(files, fileInfo{filepath.Join(cacheDirectory, entry.Name()), info.ModTime()})
	}
	sort.Slice(files, func(i, j int) bool { return files[i].modTime.Before(files[j].modTime) })
	for _, file := range files {
		// 被占用则留给下次
		_ = os.Remove(file.path)
		remaining, err := os.ReadDir(cacheDirectory)
		if err != nil || len(remaining) <= maximumIconCacheFiles/2 {
			break
		}
	}
}

// newContentGUID 生成不带连字符的小写 GUID。
func newContentGUID() string {
	b := make([]byte, 16)
	_, _ = crand.Read(b)
	return fmt.Sprintf("%x", b)
}

// ---------------------------------------------------------------------------
// 实例目录图标探测
// ---------------------------------------------------------------------------

// findInstanceIcon 探测实例目录自带图标：固定候选名 → 元数据 JSON 引用 → instance.cfg iconKey。
func findInstanceIcon(instanceDirectory, launcherRoot string) string {
	direct := findFirstExisting(
		instanceDirectory,
		"icon.png",
		"icon.jpg",
		"instance.png",
		"logo.png",
		"profile.png",
		filepath.Join("PCL", "Logo.png"),
		filepath.Join("PCL", "Logo.jpg"),
		filepath.Join("minecraft", "icon.png"),
		filepath.Join(".minecraft", "icon.png"))
	if direct != "" {
		return direct
	}

	for _, metadataName := range []string{"minecraftinstance.json", "profile.json", "instance.json"} {
		metadataPath := filepath.Join(instanceDirectory, metadataName)
		if referenced := readReferencedIcon(metadataPath, instanceDirectory); referenced != "" {
			return referenced
		}
	}

	return readCfgIconKey(instanceDirectory, launcherRoot)
}

// readCfgIconKey MultiMC 系的 instance.cfg 里 iconKey 指向启动器 icons/ 目录下的图标。
func readCfgIconKey(instanceDirectory, launcherRoot string) string {
	cfgPath := filepath.Join(instanceDirectory, "instance.cfg")
	if !fileExists(cfgPath) || strings.TrimSpace(launcherRoot) == "" {
		return ""
	}

	data, err := os.ReadFile(cfgPath)
	if err != nil {
		return ""
	}
	iconKey := ""
	for _, line := range strings.Split(string(data), "\n") {
		parts := strings.SplitN(strings.TrimRight(line, "\r"), "=", 2)
		if len(parts) == 2 && strings.EqualFold(strings.TrimSpace(parts[0]), "iconKey") {
			iconKey = strings.TrimSpace(parts[1])
			break
		}
	}
	if strings.TrimSpace(iconKey) == "" || strings.EqualFold(iconKey, "default") {
		return ""
	}
	return findFirstExisting(filepath.Join(launcherRoot, "icons"), iconKey+".png", iconKey+".jpg")
}

// readReferencedIcon 读取第三方启动器实例元数据 JSON 中引用的图标路径。
func readReferencedIcon(metadataPath, instanceDirectory string) string {
	if !fileExists(metadataPath) {
		return ""
	}
	if info, err := os.Stat(metadataPath); err != nil || info.Size() > maximumMetadataBytes {
		return ""
	}
	data, err := os.ReadFile(metadataPath)
	if err != nil {
		return ""
	}
	var document any
	if err := json.Unmarshal(data, &document); err != nil {
		return ""
	}
	return findReferencedIcon(document, instanceDirectory)
}

// findReferencedIcon 深度优先搜索 JSON 树里的图标属性（URL 或本地路径）。
func findReferencedIcon(element any, instanceDirectory string) string {
	switch value := element.(type) {
	case map[string]any:
		keys := make([]string, 0, len(value))
		for key := range value {
			keys = append(keys, key)
		}
		sort.Strings(keys)
		for _, key := range keys {
			child := value[key]
			if text, ok := child.(string); ok && isIconProperty(key) {
				if resolved := resolveIconValue(text, instanceDirectory); resolved != "" {
					return resolved
				}
			}
			if nested := findReferencedIcon(child, instanceDirectory); nested != "" {
				return nested
			}
		}
	case []any:
		for _, child := range value {
			if nested := findReferencedIcon(child, instanceDirectory); nested != "" {
				return nested
			}
		}
	}
	return ""
}

// resolveIconValue 把图标属性值解析为可用的图标引用：https URL / 存在的本地路径。
func resolveIconValue(value, instanceDirectory string) string {
	if strings.TrimSpace(value) == "" {
		return ""
	}
	if parsed, err := url.Parse(value); err == nil && parsed.Scheme == "https" {
		return value
	}
	if parsed, err := url.Parse(value); err == nil && parsed.Scheme == "file" {
		local := parsed.Path
		if runtime.GOOS == "windows" && strings.HasPrefix(local, "/") {
			local = local[1:]
		}
		if fileExists(local) {
			return mustAbsPath(local)
		}
	}
	candidate := value
	if !filepath.IsAbs(value) {
		candidate = filepath.Join(instanceDirectory, filepath.FromSlash(value))
	}
	if fileExists(candidate) {
		return mustAbsPath(candidate)
	}
	return ""
}

// isIconProperty 图标属性名匹配表。
func isIconProperty(name string) bool {
	switch strings.ToLower(name) {
	case "icon", "iconurl", "iconpath", "icon_path",
		"profileimagepath", "profile_image_path",
		"logo", "logourl", "imageurl":
		return true
	}
	return false
}

// ---------------------------------------------------------------------------
// 加载器识别
// ---------------------------------------------------------------------------

// detectLoaderFromMetadata 从版本 JSON 与 mmc-pack.json 的文本特征识别加载器
// （NeoForge > Fabric > Quilt > Forge > 原版）。版本 JSON 只读前 1 MB。
func detectLoaderFromMetadata(minecraftDirectory, instanceDirectory, versionID string) string {
	var signals strings.Builder
	signals.WriteString(versionID)
	versionJSON := filepath.Join(minecraftDirectory, "versions", versionID, versionID+".json")
	if fileExists(versionJSON) {
		if data, err := os.ReadFile(versionJSON); err == nil {
			if len(data) > 1024*1024 {
				data = data[:1024*1024]
			}
			// 只追加实际读到的内容
			signals.Write(data)
		}
	}
	packPath := filepath.Join(instanceDirectory, "mmc-pack.json")
	if fileExists(packPath) {
		if data, err := os.ReadFile(packPath); err == nil {
			signals.Write(data)
		}
	}
	return matchLoaderName(signals.String())
}

// matchLoaderName 文本特征匹配加载器名。
func matchLoaderName(text string) string {
	lower := strings.ToLower(text)
	switch {
	case strings.Contains(lower, "neoforge"):
		return "NeoForge"
	case strings.Contains(lower, "fabric"):
		return "Fabric"
	case strings.Contains(lower, "quilt"):
		return "Quilt"
	case strings.Contains(lower, "forge"):
		return "Forge"
	default:
		return "原版"
	}
}

// loaderGlyph 回退字形统一使用 Material 图标字形（Core 只存字符串，由 UI 层渲染为图标）。
func loaderGlyph(string) string { return "material:Apps" }

// ---------------------------------------------------------------------------
// 通用文件系统辅助
// ---------------------------------------------------------------------------

// findFirstExisting 返回第一个存在的相对路径候选（相对 root 的完整路径）。
func findFirstExisting(root string, relativePaths ...string) string {
	for _, relativePath := range relativePaths {
		candidate := filepath.Join(root, relativePath)
		if fileExists(candidate) {
			return mustAbsPath(candidate)
		}
	}
	return ""
}

// fileExists 判断常规文件是否存在。
func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

// enumerateFiles 枚举目录下以指定后缀结尾的文件（目录不存在返回空）。
func enumerateFiles(directory, suffix string) []string {
	entries, err := os.ReadDir(directory)
	if err != nil {
		return nil
	}
	var result []string
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}
		if strings.HasSuffix(strings.ToLower(entry.Name()), suffix) {
			result = append(result, filepath.Join(directory, entry.Name()))
		}
	}
	return result
}

// enumerateArchivesAndDirectories 枚举目录下的 *.zip 与子目录。
func enumerateArchivesAndDirectories(directory string) []string {
	entries, err := os.ReadDir(directory)
	if err != nil {
		return nil
	}
	var result []string
	for _, entry := range entries {
		if entry.IsDir() || strings.HasSuffix(strings.ToLower(entry.Name()), ".zip") {
			result = append(result, filepath.Join(directory, entry.Name()))
		}
	}
	return result
}

func sortEntries(entries []GameContentEntry) {
	sort.Slice(entries, func(i, j int) bool {
		return strings.ToLower(entries[i].Name) < strings.ToLower(entries[j].Name)
	})
}

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if value != "" {
			return value
		}
	}
	return ""
}

// ---------------------------------------------------------------------------
// level.dat（gzip + NBT）读取
// ---------------------------------------------------------------------------

// levelDatValues level.dat 中提取的目标字段。
type levelDatValues struct {
	LevelName   string
	GameVersion string
	LastPlayed  *time.Time
}

// readLevelDat 最小的 NBT 只读解析器：只提取 LevelName / Version.Name / LastPlayed，
// 其余跳过。level.dat = gzip 压缩的 NBT。
func readLevelDat(path string) (levelDatValues, error) {
	var values levelDatValues
	file, err := os.Open(path)
	if err != nil {
		return values, err
	}
	defer file.Close()

	gzipReader, err := gzip.NewReader(file)
	if err != nil {
		return values, err
	}
	defer gzipReader.Close()

	reader := &nbtReader{r: gzipReader}
	rootType, err := reader.readByte()
	if err != nil {
		return values, err
	}
	if rootType != 10 {
		return values, fmt.Errorf("level.dat root is not a compound tag.")
	}
	if _, err := reader.readNbtString(); err != nil {
		return values, err
	}
	var lastPlayed int64
	if err := readNbtCompound(reader, "", &values.LevelName, &values.GameVersion, &lastPlayed, 0); err != nil {
		return values, err
	}
	if lastPlayed > 0 {
		timestamp := time.UnixMilli(lastPlayed)
		values.LastPlayed = &timestamp
	}
	return values, nil
}

// nbtReader 大端序二进制读取器。
type nbtReader struct {
	r io.Reader
}

func (n *nbtReader) readFull(size int) ([]byte, error) {
	buffer := make([]byte, size)
	if _, err := io.ReadFull(n.r, buffer); err != nil {
		return nil, err
	}
	return buffer, nil
}

func (n *nbtReader) readByte() (byte, error) {
	buffer, err := n.readFull(1)
	if err != nil {
		return 0, err
	}
	return buffer[0], nil
}

func (n *nbtReader) readNbtString() (string, error) {
	lengthBytes, err := n.readFull(2)
	if err != nil {
		return "", err
	}
	length := binary.BigEndian.Uint16(lengthBytes)
	buffer, err := n.readFull(int(length))
	if err != nil {
		return "", err
	}
	return string(buffer), nil
}

func (n *nbtReader) readInt32() (int32, error) {
	buffer, err := n.readFull(4)
	if err != nil {
		return 0, err
	}
	return int32(binary.BigEndian.Uint32(buffer)), nil
}

func (n *nbtReader) readInt64() (int64, error) {
	buffer, err := n.readFull(8)
	if err != nil {
		return 0, err
	}
	return int64(binary.BigEndian.Uint64(buffer)), nil
}

// skipBytes 跳过指定字节数（上限 512 MB，防构造文件）。
func (n *nbtReader) skipBytes(count int64) error {
	if count < 0 || count > 512*1024*1024 {
		return fmt.Errorf("NBT payload is too large.")
	}
	if _, err := io.CopyN(io.Discard, n.r, count); err != nil {
		return fmt.Errorf("unexpected end of NBT payload")
	}
	return nil
}

// readNbtCompound 遍历 compound；目标字段按路径后缀（Data.LevelName 等）识别，其余标签跳过。
func readNbtCompound(reader *nbtReader, path string, levelName, gameVersion *string, lastPlayed *int64, depth int) error {
	if depth > 32 {
		return fmt.Errorf("NBT nesting is too deep.")
	}
	for {
		tagType, err := reader.readByte()
		if err != nil {
			return err
		}
		if tagType == 0 {
			return nil
		}
		name, err := reader.readNbtString()
		if err != nil {
			return err
		}
		currentPath := name
		if path != "" {
			currentPath = path + "." + name
		}
		switch {
		case tagType == 8 && strings.HasSuffix(currentPath, "Data.LevelName"):
			text, err := reader.readNbtString()
			if err != nil {
				return err
			}
			*levelName = text
		case tagType == 8 && strings.HasSuffix(currentPath, "Data.Version.Name"):
			text, err := reader.readNbtString()
			if err != nil {
				return err
			}
			*gameVersion = text
		case tagType == 4 && strings.HasSuffix(currentPath, "Data.LastPlayed"):
			value, err := reader.readInt64()
			if err != nil {
				return err
			}
			*lastPlayed = value
		case tagType == 10:
			if err := readNbtCompound(reader, currentPath, levelName, gameVersion, lastPlayed, depth+1); err != nil {
				return err
			}
		default:
			if err := skipNbtPayload(reader, tagType, depth+1); err != nil {
				return err
			}
		}
	}
}

// skipNbtPayload 跳过非目标标签的负载。
// 列表可嵌套列表：与 readNbtCompound 共用同一深度上限，
// 否则构造的 level.dat 会以不可恢复的栈溢出杀死进程。
func skipNbtPayload(reader *nbtReader, tagType byte, depth int) error {
	if depth > 32 {
		return fmt.Errorf("NBT nesting is too deep.")
	}
	switch tagType {
	case 1:
		return reader.skipBytes(1)
	case 2:
		return reader.skipBytes(2)
	case 3, 5:
		return reader.skipBytes(4)
	case 4, 6:
		return reader.skipBytes(8)
	case 7:
		length, err := readNbtArrayLength(reader)
		if err != nil {
			return err
		}
		return reader.skipBytes(int64(length))
	case 8:
		_, err := reader.readNbtString()
		return err
	case 9:
		elementType, err := reader.readByte()
		if err != nil {
			return err
		}
		count, err := reader.readInt32()
		if err != nil {
			return err
		}
		if count < 0 || count > 10_000_000 {
			return fmt.Errorf("Invalid NBT list size.")
		}
		for i := int32(0); i < count; i++ {
			if err := skipNbtPayload(reader, elementType, depth+1); err != nil {
				return err
			}
		}
		return nil
	case 10:
		var unusedName, unusedVersion string
		var unusedTime int64
		return readNbtCompound(reader, "", &unusedName, &unusedVersion, &unusedTime, depth+1)
	case 11:
		length, err := readNbtArrayLength(reader)
		if err != nil {
			return err
		}
		return reader.skipBytes(int64(length) * 4)
	case 12:
		length, err := readNbtArrayLength(reader)
		if err != nil {
			return err
		}
		return reader.skipBytes(int64(length) * 8)
	default:
		return fmt.Errorf("Unsupported NBT tag %d.", tagType)
	}
}

// readNbtArrayLength 读取 NBT 数组长度（带合理上限校验）。
func readNbtArrayLength(reader *nbtReader) (int32, error) {
	length, err := reader.readInt32()
	if err != nil {
		return 0, err
	}
	if length < 0 || length > 100_000_000 {
		return 0, fmt.Errorf("Invalid NBT array size.")
	}
	return length, nil
}

// ---------------------------------------------------------------------------
// 内置图标目录
// ---------------------------------------------------------------------------

// defaultInstanceIconPath 按加载器名称给出内置 GameIcons 资源符号（"gameicon:{key}"）。
// UI 层将符号解码为程序集内嵌的 GameIcons PNG；Fabric、Quilt 及未知加载器用 command_block 兜底。
func defaultInstanceIconPath(loaderName string) string {
	switch loaderName {
	case "NeoForge":
		return "gameicon:neoforge"
	case "Forge":
		return "gameicon:forge"
	case "Fabric":
		return "gameicon:fabric"
	case "原版":
		return "gameicon:vanilla"
	default:
		return "gameicon:command_block"
	}
}
