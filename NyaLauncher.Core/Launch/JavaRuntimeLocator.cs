using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NyaLauncher.Core.Launch;

public interface IJavaRuntimeLocator
{
    string FindJavaExecutable(
        string? configuredPath = null,
        int? requiredMajorVersion = null,
        string? runtimeDirectory = null);
}

public sealed class JavaRuntimeLocator : IJavaRuntimeLocator
{
    private static readonly Regex JavaVersionPattern =
        new("version\\s+\"(?<major>\\d+)(?:\\.(?<minor>\\d+))?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string FindJavaExecutable(
        string? configuredPath = null,
        int? requiredMajorVersion = null,
        string? runtimeDirectory = null)
    {
        var candidates = new List<(string? Path, bool IsPreferred)>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add((configuredPath, true));
        }

        candidates.Add((Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA"), true));

        var configuredRuntime = !string.IsNullOrWhiteSpace(runtimeDirectory)
            ? runtimeDirectory
            : Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA_RUNTIME");
        candidates.AddRange(EnumerateRuntimeJavaExecutables(configuredRuntime)
            .Select(javaExecutable => ((string?)javaExecutable, false)));

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add((Path.Combine(javaHome, "bin", GetExecutableName()), false));
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory =>
                    ((string?)Path.Combine(directory.Trim().Trim('"'), GetExecutableName()), false)));
        }

        var discoveredVersions = new List<int>();
        var compatibleCandidates = new List<(string Path, int MajorVersion, int Index)>();
        var visitedPaths = new HashSet<string>(GetPathComparer());
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (string.IsNullOrWhiteSpace(candidate.Path))
                continue;

            var expandedPath = Environment.ExpandEnvironmentVariables(candidate.Path);
            if (!visitedPaths.Add(expandedPath) || !File.Exists(expandedPath))
                continue;

            var fullPath = Path.GetFullPath(expandedPath);
            if (requiredMajorVersion is null)
                return fullPath;

            var detectedVersion = TryGetJavaMajorVersion(fullPath);
            if (detectedVersion is int majorVersion)
            {
                discoveredVersions.Add(majorVersion);
                if (majorVersion < requiredMajorVersion)
                    continue;

                // 显式配置优先；自动探测则在扫描完成后选择最接近最低要求的版本。
                if (candidate.IsPreferred)
                    return fullPath;

                compatibleCandidates.Add((fullPath, majorVersion, index));
            }
        }

        if (compatibleCandidates.Count > 0)
        {
            return compatibleCandidates
                .OrderBy(candidate => candidate.MajorVersion)
                .ThenBy(candidate => candidate.Index)
                .First()
                .Path;
        }

        var requirement = requiredMajorVersion is int required
            ? $"该 Minecraft 版本至少需要 Java {required}。"
            : string.Empty;
        var discovered = discoveredVersions.Count > 0
            ? $" 已检测到 Java {string.Join("、", discoveredVersions.Distinct().Order())}。"
            : string.Empty;
        throw new MinecraftLaunchException(
            $"{requirement}{discovered} 请配置兼容版本的 JAVA_HOME、NYALAUNCHER_JAVA 或 runtime 目录。");
    }

    private static string GetExecutableName() => OperatingSystem.IsWindows() ? "java.exe" : "java";

    private static IEnumerable<string> EnumerateRuntimeJavaExecutables(string? runtimeDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            yield break;

        var expanded = Environment.ExpandEnvironmentVariables(runtimeDirectory.Trim().Trim('"'));
        if (!Directory.Exists(expanded))
            yield break;

        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(
                expanded,
                GetExecutableName(),
                SearchOption.AllDirectories).GetEnumerator();
            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }
        finally
        {
            enumerator?.Dispose();
        }
    }

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

            var standardError = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var match = JavaVersionPattern.Match($"{standardError}\n{standardOutput}");
            if (!match.Success ||
                !int.TryParse(match.Groups["major"].Value, out var major))
            {
                return null;
            }

            if (major == 1 &&
                int.TryParse(match.Groups["minor"].Value, out var legacyMajor))
            {
                return legacyMajor;
            }

            return major;
        }
        catch
        {
            return null;
        }
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
