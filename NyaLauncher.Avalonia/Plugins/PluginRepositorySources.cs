using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia.Plugins;

/// <summary>
/// A transport route for the canonical NyaLauncher plugin registry. Mirror
/// routes prefix an already-validated GitHub URL; they never replace URLs kept
/// in the index or the install provenance snapshot.
/// </summary>
internal sealed record PluginRepositorySource
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required bool IsBuiltIn { get; init; }

    public Uri? MirrorBaseUri { get; init; }

    public bool IsDirect => MirrorBaseUri is null;

    public string RouteLabel => IsDirect
        ? "GitHub 官方地址"
        : MirrorBaseUri!.AbsoluteUri;

    public Uri Resolve(Uri canonicalUri)
    {
        ArgumentNullException.ThrowIfNull(canonicalUri);
        if (IsDirect)
            return canonicalUri;

        var resolved = new Uri(
            MirrorBaseUri!.AbsoluteUri + canonicalUri.AbsoluteUri,
            UriKind.Absolute);
        if (!AllowsMirrorUri(resolved))
            throw new InvalidOperationException("镜像源未能生成安全的 HTTPS 请求地址。");
        return resolved;
    }

    public bool AllowsMirrorUri(Uri uri)
    {
        if (MirrorBaseUri is null || !PluginRepositorySources.IsSafeHttpsUri(uri))
            return false;

        return string.Equals(uri.Scheme, MirrorBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.IdnHost, MirrorBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == MirrorBaseUri.Port &&
               uri.AbsolutePath.StartsWith(
                   MirrorBaseUri.AbsolutePath,
                   StringComparison.Ordinal);
    }
}

internal static class PluginRepositorySources
{
    public const int MaximumCustomSourceCount = 16;
    public const int MaximumSourceNameLength = 40;
    public const int MaximumMirrorBaseUrlLength = 512;

    private static readonly Regex CustomIdPattern = new(
        "^custom-[0-9a-f]{32}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex InvalidPercentEncodingPattern = new(
        "%(?![0-9A-Fa-f]{2})",
        RegexOptions.CultureInvariant);

    public static PluginRepositorySource Official { get; } = new()
    {
        Id = "official",
        Name = "GitHub 官方直连",
        Description = "直接连接 GitHub 与 GitHubusercontent。",
        IsBuiltIn = true
    };

    public static IReadOnlyList<PluginRepositorySource> BuiltIn { get; } =
    [
        Official,
        CreateBuiltIn(
            "gh-proxy-com",
            "gh-proxy.com",
            "https://gh-proxy.com/"),
        CreateBuiltIn(
            "ghfast-top",
            "ghfast.top",
            "https://ghfast.top/"),
        CreateBuiltIn(
            "gh-xmly-dev",
            "gh.xmly.dev",
            "https://gh.xmly.dev/")
    ];

    public static bool TryCreateCustom(
        string? name,
        string? mirrorBaseUrl,
        out PluginRepositorySource source,
        out string error,
        string? persistedId = null)
    {
        source = null!;
        error = string.Empty;
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) ||
            normalizedName.Length > MaximumSourceNameLength ||
            normalizedName.Any(character => char.IsControl(character) || character == '\u007f'))
        {
            error = $"名称必须为 1–{MaximumSourceNameLength} 个可见字符。";
            return false;
        }

        if (!TryNormalizeMirrorBaseUri(mirrorBaseUrl, out var baseUri, out error))
            return false;

        var id = persistedId ?? $"custom-{Guid.NewGuid():N}";
        if (!CustomIdPattern.IsMatch(id) ||
            BuiltIn.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "自定义镜像源 ID 无效。";
            return false;
        }

        source = new PluginRepositorySource
        {
            Id = id,
            Name = normalizedName,
            Description = "用户添加的 GitHub HTTPS 前缀镜像。",
            IsBuiltIn = false,
            MirrorBaseUri = baseUri
        };
        return true;
    }

    public static bool TryNormalizeMirrorBaseUri(
        string? value,
        out Uri uri,
        out string error)
    {
        uri = null!;
        error = string.Empty;
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > MaximumMirrorBaseUrlLength)
        {
            error = $"镜像前缀必须为 1–{MaximumMirrorBaseUrlLength} 个字符。";
            return false;
        }

        if (candidate.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character is '\\' or '\u007f') ||
            InvalidPercentEncodingPattern.IsMatch(candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
            !IsSafeHttpsUri(parsed) ||
            parsed.Query.Length != 0 ||
            parsed.Fragment.Length != 0)
        {
            error = "镜像前缀必须是无凭据、查询或片段的标准 HTTPS 地址。";
            return false;
        }

        if (IsLocalOrPrivateLiteral(parsed))
        {
            error = "镜像前缀不能指向 localhost、回环或私网 IP。";
            return false;
        }

        if (ContainsRelativePathSegment(candidate))
        {
            error = "镜像前缀不能包含相对路径段。";
            return false;
        }

        var normalized = parsed.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? parsed
            : new Uri(parsed.AbsoluteUri + "/", UriKind.Absolute);
        if (normalized.AbsoluteUri.Length > MaximumMirrorBaseUrlLength)
        {
            error = $"规范化后的镜像前缀不能超过 {MaximumMirrorBaseUrlLength} 个字符。";
            return false;
        }
        uri = normalized;
        return true;
    }

    public static bool IsSafeHttpsUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        !string.IsNullOrWhiteSpace(uri.IdnHost);

    private static PluginRepositorySource CreateBuiltIn(
        string id,
        string name,
        string mirrorBaseUrl)
    {
        if (!TryNormalizeMirrorBaseUri(mirrorBaseUrl, out var uri, out var error))
            throw new InvalidOperationException($"内置镜像源 {name} 无效：{error}");
        return new PluginRepositorySource
        {
            Id = id,
            Name = name,
            Description = "面向中国大陆网络的第三方公共 GitHub 镜像，服务状态可能变化。",
            IsBuiltIn = true,
            MirrorBaseUri = uri
        };
    }

    private static bool IsLocalOrPrivateLiteral(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address))
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc)
            return true;
        if (bytes.Length != 4)
            return false;
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 0;
    }

    private static bool ContainsRelativePathSegment(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        var pathStart = authorityStart < 0
            ? -1
            : value.IndexOf('/', authorityStart + 3);
        if (pathStart < 0)
            return false;

        try
        {
            return value[pathStart..]
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .Any(segment => segment is "." or "..");
        }
        catch (UriFormatException)
        {
            return true;
        }
    }
}

internal sealed record PluginRepositorySourceConfiguration
{
    public string ActiveSourceId { get; init; } = PluginRepositorySources.Official.Id;

    public IReadOnlyList<PluginRepositorySource> CustomSources { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<PluginRepositorySource> AllSources =>
        [.. PluginRepositorySources.BuiltIn, .. CustomSources];

    [JsonIgnore]
    public PluginRepositorySource ActiveSource =>
        AllSources.FirstOrDefault(source => string.Equals(
            source.Id,
            ActiveSourceId,
            StringComparison.OrdinalIgnoreCase)) ?? PluginRepositorySources.Official;

    public PluginRepositorySourceConfiguration WithActive(string sourceId) =>
        this with
        {
            ActiveSourceId = AllSources.Any(source => string.Equals(
                source.Id,
                sourceId,
                StringComparison.OrdinalIgnoreCase))
                ? sourceId
                : PluginRepositorySources.Official.Id
        };
}

internal static class PluginRepositorySourceStore
{
    private const string ConfigurationKey = "pluginRepositorySources";
    private const int SchemaVersion = 1;
    private const int MaximumSerializedBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static PluginRepositorySourceConfiguration Load(out string? warning)
    {
        warning = null;
        try
        {
            return Deserialize(LauncherConfig.GetValue(ConfigurationKey), out warning);
        }
        catch (Exception exception)
        {
            warning = $"镜像源配置读取失败，已使用 GitHub 官方直连：{exception.Message}";
            return new PluginRepositorySourceConfiguration();
        }
    }

    public static bool Save(
        PluginRepositorySourceConfiguration configuration,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        error = string.Empty;
        if (!TryValidate(configuration, out error))
            return false;

        try
        {
            var payload = Serialize(configuration);
            if (!LauncherConfig.SetValue(ConfigurationKey, payload))
            {
                error = "无法写入启动器配置文件。";
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static string Serialize(PluginRepositorySourceConfiguration configuration)
    {
        if (!TryValidate(configuration, out var error))
            throw new InvalidOperationException(error);
        return JsonSerializer.Serialize(new PersistedConfiguration
        {
            SchemaVersion = SchemaVersion,
            ActiveSourceId = configuration.ActiveSourceId,
            CustomSources = configuration.CustomSources.Select(source => new PersistedSource
            {
                Id = source.Id,
                Name = source.Name,
                MirrorBaseUrl = source.MirrorBaseUri!.AbsoluteUri
            }).ToArray()
        }, JsonOptions);
    }

    internal static PluginRepositorySourceConfiguration Deserialize(
        string? payload,
        out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(payload))
            return new PluginRepositorySourceConfiguration();
        if (payload.Length > MaximumSerializedBytes)
        {
            warning = "镜像源配置超过大小上限，已使用 GitHub 官方直连。";
            return new PluginRepositorySourceConfiguration();
        }

        PersistedConfiguration? persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<PersistedConfiguration>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            warning = "镜像源配置格式损坏，已使用 GitHub 官方直连。";
            return new PluginRepositorySourceConfiguration();
        }

        if (persisted is null ||
            persisted.SchemaVersion != SchemaVersion ||
            persisted.CustomSources is null ||
            persisted.CustomSources.Count > PluginRepositorySources.MaximumCustomSourceCount)
        {
            warning = "镜像源配置版本或条目数量无效，已使用 GitHub 官方直连。";
            return new PluginRepositorySourceConfiguration();
        }

        var customSources = new List<PluginRepositorySource>();
        var skipped = false;
        foreach (var item in persisted.CustomSources)
        {
            if (item is null ||
                string.IsNullOrWhiteSpace(item.Id) ||
                !PluginRepositorySources.TryCreateCustom(
                    item.Name,
                    item.MirrorBaseUrl,
                    out var source,
                    out _,
                    item.Id) ||
                PluginRepositorySources.BuiltIn.Concat(customSources).Any(existing =>
                    string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.Name, source.Name, StringComparison.OrdinalIgnoreCase) ||
                    existing.MirrorBaseUri is not null &&
                    string.Equals(
                        existing.MirrorBaseUri.AbsoluteUri,
                        source.MirrorBaseUri!.AbsoluteUri,
                        StringComparison.OrdinalIgnoreCase)))
            {
                skipped = true;
                continue;
            }
            customSources.Add(source);
        }

        var allSources = PluginRepositorySources.BuiltIn.Concat(customSources).ToArray();
        var activeId = allSources.Any(source => string.Equals(
            source.Id,
            persisted.ActiveSourceId,
            StringComparison.OrdinalIgnoreCase))
            ? persisted.ActiveSourceId!
            : PluginRepositorySources.Official.Id;
        if (skipped || !string.Equals(activeId, persisted.ActiveSourceId, StringComparison.Ordinal))
            warning = "部分镜像源配置无效，已忽略并安全回退。";
        return new PluginRepositorySourceConfiguration
        {
            ActiveSourceId = activeId,
            CustomSources = customSources
        };
    }

    private static bool TryValidate(
        PluginRepositorySourceConfiguration configuration,
        out string error)
    {
        error = string.Empty;
        if (configuration.CustomSources is null ||
            configuration.CustomSources.Count > PluginRepositorySources.MaximumCustomSourceCount ||
            configuration.CustomSources.Any(source => source is null || source.IsBuiltIn || source.MirrorBaseUri is null))
        {
            error = "自定义镜像源列表无效或超过数量上限。";
            return false;
        }

        var allSources = configuration.AllSources;
        if (allSources.GroupBy(source => source.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1) ||
            allSources.GroupBy(source => source.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1) ||
            allSources.Where(source => source.MirrorBaseUri is not null)
                .GroupBy(source => source.MirrorBaseUri!.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1) ||
            !allSources.Any(source => string.Equals(
                source.Id,
                configuration.ActiveSourceId,
                StringComparison.OrdinalIgnoreCase)))
        {
            error = "镜像源名称、地址或活动源选择重复或无效。";
            return false;
        }

        foreach (var source in configuration.CustomSources)
        {
            if (!PluginRepositorySources.TryCreateCustom(
                    source.Name,
                    source.MirrorBaseUri!.AbsoluteUri,
                    out var validated,
                    out error,
                    source.Id) ||
                validated != source)
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = $"镜像源 {source.Name} 未通过规范化校验。";
                return false;
            }
        }
        return true;
    }

    private sealed record PersistedConfiguration
    {
        public int SchemaVersion { get; init; }

        public string? ActiveSourceId { get; init; }

        public IReadOnlyList<PersistedSource?>? CustomSources { get; init; }
    }

    private sealed record PersistedSource
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? MirrorBaseUrl { get; init; }
    }
}
