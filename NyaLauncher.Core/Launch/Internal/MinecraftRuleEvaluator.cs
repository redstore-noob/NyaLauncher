using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NyaLauncher.Core.Launch.Internal;

internal static class MinecraftRuleEvaluator
{
    public static bool IsAllowed(JsonElement item, IReadOnlyDictionary<string, bool> features)
    {
        if (!item.TryGetProperty("rules", out var rules) ||
            rules.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!Matches(rule, features))
                continue;

            allowed = rule.TryGetProperty("action", out var action) &&
                      action.GetString() == "allow";
        }

        return allowed;
    }

    private static bool Matches(JsonElement rule, IReadOnlyDictionary<string, bool> features)
    {
        if (rule.TryGetProperty("os", out var os) && !MatchesOperatingSystem(os))
            return false;

        if (!rule.TryGetProperty("features", out var requiredFeatures) ||
            requiredFeatures.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        foreach (var feature in requiredFeatures.EnumerateObject())
        {
            var required = feature.Value.ValueKind == JsonValueKind.True;
            if (!features.TryGetValue(feature.Name, out var actual))
                actual = false;
            if (actual != required)
                return false;
        }

        return true;
    }

    private static bool MatchesOperatingSystem(JsonElement os)
    {
        if (os.TryGetProperty("name", out var name) &&
            !string.Equals(name.GetString(), GetOperatingSystemName(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (os.TryGetProperty("arch", out var arch) &&
            !MatchesArchitecture(arch.GetString()))
        {
            return false;
        }

        if (os.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.String)
        {
            try
            {
                return Regex.IsMatch(
                    RuntimeInformation.OSDescription,
                    version.GetString()!,
                    RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return true;
    }

    public static string GetOperatingSystemName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "linux";
    }

    private static bool MatchesArchitecture(string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        var actual = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x86_64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "aarch64",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        };

        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ||
               expected == "x64" && actual == "x86_64" ||
               expected == "arm64" && actual == "aarch64";
    }
}
