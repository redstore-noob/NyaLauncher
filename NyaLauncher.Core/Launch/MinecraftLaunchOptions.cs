namespace NyaLauncher.Core.Launch;

/// <summary>
/// 离线启动所需的外部配置。版本文件、依赖库和资源应已存在于游戏目录。
/// </summary>
public sealed class MinecraftLaunchOptions
{
    public required string MinecraftDirectory { get; init; }

    /// <summary>
    /// 可选的实例游戏目录，用于隔离 mods、config、saves 等内容。
    /// 为空时使用 MinecraftDirectory。
    /// </summary>
    public string? GameDirectory { get; init; }

    public required string VersionId { get; init; }

    public required OfflineAccount Account { get; init; }

    /// <summary>
    /// 可选的 Java 可执行文件。为空时依次检查 NYALAUNCHER_JAVA、JAVA_HOME 和 PATH。
    /// </summary>
    public string? JavaExecutable { get; init; }

    /// <summary>
    /// 可选的 Minecraft runtime 根目录；启动器会递归查找并选择版本要求的 Java。
    /// </summary>
    public string? JavaRuntimeDirectory { get; init; }

    public int MinimumMemoryMb { get; init; } = 512;

    public int MaximumMemoryMb { get; init; } = 4096;

    public int WindowWidth { get; init; } = 854;

    public int WindowHeight { get; init; } = 480;

    public string LauncherName { get; init; } = "NyaLauncher";

    public string LauncherVersion { get; init; } = "0.1.0";

    public IReadOnlyList<string> AdditionalJvmArguments { get; init; } = [];

    public IReadOnlyList<string> AdditionalGameArguments { get; init; } = [];
}
