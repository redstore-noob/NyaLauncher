using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Core.Launch.Internal;

internal sealed class MinecraftArgumentBuilder
{
    private static readonly Regex PlaceholderPattern =
        new(@"\$\{(?<name>[^}]+)\}", RegexOptions.CultureInvariant);

    public IReadOnlyList<string> Build(
        MinecraftVersionProfile profile,
        MinecraftLaunchOptions options,
        string nativeDirectory,
        IReadOnlyList<string> classpath)
    {
        ValidateMemory(options);

        var minecraftDirectory = Path.GetFullPath(options.MinecraftDirectory);
        var gameDirectory = Path.GetFullPath(options.GameDirectory ?? minecraftDirectory);
        var assetsDirectory = Path.Combine(minecraftDirectory, "assets");
        var librariesDirectory = Path.Combine(minecraftDirectory, "libraries");
        var gameAssetsDirectory = GetLegacyGameAssetsDirectory(assetsDirectory, profile.AssetsId);
        var classpathValue = string.Join(Path.PathSeparator, classpath);
        var (authPlayerName, authUuid, authAccessToken, authSession, clientId, authXuid, userType) =
            options.Account switch
            {
                OfflineAccount offline => (
                    offline.Username,
                    ToCompactUuid(offline.Uuid),
                    "0",
                    $"token:0:{ToCompactUuid(offline.Uuid)}",
                    string.Empty,
                    string.Empty,
                    "legacy"),
                MicrosoftAccount microsoft => (
                    microsoft.Username,
                    ToCompactUuid(microsoft.Uuid),
                    microsoft.AccessToken,
                    $"token:{microsoft.AccessToken}:{ToCompactUuid(microsoft.Uuid)}",
                    microsoft.ClientId,
                    microsoft.XboxUserId,
                    "msa"),
                _ => throw new MinecraftLaunchException(
                    $"不支持的账号类型：{options.Account.GetType().Name}")
            };
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = authPlayerName,
            ["version_name"] = profile.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsDirectory,
            ["assets_index_name"] = profile.AssetsId,
            ["auth_uuid"] = authUuid,
            ["auth_access_token"] = authAccessToken,
            ["auth_session"] = authSession,
            ["clientid"] = clientId,
            ["auth_xuid"] = authXuid,
            ["user_type"] = userType,
            ["version_type"] = profile.VersionType,
            ["user_properties"] = "{}",
            ["profile_properties"] = "{}",
            ["game_assets"] = gameAssetsDirectory,
            ["natives_directory"] = nativeDirectory,
            ["launcher_name"] = options.LauncherName,
            ["launcher_version"] = options.LauncherVersion,
            ["classpath"] = classpathValue,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = librariesDirectory,
            ["resolution_width"] = options.WindowWidth.ToString(),
            ["resolution_height"] = options.WindowHeight.ToString()
        };

        var features = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["has_custom_resolution"] = options.WindowWidth > 0 && options.WindowHeight > 0,
            ["is_demo_user"] = false,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };

        var result = new List<string>
        {
            $"-Xms{options.MinimumMemoryMb}M",
            $"-Xmx{options.MaximumMemoryMb}M"
        };
        result.AddRange(options.AdditionalJvmArguments);

        if (profile.JvmArguments.Count > 0)
        {
            AppendModernArguments(result, profile.JvmArguments, features, placeholders);
        }
        else
        {
            result.Add($"-Djava.library.path={nativeDirectory}");
            result.Add("-cp");
            result.Add(classpathValue);
        }

        if (!ContainsClasspathArgument(result))
        {
            result.Add("-cp");
            result.Add(classpathValue);
        }

        result.Add(profile.MainClass);

        if (profile.GameArguments.Count > 0)
        {
            AppendModernArguments(result, profile.GameArguments, features, placeholders);
        }
        else if (!string.IsNullOrWhiteSpace(profile.LegacyGameArguments))
        {
            result.AddRange(TokenizeLegacyArguments(profile.LegacyGameArguments)
                .Select(argument => ReplacePlaceholders(argument, placeholders)));
        }
        else
        {
            throw new MinecraftLaunchException("版本配置没有可用的游戏启动参数。");
        }

        result.AddRange(options.AdditionalGameArguments);
        return result;
    }

    /// <summary>
    /// 将 UUID 归一化为 32 位无连字符格式。
    /// 官方启动器与主流启动器（HMCL 等）对 --uuid / --session 中的 UUID 均使用
    /// 无连字符格式；此处归一化可兼容历史版本存储的带连字符 UUID。
    /// </summary>
    private static string ToCompactUuid(string uuid)
    {
        if (string.IsNullOrEmpty(uuid))
            return uuid;

        return uuid.Replace("-", "");
    }

    private static void AppendModernArguments(
        List<string> target,
        IEnumerable<JsonElement> argumentElements,
        IReadOnlyDictionary<string, bool> features,
        IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var argument in argumentElements)
        {
            if (argument.ValueKind == JsonValueKind.String)
            {
                target.Add(ReplacePlaceholders(argument.GetString()!, placeholders));
                continue;
            }

            if (argument.ValueKind != JsonValueKind.Object ||
                !MinecraftRuleEvaluator.IsAllowed(argument, features) ||
                !argument.TryGetProperty("value", out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                target.Add(ReplacePlaceholders(value.GetString()!, placeholders));
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                target.AddRange(value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => ReplacePlaceholders(item.GetString()!, placeholders)));
            }
        }
    }

    private static string ReplacePlaceholders(
        string value,
        IReadOnlyDictionary<string, string> placeholders)
    {
        return PlaceholderPattern.Replace(value, match =>
        {
            var name = match.Groups["name"].Value;
            if (!placeholders.TryGetValue(name, out var replacement))
            {
                throw new MinecraftLaunchException($"版本参数包含暂不支持的占位符：{name}");
            }

            return replacement;
        });
    }

    private static IEnumerable<string> TokenizeLegacyArguments(string commandLine)
    {
        var current = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (character == '\\' &&
                index + 1 < commandLine.Length &&
                commandLine[index + 1] == '"')
            {
                current.Append('"');
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (quoted)
            throw new MinecraftLaunchException("旧版启动参数包含未闭合的引号。");
        if (current.Length > 0)
            yield return current.ToString();
    }

    private static bool ContainsClasspathArgument(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] is "-cp" or "-classpath")
                return true;
        }

        return false;
    }

    private static string GetLegacyGameAssetsDirectory(string assetsDirectory, string assetsId)
    {
        var virtualDirectory = Path.Combine(assetsDirectory, "virtual", assetsId);
        return Directory.Exists(virtualDirectory) ? virtualDirectory : assetsDirectory;
    }

    private static void ValidateMemory(MinecraftLaunchOptions options)
    {
        if (options.MinimumMemoryMb <= 0 ||
            options.MaximumMemoryMb < options.MinimumMemoryMb)
        {
            throw new MinecraftLaunchException("内存设置无效：最大内存必须大于等于最小内存。");
        }
    }
}
