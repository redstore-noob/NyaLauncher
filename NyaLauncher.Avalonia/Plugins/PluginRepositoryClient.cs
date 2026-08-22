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

    /// <summary>Present and required in v2; absent from the immutable v1 contract.</summary>
    public string? MinimumLauncherVersion { get; init; }

    public IReadOnlyList<RepositoryPlugin> Plugins { get; init; } = [];
}

internal sealed record RepositoryPlugin
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Authors { get; init; } = [];

    public required string RepositoryUrl { get; init; }

    /// <summary>
    /// Stable opaque identity shared by every generation of one plugin lineage.
    /// Missing only for the legacy v1 index, where the plugin ID is the fallback.
    /// </summary>
    public string? LineageId { get; init; }

    /// <summary>
    /// Current publisher generation. Legacy v1 entries are generation 1.
    /// </summary>
    public int Generation { get; init; } = 1;

    public string LifecycleStatus { get; init; } = "active";

    public string Visibility { get; init; } = "listed";

    public RepositoryPublisherIdentity? Publisher { get; init; }

    public IReadOnlyList<RepositoryGenerationBinding> Generations { get; init; } = [];

    public IReadOnlyList<string> Maintainers { get; init; } = [];

    public IReadOnlyList<string> Categories { get; init; } = [];

    public string License { get; init; } = string.Empty;

    public IReadOnlyList<RepositoryRelease> Releases { get; init; } = [];

    [JsonIgnore]
    public string EffectiveLineageId => LineageId ?? Id;
}

internal sealed record RepositoryPublisherIdentity
{
    public required long RepositoryId { get; init; }

    public required long OwnerId { get; init; }
}

internal sealed record RepositoryGenerationBinding
{
    public required int Generation { get; init; }

    public required string RepositoryUrl { get; init; }

    public required RepositoryPublisherIdentity Publisher { get; init; }

    public required string Status { get; init; }
}

internal sealed record RepositoryRelease
{
    /// <summary>
    /// Publisher generation that produced these exact bytes. Legacy v1 releases
    /// belong to generation 1.
    /// </summary>
    public int Generation { get; init; } = 1;

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

internal static class RepositoryCatalogPolicy
{
    public static bool HasCurrentNonYankedRelease(RepositoryPlugin plugin) =>
        plugin.Releases.Any(release =>
            release.Generation == plugin.Generation && !release.Yanked);

    public static bool IsCurrentGenerationInstallable(RepositoryPlugin plugin) =>
        string.Equals(plugin.LifecycleStatus, "active", StringComparison.Ordinal) &&
        string.Equals(plugin.Visibility, "listed", StringComparison.Ordinal) &&
        HasCurrentNonYankedRelease(plugin);

    /// <summary>
    /// Hidden/fully withdrawn entries remain visible only to people who have an
    /// installed package, so they receive the withdrawal or identity warning.
    /// </summary>
    public static bool ShouldDisplay(RepositoryPlugin plugin, PluginSnapshot? installed) =>
        installed is not null ||
        string.Equals(plugin.Visibility, "listed", StringComparison.Ordinal) &&
        HasCurrentNonYankedRelease(plugin);

    public static RepositoryGenerationBinding? FindGeneration(
        RepositoryPlugin plugin,
        int generation)
    {
        if (plugin.Generations.Count > 0)
        {
            return plugin.Generations.FirstOrDefault(binding =>
                binding.Generation == generation);
        }

        // The v1 index predates numeric publisher identity. Its repository URL
        // is still retained as a migration binding for installs made by this
        // launcher, but an already-installed package with no origin metadata is
        // never auto-bound to it.
        return generation == 1
            ? new RepositoryGenerationBinding
            {
                Generation = 1,
                RepositoryUrl = plugin.RepositoryUrl,
                Publisher = new RepositoryPublisherIdentity
                {
                    RepositoryId = 0,
                    OwnerId = 0
                },
                Status = "active"
            }
            : null;
    }
}

internal enum RepositoryIdentityMatch
{
    Match,
    LegacyV1NeedsReinstall,
    MissingInstalledOrigin,
    DifferentLineage,
    DifferentGeneration,
    DifferentPublisher
}

internal static class RepositoryIdentityPolicy
{
    public static RepositoryIdentityMatch Compare(
        RepositoryPlugin plugin,
        RepositoryRelease release,
        PluginInstallOrigin? installedOrigin)
    {
        if (installedOrigin is null)
            return RepositoryIdentityMatch.MissingInstalledOrigin;
        if (!string.Equals(installedOrigin.Id, plugin.Id, StringComparison.Ordinal))
            return RepositoryIdentityMatch.DifferentLineage;

        var binding = RepositoryCatalogPolicy.FindGeneration(plugin, release.Generation);
        if (binding is null)
            return RepositoryIdentityMatch.DifferentGeneration;

        if (installedOrigin.SourceIndexSchemaVersion == 1 && plugin.LineageId is not null)
            return RepositoryIdentityMatch.LegacyV1NeedsReinstall;
        if (!string.Equals(
                installedOrigin.LineageId,
                plugin.EffectiveLineageId,
                StringComparison.Ordinal))
        {
            return RepositoryIdentityMatch.DifferentLineage;
        }

        if (installedOrigin.Generation != release.Generation)
            return RepositoryIdentityMatch.DifferentGeneration;

        var remoteHasNumericIdentity =
            binding.Publisher.RepositoryId > 0 && binding.Publisher.OwnerId > 0;
        var installedHasNumericIdentity =
            installedOrigin.RepositoryId is > 0 && installedOrigin.OwnerId is > 0;
        if (remoteHasNumericIdentity && installedHasNumericIdentity)
        {
            if (installedOrigin.RepositoryId != binding.Publisher.RepositoryId ||
                installedOrigin.OwnerId != binding.Publisher.OwnerId)
            {
                return RepositoryIdentityMatch.DifferentPublisher;
            }
        }
        else if (!SameRepositoryUrl(installedOrigin.RepositoryUrl, binding.RepositoryUrl))
        {
            return RepositoryIdentityMatch.DifferentPublisher;
        }

        // A contract transition must never acquire or discard numeric identity
        // from URL equality alone. GitHub repository paths can be deleted and
        // later reclaimed by a different numeric repository.
        if (remoteHasNumericIdentity != installedHasNumericIdentity)
            return RepositoryIdentityMatch.DifferentPublisher;

        return RepositoryIdentityMatch.Match;
    }

    public static bool IsSafeUpdate(RepositoryIdentityMatch match) =>
        match == RepositoryIdentityMatch.Match;

    private static bool SameRepositoryUrl(string left, string right) =>
        string.Equals(
            left.TrimEnd('/'),
            right.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
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
        "https://raw.githubusercontent.com/TouristH/NyaLauncher-Plugins/main/public/v2/index.json";
    public const string LegacyIndexUrl =
        "https://raw.githubusercontent.com/TouristH/NyaLauncher-Plugins/main/public/v1/index.json";

    private const int MaximumIndexBytes = 4 * 1024 * 1024;
    private const int MaximumPluginCount = 2048;
    private const int MaximumReleaseCount = 128;
    private const int MaximumGenerationCount = 64;
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
    private static readonly Regex LineageIdPattern = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
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
        using var v2Response = await SendWithRedirectsAsync(
            new Uri(IndexUrl),
            IsAllowedIndexUri,
            timeout.Token);
        if (v2Response.StatusCode != HttpStatusCode.NotFound)
        {
            v2Response.EnsureSuccessStatusCode();
            return await ReadIndexResponseAsync(v2Response, expectedSchemaVersion: 2, timeout.Token);
        }

        // A missing v2 endpoint means the registry has not deployed identity
        // generations yet. Do not downgrade on malformed/forbidden v2 data or
        // transient failures; only an explicit 404 may use the legacy contract.
        using var legacyResponse = await SendWithRedirectsAsync(
            new Uri(LegacyIndexUrl),
            IsAllowedIndexUri,
            timeout.Token);
        legacyResponse.EnsureSuccessStatusCode();
        return await ReadIndexResponseAsync(legacyResponse, expectedSchemaVersion: 1, timeout.Token);
    }

    private static async Task<PluginRepositoryIndex> ReadIndexResponseAsync(
        HttpResponseMessage response,
        int expectedSchemaVersion,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumIndexBytes)
            throw new InvalidDataException("插件仓库索引超过 4 MiB 上限。");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        await CopyBoundedAsync(stream, memory, MaximumIndexBytes, cancellationToken);
        PluginRepositoryIndex index;
        try
        {
            var payload = memory.ToArray();
            ValidateVersionedContractShape(payload, expectedSchemaVersion);
            index = JsonSerializer.Deserialize<PluginRepositoryIndex>(
                        payload,
                        JsonOptions) ??
                    throw new InvalidDataException("插件仓库索引不能为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"插件仓库索引格式错误：{exception.Message}", exception);
        }

        ValidateIndex(index, expectedSchemaVersion);
        return index;
    }

    public IReadOnlyList<RepositoryRelease> GetCompatibleReleases(RepositoryPlugin plugin) =>
        !RepositoryCatalogPolicy.IsCurrentGenerationInstallable(plugin)
            ? []
            : plugin.Releases
            .Where(release =>
                release.Generation == plugin.Generation &&
                !release.Yanked &&
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
            .ToArray();

    public RepositoryRelease? GetLatestCompatibleRelease(RepositoryPlugin plugin)
    {
        var releases = GetCompatibleReleases(plugin);
        return releases.FirstOrDefault(release =>
                   string.Equals(release.Channel, "stable", StringComparison.Ordinal)) ??
               releases.FirstOrDefault();
    }

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
        var binding = RepositoryCatalogPolicy.FindGeneration(plugin, release.Generation) ??
                      throw new InvalidDataException("插件版本没有对应的发布者代际绑定。");
        ValidateDownloadSource(binding.RepositoryUrl, release.Download);
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

    private static void ValidateVersionedContractShape(
        byte[] payload,
        int expectedSchemaVersion)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
        {
            MaxDepth = 32,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out var schema) ||
            !schema.TryGetInt32(out var actualSchema) ||
            actualSchema != expectedSchemaVersion ||
            !document.RootElement.TryGetProperty("plugins", out var plugins) ||
            plugins.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"插件仓库索引不是预期的 v{expectedSchemaVersion} 契约。");
        }
        var hasMinimumLauncherVersion =
            document.RootElement.TryGetProperty("minimumLauncherVersion", out _);
        if (expectedSchemaVersion == 2 && !hasMinimumLauncherVersion)
            throw new InvalidDataException("v2 索引缺少 minimumLauncherVersion。");
        if (expectedSchemaVersion == 1 && hasMinimumLauncherVersion)
            throw new InvalidDataException("v1 索引不得包含 minimumLauncherVersion。");

        string[] v2PluginProperties =
        [
            "lineageId", "generation", "lifecycleStatus", "visibility", "publisher", "generations"
        ];
        foreach (var plugin in plugins.EnumerateArray())
        {
            if (plugin.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("插件仓库条目必须是对象。");
            if (expectedSchemaVersion == 2)
            {
                if (v2PluginProperties.Any(name => !plugin.TryGetProperty(name, out _)) ||
                    !plugin.TryGetProperty("generations", out var generations) ||
                    generations.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("v2 插件条目缺少发布者身份字段。");
                }

                foreach (var binding in generations.EnumerateArray())
                {
                    if (binding.ValueKind != JsonValueKind.Object ||
                        !binding.TryGetProperty("generation", out _) ||
                        !binding.TryGetProperty("repositoryUrl", out _) ||
                        !binding.TryGetProperty("publisher", out _) ||
                        !binding.TryGetProperty("status", out _))
                    {
                        throw new InvalidDataException("v2 插件代际绑定缺少必要字段。");
                    }
                }
            }
            else if (v2PluginProperties.Any(name => plugin.TryGetProperty(name, out _)))
            {
                throw new InvalidDataException("v1 索引不得混入 v2 身份字段。");
            }

            if (!plugin.TryGetProperty("releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("插件条目缺少发行历史。");
            }
            foreach (var release in releases.EnumerateArray())
            {
                var hasGeneration = release.ValueKind == JsonValueKind.Object &&
                                    release.TryGetProperty("generation", out _);
                if (expectedSchemaVersion == 2 && !hasGeneration)
                    throw new InvalidDataException("v2 发行记录缺少 generation。");
                if (expectedSchemaVersion == 1 && hasGeneration)
                    throw new InvalidDataException("v1 发行记录不得包含 generation。");
            }
        }
    }

    private static void ValidateIndex(PluginRepositoryIndex index, int expectedSchemaVersion)
    {
        if (index.SchemaVersion != expectedSchemaVersion ||
            expectedSchemaVersion is not (1 or 2) ||
            string.IsNullOrWhiteSpace(index.Name) || index.Name.Length > 128 ||
            !Uri.TryCreate(index.SourceUrl, UriKind.Absolute, out var source) ||
            !IsSafeHttpsUri(source) ||
            !string.Equals(source.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            index.Plugins is null || index.Plugins.Count > MaximumPluginCount)
        {
            throw new InvalidDataException("插件仓库索引头无效或超过上限。");
        }
        if (expectedSchemaVersion == 2)
        {
            if (!SemanticVersion.TryParse(index.MinimumLauncherVersion, out var minimum) ||
                SemanticVersion.LauncherVersion.CompareTo(minimum) < 0)
            {
                throw new InvalidDataException(
                    $"插件仓库 v2 需要 NyaLauncher {index.MinimumLauncherVersion ?? "[未知]"} 或更高版本。");
            }
        }
        else if (index.MinimumLauncherVersion is not null)
        {
            throw new InvalidDataException("v1 索引包含不允许的启动器版本字段。");
        }

        if (index.Plugins.GroupBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("插件仓库包含重复插件 ID。");
        }

        foreach (var plugin in index.Plugins)
            ValidatePlugin(plugin, expectedSchemaVersion);
    }

    private static void ValidatePlugin(RepositoryPlugin plugin, int schemaVersion)
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
        if (schemaVersion == 2)
            ValidateV2PluginIdentity(plugin);
        else if (plugin.LineageId is not null ||
                 plugin.Generation != 1 ||
                 plugin.Publisher is not null ||
                 plugin.Generations.Count != 0)
            throw new InvalidDataException($"v1 插件 {plugin.Id} 包含 v2 身份数据。");

        if (plugin.Releases.GroupBy(
                release => (release.Generation, release.Version),
                EqualityComparer<(int, string)>.Default)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException($"插件 {plugin.Id} 包含重复代际版本。");
        }

        foreach (var release in plugin.Releases)
        {
            var binding = RepositoryCatalogPolicy.FindGeneration(plugin, release.Generation);
            if (release.Generation < 1 ||
                binding is null ||
                schemaVersion == 1 && release.Generation != 1 ||
                schemaVersion == 2 && release.Generation != plugin.Generation && !release.Yanked ||
                !SemanticVersion.TryParse(release.Version, out _) ||
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

            ValidateDownloadSource(binding.RepositoryUrl, release.Download);
            ValidateReleaseReview(plugin.Id, release);
            if (!Uri.TryCreate(release.ReleaseNotesUrl, UriKind.Absolute, out var notes) ||
                !IsSafeHttpsUri(notes))
            {
                throw new InvalidDataException($"插件 {plugin.Id} 的发行说明地址无效。");
            }
        }
    }

    private static void ValidateV2PluginIdentity(RepositoryPlugin plugin)
    {
        if (plugin.LineageId is null || !LineageIdPattern.IsMatch(plugin.LineageId) ||
            plugin.Generation < 1 ||
            plugin.LifecycleStatus is not ("active" or "retired" or "transferred") ||
            plugin.Visibility is not ("listed" or "hidden") ||
            plugin.Publisher is null ||
            !IsValidPublisher(plugin.Publisher) ||
            plugin.Generations.Count is < 1 or > MaximumGenerationCount ||
            plugin.Generations.GroupBy(binding => binding.Generation)
                .Any(group => group.Count() > 1) ||
            !plugin.Generations.Select(binding => binding.Generation)
                .SequenceEqual(Enumerable.Range(1, plugin.Generation)))
        {
            throw new InvalidDataException($"插件 {plugin.Id} 的 v2 身份无效。");
        }

        foreach (var binding in plugin.Generations)
        {
            if (binding.Generation is < 1 || binding.Generation > plugin.Generation ||
                binding.Status is not ("active" or "retired" or "transferred") ||
                binding.Generation < plugin.Generation && binding.Status != "transferred" ||
                binding.Publisher is null || !IsValidPublisher(binding.Publisher) ||
                !TryValidateGitHubRepositoryUrl(binding.RepositoryUrl, out _))
            {
                throw new InvalidDataException($"插件 {plugin.Id} 的代际绑定无效。");
            }
        }

        var current = plugin.Generations.SingleOrDefault(binding =>
            binding.Generation == plugin.Generation);
        var currentHasRelease = plugin.Releases.Any(release =>
            release.Generation == plugin.Generation && !release.Yanked);
        var expectedVisibility =
            plugin.LifecycleStatus == "active" && currentHasRelease ? "listed" : "hidden";
        var expectedCurrentStatus = plugin.LifecycleStatus == "retired" ? "retired" : "active";
        if (current is null ||
            !string.Equals(
                current.RepositoryUrl.TrimEnd('/'),
                plugin.RepositoryUrl.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase) ||
            current.Publisher.RepositoryId != plugin.Publisher.RepositoryId ||
            current.Publisher.OwnerId != plugin.Publisher.OwnerId ||
            !string.Equals(current.Status, expectedCurrentStatus, StringComparison.Ordinal) ||
            !string.Equals(plugin.Visibility, expectedVisibility, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"插件 {plugin.Id} 的当前代际展示身份不一致。");
        }
    }

    private static bool IsValidPublisher(RepositoryPublisherIdentity publisher) =>
        publisher.RepositoryId is > 0 and <= long.MaxValue &&
        publisher.OwnerId is > 0 and <= long.MaxValue;

    private static bool TryValidateGitHubRepositoryUrl(string value, out Uri repositoryUri)
    {
        repositoryUri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !IsSafeHttpsUri(parsed) ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            parsed.Segments.Length < 3)
        {
            return false;
        }

        repositoryUri = parsed;
        return true;
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
        string repositoryUrl,
        RepositoryDownload download)
    {
        if (!Uri.TryCreate(repositoryUrl.TrimEnd('/'), UriKind.Absolute, out var repository) ||
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
