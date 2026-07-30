using System.Diagnostics;
using NyaLauncher.Core.Launch.Internal;

namespace NyaLauncher.Core.Launch;

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
}
