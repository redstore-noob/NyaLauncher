using System.Text.Json;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Launch.Internal;

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
                var metadataUrl = await GetMetadataUrlAsync(currentId, cancellationToken)
                    .ConfigureAwait(false);
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

                // 3. 校验库文件（并识别是否为 NeoForge/Forge Loader 实例）
                var isLoaderInstance = TryParseLoaderInfo(
                    jsonBytes, out var loaderType, out var loaderVersion, out var loaderMcVersion);
                if (rootElement.TryGetProperty("libraries", out var libraries) &&
                    libraries.ValueKind == JsonValueKind.Array)
                {
                    var missingLibs = VerifyLibraries(root, libraries);

                    // NeoForge / Forge 特判：
                    // a) 缺失库中含 SRG 客户端（新流程安装器生成的 JSON 会声明该库）；
                    // b) 或识别为 Loader 实例但 client 库目录下没有任何 srg jar
                    //    （老流程提取式安装的 JSON 不声明 srg，必须显式检查）。
                    //    srg 只能由安装器生成，任何 Maven 仓库都不存在，必须重跑安装器。
                    var needRerun = missingLibs.Any(IsSrgClientJar);
                    if (!needRerun && isLoaderInstance &&
                        loaderType is ModLoaderType.NeoForge or ModLoaderType.Forge)
                    {
                        // NeoForge 26.x（NeoForgeV1）运行时产物是 minecraft-client-patched.jar，
                        // 不需要 srg；Forge 老架构（MCP）才需要 client-srg.jar。按类型检查对应产物，
                        // 缺失说明安装器未完整运行，重跑安装器。
                        needRerun = loaderType == ModLoaderType.NeoForge
                            ? !HasNeoForgePatchedClient(root, loaderVersion)
                            : !HasAnySrgClientJar(root);
                    }

                    if (needRerun)
                    {
                        statusCallback?.Report(
                            $"版本 {currentId} 的 Loader 安装不完整（缺少 SRG 客户端库），正在重新运行安装器修复…");
                        var rerunOk = await RerunLoaderInstallerAsync(
                                root, currentId, loaderType, loaderVersion, loaderMcVersion,
                                statusCallback, cancellationToken)
                            .ConfigureAwait(false);
                        if (rerunOk)
                        {
                            repaired++;
                            // 安装器已重建该版本目录，本链修复完成
                            break;
                        }
                        statusCallback?.Report("重新运行安装器失败，回退到直接补全缺失库…");
                    }

                    if (missingLibs.Count > 0)
                    {
                        statusCallback?.Report(
                            $"版本 {currentId} 缺少 {missingLibs.Count} 个库文件，正在补全…");
                        try
                        {
                            await _installer.InstallFromMetadataBytesAsync(
                                    currentId, root, jsonBytes, null, cancellationToken)
                                .ConfigureAwait(false);
                            repaired++;
                        }
                        catch (Exception repairException)
                        {
                            statusCallback?.Report($"补全失败：{repairException.Message}");
                        }
                    }
                }
            }
            catch (Exception verifyException) when (
                verifyException is JsonException or IOException)
            {
                // JSON 损坏或文件被占用（并发安装/杀毒软件）都视为需要重新下载
                statusCallback?.Report($"版本描述 {currentId} 读取失败，正在重新下载…");
                var metadataUrl = await GetMetadataUrlAsync(currentId, cancellationToken)
                    .ConfigureAwait(false);
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
                var metadataUrl = await GetMetadataUrlAsync(currentId, cancellationToken)
                    .ConfigureAwait(false);
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
    /// 从版本 JSON 字节解析 Loader 信息（类型、Loader 版本、继承的原版版本）。
    /// 解析失败或信息不全时返回 false。
    /// </summary>
    internal static bool TryParseLoaderInfo(
        byte[] jsonBytes,
        out ModLoaderType loaderType,
        out string loaderVersion,
        out string minecraftVersion)
    {
        loaderType = ModLoaderType.NeoForge;
        loaderVersion = string.Empty;
        minecraftVersion = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            var root = doc.RootElement;

            // MC 版本：Loader 版本 JSON 一律通过 inheritsFrom 继承原版
            if (root.TryGetProperty("inheritsFrom", out var inherits))
                minecraftVersion = inherits.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                return false;

            var versionId = root.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            var mainClass = root.TryGetProperty("mainClass", out var mcElement)
                ? mcElement.GetString()
                : null;

            // 信号 1：libraries 坐标（新流程安装器生成的 JSON 含 neoforge/forge 主库）
            if (root.TryGetProperty("libraries", out var libs) &&
                libs.ValueKind == JsonValueKind.Array)
            {
                foreach (var lib in libs.EnumerateArray())
                {
                    if (!lib.TryGetProperty("name", out var nameElement))
                        continue;
                    var name = nameElement.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    const string neoPrefix = "net.neoforged:neoforge:";
                    const string forgePrefix = "net.minecraftforge:forge:";
                    if (name.StartsWith(neoPrefix, StringComparison.Ordinal))
                    {
                        loaderType = ModLoaderType.NeoForge;
                        loaderVersion = name[neoPrefix.Length..];
                        return !string.IsNullOrWhiteSpace(loaderVersion);
                    }

                    if (name.StartsWith(forgePrefix, StringComparison.Ordinal))
                    {
                        loaderType = ModLoaderType.Forge;
                        loaderVersion = name[forgePrefix.Length..];
                        return !string.IsNullOrWhiteSpace(loaderVersion);
                    }
                }
            }

            // 信号 2：版本 id 前缀（老流程提取式安装的 JSON 无主库，id 即 "neoforge-{loader}" / "forge-{loader}"）
            if (!string.IsNullOrWhiteSpace(versionId))
            {
                const string neoIdPrefix = "neoforge-";
                const string forgeIdPrefix = "forge-";
                if (versionId.StartsWith(neoIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    loaderType = ModLoaderType.NeoForge;
                    loaderVersion = versionId[neoIdPrefix.Length..];
                    // 实例名对齐后 id 可能形如 "neoforge-{loader}-{mc}"，去掉 mc 后缀还原纯 loader 版本
                    var mcSuffix = $"-{minecraftVersion}";
                    if (loaderVersion.EndsWith(mcSuffix, StringComparison.Ordinal))
                        loaderVersion = loaderVersion[..^mcSuffix.Length];
                    return !string.IsNullOrWhiteSpace(loaderVersion);
                }

                if (versionId.StartsWith(forgeIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    loaderType = ModLoaderType.Forge;
                    loaderVersion = versionId[forgeIdPrefix.Length..];
                    return !string.IsNullOrWhiteSpace(loaderVersion);
                }
            }

            // 信号 3：mainClass（id 不含前缀时的类型兜底，版本仍需靠其它途径）
            if (!string.IsNullOrWhiteSpace(mainClass))
            {
                if (mainClass.Contains("net.neoforged", StringComparison.Ordinal))
                {
                    loaderType = ModLoaderType.NeoForge;
                    return false;
                }

                if (mainClass.Contains("net.minecraftforge", StringComparison.Ordinal))
                {
                    loaderType = ModLoaderType.Forge;
                    return false;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查 Minecraft client 库目录下是否存在任意 SRG 重映射客户端（*-srg.jar）。
    /// Forge 老架构（MCP）启动必需该文件，且只能由安装器生成。
    /// </summary>
    internal static bool HasAnySrgClientJar(string minecraftRoot)
    {
        var clientLibDir = Path.Combine(minecraftRoot, "libraries", "net", "minecraft", "client");
        if (!Directory.Exists(clientLibDir))
            return false;
        return Directory.EnumerateFiles(
                clientLibDir, "*-srg.jar", SearchOption.AllDirectories)
            .Any();
    }

    /// <summary>
    /// 检查 NeoForge 的 patched 客户端（minecraft-client-patched）是否存在。
    /// NeoForge 26.x（NeoForgeV1 架构）以它为运行时的 Minecraft 类来源，缺则安装不完整。
    /// </summary>
    internal static bool HasNeoForgePatchedClient(string minecraftRoot, string loaderVersion)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            return false;
        var patchedJar = Path.Combine(
            minecraftRoot, "libraries", "net", "neoforged", "minecraft-client-patched",
            loaderVersion, $"minecraft-client-patched-{loaderVersion}.jar");
        return File.Exists(patchedJar);
    }

    /// <summary>
    /// 重新运行 Loader 安装器（NeoForge / Forge）以修复 SRG 客户端库缺失。
    /// 安装器地址候选：BMCL 镜像优先，官方 Maven 兜底。
    /// </summary>
    private static async Task<bool> RerunLoaderInstallerAsync(
        string root,
        string versionId,
        ModLoaderType loaderType,
        string loaderVersion,
        string minecraftVersion,
        IProgress<string>? statusCallback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion) ||
            string.IsNullOrWhiteSpace(minecraftVersion))
        {
            return false;
        }

        try
        {
            // 候选安装器地址
            var candidates = loaderType switch
            {
                ModLoaderType.NeoForge => new[]
                {
                    $"https://bmclapi2.bangbang93.com/neoforge/version/{loaderVersion}/download/installer.jar",
                    $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar"
                },
                ModLoaderType.Forge => new[]
                {
                    $"https://maven.minecraftforge.net/net/minecraftforge/forge/{loaderVersion}/forge-{loaderVersion}-installer.jar"
                },
                _ => Array.Empty<string>()
            };

            var installer = new ModLoaderInstaller();
            foreach (var url in candidates)
            {
                try
                {
                    var loader = new ModLoaderVersion
                    {
                        Type = loaderType,
                        LoaderVersion = loaderVersion,
                        MetadataUrl = url,
                        RequiresInstallerExtraction = true
                    };
                    var progress = new Progress<MinecraftInstallProgress>(p =>
                        statusCallback?.Report($"[{p.StageName}] {p.Detail}"));
                    await installer.InstallAsync(
                            loader, versionId, root, minecraftVersion, progress, cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception candidateException)
                {
                    statusCallback?.Report($"安装器尝试 {url} 失败：{candidateException.Message}");
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            statusCallback?.Report($"解析 Loader 信息失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>判断缺失库是否为 NeoForge / Forge 的 SRG 重映射客户端（只能由安装器生成）。</summary>
    private static bool IsSrgClientJar(string path) =>
        path.Contains(Path.Combine("net", "minecraft", "client"), StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith("-srg.jar", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 校验库文件列表，返回缺失的库文件路径。
    /// 按当前系统与 feature 规则过滤（避免把其他平台的库误判为缺失），
    /// 优先使用 downloads.artifact.path，并检查 natives classifier（含旧版本 name 回退）。
    /// </summary>
    private static List<string> VerifyLibraries(string root, JsonElement libraries)
    {
        var missing = new List<string>();
        var librariesDir = Path.Combine(root, "libraries");
        var features = MinecraftRuleEvaluator.CreateDefaultFeatures(hasCustomResolution: true);

        foreach (var library in libraries.EnumerateArray())
        {
            // 只检查当前系统适用的库（与安装器/启动器使用同一套规则）
            if (!MinecraftRuleEvaluator.IsAllowed(library, features))
                continue;

            // 1. 主构件：优先 downloads.artifact.path，回退到 name 转 Maven 路径
            var artifactPath = GetArtifactRelativePath(library);
            if (artifactPath is not null)
            {
                var fullPath = Path.Combine(librariesDir, artifactPath);
                if (!File.Exists(fullPath))
                    missing.Add(fullPath);
            }

            // 2. natives classifier：downloads.classifiers 优先，旧版本用 name + classifier 回退
            var nativePath = GetNativeRelativePath(library);
            if (nativePath is not null)
            {
                var nativeFullPath = Path.Combine(librariesDir, nativePath);
                if (!File.Exists(nativeFullPath))
                    missing.Add(nativeFullPath);
            }
        }

        return missing;
    }

    /// <summary>
    /// 解析库的主构件相对路径；无 downloads.artifact 时按 Maven 坐标回退。
    /// </summary>
    private static string? GetArtifactRelativePath(JsonElement library)
    {
        if (library.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("artifact", out var artifact) &&
            artifact.TryGetProperty("path", out var pathElement))
        {
            var path = pathElement.GetString();
            if (!string.IsNullOrWhiteSpace(path))
                return path.Replace('/', Path.DirectorySeparatorChar);
        }

        return library.TryGetProperty("name", out var nameElement)
            ? MavenCoordinateToPath(nameElement.GetString())
            : null;
    }

    /// <summary>
    /// 解析 natives classifier 的相对路径；旧版本（无 downloads）用 name + classifier 拼路径。
    /// </summary>
    private static string? GetNativeRelativePath(JsonElement library)
    {
        if (!library.TryGetProperty("natives", out var natives) ||
            !natives.TryGetProperty(
                MinecraftRuleEvaluator.GetOperatingSystemName(),
                out var classifierElement) ||
            classifierElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var architecture = Environment.Is64BitOperatingSystem ? "64" : "32";
        var classifier = classifierElement.GetString()!
            .Replace("${arch}", architecture, StringComparison.Ordinal);

        if (library.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("classifiers", out var classifiers) &&
            classifiers.TryGetProperty(classifier, out var nativeArtifact) &&
            nativeArtifact.TryGetProperty("path", out var pathElement))
        {
            var path = pathElement.GetString();
            if (!string.IsNullOrWhiteSpace(path))
                return path.Replace('/', Path.DirectorySeparatorChar);
        }

        // 旧版本（1.7.x 及更早）无 downloads 字段：org.lwjgl:lwjgl-platform:2.9.4 + natives-windows
        // → org/lwjgl/lwjgl-platform/2.9.4/lwjgl-platform-2.9.4-natives-windows.jar
        if (library.TryGetProperty("name", out var nameElement))
        {
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var parts = name.Split(':');
            if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
                return null;
            return Path.Combine(
                parts[0].Replace('.', Path.DirectorySeparatorChar),
                parts[1],
                parts[2],
                $"{parts[1]}-{parts[2]}-{classifier}.jar");
        }

        return null;
    }

    /// <summary>
    /// 将 Maven 坐标转换为文件系统相对路径（支持 @extension 后缀）。
    /// </summary>
    private static string? MavenCoordinateToPath(string? coordinate)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
            return null;

        var extension = "jar";
        var name = coordinate;
        var extensionSeparator = coordinate.IndexOf('@');
        if (extensionSeparator >= 0)
        {
            extension = coordinate[(extensionSeparator + 1)..];
            name = coordinate[..extensionSeparator];
        }

        var parts = name.Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
            return null;
        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var classifier = parts.Length == 4 ? $"-{parts[3]}" : string.Empty;
        return Path.Combine(
            groupPath,
            parts[1],
            parts[2],
            $"{parts[1]}-{parts[2]}{classifier}.{extension}");
    }

    /// <summary>
    /// 异步获取版本的元数据 URL（用于重新下载）。
    /// 对于已知版本类型，返回 Mojang 官方 URL。
    /// </summary>
    private static async Task<string?> GetMetadataUrlAsync(
        string versionId,
        CancellationToken cancellationToken)
    {
        // 从 Mojang 版本清单查找（异步，避免阻塞 UI 线程）
        try
        {
            var versions = await ManifestGet.GetVersionsAsync()
                .ConfigureAwait(false);
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
