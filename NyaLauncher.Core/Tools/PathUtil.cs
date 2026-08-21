using System.Text.Json;

namespace NyaLauncher.Core.Tools;

/// <summary>
/// 全项目共享的路径、HttpClient、JSON 辅助方法。
/// </summary>
public static class PathUtil
{
    /// <summary>
    /// 项目共享的 HttpClient 实例（15 秒超时，统一 User-Agent）。
    /// 用于版本清单、Modrinth 搜索等轻量 GET 请求。
    /// 需要无限超时的下载场景（MinecraftVersionInstaller）使用独立实例。
    /// </summary>
    public static HttpClient SharedHttpClient { get; } = CreateSharedHttpClient();

    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NyaLauncher/1.0");
        return client;
    }

    /// <summary>
    /// 适合当前平台的路径比较器：Windows 忽略大小写，其他系统区分大小写。
    /// </summary>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// 规范化后比较两个路径是否相等（去掉尾部目录分隔符、展开为完整路径）。
    /// 任一为空或非法时按"不相等"处理，不抛异常。
    /// </summary>
    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // 非法路径（如含空字符）按不相等处理
            return false;
        }
    }

    /// <summary>
    /// 安全读取 JSON 字符串属性。属性不存在、非字符串类型或为空时返回 false。
    /// </summary>
    public static bool TryGetString(JsonElement parent, string propertyName, out string value)
    {
        value = string.Empty;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }
}
