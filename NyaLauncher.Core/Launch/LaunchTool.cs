namespace NyaLauncher.Core.Launch;
using Auth;
using System.Diagnostics;
using Internal;



/// <summary>
/// 正版（Microsoft 账号）Minecraft 启动器。
/// </summary>
public sealed class MicrosoftMinecraftLauncher : IMicrosoftMinecraftLauncher
{
    private readonly IOfflineMinecraftLauncher _launcher;

    public MicrosoftMinecraftLauncher(IOfflineMinecraftLauncher? launcher = null)
    {
        _launcher = launcher ?? new OfflineMinecraftLauncher();
    }

    /// <inheritdoc />
    public async Task<MinecraftLaunchResult> LaunchAsync(
        MicrosoftAccount account,
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            throw new MinecraftLaunchException("正版账号缺少访问令牌，请先完成登录。");
        }

        if (account.IsExpired)
        {
            throw new MinecraftLaunchException(
                "正版账号的访问令牌已过期，请先通过 IMicrosoftAuthenticator 刷新或重新登录。");
        }

        return await _launcher.LaunchAsync(
            options.WithAccount(account),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// 从本地 Minecraft 目录构造并启动离线游戏进程。
/// 本类不负责下载版本文件，也不读取或保存任何在线账号令牌。
/// </summary>
public sealed class OfflineMinecraftLauncher : IOfflineMinecraftLauncher
{
    private readonly IJavaRuntimeLocator _javaRuntimeLocator;
    private readonly MinecraftVersionProfileLoader _profileLoader = new();
    private readonly MinecraftLibraryResolver _libraryResolver = new();
    private readonly MinecraftArgumentBuilder _argumentBuilder = new();

    public OfflineMinecraftLauncher(IJavaRuntimeLocator? javaRuntimeLocator = null)
    {
        _javaRuntimeLocator = javaRuntimeLocator ?? new JavaRuntimeLocator();
    }

    private async Task<MinecraftLaunchPlan> CreatePlanAsync(
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateMinecraftDirectory(options.MinecraftDirectory);

        var minecraftDirectory = Path.GetFullPath(options.MinecraftDirectory);
        var gameDirectory = Path.GetFullPath(options.GameDirectory ?? minecraftDirectory);
        if (!Directory.Exists(gameDirectory))
            throw new MinecraftLaunchException($"实例游戏目录不存在：{gameDirectory}");

        var profile = await _profileLoader.LoadAsync(
            minecraftDirectory,
            options.VersionId,
            cancellationToken);

        var features = MinecraftRuleEvaluator.CreateDefaultFeatures(
            options.WindowWidth > 0 && options.WindowHeight > 0);
        var (classpath, nativeLibraries) =
            _libraryResolver.Resolve(profile, minecraftDirectory, features);
        var nativeDirectory = _libraryResolver.ExtractNatives(profile.Id, nativeLibraries);

        try
        {
            var javaExecutable = _javaRuntimeLocator.FindJavaExecutable(
                options.JavaExecutable,
                profile.RequiredJavaMajorVersion,
                options.JavaRuntimeDirectory);
            var arguments = _argumentBuilder.Build(
                profile,
                options,
                nativeDirectory,
                classpath);

            return new MinecraftLaunchPlan(
                javaExecutable,
                gameDirectory,
                nativeDirectory,
                profile.RequiredJavaMajorVersion,
                arguments);
        }
        catch
        {
            MinecraftLibraryResolver.TryDeleteDirectory(nativeDirectory);
            throw;
        }
    }

    public async Task<MinecraftLaunchResult> LaunchAsync(
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = await CreatePlanAsync(options, cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = plan.JavaExecutable,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in plan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.Exited += (_, _) =>
                MinecraftLibraryResolver.TryDeleteDirectory(plan.NativeDirectory);

            if (!process.Start())
            {
                process.Dispose();
                throw new MinecraftLaunchException("Java 进程未能启动。");
            }

            WriteDebugArguments(plan);

            return new MinecraftLaunchResult(
                process,
                options.VersionId,
                options.Account.Username,
                plan.RequiredJavaMajorVersion);
        }
        catch (Exception ex)
        {
            MinecraftLibraryResolver.TryDeleteDirectory(plan.NativeDirectory);
            if (ex is MinecraftLaunchException or OperationCanceledException)
                throw;
            throw new MinecraftLaunchException("启动 Java 进程失败。", ex);
        }
    }

    private static void ValidateMinecraftDirectory(string minecraftDirectory)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) ||
            !Directory.Exists(minecraftDirectory))
        {
            throw new MinecraftLaunchException($"Minecraft 目录不存在：{minecraftDirectory}");
        }
    }

    /// <summary>
    /// 调试辅助：设置环境变量 NYALAUNCHER_DEBUG_ARGS=1 时，
    /// 将实际启动参数写入 %TEMP%\nya_launcher_debug_args.txt，便于排查登录/会话问题。
    /// </summary>
    private static void WriteDebugArguments(MinecraftLaunchPlan plan)
    {
        if (Environment.GetEnvironmentVariable("NYALAUNCHER_DEBUG_ARGS") != "1")
            return;

        try
        {
            var lines = new List<string>
            {
                $"java={plan.JavaExecutable}",
                $"cwd={plan.WorkingDirectory}",
                "--- arguments ---"
            };
            lines.AddRange(plan.Arguments);
            File.WriteAllLines(
                Path.Combine(Path.GetTempPath(), "nya_launcher_debug_args.txt"),
                lines);
        }
        catch
        {
            // 调试日志失败不影响游戏启动。
        }
    }
}


/// <summary>
/// 描述一次 Minecraft 安装路径解析的结果。
/// 根据传入路径是"根目录"还是"versions/版本号 独立实例目录"，字段会有不同的取值。
/// </summary>
/// <param name="MinecraftDirectory">Minecraft 根目录（如 ~/.minecraft），始终非空。</param>
/// <param name="PreferredVersionId">
/// 当传入的是独立实例目录时，为对应的版本号；传入根目录时为 null。
/// </param>
/// <param name="GameDirectory">
/// 独立实例目录（用于隔离 mods、config、saves 等）；传入根目录时为 null。
/// </param>
public sealed record MinecraftInstallationLocation(
    string MinecraftDirectory,
    string? PreferredVersionId,
    string? GameDirectory);

/// <summary>
/// 负责定位与解析 Minecraft 安装目录的静态工具类。
/// 支持三种能力：获取系统默认目录、解析用户传入的路径、枚举已安装的版本。
/// </summary>
public static class MinecraftDirectoryLocator
{
    /// <summary>
    /// 获取当前操作系统下 Minecraft 官方启动器使用的默认 .minecraft 目录。
    /// </summary>
    /// <returns>默认目录的完整路径。</returns>
    public static string GetDefaultDirectory()
    {
        // Windows：%APPDATA%\.minecraft
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // macOS：~/Library/Application Support/minecraft
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "minecraft");
        }

        // Linux 及其他未识别系统：~/.minecraft
        return Path.Combine(home, ".minecraft");
    }

    /// <summary>
    /// Minecraft 标准目录结构中应当存在的子文件夹列表。
    /// 仅包含目录骨架，不创建任何文件。
    /// </summary>
    private static readonly string[] StandardSubDirectories =
    [
        "versions",
        "assets",
        "libraries",
        "saves",
        "resourcepacks",
        "mods",
        "config",
        "crash-reports",
        "logs",
        "screenshots",
        "shaderpacks",
    ];

    /// <summary>
    /// 检测默认 Minecraft 目录是否存在；不存在时在平台默认路径下
    /// 创建符合 Minecraft 目录结构的空文件夹骨架，并将其路径写入启动器配置。
    /// </summary>
    /// <returns>保证存在的默认 Minecraft 目录路径。</returns>
    public static string EnsureDefaultDirectory()
    {
        var defaultDir = GetDefaultDirectory();

        if (!Directory.Exists(defaultDir))
        {
            Directory.CreateDirectory(defaultDir);
            foreach (var sub in StandardSubDirectories)
            {
                Directory.CreateDirectory(Path.Combine(defaultDir, sub));
            }
        }

        return defaultDir;
    }

    /// <summary>
    /// 接受 Minecraft 根目录，或 versions/&lt;版本号&gt; 形式的独立实例目录。
    /// </summary>
    /// <param name="path">用户输入的路径，可为根目录或某个具体版本的实例目录。</param>
    /// <returns>解析后的安装位置信息。</returns>
    /// <exception cref="MinecraftLaunchException">路径为空、不存在或不是有效的 Minecraft 目录时抛出。</exception>
    public static MinecraftInstallationLocation ResolveInstallationPath(string path)
    {
        // 空路径直接拒绝
        if (string.IsNullOrWhiteSpace(path))
            throw new MinecraftLaunchException("Minecraft 路径不能为空。");

        // 规范化：去掉首尾空白与引号、展开环境变量（%VAR%/$VAR）、转换为绝对路径
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        if (!Directory.Exists(fullPath))
            throw new MinecraftLaunchException($"Minecraft 路径不存在：{fullPath}");

        // 判断是否为 "versions/<版本号>" 形式的独立实例目录：
        // 父目录名为 versions，且目录内存在与目录同名的 <版本号>.json 版本描述文件
        var directoryName = Path.GetFileName(fullPath);
        var parent = Directory.GetParent(fullPath);
        if (parent is not null &&
            string.Equals(parent.Name, "versions", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(fullPath, $"{directoryName}.json")))
        {
            // 版本目录的上一级（versions 的父级）即为 Minecraft 根目录
            var root = parent.Parent?.FullName;
            if (root is null)
                throw new MinecraftLaunchException("无法确定版本目录对应的 Minecraft 根目录。");

            // 根目录仍需通过校验（必须包含 versions 文件夹）
            ValidateRootDirectory(root);
            return new MinecraftInstallationLocation(root, directoryName, fullPath);
        }

        // 不是实例目录，则按普通根目录处理并校验
        ValidateRootDirectory(fullPath);
        return new MinecraftInstallationLocation(fullPath, null, null);
    }

    /// <summary>
    /// 扫描指定 Minecraft 根目录下已安装的版本列表。
    /// 只统计"版本文件夹内存在同名 .json 版本描述文件"的完整版本，
    /// 可过滤掉下载中断留下的残缺目录。
    /// </summary>
    /// <param name="minecraftDirectory">Minecraft 根目录。</param>
    /// <returns>按名称忽略大小写降序排列的版本 ID 列表；无 versions 目录时返回空列表。</returns>
    public static IReadOnlyList<string> GetInstalledVersionIds(string minecraftDirectory)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");

        // 尚未下载任何版本时直接返回空列表
        if (!Directory.Exists(versionsDirectory))
        {
            return [];
        }

        // 第一轮：收集所有有效版本 ID
        var allIds = Directory.EnumerateDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => File.Exists(Path.Combine(versionsDirectory, id!, $"{id}.json")))
            .Cast<string>()
            .ToList();

        // 第二轮：收集所有 inheritsFrom 目标（即被其他版本依赖的原版版本）
        var dependencyTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in allIds)
        {
            try
            {
                var jsonPath = Path.Combine(versionsDirectory, id, $"{id}.json");
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(jsonPath));
                var inheritsFrom = doc.RootElement.TryGetProperty("inheritsFrom", out var prop)
                    ? prop.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(inheritsFrom))
                    dependencyTargets.Add(inheritsFrom);
            }
            catch
            {
                // 解析失败不影响扫描
            }
        }

        // 第三轮：过滤掉仅作为依赖存在的原版版本
        // 保留：有自己内容的版本（NeoForge、Fabric 等）以及不被任何人依赖的独立版本
        return allIds
            .Where(id => !dependencyTargets.Contains(id))
            .OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 校验指定路径是否为有效的 Minecraft 根目录（必须包含 versions 文件夹）。
    /// </summary>
    /// <param name="root">待校验的根目录路径。</param>
    /// <exception cref="MinecraftLaunchException">缺少 versions 文件夹时抛出。</exception>
    private static void ValidateRootDirectory(string root)
    {
        if (!Directory.Exists(Path.Combine(root, "versions")))
        {
            throw new MinecraftLaunchException(
                $"该路径不是有效的 Minecraft 根目录，缺少 versions 文件夹：{root}");
        }
    }
}
