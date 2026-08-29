using System.Diagnostics;
using System.Text.RegularExpressions;
using NyaLauncher.Core.Tools;

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

    /// <summary>
    /// 在不进行"最低版本"回退的前提下，查找主版本精确匹配 <paramref name="requiredMajorVersion"/> 的 Java。
    /// 找不到时返回 null（不抛异常），供调用方决定是否自动下载所需 Java。
    /// 显式配置的精确匹配优先于自动探测的精确匹配。
    /// </summary>
    string? FindExactMatchJava(
        string? configuredPath,
        int requiredMajorVersion,
        string? runtimeDirectory = null);
}

/// <summary>
/// 默认的 Java 运行时定位器实现。
/// 按优先级依次从"显式配置、NYALAUNCHER_JAVA、Minecraft runtime、JAVA_HOME、PATH"寻找 java，
/// 在要求版本时通过执行 <c>java -version</c> 探测版本，并按以下优先级选择：
/// 1) 主版本精确匹配 requiredMajorVersion（避免用 Java 25 启动要求 Java 17 的旧加载器，
///    如 Forge 1.20.x 在 Java 21+ 上会因 JPMS 模块冲突崩溃）；
/// 2) 显式配置且 ≥ 最低要求；
/// 3) 自动探测中最低且 ≥ 最低要求。
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
        var candidates = BuildCandidates(configuredPath, runtimeDirectory);

        var required = requiredMajorVersion;
        var discoveredVersions = new List<int>();
        // (Path, MajorVersion, Index, IsPreferred)
        var probed = new List<(string Path, int MajorVersion, int Index, bool IsPreferred)>();
        var visitedPaths = new HashSet<string>(PathUtil.PathComparer);
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
            if (required is null)
                return fullPath;

            // 要求版本时，执行 java -version 探测实际主版本
            var detectedVersion = TryGetJavaMajorVersion(fullPath);
            if (detectedVersion is int majorVersion)
            {
                discoveredVersions.Add(majorVersion);
                probed.Add((fullPath, majorVersion, index, candidate.IsPreferred));
            }
        }

        if (probed.Count == 0)
        {
            var requirement = required is int rv
                ? $"该 Minecraft 版本至少需要 Java {rv}。"
                : string.Empty;
            throw new MinecraftLaunchException(
                $"{requirement}未找到可用的 Java 运行时。请在启动器下载页安装 Java，或配置 JAVA_HOME / NYALAUNCHER_JAVA。");
        }

        if (required is not int requiredVersion)
            return probed[0].Path; // 理论不可达（required 为 null 时已在循环内返回）

        // 优先：主版本精确匹配——避免用 Java 25 启动要求 Java 17 的旧版本/加载器
        // （如 Forge 1.20.x 在 Java 21+ 上会因 JPMS 模块冲突崩溃）。
        // 精确匹配中显式配置优先，其次来源顺序。
        var exact = probed
            .Where(c => c.MajorVersion == requiredVersion)
            .OrderBy(c => c.IsPreferred ? 0 : 1)
            .ThenBy(c => c.Index)
            .ToList();
        if (exact.Count > 0)
            return exact[0].Path;

        // 次选：显式配置且 ≥ 最低要求（尊重用户指定，只要不低于最低版本）
        var preferredCompatible = probed
            .Where(c => c.IsPreferred && c.MajorVersion >= requiredVersion)
            .OrderBy(c => c.Index)
            .ToList();
        if (preferredCompatible.Count > 0)
            return preferredCompatible[0].Path;

        // 再次：自动探测中最低且 ≥ 最低要求（最接近最低要求）
        var autoCompatible = probed
            .Where(c => c.MajorVersion >= requiredVersion)
            .OrderBy(c => c.MajorVersion)
            .ThenBy(c => c.Index)
            .ToList();
        if (autoCompatible.Count > 0)
            return autoCompatible[0].Path;

        // 全部低于最低要求
        throw new MinecraftLaunchException(
            $"该 Minecraft 版本至少需要 Java {requiredVersion}。" +
            $" 已检测到 Java {string.Join("、", discoveredVersions.Distinct().Order())}。" +
            " 请在启动器下载页安装兼容的 Java 运行时，或配置 JAVA_HOME / NYALAUNCHER_JAVA。");
    }

    /// <inheritdoc />
    public string? FindExactMatchJava(
        string? configuredPath,
        int requiredMajorVersion,
        string? runtimeDirectory = null)
    {
        var candidates = BuildCandidates(configuredPath, runtimeDirectory);
        var visitedPaths = new HashSet<string>(PathUtil.PathComparer);
        string? preferredExact = null;

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (string.IsNullOrWhiteSpace(candidate.Path))
                continue;

            var expandedPath = Environment.ExpandEnvironmentVariables(candidate.Path);
            if (!visitedPaths.Add(expandedPath) || !File.Exists(expandedPath))
                continue;

            var fullPath = Path.GetFullPath(expandedPath);
            if (TryGetJavaMajorVersion(fullPath) is int major &&
                major == requiredMajorVersion)
            {
                // 显式配置的精确匹配优先返回；否则记录第一个自动探测的精确匹配
                if (candidate.IsPreferred)
                    return fullPath;
                preferredExact ??= fullPath;
            }
        }

        return preferredExact;
    }

    /// <summary>
    /// 列出所有可用的 Java 可执行文件路径（去重、存在性过滤），
    /// 供设置页「自动检索」一次性全部加入管理列表。
    /// </summary>
    public IReadOnlyList<string> FindAllJavaExecutables(string? runtimeDirectory = null)
    {
        var candidates = BuildCandidates(configuredPath: null, runtimeDirectory);
        var results = new List<string>();
        var visitedPaths = new HashSet<string>(PathUtil.PathComparer);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Path))
                continue;

            var expandedPath = Environment.ExpandEnvironmentVariables(candidate.Path);
            if (!visitedPaths.Add(expandedPath) || !File.Exists(expandedPath))
                continue;

            results.Add(Path.GetFullPath(expandedPath));
        }

        return results;
    }

    /// <summary>
    /// 收集所有候选 java 路径；IsPreferred 表示该来源是否为"显式配置"（优先返回）。
    /// </summary>
    private List<(string? Path, bool IsPreferred)> BuildCandidates(
        string? configuredPath, string? runtimeDirectory)
    {
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

        return candidates;
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
    /// 探测指定 java 可执行文件的主版本号；无法执行或解析失败时返回 null。
    /// 供设置页等 UI 在添加 Java 路径时即时识别版本。
    /// </summary>
    public static int? TryDetectMajorVersion(string javaExecutable) =>
        TryGetJavaMajorVersion(javaExecutable);

    /// <summary>
    /// 执行 <c>java -version</c> 探测 Java 的主版本号。
    /// 兼容新旧两种版本号格式：新式 "21.0.1" 直接取主版本 21；
    /// 旧式 "1.8.0_202" 的主版本为 1、次版本为 8，需要返回次版本 8 作为实际主版本。
    /// 先等待退出（3 秒超时）再读流，避免同步 ReadToEnd 在 JVM 挂起时无限阻塞。
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

            // 先等待退出（超时则强杀），再读取输出，杜绝 ReadToEnd 永久阻塞
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var standardError = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();

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
}
