package modrinth

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"sort"
	"strconv"
	"strings"

	"nyalauncher/internal/logs"
	"nyalauncher/internal/models"
)

const apiBaseURL = "https://api.modrinth.com/v2"

// GetVersions 获取指定项目的版本列表，可按 MC 版本和 Loader 过滤。
func GetVersions(ctx context.Context, projectID string, gameVersions, loaders []string) ([]models.ModrinthVersion, error) {
	if strings.TrimSpace(projectID) == "" {
		return nil, fmt.Errorf("projectID 不能为空")
	}

	endpoint := fmt.Sprintf("%s/project/%s/version", apiBaseURL, url.PathEscape(projectID))
	var params []string
	if len(gameVersions) > 0 {
		quoted := make([]string, len(gameVersions))
		for i, v := range gameVersions {
			quoted[i] = strconv.Quote(v)
		}
		params = append(params, "game_versions="+url.QueryEscape("["+strings.Join(quoted, ",")+"]"))
	}
	if len(loaders) > 0 {
		quoted := make([]string, len(loaders))
		for i, l := range loaders {
			quoted[i] = strconv.Quote(l)
		}
		params = append(params, "loaders="+url.QueryEscape("["+strings.Join(quoted, ",")+"]"))
	}
	if len(params) > 0 {
		endpoint += "?" + strings.Join(params, "&")
	}

	var versions []models.ModrinthVersion
	if err := getJSON(ctx, endpoint, &versions); err != nil {
		// 响应格式异常不应伪装成"没有版本"：留下日志便于诊断；
		// JSON 解析失败时仍返回空列表保持对调用方的兼容行为（与 C# 一致）。
		var jsonErr *json.SyntaxError
		if strings.Contains(err.Error(), "invalid character") || strings.Contains(err.Error(), "unmarshal") ||
			strings.Contains(err.Error(), "JSON") {
			logs.Write("WARN", fmt.Sprintf("Modrinth 版本响应解析失败（%s）: %v", projectID, err))
			return []models.ModrinthVersion{}, nil
		}
		_ = jsonErr
		return nil, err
	}
	if versions == nil {
		versions = []models.ModrinthVersion{}
	}
	return versions, nil
}

// GetSupportedGameVersions 获取指定项目支持的 MC 版本列表（去重、按版本号降序）。
func GetSupportedGameVersions(ctx context.Context, projectID string) ([]string, error) {
	versions, err := GetVersions(ctx, projectID, nil, nil)
	if err != nil {
		return nil, err
	}
	seen := map[string]bool{}
	var result []string
	for _, v := range versions {
		for _, gv := range v.GameVersions {
			key := strings.ToLower(gv)
			if seen[key] {
				continue
			}
			seen[key] = true
			result = append(result, gv)
		}
	}
	sort.SliceStable(result, func(i, j int) bool {
		return CompareVersionStrings(result[i], result[j]) > 0
	})
	return result, nil
}

// CompareVersionStrings 按分段数值比较 MC 版本号（如 1.10.2 > 1.9.4），
// 替代会产生错误顺序的字符串比较。
func CompareVersionStrings(a, b string) int {
	aParts := strings.FieldsFunc(a, func(r rune) bool { return r == '.' || r == '-' || r == '_' })
	bParts := strings.FieldsFunc(b, func(r rune) bool { return r == '.' || r == '-' || r == '_' })
	length := len(aParts)
	if len(bParts) > length {
		length = len(bParts)
	}
	for i := 0; i < length; i++ {
		aPart := "0"
		if i < len(aParts) {
			aPart = aParts[i]
		}
		bPart := "0"
		if i < len(bParts) {
			bPart = bParts[i]
		}
		aNum, aErr := strconv.Atoi(aPart)
		bNum, bErr := strconv.Atoi(bPart)
		if aErr == nil && bErr == nil {
			if aNum != bNum {
				if aNum < bNum {
					return -1
				}
				return 1
			}
		} else if aPart != bPart {
			return strings.Compare(aPart, bPart)
		}
	}
	return 0
}

// GetSupportedLoaders 获取指定项目在指定 MC 版本下支持的 Loader 列表（去重、排序）。
func GetSupportedLoaders(ctx context.Context, projectID, gameVersion string) ([]string, error) {
	versions, err := GetVersions(ctx, projectID, []string{gameVersion}, nil)
	if err != nil {
		return nil, err
	}
	seen := map[string]bool{}
	var result []string
	for _, v := range versions {
		for _, l := range v.Loaders {
			key := strings.ToLower(l)
			if seen[key] {
				continue
			}
			seen[key] = true
			result = append(result, l)
		}
	}
	sort.SliceStable(result, func(i, j int) bool {
		return strings.ToLower(result[i]) < strings.ToLower(result[j])
	})
	return result, nil
}

// GetVersionsForCombo 获取指定项目在指定 MC 版本 + Loader 下的可用 Mod 版本列表。
func GetVersionsForCombo(ctx context.Context, projectID, gameVersion, loader string) ([]models.ModrinthVersion, error) {
	return GetVersions(ctx, projectID, []string{gameVersion}, []string{loader})
}

// ---- 内部 HTTP 辅助 ----

func httpNewRequestWithContext(ctx context.Context, method, endpoint string) (*http.Request, error) {
	return http.NewRequestWithContext(ctx, method, endpoint, nil)
}

func jsonDecode(r io.Reader, target any) error {
	return json.NewDecoder(r).Decode(target)
}
