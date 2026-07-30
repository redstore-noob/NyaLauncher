using System.Text.Json;

namespace NyaLauncher.Core.Launch.Internal;

internal sealed class MinecraftVersionProfile
{
    public required string Id { get; init; }

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
