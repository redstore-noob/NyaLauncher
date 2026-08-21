using System.Text.Json;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 游戏文件完整性校验器。启动前检查关键文件是否齐全，缺失时自动补全。
/// </summary>
public sealed class GameFileVerifier
{
    private readonly MinecraftVersionInstaller _installer = new();

    /// <summary>
    /// 校验并补全指定版本的游戏文件。沿 inheritsFrom 链逐级检查：
    /// 版本 JSON、客户端 JAR、库文件。缺失时重新下载。
    /// </summary>
    /// <returns>补全的文件数；0 表示无需补全。</returns>
    public async Task<int> VerifyAndRepairAsync(
        string minecraftDirectory,
        string versionId,
        IProgress<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(minecraftDirectory));
        var repaired = 0;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = versionId;

        // 沿 inheritsFrom 链逐级校验
        while (!string.IsNullOrWhiteSpace(currentId) && visited.Add(currentId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versionDir = Path.Combine(root, "versions", currentId);
            var jsonPath = Path.Combine(versionDir, $"{currentId}.json");

            // 1. 版本 JSON 不存在 → 需要重新下载整个版本
            if (!File.Exists(jsonPath))
            {
                statusCallback?.Report($"版本描述 {currentId} 缺失，正在重新下载…");
                var metadataUrl = GetMetadataUrl(currentId);
                if (!string.IsNullOrWhiteSpace(metadataUrl))
                {
                    await _installer.InstallAsync(currentId, metadataUrl, root, null, cancellationToken)
                        .ConfigureAwait(false);
                    repaired++;
                }
                break;
            }

            // 2. 客户端 JAR 不存在（仅对有 downloads.client 的版本检查）
            var jarPath = Path.Combine(versionDir, $"{currentId}.jar");
            bool hasClientDownload;
            string? parentId = null;
            try
            {
                var jsonBytes = await File.ReadAllBytesAsync(jsonPath, cancellationToken)
                    .ConfigureAwait(false);
                using var doc = JsonDocument.Parse(jsonBytes);
                var rootElement = doc.RootElement;

                hasClientDownload = rootElement.TryGetProperty("downloads", out var downloads) &&
                                    downloads.TryGetProperty("client", out _);

                parentId = rootElement.TryGetProperty("inheritsFrom", out var inheritsProp)
                    ? inheritsProp.GetString()
                    : null;

                // 3. 校验库文件
                if (rootElement.TryGetProperty("libraries", out var libraries) &&
                    libraries.ValueKind == JsonValueKind.Array)
                {
                    var missingLibs = VerifyLibraries(root, libraries);
                    if (missingLibs.Count > 0)
                    {
                        statusCallback?.Report($"版本 {currentId} 缺少 {missingLibs.Count} 个库文件，正在补全…");
                        var metadataUrl = GetMetadataUrl(currentId);
                        if (!string.IsNullOrWhiteSpace(metadataUrl))
                        {
                            await _installer.InstallAsync(currentId, metadataUrl, root, null, cancellationToken)
                                .ConfigureAwait(false);
                            repaired++;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // JSON 损坏，重新下载
                statusCallback?.Report($"版本描述 {currentId} 已损坏，正在重新下载…");
                var metadataUrl = GetMetadataUrl(currentId);
                if (!string.IsNullOrWhiteSpace(metadataUrl))
                {
                    await _installer.InstallAsync(currentId, metadataUrl, root, null, cancellationToken)
                        .ConfigureAwait(false);
                    repaired++;
                }
                break;
            }

            // 客户端 JAR 缺失且该版本应该有
            if (hasClientDownload && !File.Exists(jarPath))
            {
                statusCallback?.Report($"客户端文件 {currentId} 缺失，正在重新下载…");
                var metadataUrl = GetMetadataUrl(currentId);
                if (!string.IsNullOrWhiteSpace(metadataUrl))
                {
                    await _installer.InstallAsync(currentId, metadataUrl, root, null, cancellationToken)
                        .ConfigureAwait(false);
                    repaired++;
                }
            }

            currentId = parentId;
        }

        return repaired;
    }

    /// <summary>
    /// 校验库文件列表，返回缺失的库文件路径。
    /// </summary>
    private static List<string> VerifyLibraries(string root, JsonElement libraries)
    {
        var missing = new List<string>();
        var librariesDir = Path.Combine(root, "libraries");

        foreach (var library in libraries.EnumerateArray())
        {
            if (!library.TryGetProperty("name", out var nameElement))
                continue;
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var relativePath = MavenCoordinateToPath(name);
            if (relativePath is null)
                continue;

            var fullPath = Path.Combine(librariesDir, relativePath);
            if (!File.Exists(fullPath))
                missing.Add(fullPath);
        }

        return missing;
    }

    /// <summary>
    /// 将 Maven 坐标转换为文件系统相对路径。
    /// </summary>
    private static string? MavenCoordinateToPath(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
            return null;
        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var classifier = parts.Length == 4 ? $"-{parts[3]}" : string.Empty;
        return Path.Combine(
            groupPath,
            parts[1],
            parts[2],
            $"{parts[1]}-{parts[2]}{classifier}.jar");
    }

    /// <summary>
    /// 获取版本的元数据 URL（用于重新下载）。
    /// 对于已知版本类型，返回 Mojang 官方 URL。
    /// </summary>
    private static string? GetMetadataUrl(string versionId)
    {
        // 从 Mojang 版本清单查找
        try
        {
            var versions = ManifestGet.GetVersionsAsync().GetAwaiter().GetResult();
            var match = versions.FirstOrDefault(v =>
                string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match?.Url))
                return match.Url;
        }
        catch
        {
            // 版本清单获取失败，无法自动修复
        }
        return null;
    }
}
