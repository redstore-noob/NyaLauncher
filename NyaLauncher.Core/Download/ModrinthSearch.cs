using System.Net.Http.Json;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

/// <summary>
/// Modrinth API v2 搜索服务（免费开源，无需 API Key）
/// </summary>
public static class ModrinthSearch
{
    private const string BaseUrl = "https://api.modrinth.com/v2/search";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            { "User-Agent", "NyaLauncher/1.0" }
        }
    };

    /// <summary>
    /// 通用搜索（无关键词）
    /// </summary>
    /// <param name="projectType">项目类型: mod / modpack / shader / resourcepack</param>
    /// <param name="limit">返回数量上限</param>
    public static Task<List<ModrinthProject>> SearchAsync(string projectType, int limit = 50)
        => SearchAsync(projectType, "", limit);

    /// <summary>
    /// 通用搜索（带关键词，用于用户搜索）
    /// </summary>
    /// <param name="projectType">项目类型: mod / modpack / shader / resourcepack</param>
    /// <param name="query">搜索关键词</param>
    /// <param name="limit">返回数量上限</param>
    public static async Task<List<ModrinthProject>> SearchAsync(string projectType, string query, int limit = 50)
    {
        // Modrinth API 要求双引号 JSON 格式的 facets
        var facets = Uri.EscapeDataString($"[[\"project_type:{projectType}\"]]");
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{BaseUrl}?query={encodedQuery}&facets={facets}&limit={limit}";

        var result = await HttpClient.GetFromJsonAsync<ModrinthSearchResult>(url);
        return result?.Hits ?? [];
    }

    public static Task<List<ModrinthProject>> GetModsAsync(int limit = 50)
        => SearchAsync("mod", limit);

    public static Task<List<ModrinthProject>> GetModpacksAsync(int limit = 50)
        => SearchAsync("modpack", limit);

    public static Task<List<ModrinthProject>> GetShadersAsync(int limit = 50)
        => SearchAsync("shader", limit);

    public static Task<List<ModrinthProject>> GetResourcePacksAsync(int limit = 50)
        => SearchAsync("resourcepack", limit);
}
