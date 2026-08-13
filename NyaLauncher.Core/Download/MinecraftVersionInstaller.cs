using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NyaLauncher.Core.Launch.Internal;

namespace NyaLauncher.Core.Download;

public sealed record MinecraftInstallProgress(
    int StageIndex,
    string StageName,
    string Detail,
    long CompletedBytes,
    long TotalBytes,
    int CompletedFiles,
    int TotalFiles,
    double BytesPerSecond)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(CompletedBytes * 100d / TotalBytes, 0, 100);
}

/// <summary>
/// Installs an official vanilla Minecraft version from Mojang metadata.
/// Existing files with matching SHA-1 values are reused; downloads are written
/// to temporary files and moved into place only after verification.
/// </summary>
public sealed class MinecraftVersionInstaller
{
    public const int StageCount = 7;
    private const int BufferSize = 128 * 1024;
    private const int MaximumParallelDownloads = 8;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(120);
    private static readonly long ProgressIntervalStopwatchTicks =
        (long)(Stopwatch.Frequency * ProgressInterval.TotalSeconds);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task InstallAsync(
        string versionId,
        string metadataUrl,
        string minecraftDirectory,
        IProgress<MinecraftInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(minecraftDirectory));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "versions"));

        Report(progress, 1, "获取版本元数据", $"正在读取 Minecraft {versionId} 的版本描述", 0, 0, 0, 0, 0);
        var metadataBytes = await DownloadBytesAsync(metadataUrl, cancellationToken)
            .ConfigureAwait(false);
        using var metadata = JsonDocument.Parse(metadataBytes);

        Report(progress, 2, "分析下载清单", "正在整理客户端、依赖库与资源索引", 0, 0, 0, 0, 0);
        var versionDirectory = Path.Combine(root, "versions", versionId);
        Directory.CreateDirectory(versionDirectory);
        var clientFiles = CreateClientPlan(metadata.RootElement, versionId, versionDirectory);
        var libraryFiles = CreateLibraryPlan(metadata.RootElement, root);
        var assetIndexFile = CreateAssetIndexPlan(metadata.RootElement, root);

        var completedBytes = 0L;
        var networkBytes = 0L;
        var completedFiles = 0;
        var totalBytes = clientFiles.Concat(libraryFiles)
            .Append(assetIndexFile)
            .Sum(file => Math.Max(0, file.Size));
        var totalFiles = clientFiles.Count + libraryFiles.Count + 1;
        var stopwatch = Stopwatch.StartNew();

        await DownloadStageAsync(
                3,
                "下载游戏客户端",
                clientFiles,
                progress,
                () => Volatile.Read(ref completedBytes),
                value => Interlocked.Add(ref completedBytes, value),
                () => Volatile.Read(ref networkBytes),
                value => Interlocked.Add(ref networkBytes, value),
                () => Volatile.Read(ref completedFiles),
                () => Interlocked.Increment(ref completedFiles),
                () => Volatile.Read(ref totalBytes),
                () => Volatile.Read(ref totalFiles),
                stopwatch,
                cancellationToken)
            .ConfigureAwait(false);

        await DownloadStageAsync(
                4,
                "下载依赖库",
                libraryFiles,
                progress,
                () => Volatile.Read(ref completedBytes),
                value => Interlocked.Add(ref completedBytes, value),
                () => Volatile.Read(ref networkBytes),
                value => Interlocked.Add(ref networkBytes, value),
                () => Volatile.Read(ref completedFiles),
                () => Interlocked.Increment(ref completedFiles),
                () => Volatile.Read(ref totalBytes),
                () => Volatile.Read(ref totalFiles),
                stopwatch,
                cancellationToken)
            .ConfigureAwait(false);

        await DownloadStageAsync(
                5,
                "下载资源索引",
                [assetIndexFile],
                progress,
                () => Volatile.Read(ref completedBytes),
                value => Interlocked.Add(ref completedBytes, value),
                () => Volatile.Read(ref networkBytes),
                value => Interlocked.Add(ref networkBytes, value),
                () => Volatile.Read(ref completedFiles),
                () => Interlocked.Increment(ref completedFiles),
                () => Volatile.Read(ref totalBytes),
                () => Volatile.Read(ref totalFiles),
                stopwatch,
                cancellationToken)
            .ConfigureAwait(false);

        var assetFiles = await CreateAssetPlanAsync(assetIndexFile.TargetPath, root, cancellationToken)
            .ConfigureAwait(false);
        Interlocked.Add(ref totalBytes, assetFiles.Sum(file => Math.Max(0, file.Size)));
        Interlocked.Add(ref totalFiles, assetFiles.Count);
        await DownloadStageAsync(
                6,
                "下载游戏资源",
                assetFiles,
                progress,
                () => Volatile.Read(ref completedBytes),
                value => Interlocked.Add(ref completedBytes, value),
                () => Volatile.Read(ref networkBytes),
                value => Interlocked.Add(ref networkBytes, value),
                () => Volatile.Read(ref completedFiles),
                () => Interlocked.Increment(ref completedFiles),
                () => Volatile.Read(ref totalBytes),
                () => Volatile.Read(ref totalFiles),
                stopwatch,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        Report(
            progress,
            7,
            "完成校验与安装",
            "正在写入版本描述并完成安装",
            completedBytes,
            totalBytes,
            completedFiles,
            totalFiles,
            CalculateSpeed(networkBytes, stopwatch));
        var versionJsonPath = Path.Combine(versionDirectory, $"{versionId}.json");
        await WriteAllBytesAtomicallyAsync(versionJsonPath, metadataBytes, cancellationToken)
            .ConfigureAwait(false);
        Report(
            progress,
            7,
            "完成校验与安装",
            $"Minecraft {versionId} 已安装完成",
            totalBytes,
            totalBytes,
            totalFiles,
            totalFiles,
            CalculateSpeed(networkBytes, stopwatch));
    }

    private static IReadOnlyList<DownloadFile> CreateClientPlan(
        JsonElement root,
        string versionId,
        string versionDirectory)
    {
        if (!root.TryGetProperty("downloads", out var downloads) ||
            !downloads.TryGetProperty("client", out var client))
        {
            throw new InvalidDataException("版本元数据缺少客户端下载信息。");
        }

        return
        [
            CreateDownloadFile(
                client,
                Path.Combine(versionDirectory, $"{versionId}.jar"),
                "客户端文件")
        ];
    }

    private static IReadOnlyList<DownloadFile> CreateLibraryPlan(JsonElement root, string minecraftDirectory)
    {
        if (!root.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new Dictionary<string, DownloadFile>(StringComparer.OrdinalIgnoreCase);
        var features = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["has_custom_resolution"] = true,
            ["is_demo_user"] = false,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };
        foreach (var library in libraries.EnumerateArray())
        {
            if (!MinecraftRuleEvaluator.IsAllowed(library, features))
                continue;

            var artifactAdded = false;
            if (library.TryGetProperty("downloads", out var downloads))
            {
                if (downloads.TryGetProperty("artifact", out var artifact))
                {
                    AddLibraryDownload(result, artifact, minecraftDirectory, "依赖库");
                    artifactAdded = true;
                }
                if (downloads.TryGetProperty("classifiers", out var classifiers) &&
                    classifiers.ValueKind == JsonValueKind.Object &&
                    library.TryGetProperty("natives", out var natives) &&
                    natives.TryGetProperty(
                        MinecraftRuleEvaluator.GetOperatingSystemName(),
                        out var nativeClassifierElement) &&
                    nativeClassifierElement.ValueKind == JsonValueKind.String)
                {
                    var architecture = Environment.Is64BitOperatingSystem ? "64" : "32";
                    var nativeClassifier = nativeClassifierElement.GetString()!
                        .Replace("${arch}", architecture, StringComparison.Ordinal);
                    if (classifiers.TryGetProperty(nativeClassifier, out var nativeArtifact))
                    {
                        AddLibraryDownload(
                            result,
                            nativeArtifact,
                            minecraftDirectory,
                            "原生依赖库");
                    }
                }

                if (artifactAdded)
                    continue;
            }

            if (!library.TryGetProperty("name", out var nameElement))
                continue;
            var relativePath = CreateMavenPath(nameElement.GetString());
            if (relativePath is null)
                continue;
            var baseUrl = library.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : "https://libraries.minecraft.net/";
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "https://libraries.minecraft.net/";
            var url = $"{baseUrl.TrimEnd('/')}/{relativePath.Replace('\\', '/')}";
            var target = ResolveRelativePath(
                Path.Combine(minecraftDirectory, "libraries"),
                relativePath);
            result[target] = new DownloadFile(url, target, null, 0, "依赖库");
        }

        return result.Values.ToArray();
    }

    private static DownloadFile CreateAssetIndexPlan(JsonElement root, string minecraftDirectory)
    {
        if (!root.TryGetProperty("assetIndex", out var assetIndex))
            throw new InvalidDataException("版本元数据缺少资源索引。");
        var id = assetIndex.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : root.TryGetProperty("assets", out var assetsElement)
                ? assetsElement.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("资源索引缺少 ID。");

        return CreateDownloadFile(
            assetIndex,
            Path.Combine(minecraftDirectory, "assets", "indexes", $"{id}.json"),
            "资源索引");
    }

    private static async Task<IReadOnlyList<DownloadFile>> CreateAssetPlanAsync(
        string indexPath,
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(indexPath);
        using var index = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!index.RootElement.TryGetProperty("objects", out var objects) ||
            objects.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<string, DownloadFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in objects.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!asset.Value.TryGetProperty("hash", out var hashElement))
                continue;
            var hash = hashElement.GetString();
            if (!IsSha1(hash))
                continue;
            var size = asset.Value.TryGetProperty("size", out var sizeElement) &&
                       sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;
            var relativePath = Path.Combine(hash![..2], hash);
            var target = ResolveRelativePath(
                Path.Combine(minecraftDirectory, "assets", "objects"),
                relativePath);
            result[target] = new DownloadFile(
                $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                target,
                hash,
                size,
                asset.Name);
        }

        return result.Values.ToArray();
    }

    private static async Task DownloadStageAsync(
        int stageIndex,
        string stageName,
        IReadOnlyList<DownloadFile> files,
        IProgress<MinecraftInstallProgress>? progress,
        Func<long> getCompletedBytes,
        Action<long> addCompletedBytes,
        Func<long> getNetworkBytes,
        Action<long> addNetworkBytes,
        Func<int> getCompletedFiles,
        Action incrementCompletedFiles,
        Func<long> getTotalBytes,
        Func<int> getTotalFiles,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            Report(
                progress,
                stageIndex,
                stageName,
                "此阶段没有需要下载的文件",
                getCompletedBytes(),
                getTotalBytes(),
                getCompletedFiles(),
                getTotalFiles(),
                CalculateSpeed(getNetworkBytes(), stopwatch));
            return;
        }

        using var semaphore = new SemaphoreSlim(MaximumParallelDownloads);
        var lastReportTicks = 0L;
        var currentFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        async Task DownloadOneAsync(DownloadFile file)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                currentFiles.TryAdd(file.DisplayName, 0);
                var reusedBytes = await DownloadFileAsync(
                        file,
                        value =>
                        {
                            addCompletedBytes(value);
                            addNetworkBytes(value);
                        },
                        () =>
                        {
                            var now = stopwatch.ElapsedTicks;
                            var previous = Interlocked.Read(ref lastReportTicks);
                            if (now - previous < ProgressIntervalStopwatchTicks ||
                                Interlocked.CompareExchange(ref lastReportTicks, now, previous) != previous)
                            {
                                return;
                            }

                            Report(
                                progress,
                                stageIndex,
                                stageName,
                                $"正在处理 {currentFiles.Count} 个文件 · {file.DisplayName}",
                                getCompletedBytes(),
                                getTotalBytes(),
                                getCompletedFiles(),
                                getTotalFiles(),
                                CalculateSpeed(getNetworkBytes(), stopwatch));
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (reusedBytes > 0)
                    addCompletedBytes(reusedBytes);
                incrementCompletedFiles();
            }
            finally
            {
                currentFiles.TryRemove(file.DisplayName, out _);
                semaphore.Release();
            }
        }

        await Task.WhenAll(files.Select(DownloadOneAsync)).ConfigureAwait(false);
        Report(
            progress,
            stageIndex,
            stageName,
            $"{stageName}完成",
            getCompletedBytes(),
            getTotalBytes(),
            getCompletedFiles(),
            getTotalFiles(),
            CalculateSpeed(getNetworkBytes(), stopwatch));
    }

    private static async Task<long> DownloadFileAsync(
        DownloadFile file,
        Action<long> addCompletedBytes,
        Action reportProgress,
        CancellationToken cancellationToken)
    {
        if (await IsExistingFileValidAsync(file, cancellationToken).ConfigureAwait(false))
            return file.Size > 0 ? file.Size : new FileInfo(file.TargetPath).Length;

        var directory = Path.GetDirectoryName(file.TargetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var temporaryPath = $"{file.TargetPath}.nya-download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ValidateHttpsUrl(file.Url));
            using var response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             useAsync: true))
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    addCompletedBytes(read);
                    reportProgress();
                }
            }

            if (!await MatchesSha1Async(temporaryPath, file.Sha1, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidDataException($"下载文件校验失败：{file.DisplayName}");
            }

            File.Move(temporaryPath, file.TargetPath, overwrite: true);
            return 0;
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private static async Task<bool> IsExistingFileValidAsync(
        DownloadFile file,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(file.TargetPath))
            return false;
        if (file.Size > 0 && new FileInfo(file.TargetPath).Length != file.Size)
            return false;
        return await MatchesSha1Async(file.TargetPath, file.Sha1, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> MatchesSha1Async(
        string path,
        string? expectedSha1,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1))
            return true;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexStringLower(hash),
            expectedSha1,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> DownloadBytesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ValidateHttpsUrl(url));
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAllBytesAtomicallyAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.nya-download";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private static DownloadFile CreateDownloadFile(
        JsonElement element,
        string targetPath,
        string displayName)
    {
        var url = element.TryGetProperty("url", out var urlElement)
            ? urlElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidDataException($"{displayName}缺少下载地址。");
        var sha1 = element.TryGetProperty("sha1", out var sha1Element)
            ? sha1Element.GetString()
            : null;
        var size = element.TryGetProperty("size", out var sizeElement) &&
                   sizeElement.TryGetInt64(out var parsedSize)
            ? parsedSize
            : 0;
        return new DownloadFile(url, targetPath, sha1, size, displayName);
    }

    private static void AddLibraryDownload(
        IDictionary<string, DownloadFile> result,
        JsonElement element,
        string minecraftDirectory,
        string displayName)
    {
        if (!element.TryGetProperty("path", out var pathElement))
            return;
        var relativePath = pathElement.GetString();
        if (string.IsNullOrWhiteSpace(relativePath))
            return;
        var target = ResolveRelativePath(
            Path.Combine(minecraftDirectory, "libraries"),
            relativePath);
        result[target] = CreateDownloadFile(element, target, Path.GetFileName(target) ?? displayName);
    }

    private static string? CreateMavenPath(string? coordinate)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
            return null;
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

    private static string ResolveRelativePath(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison))
        {
            throw new InvalidDataException($"下载路径超出 Minecraft 目录：{relativePath}");
        }

        return target;
    }

    private static Uri ValidateHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"下载地址不是有效的 HTTPS URL：{url}");
        }

        return uri;
    }

    private static bool IsSha1(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static double CalculateSpeed(long completedBytes, Stopwatch stopwatch) =>
        stopwatch.Elapsed.TotalSeconds <= 0
            ? 0
            : completedBytes / stopwatch.Elapsed.TotalSeconds;

    private static void Report(
        IProgress<MinecraftInstallProgress>? progress,
        int stageIndex,
        string stageName,
        string detail,
        long completedBytes,
        long totalBytes,
        int completedFiles,
        int totalFiles,
        double speed) =>
        progress?.Report(new MinecraftInstallProgress(
            stageIndex,
            stageName,
            detail,
            completedBytes,
            totalBytes,
            completedFiles,
            totalFiles,
            speed));

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A stale temporary file can be overwritten by the next repair.
        }
    }

    private sealed record DownloadFile(
        string Url,
        string TargetPath,
        string? Sha1,
        long Size,
        string DisplayName);
}
