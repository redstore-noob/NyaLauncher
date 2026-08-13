using System.Diagnostics;
using NyaLauncher.Core.Launch.Auth;
using NyaLauncher.Core.Launch.Internal;

namespace NyaLauncher.Core.Launch;

/// <summary>使用 Microsoft 账号启动 Minecraft。</summary>
public sealed class MicrosoftMinecraftLauncher : IMicrosoftMinecraftLauncher
{
    private readonly IOfflineMinecraftLauncher _launcher;

    public MicrosoftMinecraftLauncher(IOfflineMinecraftLauncher? launcher = null)
    {
        _launcher = launcher ?? new OfflineMinecraftLauncher();
    }

    public async Task<MinecraftLaunchResult> LaunchAsync(
        MicrosoftAccount account,
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new MinecraftLaunchException("正版账号缺少访问令牌，请先完成登录。");
        if (account.IsExpired)
        {
            throw new MinecraftLaunchException(
                "正版账号的访问令牌已过期，请先刷新或重新登录。");
        }

        return await _launcher
            .LaunchAsync(options.WithAccount(account), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// 从本地 Minecraft 目录构造并启动游戏进程；不负责下载文件或持久化账号。
/// </summary>
public sealed class OfflineMinecraftLauncher : IOfflineMinecraftLauncher
{
    private static readonly HashSet<string> SensitiveArgumentNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--accessToken",
            "--access_token",
            "--authSession",
            "--session"
        };

    private readonly IJavaRuntimeLocator _javaRuntimeLocator;
    private readonly MinecraftVersionProfileLoader _profileLoader = new();
    private readonly MinecraftLibraryResolver _libraryResolver = new();
    private readonly MinecraftArgumentBuilder _argumentBuilder = new();

    public OfflineMinecraftLauncher(IJavaRuntimeLocator? javaRuntimeLocator = null)
    {
        _javaRuntimeLocator = javaRuntimeLocator ?? new JavaRuntimeLocator();
    }

    public async Task<MinecraftLaunchResult> LaunchAsync(
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = await CreatePlanAsync(options, cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = plan.JavaExecutable,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in plan.Arguments)
                startInfo.ArgumentList.Add(argument);

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

            WriteDebugArguments(plan, options.Account);
            return new MinecraftLaunchResult(
                process,
                options.VersionId,
                options.Account.Username,
                plan.RequiredJavaMajorVersion);
        }
        catch (Exception exception)
        {
            MinecraftLibraryResolver.TryDeleteDirectory(plan.NativeDirectory);
            if (exception is MinecraftLaunchException or OperationCanceledException)
                throw;

            throw new MinecraftLaunchException("启动 Java 进程失败。", exception);
        }
    }

    private async Task<MinecraftLaunchPlan> CreatePlanAsync(
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateMinecraftDirectory(options.MinecraftDirectory);

        var minecraftDirectory = Path.GetFullPath(options.MinecraftDirectory);
        var gameDirectory = Path.GetFullPath(options.GameDirectory ?? minecraftDirectory);
        if (!Directory.Exists(gameDirectory))
            throw new MinecraftLaunchException($"实例游戏目录不存在：{gameDirectory}");

        var profile = await _profileLoader
            .LoadAsync(minecraftDirectory, options.VersionId, cancellationToken)
            .ConfigureAwait(false);

        var features = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["has_custom_resolution"] = options.WindowWidth > 0 && options.WindowHeight > 0,
            ["is_demo_user"] = false,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };
        var (classpath, nativeLibraries) =
            _libraryResolver.Resolve(profile, minecraftDirectory, features);
        var javaExecutable = _javaRuntimeLocator.FindJavaExecutable(
            options.JavaExecutable,
            profile.RequiredJavaMajorVersion,
            options.JavaRuntimeDirectory);

        var nativeDirectory = _libraryResolver.ExtractNatives(profile.Id, nativeLibraries);

        try
        {
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

    private static void ValidateMinecraftDirectory(string minecraftDirectory)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) ||
            !Directory.Exists(minecraftDirectory))
        {
            throw new MinecraftLaunchException(
                $"Minecraft 目录不存在：{minecraftDirectory}");
        }
    }

    /// <summary>
    /// 设置 NYALAUNCHER_DEBUG_ARGS=1 时输出最终参数，诊断失败不能影响启动。
    /// </summary>
    private static void WriteDebugArguments(
        MinecraftLaunchPlan plan,
        IMinecraftAccount account)
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
            lines.AddRange(RedactArguments(plan.Arguments, account));
            File.WriteAllLines(
                Path.Combine(Path.GetTempPath(), "nya_launcher_debug_args.txt"),
                lines);
        }
        catch
        {
            // 调试日志失败不影响游戏启动。
        }
    }

    /// <summary>Redacts credentials while preserving the useful argument layout.</summary>
    private static IEnumerable<string> RedactArguments(
        IReadOnlyList<string> arguments,
        IMinecraftAccount account)
    {
        var accessToken = (account as MicrosoftAccount)?.AccessToken;
        var redactNext = false;
        foreach (var argument in arguments)
        {
            if (redactNext)
            {
                yield return "<redacted>";
                redactNext = false;
                continue;
            }

            var separator = argument.IndexOf('=');
            var name = separator >= 0 ? argument[..separator] : argument;
            if (SensitiveArgumentNames.Contains(name))
            {
                if (separator >= 0)
                    yield return $"{name}=<redacted>";
                else
                {
                    yield return argument;
                    redactNext = true;
                }
                continue;
            }

            yield return string.IsNullOrEmpty(accessToken)
                ? argument
                : argument.Replace(accessToken, "<redacted>", StringComparison.Ordinal);
        }
    }

    /// <summary>Fully resolved process data kept internal to this launcher.</summary>
    private sealed record MinecraftLaunchPlan(
        string JavaExecutable,
        string WorkingDirectory,
        string NativeDirectory,
        int? RequiredJavaMajorVersion,
        IReadOnlyList<string> Arguments);
}
