using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NyaLauncher.Core.Launch;

/// <summary>
/// Java 运行时定位器的抽象接口。
/// 负责在系统中找到可用的 java 可执行文件，并可校验其版本是否满足 Minecraft 版本的最低要求。
/// </summary>
public interface IJavaRuntimeLocator
{
    /// <summary>
    /// 查找 Java 可执行文件。
    /// </summary>
    /// <param name="configuredPath">用户显式配置的 java 路径（最高优先）。</param>
    /// <param name="requiredMajorVersion">Minecraft 版本要求的最低 Java 主版本；为 null 时不校验版本。</param>
    /// <param name="runtimeDirectory">Minecraft runtime 根目录（会递归扫描其中的 java）；为空时读取 NYALAUNCHER_JAVA_RUNTIME。</param>
    /// <returns>符合条件的 java 可执行文件完整路径。</returns>
    string FindJavaExecutable(
        string? configuredPath = null,
        int? requiredMajorVersion = null,
        string? runtimeDirectory = null);
}

/// <summary>
/// 默认的 Java 运行时定位器实现。
/// 按优先级依次从"显式配置、NYALAUNCHER_JAVA、Minecraft runtime、JAVA_HOME、PATH"寻找 java，
/// 在要求最低版本时通过执行 <c>java -version</c> 校验版本，并优先选择显式配置、其次选择最接近最低要求的版本。
/// </summary>
public sealed class JavaRuntimeLocator : IJavaRuntimeLocator
{
    /// <summary>
    /// 用于解析 <c>java -version</c> 输出中的主/次版本号。
    /// 匹配形如 version "21.0.1" 或旧式的 version "1.8.0_202"。
    /// </summary>
    private static readonly Regex JavaVersionPattern =
        new("version\\s+\"(?<major>\\d+)(?:\\.(?<minor>\\d+))?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string FindJavaExecutable(
        string? configuredPath = null,
        int? requiredMajorVersion = null,
        string? runtimeDirectory = null)
    {
        // 收集所有候选 java 路径；IsPreferred 表示该来源是否为"显式配置"（优先返回）
        var candidates = new List<(string? Path, bool IsPreferred)>();

        // 1. 用户显式指定的路径（来自启动选项 JavaExecutable）
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add((configuredPath, true));
        }

        // 2. NYALAUNCHER_JAVA 环境变量
        candidates.Add((Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA"), true));

        // 3. Minecraft runtime 目录：优先使用参数，其次读取 NYALAUNCHER_JAVA_RUNTIME，
        //    递归扫描其中所有名为 java/java.exe 的可执行文件
        var configuredRuntime = !string.IsNullOrWhiteSpace(runtimeDirectory)
            ? runtimeDirectory
            : Environment.GetEnvironmentVariable("NYALAUNCHER_JAVA_RUNTIME");
        candidates.AddRange(EnumerateRuntimeJavaExecutables(configuredRuntime)
            .Select(javaExecutable => ((string?)javaExecutable, false)));

        // 4. JAVA_HOME/bin/java
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add((Path.Combine(javaHome, "bin", GetExecutableName()), false));
        }

        // 5. PATH 环境变量中的每个目录
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

            // 展开环境变量（%VAR%/$VAR），去重（同一路径只处理一次），并确认文件存在
            var expandedPath = Environment.ExpandEnvironmentVariables(candidate.Path);
            if (!visitedPaths.Add(expandedPath) || !File.Exists(expandedPath))
                continue;

            var fullPath = Path.GetFullPath(expandedPath);

            // 不要求特定版本时，直接返回第一个找到的 java
            if (requiredMajorVersion is null)
                return fullPath;

            // 要求最低版本时，执行 java -version 探测实际主版本
            var detectedVersion = TryGetJavaMajorVersion(fullPath);
            if (detectedVersion is int majorVersion)
            {
                discoveredVersions.Add(majorVersion);
                // 版本低于最低要求则跳过
                if (majorVersion < requiredMajorVersion)
                    continue;

                // 显式配置优先；自动探测则在扫描完成后选择最接近最低要求的版本。
                if (candidate.IsPreferred)
                    return fullPath;

                compatibleCandidates.Add((fullPath, majorVersion, index));
            }
        }

        // 在自动探测的候选中，选择"版本最低但满足要求"（最接近最低要求）且来源顺序靠前的
        if (compatibleCandidates.Count > 0)
        {
            return compatibleCandidates
                .OrderBy(candidate => candidate.MajorVersion)
                .ThenBy(candidate => candidate.Index)
                .First()
                .Path;
        }

        // 全部不满足：构造包含需求与已检测版本信息的错误提示
        var requirement = requiredMajorVersion is int required
            ? $"该 Minecraft 版本至少需要 Java {required}。"
            : string.Empty;
        var discovered = discoveredVersions.Count > 0
            ? $" 已检测到 Java {string.Join("、", discoveredVersions.Distinct().Order())}。"
            : string.Empty;
        throw new MinecraftLaunchException(
            $"{requirement}{discovered} 请配置兼容版本的 JAVA_HOME、NYALAUNCHER_JAVA 或 runtime 目录。");
    }

    /// <summary>
    /// 返回当前平台下 Java 可执行文件的文件名（Windows 为 java.exe，其余为 java）。
    /// </summary>
    private static string GetExecutableName() => OperatingSystem.IsWindows() ? "java.exe" : "java";

    /// <summary>
    /// 递归枚举 runtime 目录下所有名为 java/java.exe 的文件。
    /// 使用手写枚举器是为了确保迭代中断时也能正确释放文件系统枚举资源。
    /// </summary>
    private static IEnumerable<string> EnumerateRuntimeJavaExecutables(string? runtimeDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            yield break;

        // 规范化：展开环境变量、去掉首尾引号
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

    /// <summary>
    /// 执行 <c>java -version</c> 探测 Java 的主版本号。
    /// 兼容新旧两种版本号格式：新式 "21.0.1" 直接取主版本 21；
    /// 旧式 "1.8.0_202" 的主版本为 1、次版本为 8，需要返回次版本 8 作为实际主版本。
    /// 探测失败（进程无法启动、超时、输出无法解析）时返回 null，不影响整体查找流程。
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

            // java -version 通常输出到标准错误，但为兼容不同 JVM 实现，两者都读取
            var standardError = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();

            // 超时 3 秒仍未退出则强制结束并视为探测失败
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

            // 旧式版本号：如 1.8，主版本是 1，实际对应的 Java 主版本是次版本 8
            if (major == 1 &&
                int.TryParse(match.Groups["minor"].Value, out var legacyMajor))
            {
                return legacyMajor;
            }

            return major;
        }
        catch
        {
            // 任何异常（如文件不是有效的可执行文件）都视为探测失败
            return null;
        }
    }

    /// <summary>
    /// 返回适合当前平台的路径比较器：Windows 忽略大小写，其他系统区分大小写。
    /// </summary>
    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
