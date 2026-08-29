using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Core.Download;

/// <summary>
/// Java 运行时安装进度。
/// </summary>
public sealed record JavaRuntimeInstallProgress(
    string Phase,
    long CompletedBytes,
    long TotalBytes,
    double BytesPerSecond);

/// <summary>
/// Java JDK 供应商。
/// </summary>
public enum JavaVendor
{
    /// <summary>Azul Zulu（默认，官方构建、免费商用）。</summary>
    Zulu,

    /// <summary>Oracle JDK（官方直接下载仅提供 Java 21+）。</summary>
    Oracle,

    /// <summary>Eclipse Temurin（Adoptium，带 SHA-256 校验）。</summary>
    Temurin
}

/// <summary>
/// 已安装的 Java 运行时。
/// </summary>
public sealed record InstalledJavaRuntime(
    string DirectoryPath,
    string JavaExecutablePath,
    int? MajorVersion,
    JavaVendor? Vendor)
{
    public string DisplayName
    {
        get
        {
            var version = MajorVersion is int major ? $"JDK {major}" : "JDK";
            return Vendor is JavaVendor vendor ? $"{VendorDisplayName(vendor)} {version}" : $"Java {version}";
        }
    }

    /// <summary>供应商中文显示名。</summary>
    public static string VendorDisplayName(JavaVendor vendor) => vendor switch
    {
        JavaVendor.Zulu => "Zulu",
        JavaVendor.Oracle => "Oracle",
        JavaVendor.Temurin => "Temurin",
        _ => vendor.ToString()
    };
}

/// <summary>
/// 从供应商 API 实时查询到的 JDK 下载候选条目（供版本列表展示与下载）。
/// </summary>
public sealed record JavaDownloadCandidate(
    JavaVendor Vendor,
    int MajorVersion,
    string BuildVersion,
    string DownloadUrl,
    string? Sha256,
    long SizeBytes)
{
    public string VendorName => InstalledJavaRuntime.VendorDisplayName(Vendor);

    public string SizeDisplay => SizeBytes > 0 ? $"{SizeBytes / 1048576.0:0.0} MB" : "";

    /// <summary>列表主标题：如 "Zulu JDK 21"。</summary>
    public string DisplayName => $"{VendorName} JDK {MajorVersion}";

    /// <summary>列表副标题：实际构建版本号 + 大小，如 "21.0.12.1 · 184.2 MB"。</summary>
    public string DetailText
    {
        get
        {
            var build = string.IsNullOrWhiteSpace(BuildVersion) ? "latest" : BuildVersion;
            return SizeDisplay.Length > 0 ? $"{build} · {SizeDisplay}" : build;
        }
    }
}

/// <summary>
/// Java 运行时安装器。按平台自动选择安装包（Windows/macOS/Linux × x64/arm64），
/// 支持从 Azul Zulu、Oracle JDK、Eclipse Temurin 三个供应商实时查询并下载 JDK。
/// </summary>
public sealed class JavaRuntimeInstaller
{
    /// <summary>可自动下载的 Java 主版本（覆盖 Minecraft 全系版本需求）。</summary>
    public static readonly int[] SupportedVersions = [8, 11, 17, 21, 25];

    /// <summary>Oracle JDK 官方直接下载仅支持 Java 21+（11/17 的 latest 链接返回 404，8 需登录许可）。</summary>
    public const string OracleUnsupportedMessage =
        "Oracle JDK 官方直接下载仅提供 Java 21 及以上版本，请选择 21 / 25 或改用 Zulu / Temurin。";

    private const string ZuluMetadataApi =
        "https://api.azul.com/metadata/v1/zulu/packages/";

    private const string AdoptiumAssetsApi =
        "https://api.adoptium.net/v3/assets/latest/{0}/hotspot";

    private const string OracleDownloadBase =
        "https://download.oracle.com/java/{0}/latest/jdk-{0}_{1}_bin.{2}";

    private static readonly HttpClient Client = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NyaLauncher/1.0");
        return client;
    }

    /// <summary>
    /// 获取 Java 运行时根目录（与启动器启动时的搜索目录保持一致）。
    /// </summary>
    public static string GetRuntimeDirectory() =>
        Path.Combine(MinecraftDirectoryLocator.GetDefaultDirectory(), "runtime");

    /// <summary>
    /// 当前平台的说明文字（如 "Windows x64"、"macOS Apple Silicon"）。
    /// </summary>
    public static string GetPlatformDisplayName()
    {
        var os = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsMacOS() ? "macOS"
            : "Linux";
        var arch = IsArm64() ? (OperatingSystem.IsMacOS() ? "Apple Silicon" : "ARM64") : "x64";
        return $"{os} {arch}";
    }

    /// <summary>
    /// 扫描已安装的 Java 运行时（支持 vendor 命名目录与历史遗留目录）。
    /// </summary>
    public static IReadOnlyList<InstalledJavaRuntime> GetInstalledRuntimes()
    {
        var runtimeDirectory = GetRuntimeDirectory();
        if (!Directory.Exists(runtimeDirectory))
            return [];

        var result = new List<InstalledJavaRuntime>();
        foreach (var directory in Directory.EnumerateDirectories(runtimeDirectory))
        {
            var javaPath = FindJavaExecutable(directory);
            if (javaPath is null)
                continue;
            var version = TryGetJavaMajorVersion(javaPath);
            result.Add(new InstalledJavaRuntime(
                directory, javaPath, version, ResolveVendorFromDirectory(directory)));
        }

        return result
            .OrderByDescending(runtime => runtime.MajorVersion ?? 0)
            .ToArray();
    }

    /// <summary>
    /// 删除已安装的 Java 运行时目录。
    /// </summary>
    public static void DeleteRuntime(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;
        var fullPath = Path.GetFullPath(directoryPath);
        var runtimeRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetRuntimeDirectory()));
        if (!fullPath.StartsWith(runtimeRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("仅允许删除 runtime 目录内的 Java 运行时。");
        }

        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
    }

    // ------------------------------------------------------------------
    // 实时查询可用版本
    // ------------------------------------------------------------------

    /// <summary>
    /// 实时查询指定供应商在当前平台下所有可用 JDK 版本（含实际构建号与大小）。
    /// Oracle 无公开动态 API，返回其官方直接下载支持的两个版本。
    /// </summary>
    public static async Task<IReadOnlyList<JavaDownloadCandidate>> QueryAvailableVersionsAsync(
        JavaVendor vendor,
        CancellationToken cancellationToken = default)
    {
        var os = GetOperatingSystemKey();
        var arch = IsArm64() ? "aarch64" : "x64";

        if (vendor == JavaVendor.Oracle)
        {
            var oracleResults = new List<JavaDownloadCandidate>();
            foreach (var version in SupportedVersions.Where(v => v >= 21))
            {
                try
                {
                    oracleResults.Add(CreateOracleCandidate(version, os, arch));
                }
                catch
                {
                    // 某个版本不可用则跳过
                }
            }
            return oracleResults;
        }

        var tasks = SupportedVersions.Select(async version =>
        {
            try
            {
                return vendor == JavaVendor.Zulu
                    ? await QueryZuluCandidateAsync(version, os, arch, cancellationToken).ConfigureAwait(false)
                    : await QueryTemurinCandidateAsync(version, os, arch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"查询 Java {version} ({vendor}) 失败：{ex.Message}");
                return null;
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results
            .Where(candidate => candidate is not null)
            .Cast<JavaDownloadCandidate>()
            .OrderBy(candidate => candidate.MajorVersion)
            .ToArray();
    }

    /// <summary>Zulu：Azul Metadata API，返回最新 GA 构建。</summary>
    private static async Task<JavaDownloadCandidate?> QueryZuluCandidateAsync(
        int majorVersion,
        string os,
        string arch,
        CancellationToken cancellationToken)
    {
        var archiveType = os == "windows" ? "zip" : "tar.gz";
        var url = $"{ZuluMetadataApi}?java_version={majorVersion}" +
                  $"&os={os}&arch={arch}&archive_type={archiveType}" +
                  $"&java_package_type=jdk&latest=true&release_status=ga&page_size=1";

        var json = await Client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = document.RootElement[0];
        var downloadUrl = first.TryGetProperty("download_url", out var urlElement)
            ? urlElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return null;

        // 从文件名提取实际构建版本，如 "zulu21.52.203-ca-jdk21.0.12.1-win_x64.zip" → "21.0.12.1"
        var name = first.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : string.Empty;
        var buildVersion = ExtractJdkVersionFromName(name);
        // Zulu Metadata API 未提供 sha256，下载后靠 https + 解压验证兜底
        return new JavaDownloadCandidate(
            JavaVendor.Zulu, majorVersion, buildVersion, downloadUrl, null, 0);
    }

    /// <summary>Temurin：Adoptium API，带 SHA-256 校验。</summary>
    private static async Task<JavaDownloadCandidate?> QueryTemurinCandidateAsync(
        int majorVersion,
        string os,
        string arch,
        CancellationToken cancellationToken)
    {
        // Adoptium API 的 os 枚举是 linux/windows/mac（无 macos），需单独映射
        var adoptiumOs = os == "macos" ? "mac" : os;
        var url = string.Format(AdoptiumAssetsApi, majorVersion) +
                  $"?architecture={arch}&image_type=jdk&os={adoptiumOs}&vendor=eclipse&page_size=1";

        var json = await Client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var binary = document.RootElement[0].GetProperty("binary");
        var link = binary.TryGetProperty("package", out var package)
            ? package.TryGetProperty("link", out var linkElement) ? linkElement.GetString() : null
            : null;
        var checksum = binary.TryGetProperty("package", out package)
            ? package.TryGetProperty("checksum", out var checksumElement) ? checksumElement.GetString() : null
            : null;
        var size = binary.TryGetProperty("package", out package) &&
                   package.TryGetProperty("size", out var sizeElement) &&
                   sizeElement.TryGetInt64(out var parsedSize)
            ? parsedSize
            : 0;
        var buildVersion = binary.TryGetProperty("version", out var versionElement) &&
                           versionElement.TryGetProperty("semver", out var semverElement)
            ? semverElement.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(checksum))
            return null;

        return new JavaDownloadCandidate(
            JavaVendor.Temurin, majorVersion, buildVersion, link, checksum, size);
    }

    /// <summary>Oracle：官方直接下载链接（无需登录，实测仅 Java 21/25 可用）。</summary>
    private static JavaDownloadCandidate CreateOracleCandidate(int majorVersion, string os, string arch)
    {
        var platform = os switch
        {
            "windows" => arch == "aarch64" ? "windows-aarch64" : "windows-x64",
            "macos" => arch == "aarch64" ? "macos-aarch64" : "macos-x64",
            _ => arch == "aarch64" ? "linux-aarch64" : "linux-x64"
        };
        var extension = os == "windows" ? "zip" : "tar.gz";
        var url = string.Format(OracleDownloadBase, majorVersion, platform, extension);
        return new JavaDownloadCandidate(
            JavaVendor.Oracle, majorVersion, "latest", url, null, 0);
    }

    /// <summary>
    /// 从 Zulu 文件名中提取实际 JDK 版本号（如 "zulu21.52.203-ca-jdk21.0.12.1-win_x64.zip" → "21.0.12.1"）。
    /// </summary>
    internal static string ExtractJdkVersionFromName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;
        var marker = fileName.IndexOf("jdk", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return string.Empty;
        var segment = fileName[(marker + 3)..];
        var end = segment.IndexOfAny(['-', '_', '+']);
        if (end < 0)
            end = segment.Length;
        return segment[..end];
    }

    // ------------------------------------------------------------------
    // 安装
    // ------------------------------------------------------------------

    /// <summary>
    /// 下载并安装指定的 JDK 候选条目到 runtime 目录。
    /// </summary>
    /// <param name="candidate">由 <see cref="QueryAvailableVersionsAsync"/> 查询到的候选条目。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>安装完成的运行时信息。</returns>
    public async Task<InstalledJavaRuntime> InstallCandidateAsync(
        JavaDownloadCandidate candidate,
        IProgress<JavaRuntimeInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var runtimeDirectory = GetRuntimeDirectory();
        Directory.CreateDirectory(runtimeDirectory);
        var vendorKey = candidate.Vendor.ToString().ToLowerInvariant();
        var targetDirectory = Path.Combine(
            runtimeDirectory, $"java-{vendorKey}-{candidate.MajorVersion}");

        var vendorDisplay = candidate.VendorName;
        var extension = GetArchiveExtension(candidate.Vendor);
        var temporaryArchive = Path.Combine(
            Path.GetTempPath(), $"nyalauncher-java-{vendorKey}-{candidate.MajorVersion}-{Guid.NewGuid():N}.{extension}");
        try
        {
            Report(progress, $"正在下载 {vendorDisplay} JDK {candidate.MajorVersion}", 0, candidate.SizeBytes, 0);
            await DownloadAsync(candidate.DownloadUrl, temporaryArchive, candidate.SizeBytes, progress, cancellationToken)
                .ConfigureAwait(false);

            // SHA-256 完整性校验（有校验值才校验；Zulu/Oracle 无公开校验值时靠 https + 解压验证兜底）
            if (!string.IsNullOrWhiteSpace(candidate.Sha256))
            {
                Report(progress, "校验文件完整性", candidate.SizeBytes, candidate.SizeBytes, 0);
                await VerifySha256Async(temporaryArchive, candidate.Sha256, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 解压到临时目录（防部分解压污染已有安装）
            var extractionDirectory = Path.Combine(
                Path.GetTempPath(), $"nyalauncher-java-extract-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(extractionDirectory);
                Report(progress, "正在解压", candidate.SizeBytes, candidate.SizeBytes, 0);
                await ExtractArchiveAsync(temporaryArchive, extractionDirectory, cancellationToken)
                    .ConfigureAwait(false);

                // 定位解压出的 JDK 根目录并移动到正式位置
                var extractedRoot = FindJdkRoot(extractionDirectory);
                if (extractedRoot is null)
                    throw new InvalidDataException("下载的压缩包中没有找到 JDK 目录。");

                if (Directory.Exists(targetDirectory))
                    Directory.Delete(targetDirectory, recursive: true);
                Directory.Move(extractedRoot, targetDirectory);
            }
            finally
            {
                TryDeleteDirectory(extractionDirectory);
            }

            var javaPath = FindJavaExecutable(targetDirectory)
                ?? throw new InvalidDataException("安装完成后未找到 java 可执行文件。");
            var installedVersion = TryGetJavaMajorVersion(javaPath);
            Report(progress, "安装完成", candidate.SizeBytes, candidate.SizeBytes, 0);
            return new InstalledJavaRuntime(
                targetDirectory, javaPath, installedVersion, candidate.Vendor);
        }
        finally
        {
            TryDeleteFile(temporaryArchive);
        }
    }

    // ------------------------------------------------------------------
    // 平台判定
    // ------------------------------------------------------------------

    private static bool IsArm64() =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            is System.Runtime.InteropServices.Architecture.Arm64;

    private static string GetOperatingSystemKey() =>
        OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : "linux";

    private static string GetArchiveExtension(JavaVendor vendor) =>
        OperatingSystem.IsWindows() ? "zip" : "tar.gz";

    // ------------------------------------------------------------------
    // 下载与校验
    // ------------------------------------------------------------------

    private static async Task DownloadAsync(
        string url,
        string targetPath,
        long expectedSize,
        IProgress<JavaRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            128 * 1024, useAsync: true);

        var buffer = new byte[128 * 1024];
        long downloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            downloaded += read;
            var speed = stopwatch.Elapsed.TotalSeconds > 0
                ? downloaded / stopwatch.Elapsed.TotalSeconds
                : 0;
            Report(progress, "正在下载 JDK", downloaded, totalBytes, speed);
        }
    }

    private static async Task VerifySha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(hash);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Java 安装包校验失败（SHA-256 不匹配，文件可能损坏或被篡改）。");
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destinationDirectory), cancellationToken)
                .ConfigureAwait(false);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(archivePath);
            await TarFile.ExtractToDirectoryAsync(
                    stream, destinationDirectory, overwriteFiles: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            throw new InvalidDataException($"不支持的安装包格式：{archivePath}");
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    /// <summary>
    /// 在解压目录中定位 JDK 根目录（含 bin/java 的那一层）。
    /// </summary>
    private static string? FindJdkRoot(string baseDirectory)
    {
        var root = FindJavaExecutable(baseDirectory);
        return root is null ? null : Directory.GetParent(root)?.Parent?.FullName;
    }

    private static string? FindJavaExecutable(string directory)
    {
        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var candidate in Directory.EnumerateFiles(
                     directory, executableName, SearchOption.AllDirectories))
        {
            var parent = Directory.GetParent(candidate)?.Name;
            if (string.Equals(parent, "bin", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// 从目录名（java-zulu-21 / java-oracle-17 / java-temurin-8 或历史遗留 java-21）解析供应商。
    /// </summary>
    private static JavaVendor? ResolveVendorFromDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        if (string.IsNullOrWhiteSpace(name))
            return null;
        foreach (JavaVendor vendor in Enum.GetValues<JavaVendor>())
        {
            if (name.StartsWith($"java-{vendor.ToString().ToLowerInvariant()}-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return vendor;
            }
        }
        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex JavaVersionPattern =
        new("version\\s+\"(?<major>\\d+)(?:\\.(?<minor>\\d+))?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// 执行 java -version 探测主版本号；失败返回 null。
    /// 先等待退出（带 3 秒超时）再读流，避免同步 ReadToEnd 永久阻塞 UI 线程。
    /// </summary>
    private static int? TryGetJavaMajorVersion(string javaExecutable)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add("-version");
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            // 先等待退出（超时则强杀），再读取输出，杜绝挂起
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var standardError = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();

            var match = JavaVersionPattern.Match($"{standardError}\n{standardOutput}");
            if (!match.Success || !int.TryParse(match.Groups["major"].Value, out var major))
                return null;
            if (major == 1 && int.TryParse(match.Groups["minor"].Value, out var legacyMajor))
                return legacyMajor;
            return major;
        }
        catch
        {
            return null;
        }
    }

    private static void Report(
        IProgress<JavaRuntimeInstallProgress>? progress,
        string phase,
        long completedBytes,
        long totalBytes,
        double speed) =>
        progress?.Report(new JavaRuntimeInstallProgress(
            phase, completedBytes, totalBytes, speed));

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
