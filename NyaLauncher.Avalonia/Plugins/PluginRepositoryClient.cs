using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Avalonia.Plugins;

internal sealed record PluginRepositoryIndex
{
    public required int SchemaVersion { get; init; }

    public required string Name { get; init; }

    public required string SourceUrl { get; init; }

    public IReadOnlyList<RepositoryPlugin> Plugins { get; init; } = [];
}

internal sealed record RepositoryPlugin
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Authors { get; init; } = [];

    public required string RepositoryUrl { get; init; }

    public IReadOnlyList<string> Maintainers { get; init; } = [];

    public IReadOnlyList<string> Categories { get; init; } = [];

    public string License { get; init; } = string.Empty;

    public IReadOnlyList<RepositoryRelease> Releases { get; init; } = [];
}

internal sealed record RepositoryRelease
{
    public required string Version { get; init; }

    public required string Channel { get; init; }

    public required string PublishedAt { get; init; }

    public required string ReleaseNotesUrl { get; init; }

    public required RepositoryDownload Download { get; init; }

    public required RepositoryCompatibility Compatibility { get; init; }

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyList<string> OptionalCapabilities { get; init; } = [];

    public bool Yanked { get; init; }

    public string? YankReason { get; init; }

    public RepositoryReleaseReview? Review { get; init; }
}

internal sealed record RepositoryReleaseReview
{
    public required string Status { get; init; }

    public required string ReviewedBy { get; init; }

    public required string ReviewedAt { get; init; }

    public required string Sha256 { get; init; }

    public string? Notes { get; init; }
}

internal sealed record RepositoryDownload
{
    public required string Url { get; init; }

    public required string Sha256 { get; init; }

    public required long Size { get; init; }
}

internal sealed record RepositoryCompatibility
{
    public required int ManifestVersion { get; init; }

    public required string ApiVersion { get; init; }

    public required string MinimumLauncherVersion { get; init; }

    public string? MaximumLauncherVersionExclusive { get; init; }
}

internal sealed record RepositoryDownloadProgress(long BytesReceived, long TotalBytes);

internal static class RepositoryReviewPolicy
{
    public static bool RequiresInstallConfirmation(RepositoryRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Review is not { } review ||
               !string.Equals(review.Status, "verified", StringComparison.Ordinal) ||
               release.Download is null ||
               review.Sha256 is not { Length: 64 } ||
               review.Sha256.Any(character =>
                   character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
               !string.Equals(
                   review.Sha256,
                   release.Download.Sha256,
                   StringComparison.Ordinal);
    }
}

internal readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    IReadOnlyList<string>? Prerelease = null) : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)" +
        "(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?" +
        "(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant);

    public static SemanticVersion LauncherVersion
    {
        get
        {
            var version = typeof(PluginRepositoryClient).Assembly.GetName().Version ??
                          new Version(0, 1, 0);
            return new SemanticVersion(
                Math.Max(version.Major, 0),
                Math.Max(version.Minor, 0),
                Math.Max(version.Build, 0));
        }
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return false;
        var match = Pattern.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups[4].Success
            ? match.Groups[4].Value.Split('.')
            : [];
        if (prerelease.Any(identifier =>
                identifier.Length == 0 ||
                identifier.Length > 64 ||
                (identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsDigit))))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0)
            core = Minor.CompareTo(other.Minor);
        if (core == 0)
            core = Patch.CompareTo(other.Patch);
        if (core != 0)
            return core;

        var left = Prerelease ?? [];
        var right = other.Prerelease ?? [];
        if (left.Count == 0 || right.Count == 0)
            return left.Count == right.Count ? 0 : left.Count == 0 ? 1 : -1;
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var leftNumeric = int.TryParse(
                left[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(
                right[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            int comparison;
            if (leftNumeric && rightNumeric)
                comparison = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric != rightNumeric)
                comparison = leftNumeric ? -1 : 1;
            else
                comparison = string.CompareOrdinal(left[index], right[index]);
            if (comparison != 0)
                return comparison;
        }

        return left.Count.CompareTo(right.Count);
    }

    public override string ToString() =>
        Prerelease is { Count: > 0 }
            ? $"{Major}.{Minor}.{Patch}-{string.Join('.', Prerelease)}"
            : $"{Major}.{Minor}.{Patch}";
}

/// <summary>
/// Reads the immutable public registry and downloads verified GitHub Release assets.
/// Registry metadata is still treated as untrusted input and validated before use.
/// </summary>
internal sealed class PluginRepositoryClient : IDisposable
{
    public const string RepositoryUrl =
        "https://github.com/TouristH/NyaLauncher-Plugins";
    public const string IndexUrl =
        "https://raw.githubusercontent.com/TouristH/NyaLauncher-Plugins/main/public/v1/index.json";

    private const int MaximumIndexBytes = 4 * 1024 * 1024;
    private const int MaximumPluginCount = 2048;
    private const int MaximumReleaseCount = 128;
    private const long MaximumPackageBytes = 256L * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly Regex PluginIdPattern = new(
        "^[a-z][a-z0-9]*(?:\\.[a-z0-9][a-z0-9-]*)+$",
        RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex GitHubLoginPattern = new(
        "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ApiVersionPattern = new(
        "^1(?:\\.[0-9]+){1,2}$",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> KnownCapabilities = new(
        [
            "ui.components",
            "ui.native",
            "network.http",
            "system.info.read",
            "user-files.read",
            "user-files.write",
            "process.start",
            "minecraft.instance.read",
            "minecraft.instance.modify",
            "minecraft.launch.modify"
        ],
        StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public PluginRepositoryClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            _ownsClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }

        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NyaLauncher", SemanticVersion.LauncherVersion.ToString()));
    }

    public async Task<PluginRepositoryIndex> LoadIndexAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendWithRedirectsAsync(
            new Uri(IndexUrl),
            IsAllowedIndexUri,
            timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumIndexBytes)
            throw new InvalidDataException("插件仓库索引超过 4 MiB 上限。");

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var memory = new MemoryStream();
        await CopyBoundedAsync(stream, memory, MaximumIndexBytes, timeout.Token);
        PluginRepositoryIndex index;
        try
        {
            index = JsonSerializer.Deserialize<PluginRepositoryIndex>(
                        memory.GetBuffer().AsSpan(0, checked((int)memory.Length)),
                        JsonOptions) ??
                    throw new InvalidDataException("插件仓库索引不能为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"插件仓库索引格式错误：{exception.Message}", exception);
        }

        ValidateIndex(index);
        return index;
    }

    public RepositoryRelease? GetLatestCompatibleRelease(RepositoryPlugin plugin) =>
        plugin.Releases
            .Where(release =>
                !release.Yanked &&
                string.Equals(release.Channel, "stable", StringComparison.Ordinal) &&
                IsCompatible(release))
            .Select(release => new
            {
                Release = release,
                Version = SemanticVersion.TryParse(release.Version, out var version)
                    ? version
                    : default
            })
            .OrderByDescending(item => item.Version)
            .Select(item => item.Release)
            .FirstOrDefault();

    public static bool IsCompatible(RepositoryRelease release)
    {
        if (release.Compatibility.ManifestVersion != 1 ||
            string.IsNullOrWhiteSpace(release.Compatibility.ApiVersion) ||
            !ApiVersionPattern.IsMatch(release.Compatibility.ApiVersion) ||
            !SemanticVersion.TryParse(
                release.Compatibility.MinimumLauncherVersion,
                out var minimum) ||
            SemanticVersion.LauncherVersion.CompareTo(minimum) < 0)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(release.Compatibility.MaximumLauncherVersionExclusive) ||
               SemanticVersion.TryParse(
                   release.Compatibility.MaximumLauncherVersionExclusive,
                   out var maximum) &&
               SemanticVersion.LauncherVersion.CompareTo(maximum) < 0;
    }

    public async Task DownloadPackageAsync(
        RepositoryPlugin plugin,
        RepositoryRelease release,
        string destinationPath,
        IProgress<RepositoryDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateDownloadSource(plugin, release.Download);
        ValidateReleaseReview(plugin.Id, release);
        if (release.Download.Size is <= 0 or > MaximumPackageBytes)
            throw new InvalidDataException("插件包大小不在允许范围内。");
        if (!HashPattern.IsMatch(release.Download.Sha256))
            throw new InvalidDataException("插件包 SHA-256 格式无效。");

        var directory = Path.GetDirectoryName(destinationPath) ??
                        throw new InvalidOperationException("下载路径缺少父目录。");
        Directory.CreateDirectory(directory);
        if (File.Exists(destinationPath))
            throw new IOException("下载目标已经存在。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var response = await SendWithRedirectsAsync(
                new Uri(release.Download.Url),
                IsAllowedPackageUri,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength != release.Download.Size)
            {
                throw new InvalidDataException(
                    $"插件包长度与索引不一致（期望 {release.Download.Size}，实际 {contentLength}）。");
            }

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long total = 0;
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)) > 0)
                {
                    total += read;
                    if (total > release.Download.Size || total > MaximumPackageBytes)
                        throw new InvalidDataException("插件包超过索引声明的大小。");
                    hasher.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    progress?.Report(new RepositoryDownloadProgress(total, release.Download.Size));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await destination.FlushAsync(timeout.Token);
            if (total != release.Download.Size)
                throw new InvalidDataException("插件包实际长度与索引不一致。");
            var actualHash = hasher.GetHashAndReset();
            var expectedHash = Convert.FromHexString(release.Download.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new InvalidDataException("插件包 SHA-256 校验失败，文件可能已损坏或被替换。");
        }
        catch
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsClient)
            _httpClient.Dispose();
    }

    private async Task<HttpResponseMessage> SendWithRedirectsAsync(
        Uri initialUri,
        Func<Uri, bool> isAllowed,
        CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            if (!isAllowed(current))
                throw new InvalidDataException($"插件仓库拒绝了下载地址 {current.Host}。");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode is not (
                    HttpStatusCode.Moved or
                    HttpStatusCode.Redirect or
                    HttpStatusCode.RedirectMethod or
                    HttpStatusCode.TemporaryRedirect or
                    HttpStatusCode.PermanentRedirect))
            {
                return response;
            }

            if (redirect == MaximumRedirects || response.Headers.Location is null)
            {
                response.Dispose();
                throw new HttpRequestException("插件下载重定向次数过多或缺少目标地址。");
            }
            var location = response.Headers.Location;
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            response.Dispose();
        }

        throw new HttpRequestException("插件下载重定向失败。");
    }

    private static bool IsAllowedIndexUri(Uri uri) =>
        IsSafeHttpsUri(uri) &&
        string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedPackageUri(Uri uri) =>
        IsSafeHttpsUri(uri) &&
        (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsSafeHttpsUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException($"响应超过 {maximumBytes} 字节上限。");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateIndex(PluginRepositoryIndex index)
    {
        if (index.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(index.Name) || index.Name.Length > 128 ||
            !Uri.TryCreate(index.SourceUrl, UriKind.Absolute, out var source) ||
            !IsSafeHttpsUri(source) ||
            !string.Equals(source.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            index.Plugins is null || index.Plugins.Count > MaximumPluginCount)
        {
            throw new InvalidDataException("插件仓库索引头无效或超过上限。");
        }

        if (index.Plugins.GroupBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("插件仓库包含重复插件 ID。");
        }

        foreach (var plugin in index.Plugins)
            ValidatePlugin(plugin);
    }

    private static void ValidatePlugin(RepositoryPlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.Id) || plugin.Id.Length > 128 ||
            !PluginIdPattern.IsMatch(plugin.Id) ||
            string.IsNullOrWhiteSpace(plugin.Name) || plugin.Name.Length > 256 ||
            plugin.Description.Length > 8192 ||
            plugin.Authors.Count > 64 || plugin.Authors.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > 256) ||
            plugin.Maintainers.Count is < 1 or > 16 ||
            plugin.Categories.Count is < 1 or > 8 ||
            plugin.Releases.Count > MaximumReleaseCount ||
            !Uri.TryCreate(plugin.RepositoryUrl, UriKind.Absolute, out var repositoryUri) ||
            !IsSafeHttpsUri(repositoryUri) ||
            !string.Equals(repositoryUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            repositoryUri.Segments.Length < 3)
        {
            throw new InvalidDataException($"插件条目 {plugin.Id ?? "[未知]"} 无效。");
        }

        if (plugin.Releases.GroupBy(release => release.Version, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException($"插件 {plugin.Id} 包含重复版本。");
        }

        foreach (var release in plugin.Releases)
        {
            if (!SemanticVersion.TryParse(release.Version, out _) ||
                release.Channel is not ("stable" or "preview") ||
                !DateTimeOffset.TryParseExact(
                    release.PublishedAt,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _) ||
                release.Download is null || release.Compatibility is null ||
                release.Compatibility.ManifestVersion != 1 ||
                string.IsNullOrWhiteSpace(release.Compatibility.ApiVersion) ||
                !ApiVersionPattern.IsMatch(release.Compatibility.ApiVersion) ||
                !SemanticVersion.TryParse(
                    release.Compatibility.MinimumLauncherVersion,
                    out _) ||
                release.Compatibility.MaximumLauncherVersionExclusive is not null &&
                !SemanticVersion.TryParse(
                    release.Compatibility.MaximumLauncherVersionExclusive,
                    out _) ||
                release.Download.Size is <= 0 or > MaximumPackageBytes ||
                !HashPattern.IsMatch(release.Download.Sha256) ||
                release.RequiredCapabilities.Count + release.OptionalCapabilities.Count > 64 ||
                release.RequiredCapabilities.Concat(release.OptionalCapabilities)
                    .Any(capability => !KnownCapabilities.Contains(capability)) ||
                release.RequiredCapabilities.Concat(release.OptionalCapabilities)
                    .GroupBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1) ||
                release.Yanked && string.IsNullOrWhiteSpace(release.YankReason))
            {
                throw new InvalidDataException($"插件 {plugin.Id} 的版本 {release.Version} 无效。");
            }

            ValidateDownloadSource(plugin, release.Download);
            ValidateReleaseReview(plugin.Id, release);
            if (!Uri.TryCreate(release.ReleaseNotesUrl, UriKind.Absolute, out var notes) ||
                !IsSafeHttpsUri(notes))
            {
                throw new InvalidDataException($"插件 {plugin.Id} 的发行说明地址无效。");
            }
        }
    }

    private static void ValidateReleaseReview(string pluginId, RepositoryRelease release)
    {
        if (release.Review is not { } review)
            return;

        if (!string.Equals(review.Status, "verified", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(review.ReviewedBy) ||
            review.ReviewedBy.Length > 39 ||
            !GitHubLoginPattern.IsMatch(review.ReviewedBy) ||
            !DateTimeOffset.TryParseExact(
                review.ReviewedAt,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _) ||
            string.IsNullOrWhiteSpace(review.Sha256) ||
            !HashPattern.IsMatch(review.Sha256) ||
            !string.Equals(
                review.Sha256,
                release.Download.Sha256,
                StringComparison.Ordinal) ||
            review.Notes is not null && review.Notes.Length > 4096)
        {
            throw new InvalidDataException(
                $"插件 {pluginId} 的版本 {release.Version} 审核记录无效。");
        }
    }

    private static void ValidateDownloadSource(
        RepositoryPlugin plugin,
        RepositoryDownload download)
    {
        if (!Uri.TryCreate(plugin.RepositoryUrl.TrimEnd('/'), UriKind.Absolute, out var repository) ||
            !Uri.TryCreate(download.Url, UriKind.Absolute, out var asset) ||
            !IsAllowedPackageUri(asset) ||
            !string.Equals(asset.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("插件包必须来自插件自己的 GitHub Release。");
        }

        var repositoryPath = repository.AbsolutePath.TrimEnd('/');
        if (repositoryPath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repositoryPath = repositoryPath[..^4];
        var expectedPrefix = repositoryPath + "/releases/download/";
        if (!asset.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            asset.AbsolutePath.Length <= expectedPrefix.Length)
        {
            throw new InvalidDataException("插件包下载地址不属于条目声明的 GitHub Release。");
        }
    }
}
