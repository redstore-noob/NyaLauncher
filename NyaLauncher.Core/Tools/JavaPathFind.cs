using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NyaLauncher.Core.Tools;

/// <summary>
/// 表示已找到的 Java 安装信息。
/// </summary>
public sealed class JavaPathInfo
{
    /// <summary>
    /// Java 可执行文件的完整路径。
    /// </summary>
    public string? JavaExecutablePath { get; init; }

    /// <summary>
    /// Java 安装根目录，通常是 Java Home。
    /// </summary>
    public string? JavaHomePath { get; init; }

    /// <summary>
    /// Java 的版本字符串，例如 21.0.2。
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// 指示当前是否已经成功找到有效的 Java 可执行文件。
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(JavaExecutablePath);

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Version)
            ? JavaExecutablePath ?? "Unable to find Java"
            : $"{JavaExecutablePath} ({Version})";
    }
}

/// <summary>
/// 负责根据当前操作系统查找可用的 Java 安装路径。
/// </summary>
public static class JavaPathFind
{
    /// <summary>
    /// 根据当前平台自动选择 Windows、macOS 或 Linux 的查找逻辑。
    /// </summary>
    public static JavaPathInfo FindJava()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindOnWindows();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindOnMacOS();
        }

        return FindOnLinux();
    }

    /// <summary>
    /// 查找当前平台下的所有可用 Java 安装，并附带各自的版本信息。
    /// </summary>
    public static IReadOnlyList<JavaPathInfo> FindAllJava()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindAllOnWindows();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindAllOnMacOS();
        }

        return FindAllOnLinux();
    }

    /// <summary>
    /// 在 Windows 平台上查找 Java。
    /// </summary>
    public static JavaPathInfo FindOnWindows()
    {
        return FindJavaFromCandidates(GetWindowsCandidatePaths(), "java.exe", "javaw.exe");
    }

    private static IReadOnlyList<JavaPathInfo> FindAllOnWindows()
    {
        return FindAllJavaFromCandidates(GetWindowsCandidatePaths(), "java.exe", "javaw.exe");
    }

    /// <summary>
    /// 在 macOS 平台上查找 Java。
    /// </summary>
    public static JavaPathInfo FindOnMacOS()
    {
        return FindJavaFromCandidates(GetMacOSCandidatePaths(), "java");
    }

    private static IReadOnlyList<JavaPathInfo> FindAllOnMacOS()
    {
        return FindAllJavaFromCandidates(GetMacOSCandidatePaths(), "java");
    }

    /// <summary>
    /// 在 Linux 平台上查找 Java。
    /// </summary>
    public static JavaPathInfo FindOnLinux()
    {
        return FindJavaFromCandidates(GetLinuxCandidatePaths(), "java");
    }

    private static IReadOnlyList<JavaPathInfo> FindAllOnLinux()
    {
        return FindAllJavaFromCandidates(GetLinuxCandidatePaths(), "java");
    }

    /// <summary>
    /// 通过执行 Java 的 <c>-version</c> 命令来获取版本信息。
    /// </summary>
    public static string? GetJavaVersion(string javaExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(javaExecutablePath))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(javaExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardError.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = process.StandardOutput.ReadToEnd();
            }

            process.WaitForExit(5000);

            return ExtractVersion(output);
        }
        catch
        {
            return null;
        }
    }

    private static JavaPathInfo FindJavaFromCandidates(IEnumerable<string> candidatePaths, params string[] executableNames)
    {
        return FindAllJavaFromCandidates(candidatePaths, executableNames).FirstOrDefault() ?? new JavaPathInfo();
    }

    private static IReadOnlyList<JavaPathInfo> FindAllJavaFromCandidates(IEnumerable<string> candidatePaths, params string[] executableNames)
    {
        var result = new List<JavaPathInfo>();

        foreach (var candidatePath in candidatePaths)
        {
            if (!Directory.Exists(candidatePath))
            {
                continue;
            }

            foreach (var executableName in executableNames)
            {
                var matchedPath = FindExecutableWithinDirectory(candidatePath, executableName);
                if (matchedPath is null)
                {
                    continue;
                }

                var version = GetJavaVersion(matchedPath);
                var javaHome = InferJavaHome(matchedPath);

                result.Add(new JavaPathInfo
                {
                    JavaExecutablePath = matchedPath,
                    JavaHomePath = javaHome,
                    Version = version
                });
            }
        }

        return result.DistinctBy(static item => item.JavaExecutablePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindExecutableWithinDirectory(string directory, string executableName)
    {
        var candidates = new[]
        {
            Path.Combine(directory, executableName),
            Path.Combine(directory, "bin", executableName),
            Path.Combine(directory, "bin", "java")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var binDirectory = Path.Combine(directory, "bin");
        if (Directory.Exists(binDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(binDirectory, executableName, SearchOption.TopDirectoryOnly))
            {
                if (File.Exists(file))
                {
                    return file;
                }
            }
        }

        return null;
    }

    private static string? InferJavaHome(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        if (string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(directory);
        }

        return directory;
    }

    private static string? ExtractVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(output, @"(\d+(?:\.\d+){0,2})");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static IEnumerable<string> GetWindowsCandidatePaths()
    {
        var paths = new List<string>();

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            paths.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        paths.AddRange(new[]
        {
            "C:\\Program Files\\Java",
            "C:\\Program Files\\Eclipse Adoptium",
            "C:\\Program Files\\Microsoft",
            "C:\\Program Files\\BellSoft\\Liberica",
            "C:\\Program Files\\Zulu",
            "C:\\Program Files\\Amazon Corretto"
        });

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetMacOSCandidatePaths()
    {
        var paths = new List<string>();

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            paths.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        paths.AddRange(new[]
        {
            "/Library/Java/JavaVirtualMachines",
            "/System/Library/Java/JavaVirtualMachines",
            "/opt/homebrew/opt/openjdk/bin",
            "/opt/homebrew/opt/java/bin",
            "/usr/local/opt/openjdk/bin",
            "/usr/local/opt/java/bin",
            "/opt/homebrew/bin",
            "/usr/local/bin"
        });

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetLinuxCandidatePaths()
    {
        var paths = new List<string>();

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            paths.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        paths.AddRange(new[]
        {
            "/usr/lib/jvm",
            "/usr/java",
            "/opt/java",
            "/usr/local/java",
            "/snap/bin",
            "/usr/bin",
            "/bin"
        });

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
