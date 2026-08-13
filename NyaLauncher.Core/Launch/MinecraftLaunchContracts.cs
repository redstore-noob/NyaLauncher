using System.Diagnostics;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Core.Launch;

/// <summary>
/// 启动 Minecraft 所需的外部配置。版本文件、依赖库和资源应已存在于游戏目录。
/// </summary>
public sealed class MinecraftLaunchOptions
{
    public required string MinecraftDirectory { get; init; }

    /// <summary>用于隔离 mods、config、saves 的实例目录；为空时使用根目录。</summary>
    public string? GameDirectory { get; init; }

    public required string VersionId { get; init; }

    public required IMinecraftAccount Account { get; init; }

    /// <summary>显式指定的 Java 可执行文件。</summary>
    public string? JavaExecutable { get; init; }

    /// <summary>Minecraft runtime 根目录，启动器会从中递归查找 Java。</summary>
    public string? JavaRuntimeDirectory { get; init; }

    public int MinimumMemoryMb { get; init; } = 512;

    public int MaximumMemoryMb { get; init; } = 4096;

    public int WindowWidth { get; init; } = 854;

    public int WindowHeight { get; init; } = 480;

    public string LauncherName { get; init; } = "NyaLauncher";

    public string LauncherVersion { get; init; } = "0.1.0";

    public IReadOnlyList<string> AdditionalJvmArguments { get; init; } = [];

    public IReadOnlyList<string> AdditionalGameArguments { get; init; } = [];

    /// <summary>
    /// Optional declarative changes applied after the vanilla launch metadata is
    /// resolved and before the Java command line is rendered.
    /// </summary>
    public MinecraftLaunchTransform LaunchTransform { get; init; } = new();

    /// <summary>复制当前配置并替换账号。</summary>
    public MinecraftLaunchOptions WithAccount(IMinecraftAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new MinecraftLaunchOptions
        {
            MinecraftDirectory = MinecraftDirectory,
            GameDirectory = GameDirectory,
            VersionId = VersionId,
            Account = account,
            JavaExecutable = JavaExecutable,
            JavaRuntimeDirectory = JavaRuntimeDirectory,
            MinimumMemoryMb = MinimumMemoryMb,
            MaximumMemoryMb = MaximumMemoryMb,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            LauncherName = LauncherName,
            LauncherVersion = LauncherVersion,
            AdditionalJvmArguments = AdditionalJvmArguments,
            AdditionalGameArguments = AdditionalGameArguments,
            LaunchTransform = LaunchTransform
        };
    }
}

/// <summary>通过统一启动选项启动 Minecraft。</summary>
public interface IOfflineMinecraftLauncher
{
    Task<MinecraftLaunchResult> LaunchAsync(
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>使用 Microsoft 账号启动 Minecraft。</summary>
public interface IMicrosoftMinecraftLauncher
{
    Task<MinecraftLaunchResult> LaunchAsync(
        MicrosoftAccount account,
        MinecraftLaunchOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Minecraft 启动失败。</summary>
public sealed class MinecraftLaunchException : Exception
{
    public MinecraftLaunchException(string message)
        : base(message)
    {
    }

    public MinecraftLaunchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>已启动的 Minecraft 进程及其解析结果。</summary>
public sealed record MinecraftLaunchResult(
    Process Process,
    string VersionId,
    string Username,
    int? RequiredJavaMajorVersion);
