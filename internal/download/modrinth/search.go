// Package modrinth Modrinth API v2 客户端（搜索、版本查询与下载）。
// 移植自 C# NyaLauncher.Core.Download 的 ModrinthSearch / ModrinthVersionApi。
package modrinth

import (
	"context"
	"fmt"
	"net/url"
	"strings"

	"nyalauncher/internal/models"
	"nyalauncher/internal/tools"
)

const searchBaseURL = "https://api.modrinth.com/v2/search"

// Search 通用搜索（无关键词）。
func Search(ctx context.Context, projectType string, limit int) ([]models.ModrinthProject, error) {
	return SearchFull(ctx, projectType, "", "", limit)
}

// SearchQuery 通用搜索（带关键词）。
func SearchQuery(ctx context.Context, projectType, query string, limit int) ([]models.ModrinthProject, error) {
	return SearchFull(ctx, projectType, query, "", limit)
}

// SearchFull 通用搜索（带关键词 + MC 版本过滤）。
// projectType: mod / modpack / shader / resourcepack；
// gameVersion: 按 MC 版本过滤（可选）；limit: 返回数量上限。
func SearchFull(ctx context.Context, projectType, query, gameVersion string, limit int) ([]models.ModrinthProject, error) {
	facetParts := []string{fmt.Sprintf("%q", "project_type:"+projectType)}
	if strings.TrimSpace(gameVersion) != "" {
		facetParts = append(facetParts, fmt.Sprintf("%q", "versions:"+gameVersion))
	}

	facets := url.QueryEscape(fmt.Sprintf("[[%s]]", strings.Join(facetParts, ",")))
	encodedQuery := url.QueryEscape(query)
	endpoint := fmt.Sprintf("%s?query=%s&facets=%s&limit=%d", searchBaseURL, encodedQuery, facets, limit)

	var result models.ModrinthSearchResult
	if err := getJSON(ctx, endpoint, &result); err != nil {
		return nil, err
	}
	return result.Hits, nil
}

// GetMods Mods 列表。
func GetMods(ctx context.Context, limit int) ([]models.ModrinthProject, error) {
	return Search(ctx, "mod", limit)
}

// GetModpacks 整合包列表。
func GetModpacks(ctx context.Context, limit int) ([]models.ModrinthProject, error) {
	return Search(ctx, "modpack", limit)
}

// GetShaders 光影包列表。
func GetShaders(ctx context.Context, limit int) ([]models.ModrinthProject, error) {
	return Search(ctx, "shader", limit)
}

// GetResourcePacks 材质包列表。
func GetResourcePacks(ctx context.Context, limit int) ([]models.ModrinthProject, error) {
	return Search(ctx, "resourcepack", limit)
}

// getJSON 共享 GET + JSON 解析（与 C# 的 GetFromJsonAsync 一致，走 15s 共享客户端）。
func getJSON(ctx context.Context, endpoint string, target any) error {
	req, err := httpNewRequestWithContext(ctx, "GET", endpoint)
	if err != nil {
		return err
	}
	resp, err := tools.SharedHTTPClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("modrinth 请求失败：HTTP %d", resp.StatusCode)
	}
	return jsonDecode(resp.Body, target)
}
