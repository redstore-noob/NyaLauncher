using System.Text.Json;

namespace NyaLauncher.Core.Launch.Internal;

internal sealed class MinecraftVersionProfile
{
    public required string Id { get; init; }

    /// <summary>
    /// version.json 中声明的原始 id（如 NeoForge 的 "neoforge-21.8.54"）。
    /// 与实例目录名（<see cref="Id"/>）不同；部分启动参数（如 -DignoreList=${version_name}.jar）
    /// 依赖原始 id 才能正确匹配。
    /// </summary>
    public string? SourceId { get; init; }

    public required string MainClass { get; init; }

    public required string ClientJarVersionId { get; init; }

    public required string AssetsId { get; init; }

    public required string VersionType { get; init; }

    public int? RequiredJavaMajorVersion { get; init; }

    public string? LegacyGameArguments { get; init; }

    public IReadOnlyList<JsonElement> JvmArguments { get; init; } = [];

    public IReadOnlyList<JsonElement> GameArguments { get; init; } = [];

    public IReadOnlyList<JsonElement> Libraries { get; init; } = [];
}
