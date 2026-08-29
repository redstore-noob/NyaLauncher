using System.IO.Compression;
using System.Text.Json;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Core.Download;

/// <summary>
/// 内容安装服务：把 Mod / 资源包 / 光影包 / 整合包下载到指定实例的内容目录，
/// 或按自定义路径保存。整合包支持解压 .mrpack 并解析依赖。
/// </summary>
public static class ContentInstallService
{
    /// <summary>
    /// 下载文件到实例内容目录的指定子目录（如 mods / resourcepacks / shaderpacks）。
    /// </summary>
    /// <returns>最终保存的文件路径。</returns>
    public static async Task<string> DownloadFileToInstanceAsync(
        string downloadUrl,
        string fileName,
        string contentDirectory,
        string subDirectory,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);

        var targetDir = Path.Combine(contentDirectory, subDirectory);
        var targetPath = Path.Combine(targetDir, SanitizeFileName(fileName));
        await ModDownloadService.DownloadAsync(downloadUrl, targetPath, progress, cancellationToken)
            .ConfigureAwait(false);
        return targetPath;
    }

    /// <summary>
    /// 解析已安装实例的内容目录（mods / resourcepacks 等的父目录）。
    /// 与启动时的隔离判定完全一致（全局默认隔离 + 版本自身设置），
    /// 避免安装内容落点与游戏运行时目录不一致（例如默认隔离下 mods 被装进共享根目录）。
    /// </summary>
    public static string ResolveContentDirectory(
        string minecraftDirectory,
        string? sourcePath,
        string versionId)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory))
            return string.Empty;
        try
        {
            // GameVersionIsolation.Resolve 只依赖 SourcePath 与 MinecraftDirectory，
            // 其余快照字段对本判定无影响，构造最小快照即可完全复用启动侧逻辑。
            var snapshot = new GameInstanceSnapshot(
                sourcePath ?? string.Empty,
                minecraftDirectory,
                null,
                [],
                null,
                false,
                false,
                null);
            return GameVersionIsolation.Resolve(snapshot, versionId).ContentDirectory;
        }
        catch
        {
            return minecraftDirectory;
        }
    }

    /// <summary>
    /// 安装整合包到实例内容目录：
    /// 1) 解压包内所有文件（mods / config / saves / options.txt 等）到内容目录；
    /// 2) 解析 index，下载未包含在包内但声明了下载地址的 mods。
    /// 支持两种格式：Modrinth 的 <c>.mrpack</c>（modrinth.index.json / index.json，
    /// 声明文件带下载地址）与 CurseForge 的 <c>.zip</c>（manifest.json，mods 直接内嵌在包内，
    /// 声明文件为 projectID/fileID 引用、无下载地址，自动跳过）。
    /// </summary>
    /// <returns>安装统计：解压文件数、下载的依赖 mod 数、错误列表。</returns>
    public static async Task<(int InstalledFiles, int DownloadedMods, List<string> Errors)>
        InstallModpackAsync(
            string mrpackPath,
            string contentDirectory,
            IProgress<(long downloaded, long total)>? progress = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mrpackPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);

        var errors = new List<string>();
        var installedFiles = 0;
        var downloadedMods = 0;

        Directory.CreateDirectory(contentDirectory);

        // 1) 解析索引：mrpack 用 modrinth.index.json（v1 标准）/ 旧版 index.json；
        //    CurseForge .zip 用 manifest.json（其 files 为 projectID/fileID 引用，无下载地址，
        //    下方下载循环会因缺 downloadUrl 自动跳过，仅做包内解压）。
        ModpackIndex? index = null;
        var inArchive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var archive = ZipFile.OpenRead(mrpackPath))
        {
            foreach (var entry in archive.Entries)
                inArchive.Add(entry.FullName.Replace('\\', '/'));

            var indexEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "modrinth.index.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.FullName, "index.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase));
            if (indexEntry is not null)
            {
                try
                {
                    using var reader = new StreamReader(indexEntry.Open());
                    index = JsonSerializer.Deserialize<ModpackIndex>(
                        await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    errors.Add($"解析 index.json 失败：{ex.Message}");
                }
            }

            // 2) 解压包内文件（处理 overrides/ 前缀剥离；跳过 index 文件）
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                    continue;
                if (indexEntry is not null &&
                    string.Equals(entry.FullName, indexEntry.FullName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    // overrides/ 前缀剥离：overrides/config/... -> config/...（mrpack 规范）
                    var relative = entry.FullName.Replace('\\', '/');
                    if (relative.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                        relative = relative["overrides/".Length..];

                    var destination = SafeCombine(contentDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                    installedFiles++;
                }
                catch (Exception ex)
                {
                    errors.Add($"解压 {entry.FullName} 失败：{ex.Message}");
                }
            }
        }

        // 3) 下载 index 中声明但不在包内的文件（mods / resourcepacks / shaderpacks 等）
        if (index?.Files is { Count: > 0 })
        {
            foreach (var file in index.Files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                if (string.IsNullOrWhiteSpace(file.Path))
                    continue;
                if (inArchive.Contains(file.Path.Replace('\\', '/')))
                    continue; // 文件已包含在包内，解压步骤已处理

                var downloadUrl = file.Downloads?.FirstOrDefault(u =>
                    !string.IsNullOrWhiteSpace(u) && Uri.IsWellFormedUriString(u, UriKind.Absolute));
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    continue;

                try
                {
                    // file.Path 已是相对实例根的路径（如 mods/foo.jar），直接拼到内容目录
                    var targetPath = SafeCombine(contentDirectory, file.Path.Replace('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await ModDownloadService.DownloadAsync(
                            downloadUrl, targetPath, progress, cancellationToken)
                        .ConfigureAwait(false);
                    downloadedMods++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"下载依赖 {file.Path} 失败：{ex.Message}");
                }
            }
        }

        return (installedFiles, downloadedMods, errors);
    }

    /// <summary>
    /// 下载整合包文件（.mrpack）到自定义路径或实例目录。
    /// 用于"自定义保存路径"场景：仅保存文件，不做解压。
    /// </summary>
    public static async Task DownloadModpackFileAsync(
        string downloadUrl,
        string fileName,
        string targetPath,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ModDownloadService.DownloadAsync(downloadUrl, targetPath, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>安全拼接：确保解压目标位于内容目录内，阻止路径穿越。</summary>
    private static string SafeCombine(string root, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"非法的整合包内路径：{relativePath}");

        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var combined = Path.GetFullPath(Path.Combine(rootFull, normalized));
        if (!combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"整合包路径越界：{relativePath}");

        return combined;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim().Trim('"'));
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }

    // ------------------------------------------------------------------
    // 整合包依赖（index.json -> dependencies）
    // ------------------------------------------------------------------

    /// <summary>
    /// 整合包声明的运行要求，解析自 mrpack 的 index.json dependencies。
    /// 例如 Fabulously Optimized：{ "fabric-loader": "0.19.3", "minecraft": "26.2" }。
    /// </summary>
    public sealed class ModpackRequirements
    {
        /// <summary>要求的 Minecraft 版本，如 "1.21.8"。</summary>
        public string? MinecraftVersion { get; set; }

        /// <summary>
        /// 加载器类型。无加载器键时为 Vanilla；遇到 quilt 等本启动器不支持的
        /// 加载器时保持 Vanilla，并通过 <see cref="RawLoaderKey"/> 暴露原始键以便上层告警。
        /// </summary>
        public ModLoaderType LoaderType { get; set; } = ModLoaderType.Vanilla;

        /// <summary>加载器版本，如 "0.19.3"。</summary>
        public string? LoaderVersion { get; set; }

        /// <summary>原始加载器依赖键（如 "fabric-loader" / "quilt-loader"），用于不支持时告警。</summary>
        public string? RawLoaderKey { get; set; }

        /// <summary>本启动器是否能安装该加载器（Fabric / NeoForge / Forge 支持）。</summary>
        public bool LoaderSupported =>
            LoaderType is ModLoaderType.Fabric or ModLoaderType.NeoForge or ModLoaderType.Forge;
    }

    /// <summary>
    /// 解析 mrpack 的 index.json dependencies，得到整合包要求的游戏版本与加载器。
    /// 解析失败（无索引 / 无 minecraft 依赖 / JSON 损坏）时返回 null。
    /// </summary>
    public static async Task<ModpackRequirements?> ReadRequirementsAsync(
        string mrpackPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mrpackPath);

        try
        {
            using var archive = ZipFile.OpenRead(mrpackPath);
            var indexEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "modrinth.index.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.FullName, "index.json", StringComparison.OrdinalIgnoreCase));
            if (indexEntry is null)
                return null;

            using var reader = new StreamReader(indexEntry.Open());
            var index = JsonSerializer.Deserialize<ModpackIndex>(
                await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
            if (index?.Dependencies is null)
                return null;

            return FromDependencies(index.Dependencies);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ModpackRequirements? FromDependencies(Dictionary<string, string> deps)
    {
        var mc = deps.TryGetValue("minecraft", out var m) ? m : null;
        if (string.IsNullOrWhiteSpace(mc))
            return null;

        var req = new ModpackRequirements { MinecraftVersion = mc };

        // 取第一个非 minecraft 的依赖键作为加载器（mrpack 规范最多一个加载器）
        foreach (var kv in deps)
        {
            if (string.Equals(kv.Key, "minecraft", StringComparison.OrdinalIgnoreCase))
                continue;
            req.RawLoaderKey = kv.Key;
            var type = MapLoaderKey(kv.Key);
            if (type is not null)
            {
                req.LoaderType = type.Value;
                req.LoaderVersion = kv.Value;
            }
            // type 为 null（如 quilt-loader）：保持 Vanilla，由上层据 RawLoaderKey 告警
            break;
        }

        return req;
    }

    private static ModLoaderType? MapLoaderKey(string key)
    {
        return key.Trim().ToLowerInvariant() switch
        {
            "fabric-loader" => ModLoaderType.Fabric,
            "neoforge" => ModLoaderType.NeoForge,
            "forge" => ModLoaderType.Forge,
            _ => null
        };
    }

    // ------------------------------------------------------------------
    // mrpack index.json 模型
    // ------------------------------------------------------------------

    private sealed class ModpackIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("files")]
        public List<ModpackFileEntry>? Files { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dependencies")]
        public Dictionary<string, string>? Dependencies { get; set; }
    }

    private sealed class ModpackFileEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("downloads")]
        public List<string>? Downloads { get; set; }
    }
}
