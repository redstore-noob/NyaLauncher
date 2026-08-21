using System.IO.Compression;

namespace NyaLauncher.Core.Download;

/// <summary>
/// Mod Loader 安装器。在原版 Minecraft 已安装的基础上，叠加安装指定的 Mod Loader。
/// 核心原理：Loader 的版本 JSON 通过 <c>inheritsFrom</c> 继承原版，
/// 现有 <see cref="MinecraftVersionInstaller"/> 可直接处理此类 JSON。
/// </summary>
public sealed class ModLoaderInstaller
{
    private readonly MinecraftVersionInstaller _baseInstaller = new();

    /// <summary>
    /// 安装指定 Mod Loader 到 Minecraft 目录。
    /// </summary>
    /// <param name="loader">要安装的 Loader 版本信息（含元数据 URL）。</param>
    /// <param name="instanceName">
    /// 实例名称，用作 <c>versions/</c> 下的文件夹名。
    /// 如 "fabric-loader-0.16.14-1.21.8" 或用户自定义名称。
    /// </param>
    /// <param name="minecraftDirectory">Minecraft 根目录。</param>
    /// <param name="minecraftVersion">原版 Minecraft 版本号，用于确保原版已安装。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task InstallAsync(
        ModLoaderVersion loader,
        string instanceName,
        string minecraftDirectory,
        string minecraftVersion,
        IProgress<MinecraftInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(minecraftDirectory));

        // 1. 确保原版 Minecraft 已安装（Loader 的 inheritsFrom 需要原版文件）
        await EnsureVanillaInstalledAsync(root, minecraftVersion, cancellationToken)
            .ConfigureAwait(false);

        if (loader.RequiresInstallerExtraction)
        {
            // NeoForge / Forge：需要从安装器 JAR 中提取版本 JSON
            await InstallFromInstallerJarAsync(loader, instanceName, root, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Fabric：版本 JSON 可直接从 API 获取
            await _baseInstaller.InstallAsync(
                    instanceName,
                    loader.MetadataUrl,
                    root,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 生成默认的实例名称。
    /// </summary>
    public static string CreateDefaultInstanceName(
        ModLoaderType type,
        string loaderVersion,
        string minecraftVersion) => type switch
    {
        ModLoaderType.Fabric => $"fabric-loader-{loaderVersion}-{minecraftVersion}",
        ModLoaderType.NeoForge => $"neoforge-{loaderVersion}-{minecraftVersion}",
        ModLoaderType.Forge => $"forge-{loaderVersion}",
        _ => minecraftVersion
    };

    /// <summary>
    /// 检查原版 Minecraft 是否已安装；未安装时使用 Mojang 官方源下载。
    /// 作为依赖安装时静默执行，不报告进度。
    /// </summary>
    private async Task EnsureVanillaInstalledAsync(
        string root,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var versionJsonPath = Path.Combine(
            root, "versions", minecraftVersion, $"{minecraftVersion}.json");

        if (File.Exists(versionJsonPath))
            return;

        // 原版未安装，从 Mojang 版本清单获取元数据 URL
        var versions = await ManifestGet.GetVersionsAsync()
            .ConfigureAwait(false);
        var vanilla = versions.FirstOrDefault(v =>
            string.Equals(v.Id, minecraftVersion, StringComparison.OrdinalIgnoreCase));

        if (vanilla is null || string.IsNullOrWhiteSpace(vanilla.Url))
            throw new InvalidOperationException(
                $"无法从 Mojang 版本清单中找到 Minecraft {minecraftVersion}。");

        // 作为依赖静默下载，不向用户报告进度
        await _baseInstaller.InstallAsync(
                minecraftVersion,
                vanilla.Url,
                root,
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 从安装器 JAR 中提取版本 JSON 并完成安装。
    /// NeoForge / Forge 的安装器 JAR 内含 version.json（或 install_profile.json），
    /// 提取后写入 versions/{instanceName}/ 并下载其中声明的库文件。
    /// </summary>
    private async Task InstallFromInstallerJarAsync(
        ModLoaderVersion loader,
        string instanceName,
        string root,
        IProgress<MinecraftInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 1. 下载安装器 JAR 到临时文件
        var tempJar = Path.Combine(Path.GetTempPath(), $"nyalauncher-installer-{Guid.NewGuid():N}.jar");
        try
        {
            var jarBytes = await DownloadSourceProvider.GetBytesAsync(
                    loader.MetadataUrl, TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(tempJar, jarBytes, cancellationToken)
                .ConfigureAwait(false);

            // 2. 从 JAR 的 maven/ 目录提取所有库文件到 libraries/
            ExtractLibrariesFromJar(tempJar, root);

            // 3. 从 JAR 中提取版本 JSON
            var versionJson = ExtractVersionJsonFromJar(tempJar);
            if (string.IsNullOrWhiteSpace(versionJson))
                throw new InvalidOperationException(
                    $"无法从安装器 JAR 中提取版本 JSON：{loader.DisplayName}");

            // 4. 将版本 JSON 转为字节，交给 installer 完成剩余下载和写入
            var metadataBytes = System.Text.Encoding.UTF8.GetBytes(versionJson);
            await _baseInstaller.InstallFromMetadataBytesAsync(
                    instanceName,
                    root,
                    metadataBytes,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tempJar); } catch { /* 清理临时文件 */ }
        }
    }

    /// <summary>
    /// 从安装器 JAR 的 maven/ 目录提取所有库文件到 Minecraft 的 libraries/ 目录。
    /// NeoForge/Forge 安装器 JAR 内含完整的 Maven 仓库结构，提取后可直接使用。
    /// </summary>
    private static void ExtractLibrariesFromJar(string jarPath, string minecraftRoot)
    {
        var librariesDir = Path.Combine(minecraftRoot, "libraries");

        using var archive = ZipFile.OpenRead(jarPath);
        foreach (var entry in archive.Entries)
        {
            // 只处理 maven/ 目录下的 .jar 文件（跳过校验文件和目录）
            if (!entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry.Length == 0)
                continue;

            // maven/net/neoforged/neoforge/21.8.54/neoforge-21.8.54-universal.jar
            // → libraries/net/neoforged/neoforge/21.8.54/neoforge-21.8.54-universal.jar
            var relativePath = entry.FullName["maven/".Length..];
            var targetPath = Path.Combine(librariesDir, relativePath);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
                Directory.CreateDirectory(targetDir);

            // 如果目标文件已存在且大小一致，跳过
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length == entry.Length)
                continue;

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// 从安装器 JAR（ZIP 格式）中提取版本 JSON 字符串。
    /// 优先查找 version.json，其次查找 install_profile.json 中的 versionInfo 字段。
    /// </summary>
    private static string? ExtractVersionJsonFromJar(string jarPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(jarPath);

        // 优先查找 version.json（NeoForge 新版本、Forge 新版本均有此文件）
        var versionEntry = archive.GetEntry("version.json");
        if (versionEntry is not null)
        {
            using var stream = versionEntry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // 回退：从 install_profile.json 的 versionInfo 字段提取
        var profileEntry = archive.GetEntry("install_profile.json");
        if (profileEntry is not null)
        {
            using var stream = profileEntry.Open();
            using var reader = new StreamReader(stream);
            var profileJson = reader.ReadToEnd();

            using var doc = System.Text.Json.JsonDocument.Parse(profileJson);
            if (doc.RootElement.TryGetProperty("versionInfo", out var versionInfo))
            {
                return versionInfo.GetRawText();
            }
        }

        return null;
    }
}
