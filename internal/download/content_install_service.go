package download

import (
	"archive/zip"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// ContentInstallService 内容安装服务：把 Mod / 资源包 / 光影包 / 整合包下载到
// 指定实例的内容目录，或按自定义路径保存。整合包支持解压 .mrpack 并解析依赖。

// 解压防护常量。
const (
	// maximumArchiveEntries 解压防护：条目数上限。
	maximumArchiveEntries = 20000
	// maximumEntryBytes 解压防护：单条目解压后大小上限（2 GB）。
	maximumEntryBytes = 2 * 1024 * 1024 * 1024
	// maximumExtractedBytes 解压防护：累计解压字节上限（8 GB，防止压缩炸弹撑爆磁盘）。
	maximumExtractedBytes = 8 * 1024 * 1024 * 1024
)

// DownloadFileToInstance 下载文件到实例内容目录的指定子目录（如 mods / resourcepacks / shaderpacks）。
// 返回最终保存的文件路径。
func DownloadFileToInstance(
	ctx context.Context,
	downloadURL, fileName, contentDirectory, subDirectory string,
	progress ProgressBytes,
) (string, error) {
	if strings.TrimSpace(downloadURL) == "" {
		return "", fmt.Errorf("downloadURL 不能为空")
	}
	if strings.TrimSpace(contentDirectory) == "" {
		return "", fmt.Errorf("contentDirectory 不能为空")
	}

	targetPath := filepath.Join(contentDirectory, subDirectory, sanitizeFileName(fileName))
	if err := DownloadFileToPath(ctx, downloadURL, targetPath, progress); err != nil {
		return "", err
	}
	return targetPath, nil
}

// ResolveContentDirectoryForInstance 解析已安装实例的内容目录（mods / resourcepacks
// 等的父目录）。与启动时的隔离判定完全一致（全局默认隔离 + 版本自身设置），
// 避免安装内容落点与游戏运行时目录不一致（例如默认隔离下 mods 被装进共享根目录）。
func ResolveContentDirectoryForInstance(minecraftDirectory, sourcePath, versionID string) string {
	if strings.TrimSpace(minecraftDirectory) == "" {
		return ""
	}
	// GameVersionIsolation.Resolve 只依赖 SourcePath 与 MinecraftDirectory，
	// 其余快照字段对本判定无影响（见 launch_bridge.go 的钩子说明）。
	if ResolveContentDirectoryHook != nil {
		if dir := ResolveContentDirectoryHook(minecraftDirectory, sourcePath, versionID); dir != "" {
			return dir
		}
	}
	return minecraftDirectory
}

// ---------------------------------------------------------------------------
// 整合包安装
// ---------------------------------------------------------------------------

// ModpackInstallResult 安装统计：解压文件数、下载的依赖 mod 数、错误列表。
type ModpackInstallResult struct {
	InstalledFiles int
	DownloadedMods int
	Errors         []string
}

// modpackIndex mrpack index.json / CurseForge manifest.json 的公共解析模型。
type modpackIndex struct {
	Files        []modpackFileEntry `json:"files"`
	Dependencies map[string]string  `json:"dependencies"`
}

type modpackFileEntry struct {
	Path      string   `json:"path"`
	Downloads []string `json:"downloads"`
	// CurseForge manifest 引用（无 downloads 直链、无 path）
	ProjectID *int  `json:"projectID"`
	FileID    *int  `json:"fileID"`
	Required  *bool `json:"required"`
}

// InstallModpack 安装整合包到实例内容目录：
//  1. 解压包内所有文件（mods / config / saves / options.txt 等）到内容目录；
//  2. 解析 index，下载未包含在包内但声明了下载地址的 mods。
//
// 支持两种格式：
//   - Modrinth 的 .mrpack（modrinth.index.json / index.json，声明文件带下载地址）；
//   - CurseForge 的 .zip（manifest.json，mods 为 projectID/fileID 引用，
//     通过 CurseForge 公开下载端点解析实际文件）。
func InstallModpack(
	ctx context.Context,
	mrpackPath, contentDirectory string,
	progress ProgressBytes,
) (*ModpackInstallResult, error) {
	if strings.TrimSpace(mrpackPath) == "" {
		return nil, fmt.Errorf("mrpackPath 不能为空")
	}
	if strings.TrimSpace(contentDirectory) == "" {
		return nil, fmt.Errorf("contentDirectory 不能为空")
	}

	result := &ModpackInstallResult{Errors: []string{}}
	if err := os.MkdirAll(contentDirectory, 0o755); err != nil {
		return nil, err
	}

	// 1) 解析索引：mrpack 用 modrinth.index.json（v1 标准）/ 旧版 index.json；
	//    CurseForge .zip 用 manifest.json（其 files 为 projectID/fileID 引用，
	//    在下方下载循环中经 CurseForge 公开端点解析真实文件）。
	archive, err := zip.OpenReader(mrpackPath)
	if err != nil {
		return nil, err
	}
	defer archive.Close()

	var index *modpackIndex
	inArchive := map[string]bool{}
	// 包根的启动器元数据文件不属于游戏内容：解压时跳过，
	// 否则导入 MultiMC zip 会把 mmc-pack.json / instance.cfg 等落进游戏目录。
	// 仅跳过包根（无子路径）的同名文件；overrides/ 内的同名文件不受影响。
	launcherMetadataNames := map[string]bool{
		"modrinth.index.json": true, "index.json": true, "manifest.json": true,
		"mmc-pack.json": true, "instance.cfg": true, ".packignore": true, "icon.png": true,
	}
	var totalArchiveBytes int64
	for _, entry := range archive.File {
		inArchive[strings.ReplaceAll(entry.Name, "\\", "/")] = true
		if !strings.HasSuffix(entry.Name, "/") {
			totalArchiveBytes += int64(entry.UncompressedSize64)
		}
	}

	var indexEntry *zip.File
	for i := range archive.File {
		name := archive.File[i].Name
		lower := strings.ToLower(name)
		if lower == "modrinth.index.json" || lower == "index.json" || lower == "manifest.json" {
			indexEntry = archive.File[i]
			break
		}
	}
	if indexEntry != nil {
		reader, openErr := indexEntry.Open()
		if openErr == nil {
			data, readErr := io.ReadAll(reader)
			reader.Close()
			if readErr != nil {
				result.Errors = append(result.Errors, fmt.Sprintf("解析 index.json 失败：%v", readErr))
			} else {
				var parsed modpackIndex
				if err := json.Unmarshal(data, &parsed); err != nil {
					result.Errors = append(result.Errors, fmt.Sprintf("解析 index.json 失败：%v", err))
				} else {
					index = &parsed
				}
			}
		}
	}

	// 2) 解压包内文件（处理 overrides/ 前缀剥离；跳过 index 文件）。
	//    防解压炸弹：条目数、单文件与累计解压字节超限时中止。
	totalFiles := 0
	for _, entry := range archive.File {
		if !strings.HasSuffix(entry.Name, "/") {
			totalFiles++
		}
	}
	if totalFiles > maximumArchiveEntries {
		return nil, fmt.Errorf("整合包含 %d 个文件条目，超过安全上限（%d）。", totalFiles, maximumArchiveEntries)
	}
	var extractedBytes int64
	for _, entry := range archive.File {
		if strings.HasSuffix(entry.Name, "/") {
			continue
		}
		if indexEntry != nil && strings.EqualFold(entry.Name, indexEntry.Name) {
			continue
		}
		normalizedEntryName := strings.ReplaceAll(entry.Name, "\\", "/")
		if !strings.Contains(normalizedEntryName, "/") && launcherMetadataNames[strings.ToLower(normalizedEntryName)] {
			continue
		}

		if err := extractEntry(ctx, entry, contentDirectory, &extractedBytes, totalArchiveBytes, progress, result); err != nil {
			if ctx.Err() != nil {
				return nil, ctx.Err()
			}
			result.Errors = append(result.Errors, fmt.Sprintf("解压 %s 失败：%v", entry.Name, err))
			continue
		}
		result.InstalledFiles++
	}

	// 3) 下载 index 中声明但不在包内的文件（mods / resourcepacks / shaderpacks 等）
	if index != nil && len(index.Files) > 0 {
		for i := range index.Files {
			if ctx.Err() != nil {
				break
			}
			file := &index.Files[i]
			// CurseForge manifest 条目没有 path，只有 projectID/fileID + required
			isCurseForgeRef := file.Path == "" && file.ProjectID != nil && *file.ProjectID > 0 &&
				file.FileID != nil && *file.FileID > 0
			if strings.TrimSpace(file.Path) == "" && !isCurseForgeRef {
				continue
			}
			if file.Required != nil && !*file.Required {
				continue // 可选依赖不自动下载
			}
			// 解压阶段会剥离 overrides/ 前缀，因此依赖路径匹配必须同时
			// 比对 "overrides/<path>"，否则包内已附带的依赖会被误判为缺失而联网下载
			if file.Path != "" {
				normalized := strings.ReplaceAll(file.Path, "\\", "/")
				if inArchive[normalized] || inArchive["overrides/"+normalized] {
					continue // 文件已包含在包内，解压步骤已处理
				}
			}

			downloadURL := resolveDeclaredFileURL(file, result)
			if strings.TrimSpace(downloadURL) == "" {
				continue
			}

			// mrpack：file.Path 是相对实例根的路径（如 mods/foo.jar），直接拼到内容目录；
			// CurseForge 引用：解析 CDN 文件名后落到 mods/ 目录
			var targetPath string
			if file.Path != "" {
				targetPath = mustSafeCombine(contentDirectory, strings.ReplaceAll(file.Path, "\\", "/"))
			} else {
				fileName := resolveCurseForgeFileName(ctx, downloadURL)
				if strings.TrimSpace(fileName) == "" {
					result.Errors = append(result.Errors,
						fmt.Sprintf("无法解析 CurseForge 依赖 %d/%d 的文件名，已跳过。", *file.ProjectID, *file.FileID))
					continue
				}
				targetPath = mustSafeCombine(contentDirectory, "mods/"+fileName)
			}

			if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
				result.Errors = append(result.Errors, fmt.Sprintf("下载依赖 %s 失败：%v", file.Path, err))
				continue
			}
			if err := DownloadFileToPath(ctx, downloadURL, targetPath, progress); err != nil {
				if ctx.Err() != nil {
					return nil, ctx.Err()
				}
				label := file.Path
				if label == "" && file.ProjectID != nil && file.FileID != nil {
					label = fmt.Sprintf("%d/%d", *file.ProjectID, *file.FileID)
				}
				result.Errors = append(result.Errors, fmt.Sprintf("下载依赖 %s 失败：%v", label, err))
				continue
			}
			result.DownloadedMods++
		}
	}

	return result, nil
}

// extractEntry 解压单个 zip 条目（含大小与累计字节防护、overrides/ 前缀剥离）。
func extractEntry(
	ctx context.Context,
	entry *zip.File,
	contentDirectory string,
	extractedBytes *int64,
	totalArchiveBytes int64,
	progress ProgressBytes,
	result *ModpackInstallResult,
) error {
	if int64(entry.UncompressedSize64) > maximumEntryBytes {
		return fmt.Errorf("条目 %s 大小 %d MB 超过单文件上限。", entry.Name, entry.UncompressedSize64/1024/1024)
	}
	*extractedBytes += int64(entry.UncompressedSize64)
	if *extractedBytes > maximumExtractedBytes {
		return fmt.Errorf("累计解压字节超过安全上限（%d MB），疑似压缩炸弹。", maximumExtractedBytes/1024/1024)
	}

	// overrides/ 前缀剥离：overrides/config/... -> config/...（mrpack 规范）
	relative := strings.ReplaceAll(entry.Name, "\\", "/")
	if len(relative) >= len("overrides/") && strings.EqualFold(relative[:len("overrides/")], "overrides/") {
		relative = relative[len("overrides/"):]
	}

	destination := mustSafeCombine(contentDirectory, relative)
	if err := os.MkdirAll(filepath.Dir(destination), 0o755); err != nil {
		return err
	}
	reader, err := entry.Open()
	if err != nil {
		return err
	}
	defer reader.Close()
	writer, err := os.OpenFile(destination, os.O_TRUNC|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	defer writer.Close()
	if _, err := io.Copy(writer, reader); err != nil {
		return err
	}
	// 字节口径与依赖下载一致（UI 按 MB 展示）
	if progress != nil {
		progress(*extractedBytes, totalArchiveBytes)
	}
	return nil
}

// curseForgeMetadataClient CurseForge 依赖解析专用客户端：禁用自动重定向以读取
// 302 Location 中的文件名（默认会自动跟随 302 到 CDN，最终 200 无文件名信息）。
var curseForgeMetadataClient = &http.Client{
	CheckRedirect: func(req *http.Request, via []*http.Request) error {
		return http.ErrUseLastResponse // 不跟随重定向
	},
	Timeout: 30 * time30Seconds,
}

const time30Seconds = 30 * time.Second

// resolveCurseForgeFileName 解析 CurseForge 公开下载端点的真实文件名：
// 端点返回 302，Location 指向 edge.forgecdn.net 的 CDN 地址（尾段即文件名）。
func resolveCurseForgeFileName(ctx context.Context, downloadURL string) string {
	req, err := http.NewRequestWithContext(ctx, "GET", downloadURL, nil)
	if err != nil {
		return ""
	}
	resp, err := curseForgeMetadataClient.Do(req)
	if err != nil {
		return ""
	}
	defer resp.Body.Close()

	switch resp.StatusCode {
	case http.StatusFound, http.StatusMovedPermanently,
		http.StatusTemporaryRedirect, http.StatusPermanentRedirect:
		location := resp.Header.Get("Location")
		if strings.TrimSpace(location) == "" {
			return ""
		}
		if decoded, err := url.QueryUnescape(location); err == nil {
			location = decoded
		}
		segments := strings.Split(location, "/")
		for i := len(segments) - 1; i >= 0; i-- {
			if segments[i] != "" {
				return segments[i]
			}
		}
		return ""
	}

	// 未重定向（直接 200）时尝试 Content-Disposition
	disposition := resp.Header.Get("Content-Disposition")
	if filename := parseContentDispositionFileName(disposition); filename != "" {
		return filename
	}
	return ""
}

// resolveDeclaredFileURL 解析声明文件的下载地址：优先直链（Modrinth mrpack）；
// 无直链但带 projectID/fileID 时（CurseForge manifest）构造公开下载端点，
// 该端点 302 到 CDN，无需 API key。
func resolveDeclaredFileURL(file *modpackFileEntry, result *ModpackInstallResult) string {
	for _, candidate := range file.Downloads {
		if isWellFormedHTTPURL(candidate) {
			return candidate
		}
	}

	if file.ProjectID != nil && *file.ProjectID > 0 && file.FileID != nil && *file.FileID > 0 {
		return fmt.Sprintf("https://www.curseforge.com/api/v1/mods/%d/files/%d/download", *file.ProjectID, *file.FileID)
	}

	detail := "（且非 CurseForge 引用）"
	if file.ProjectID != nil && *file.ProjectID > 0 {
		detail = "（fileID 缺失）"
	}
	result.Errors = append(result.Errors,
		fmt.Sprintf("依赖 %s 无可用下载地址%s，已跳过。", file.Path, detail))
	return ""
}

// DownloadModpackFile 下载整合包文件（.mrpack）到自定义路径或实例目录。
// 用于"自定义保存路径"场景：仅保存文件，不做解压。
func DownloadModpackFile(ctx context.Context, downloadURL, fileName, targetPath string, progress ProgressBytes) error {
	return DownloadFileToPath(ctx, downloadURL, targetPath, progress)
}

// mustSafeCombine 安全拼接：确保解压目标位于内容目录内，阻止路径穿越。
func mustSafeCombine(root, relativePath string) string {
	normalized := strings.ReplaceAll(relativePath, "\\", "/")
	if strings.HasPrefix(normalized, "/") || strings.Contains(normalized, "..") {
		panic(fmt.Sprintf("非法的整合包内路径：%s", relativePath))
	}

	rootFull := filepath.Clean(root)
	combined := filepath.Clean(filepath.Join(rootFull, filepath.FromSlash(normalized)))
	if combined != rootFull && !strings.HasPrefix(strings.ToLower(combined), strings.ToLower(rootFull+string(filepath.Separator))) {
		panic(fmt.Sprintf("整合包路径越界：%s", relativePath))
	}
	return combined
}

func sanitizeFileName(fileName string) string {
	name := strings.Trim(strings.TrimSpace(fileName), "\"")
	name = filepath.Base(name)
	name = sanitizeSegment(name)
	if strings.TrimSpace(name) == "" {
		return "download"
	}
	return name
}

// ---------------------------------------------------------------------------
// 整合包依赖（index.json -> dependencies）
// ---------------------------------------------------------------------------

// ModpackRequirements 整合包声明的运行要求，解析自 mrpack 的 index.json dependencies。
// 例如 Fabulously Optimized：{ "fabric-loader": "0.19.3", "minecraft": "26.2" }。
type ModpackRequirements struct {
	// MinecraftVersion 要求的 Minecraft 版本，如 "1.21.8"。
	MinecraftVersion string
	// LoaderType 加载器类型。无加载器键时为 Vanilla；遇到不支持的加载器键时保持
	// Vanilla，并通过 RawLoaderKey 暴露原始键以便上层告警。
	LoaderType ModLoaderType
	// LoaderVersion 加载器版本，如 "0.19.3"。
	LoaderVersion string
	// RawLoaderKey 原始加载器依赖键（如 "fabric-loader" / "quilt-loader"），用于不支持时告警。
	RawLoaderKey string
}

// LoaderSupported 本启动器是否能安装该加载器（Fabric / Quilt / NeoForge / Forge 支持）。
func (r *ModpackRequirements) LoaderSupported() bool {
	switch r.LoaderType {
	case ModLoaderFabric, ModLoaderQuilt, ModLoaderNeoForge, ModLoaderForge:
		return true
	default:
		return false
	}
}

// ReadModpackRequirements 解析整合包的版本要求。
// 支持：mrpack 的 modrinth.index.json / index.json（dependencies 字典），
// 以及 CurseForge 的 manifest.json（minecraft.version + minecraft.modLoaders[].id）。
// 解析失败（无索引 / 无 minecraft 依赖 / JSON 损坏）时返回 nil。
func ReadModpackRequirements(ctx context.Context, mrpackPath string) (*ModpackRequirements, error) {
	if strings.TrimSpace(mrpackPath) == "" {
		return nil, fmt.Errorf("mrpackPath 不能为空")
	}

	archive, err := zip.OpenReader(mrpackPath)
	if err != nil {
		return nil, err
	}
	defer archive.Close()

	var mrpackEntry, manifestEntry *zip.File
	for i := range archive.File {
		lower := strings.ToLower(archive.File[i].Name)
		switch lower {
		case "modrinth.index.json", "index.json":
			if mrpackEntry == nil {
				mrpackEntry = archive.File[i]
			}
		case "manifest.json":
			if manifestEntry == nil {
				manifestEntry = archive.File[i]
			}
		}
	}

	if mrpackEntry != nil {
		reader, openErr := mrpackEntry.Open()
		if openErr != nil {
			return nil, openErr
		}
		data, readErr := io.ReadAll(reader)
		reader.Close()
		if readErr != nil {
			return nil, readErr
		}
		var index modpackIndex
		if err := json.Unmarshal(data, &index); err != nil || index.Dependencies == nil {
			return nil, nil
		}
		return requirementsFromDependencies(index.Dependencies), nil
	}

	// CurseForge manifest.json：minecraft.version + modLoaders[].id（如 "forge-47.2.0"）
	if manifestEntry != nil {
		reader, openErr := manifestEntry.Open()
		if openErr != nil {
			return nil, openErr
		}
		data, readErr := io.ReadAll(reader)
		reader.Close()
		if readErr != nil {
			return nil, readErr
		}
		var root struct {
			Minecraft *struct {
				Version    string `json:"version"`
				ModLoaders []struct {
					ID      string `json:"id"`
					Primary bool   `json:"primary"`
				} `json:"modLoaders"`
			} `json:"minecraft"`
		}
		if err := json.Unmarshal(data, &root); err != nil || root.Minecraft == nil || root.Minecraft.Version == "" {
			return nil, nil
		}
		req := &ModpackRequirements{MinecraftVersion: root.Minecraft.Version}
		// primary 优先，缺省取第一个；id 形如 "forge-47.2.0" / "fabric-0.15.11"
		for _, loader := range root.Minecraft.ModLoaders {
			if loader.ID == "" {
				continue
			}
			req.RawLoaderKey, req.LoaderType, req.LoaderVersion = parseLoaderID(loader.ID)
			if req.LoaderType != ModLoaderVanilla || loader.Primary {
				break
			}
		}
		return req, nil
	}

	return nil, nil
}

// parseLoaderID 解析 "forge-47.2.0" 形式的加载器 id。
func parseLoaderID(loaderID string) (rawKey string, loaderType ModLoaderType, loaderVersion string) {
	separator := strings.Index(loaderID, "-")
	if separator <= 0 {
		rawKey = loaderID
	} else {
		rawKey = loaderID[:separator]
		loaderVersion = loaderID[separator+1:]
	}
	if t, ok := mapLoaderKey(rawKey); ok {
		loaderType = t
		if strings.TrimSpace(loaderVersion) == "" {
			loaderVersion = ""
		}
	} else {
		loaderType = ModLoaderVanilla
	}
	return rawKey, loaderType, loaderVersion
}

func requirementsFromDependencies(deps map[string]string) *ModpackRequirements {
	mc := deps["minecraft"]
	if strings.TrimSpace(mc) == "" {
		return nil
	}

	req := &ModpackRequirements{MinecraftVersion: mc}

	// 取第一个非 minecraft 的依赖键作为加载器（mrpack 规范最多一个加载器）
	for key, value := range deps {
		if strings.EqualFold(key, "minecraft") {
			continue
		}
		req.RawLoaderKey = key
		if t, ok := mapLoaderKey(key); ok {
			req.LoaderType = t
			req.LoaderVersion = value
		}
		// 未知加载器键：保持 Vanilla，由上层据 RawLoaderKey 告警
		break
	}
	return req
}

func mapLoaderKey(key string) (ModLoaderType, bool) {
	normalized := strings.ToLower(strings.TrimSpace(key))
	switch normalized {
	// mrpack 用 "fabric-loader"；CurseForge manifest 的 modLoaders id 前缀是 "fabric"
	case "fabric-loader", "fabric":
		return ModLoaderFabric, true
	case "quilt-loader", "quilt":
		return ModLoaderQuilt, true
	case "neoforge":
		return ModLoaderNeoForge, true
	case "forge":
		return ModLoaderForge, true
	default:
		return ModLoaderVanilla, false
	}
}

// ---- 小工具 ----

func isWellFormedHTTPURL(raw string) bool {
	return strings.HasPrefix(raw, "http://") || strings.HasPrefix(raw, "https://")
}

func parseContentDispositionFileName(disposition string) string {
	if disposition == "" {
		return ""
	}
	lower := strings.ToLower(disposition)
	idx := strings.Index(lower, "filename")
	if idx < 0 {
		return ""
	}
	rest := disposition[idx+len("filename"):]
	rest = strings.TrimLeft(rest, " ")
	if strings.HasPrefix(rest, "*=") {
		rest = rest[2:]
	} else if strings.HasPrefix(rest, "=") {
		rest = rest[1:]
	} else {
		return ""
	}
	rest = strings.Trim(strings.TrimSpace(rest), "\"")
	return rest
}
