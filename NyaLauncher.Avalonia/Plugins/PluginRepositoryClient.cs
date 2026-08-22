using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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

    /// <summary>
    /// Append-only canonical GitHub repository paths used by this numeric
    /// repository identity. The final entry is always RepositoryUrl.
    /// </summary>
    public IReadOnlyList<string> RepositoryUrlHistory { get; init; } = [];

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
                RepositoryUrlHistory = [plugin.RepositoryUrl],
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
    DifferentPublisher,
    InvalidRepositoryHistory
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

            // A same-generation GitHub rename is safe only when the new index
            // keeps the repository URL recorded by the launcher in that
            // generation's append-only history. Numeric IDs remain the primary
            // identity; this continuity check prevents a malformed history from
            // silently rewriting the installed provenance.
            if (binding.RepositoryUrlHistory is null ||
                binding.RepositoryUrlHistory.Count == 0 ||
                !string.Equals(
                    binding.RepositoryUrlHistory[^1],
                    binding.RepositoryUrl,
                    StringComparison.Ordinal) ||
                binding.RepositoryUrlHistory
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != binding.RepositoryUrlHistory.Count ||
                !binding.RepositoryUrlHistory.Contains(
                    installedOrigin.RepositoryUrl,
                    StringComparer.OrdinalIgnoreCase))
            {
                return RepositoryIdentityMatch.InvalidRepositoryHistory;
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
            var assembly = typeof(PluginRepositoryClient).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (TryParse(informationalVersion, out var semanticVersion))
                return semanticVersion;

            // AssemblyVersion cannot represent prerelease labels. It is only a
            // fail-safe fallback for unusual builds without valid informational
            // SemVer metadata.
            var version = assembly.GetName().Version ??
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
            var leftNumeric = IsNumericPrereleaseIdentifier(left[index]);
            var rightNumeric = IsNumericPrereleaseIdentifier(right[index]);
            int comparison;
            if (leftNumeric && rightNumeric)
                comparison = CompareNumericPrereleaseIdentifiers(left[index], right[index]);
            else if (leftNumeric != rightNumeric)
                comparison = leftNumeric ? -1 : 1;
            else
                comparison = string.CompareOrdinal(left[index], right[index]);
            if (comparison != 0)
                return comparison;
        }

        return left.Count.CompareTo(right.Count);
    }

    private static bool IsNumericPrereleaseIdentifier(string value) =>
        value.All(character => character is >= '0' and <= '9');

    private static int CompareNumericPrereleaseIdentifiers(string left, string right)
    {
        // SemVer numeric identifiers are not bounded by Int32. The parser has
        // already rejected leading zeroes, so digit count followed by ordinal
        // comparison gives exact arbitrary-precision ordering without allocation.
        var lengthComparison = left.Length.CompareTo(right.Length);
        return lengthComparison != 0
            ? lengthComparison
            : string.CompareOrdinal(left, right);
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
    private const int MaximumRepositoryUrlHistoryCount = 64;
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
    private static readonly Regex GitHubRepositoryPattern = new(
        "^https://github\\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex V1HttpsUrlPattern = new(
        "^(?!.*[\\s\\\\\\x00-\\x1F\\x7F])(?!.*%(?![0-9A-Fa-f]{2}))" +
        "https://(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)" +
        "(?:\\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))*" +
        "(?::443)?(?:[/?#]|$)",
        RegexOptions.CultureInvariant);
    private static readonly Regex InvalidPercentEncodingPattern = new(
        "%(?![0-9A-Fa-f]{2})",
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
    private static readonly HashSet<string> KnownCategories = new(
        [
            "appearance",
            "automation",
            "gameplay",
            "integration",
            "launch",
            "management",
            "utilities"
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
        ValidateDownloadSource(binding, release.Download);
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
        string[] commonPluginProperties =
        [
            "id", "name", "description", "authors", "repositoryUrl", "maintainers",
            "categories", "license", "releases"
        ];
        string[] commonReleaseProperties =
        [
            "version", "channel", "publishedAt", "releaseNotesUrl", "download",
            "compatibility", "requiredCapabilities", "optionalCapabilities", "yanked"
        ];
        foreach (var plugin in plugins.EnumerateArray())
        {
            if (plugin.ValueKind != JsonValueKind.Object ||
                commonPluginProperties.Any(name => !plugin.TryGetProperty(name, out _)))
            {
                throw new InvalidDataException("插件仓库条目缺少公开契约字段。");
            }
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
                        !binding.TryGetProperty("repositoryUrlHistory", out _) ||
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
                if (release.ValueKind != JsonValueKind.Object ||
                    commonReleaseProperties.Any(name => !release.TryGetProperty(name, out _)))
                {
                    throw new InvalidDataException("插件发行记录缺少公开契约字段。");
                }
                var hasGeneration = release.ValueKind == JsonValueKind.Object &&
                                    release.TryGetProperty("generation", out _);
                if (expectedSchemaVersion == 2 && !hasGeneration)
                    throw new InvalidDataException("v2 发行记录缺少 generation。");
                if (expectedSchemaVersion == 1 && hasGeneration)
                    throw new InvalidDataException("v1 发行记录不得包含 generation。");
                if (!release.TryGetProperty("yanked", out var yanked) ||
                    yanked.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException("发行记录的 yanked 必须显式为布尔值。");
                }
                var hasYankReason = release.TryGetProperty("yankReason", out var yankReason);
                if (yanked.GetBoolean() != hasYankReason ||
                    hasYankReason && yankReason.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("发行记录的撤回原因与 yanked 状态不一致。");
                }
            }
        }
    }

    private static void ValidateIndex(PluginRepositoryIndex index, int expectedSchemaVersion)
    {
        if (index.SchemaVersion != expectedSchemaVersion ||
            expectedSchemaVersion is not (1 or 2) ||
            string.IsNullOrWhiteSpace(index.Name) || index.Name.Length > 128 ||
            index.Plugins is null || index.Plugins.Count > MaximumPluginCount ||
            expectedSchemaVersion == 1 &&
            !TryValidatePublicHttpsUrl(index.SourceUrl, 1, maximumLength: null, out _) ||
            expectedSchemaVersion == 2 &&
            !TryValidateGitHubRepositoryUrl(index.SourceUrl, out _))
        {
            throw new InvalidDataException("插件仓库索引头无效或超过上限。");
        }
        SemanticVersion? repositoryMinimum = null;
        if (expectedSchemaVersion == 2)
        {
            if (!SemanticVersion.TryParse(index.MinimumLauncherVersion, out var minimum) ||
                SemanticVersion.LauncherVersion.CompareTo(minimum) < 0)
            {
                throw new InvalidDataException(
                    $"插件仓库 v2 需要 NyaLauncher {index.MinimumLauncherVersion ?? "[未知]"} 或更高版本。");
            }
            repositoryMinimum = minimum;
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
            ValidatePlugin(plugin, expectedSchemaVersion, repositoryMinimum);
    }

    private static void ValidatePlugin(
        RepositoryPlugin plugin,
        int schemaVersion,
        SemanticVersion? repositoryMinimum)
    {
        if (string.IsNullOrWhiteSpace(plugin.Id) || plugin.Id.Length > 128 ||
            !PluginIdPattern.IsMatch(plugin.Id) ||
            string.IsNullOrWhiteSpace(plugin.Name) || plugin.Name.Length > 256 ||
            string.IsNullOrWhiteSpace(plugin.Description) || plugin.Description.Length > 8192 ||
            plugin.Authors is null ||
            plugin.Authors.Count == 0 ||
            plugin.Authors.Count > 64 || plugin.Authors.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > 256) ||
            HasDuplicates(plugin.Authors, StringComparer.OrdinalIgnoreCase) ||
            plugin.Maintainers is null || plugin.Maintainers.Count is < 1 or > 16 ||
            plugin.Maintainers.Any(value =>
                string.IsNullOrWhiteSpace(value) ||
                value.Length > 39 ||
                !GitHubLoginPattern.IsMatch(value)) ||
            HasDuplicates(plugin.Maintainers, StringComparer.OrdinalIgnoreCase) ||
            plugin.Categories is null || plugin.Categories.Count is < 1 or > 8 ||
            plugin.Categories.Any(value =>
                string.IsNullOrWhiteSpace(value) || !KnownCategories.Contains(value)) ||
            HasDuplicates(plugin.Categories, StringComparer.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(plugin.License) || plugin.License.Length > 256 ||
            plugin.Releases is null ||
            plugin.Releases.Count == 0 ||
            plugin.Releases.Count > MaximumReleaseCount ||
            !TryValidateGitHubRepositoryUrl(plugin.RepositoryUrl, out _))
        {
            throw new InvalidDataException($"插件条目 {plugin.Id ?? "[未知]"} 无效。");
        }
        if (schemaVersion == 2)
            ValidateV2PluginIdentity(plugin);
        else if (plugin.LineageId is not null ||
                 plugin.Generation != 1 ||
                 plugin.Publisher is not null ||
                 plugin.Generations is null ||
                 plugin.Generations.Count != 0)
            throw new InvalidDataException($"v1 插件 {plugin.Id} 包含 v2 身份数据。");

        if (plugin.Releases.Any(release => release is null))
            throw new InvalidDataException($"插件 {plugin.Id} 包含空发行记录。");
        if (plugin.Releases.GroupBy(
                release =>
                {
                    var precedence = SemanticVersion.TryParse(release.Version, out var version)
                        ? version.ToString()
                        : release.Version;
                    return (release.Generation, precedence);
                },
                EqualityComparer<(int, string)>.Default)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException($"插件 {plugin.Id} 包含重复代际版本。");
        }

        foreach (var release in plugin.Releases)
        {
            var binding = RepositoryCatalogPolicy.FindGeneration(plugin, release.Generation);
            var requiredCapabilities = release.RequiredCapabilities;
            var optionalCapabilities = release.OptionalCapabilities;
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
                    out var minimumCompatibility) ||
                release.Compatibility.MaximumLauncherVersionExclusive is not null &&
                (!SemanticVersion.TryParse(
                     release.Compatibility.MaximumLauncherVersionExclusive,
                     out var maximumCompatibility) ||
                 maximumCompatibility.CompareTo(minimumCompatibility) <= 0) ||
                schemaVersion == 2 &&
                release.Generation > 1 &&
                repositoryMinimum is SemanticVersion rootMinimum &&
                minimumCompatibility.CompareTo(rootMinimum) < 0 ||
                release.Download.Size is <= 0 or > MaximumPackageBytes ||
                string.IsNullOrWhiteSpace(release.Download.Sha256) ||
                !HashPattern.IsMatch(release.Download.Sha256) ||
                requiredCapabilities is null || optionalCapabilities is null ||
                requiredCapabilities.Count > 64 || optionalCapabilities.Count > 64 ||
                requiredCapabilities.Count + optionalCapabilities.Count > 64 ||
                requiredCapabilities.Concat(optionalCapabilities)
                    .Any(capability => !KnownCapabilities.Contains(capability)) ||
                requiredCapabilities.Concat(optionalCapabilities)
                    .GroupBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1) ||
                release.Yanked &&
                (string.IsNullOrWhiteSpace(release.YankReason) || release.YankReason.Length > 1024) ||
                !release.Yanked && release.YankReason is not null)
            {
                throw new InvalidDataException($"插件 {plugin.Id} 的版本 {release.Version} 无效。");
            }

            ValidateDownloadSource(binding, release.Download);
            ValidateReleaseReview(plugin.Id, release);
            if (!TryValidatePublicHttpsUrl(
                    release.ReleaseNotesUrl,
                    schemaVersion,
                    maximumLength: 2048,
                    out _) ||
                !ValidateReleaseNotesSource(binding, release.ReleaseNotesUrl))
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
            plugin.Generations is null ||
            plugin.Generations.Count is < 1 or > MaximumGenerationCount ||
            plugin.Generations.Any(binding => binding is null) ||
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
                !TryValidateCanonicalGitHubRepositoryUrl(binding.RepositoryUrl, out _) ||
                binding.RepositoryUrlHistory is null ||
                binding.RepositoryUrlHistory.Count is < 1 or > MaximumRepositoryUrlHistoryCount ||
                binding.RepositoryUrlHistory.Any(repositoryUrl =>
                    !TryValidateCanonicalGitHubRepositoryUrl(repositoryUrl, out _)) ||
                HasDuplicateGitHubRepositories(binding.RepositoryUrlHistory) ||
                !string.Equals(
                    binding.RepositoryUrlHistory[^1],
                    binding.RepositoryUrl,
                    StringComparison.Ordinal))
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
                current.RepositoryUrl,
                plugin.RepositoryUrl,
                StringComparison.Ordinal) ||
            current.Publisher.RepositoryId != plugin.Publisher.RepositoryId ||
            current.Publisher.OwnerId != plugin.Publisher.OwnerId ||
            !string.Equals(current.Status, expectedCurrentStatus, StringComparison.Ordinal) ||
            !string.Equals(plugin.Visibility, expectedVisibility, StringComparison.Ordinal) ||
            plugin.LifecycleStatus != "active" && currentHasRelease ||
            plugin.LifecycleStatus == "transferred" && plugin.Generation < 2 ||
            plugin.Generations.GroupBy(binding => binding.Publisher.RepositoryId)
                .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException($"插件 {plugin.Id} 的当前代际展示身份不一致。");
        }
    }

    private static bool IsValidPublisher(RepositoryPublisherIdentity publisher) =>
        publisher.RepositoryId is > 0 and <= long.MaxValue &&
        publisher.OwnerId is > 0 and <= long.MaxValue;

    private static bool HasDuplicates(
        IEnumerable<string> values,
        StringComparer comparer) =>
        values.Distinct(comparer).Count() != values.Count();

    private static bool HasDuplicateGitHubRepositories(IEnumerable<string> values) =>
        values
            .Select(NormalizeGitHubRepositoryIdentity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != values.Count();

    private static string NormalizeGitHubRepositoryIdentity(string value)
    {
        if (!TryValidateGitHubRepositoryUrl(value, out var repository))
            return value;
        var owner = repository.Segments[1].TrimEnd('/');
        var name = repository.Segments[2].TrimEnd('/');
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return $"{owner}/{name}";
    }

    private static bool TryValidatePublicHttpsUrl(
        string? value,
        int schemaVersion,
        int? maximumLength,
        out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            maximumLength is int limit && value.Length > limit ||
            value.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character is '\\' or '\u007f') ||
            InvalidPercentEncodingPattern.IsMatch(value) ||
            schemaVersion == 1 && !V1HttpsUrlPattern.IsMatch(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !IsSafeHttpsUri(parsed))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool TryValidateGitHubRepositoryUrl(string? value, out Uri repositoryUri)
    {
        repositoryUri = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 2048 ||
            !GitHubRepositoryPattern.IsMatch(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !IsSafeHttpsUri(parsed) ||
            !string.Equals(parsed.Host, "github.com", StringComparison.Ordinal) ||
            parsed.Segments.Length != 3 ||
            parsed.Query.Length != 0 ||
            parsed.Fragment.Length != 0 ||
            !GitHubLoginPattern.IsMatch(parsed.Segments[1].TrimEnd('/')) ||
            parsed.Segments[2].TrimEnd('/') is not { Length: >= 1 and <= 100 } repositoryName ||
            repositoryName is "." or ".." ||
            repositoryName.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        repositoryUri = parsed;
        return true;
    }

    private static bool TryValidateCanonicalGitHubRepositoryUrl(
        string? value,
        out Uri repositoryUri)
    {
        if (!TryValidateGitHubRepositoryUrl(value, out repositoryUri))
            return false;

        var owner = repositoryUri.Segments[1].TrimEnd('/');
        var repository = repositoryUri.Segments[2].TrimEnd('/');
        return !repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   value,
                   $"https://github.com/{owner}/{repository}",
                   StringComparison.Ordinal);
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
        RepositoryGenerationBinding binding,
        RepositoryDownload download)
    {
        if (binding.RepositoryUrlHistory is null ||
            download is null ||
            !TryValidatePublicHttpsUrl(download.Url, 2, maximumLength: 2048, out var asset) ||
            !string.Equals(asset.Host, "github.com", StringComparison.Ordinal) ||
            asset.Query.Length != 0 ||
            asset.Fragment.Length != 0 ||
            asset.Segments.Length != 7 ||
            !string.Equals(asset.Segments[3], "releases/", StringComparison.Ordinal) ||
            !string.Equals(asset.Segments[4], "download/", StringComparison.Ordinal) ||
            !TryDecodeReleasePathSegment(asset.Segments[5].TrimEnd('/'), out var tag) ||
            !TryDecodeReleasePathSegment(asset.Segments[6], out var assetName) ||
            tag is "." or ".." ||
            assetName.Length <= 4 ||
            !assetName.EndsWith(".zip", StringComparison.Ordinal) ||
            !binding.RepositoryUrlHistory.Any(repositoryUrl =>
                MatchesGitHubRepositoryPath(repositoryUrl, asset)))
        {
            throw new InvalidDataException("插件包必须来自插件自己的 GitHub Release。");
        }
    }

    private static bool ValidateReleaseNotesSource(
        RepositoryGenerationBinding binding,
        string releaseNotesUrl)
    {
        if (binding.RepositoryUrlHistory is null ||
            !TryValidatePublicHttpsUrl(
                releaseNotesUrl,
                schemaVersion: 2,
                maximumLength: 2048,
                out var notes) ||
            !string.Equals(notes.Host, "github.com", StringComparison.Ordinal) ||
            notes.Query.Length != 0 ||
            notes.Fragment.Length != 0)
        {
            return false;
        }

        var matchedRepository = binding.RepositoryUrlHistory
            .Select(repositoryUrl =>
                TryValidateGitHubRepositoryUrl(repositoryUrl, out var repository)
                    ? repository
                    : null)
            .FirstOrDefault(repository =>
                repository is not null &&
                MatchesGitHubRepositoryPath(repository, notes) &&
                notes.AbsolutePath.AsSpan(repository.AbsolutePath.TrimEnd('/').Length)
                    .StartsWith("/releases/tag/", StringComparison.Ordinal));
        if (matchedRepository is null)
            return false;
        var prefix = matchedRepository.AbsolutePath.TrimEnd('/') + "/releases/tag/";
        try
        {
            var decodedTag = Uri.UnescapeDataString(notes.AbsolutePath[prefix.Length..]);
            var segments = decodedTag.Split('/');
            return segments.Length > 0 && segments.All(segment =>
                segment.Length > 0 &&
                segment is not ("." or "..") &&
                !segment.Contains('\\'));
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool MatchesGitHubRepositoryPath(string repositoryUrl, Uri candidate) =>
        TryValidateGitHubRepositoryUrl(repositoryUrl, out var repository) &&
        MatchesGitHubRepositoryPath(repository, candidate);

    private static bool MatchesGitHubRepositoryPath(Uri repository, Uri candidate) =>
        candidate.Segments.Length >= 3 &&
        string.Equals(
            repository.Segments[1].TrimEnd('/'),
            candidate.Segments[1].TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            repository.Segments[2].TrimEnd('/'),
            candidate.Segments[2].TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeReleasePathSegment(string value, out string decoded)
    {
        decoded = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            decoded = Uri.UnescapeDataString(value);
            return decoded.Length > 0 &&
                   decoded.IndexOfAny(['/', '\\']) < 0 &&
                   decoded is not ("." or "..");
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}
