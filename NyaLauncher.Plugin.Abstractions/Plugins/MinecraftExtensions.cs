using System.Collections.ObjectModel;

namespace NyaLauncher.Plugin.Abstractions.Minecraft;

/// <summary>A launcher-owned snapshot of one selectable Minecraft instance.</summary>
public sealed record MinecraftInstanceDescriptor
{
    public required string InstanceId { get; init; }

    public required string DisplayName { get; init; }

    public required string VersionId { get; init; }

    public required string MinecraftDirectory { get; init; }

    public required string GameDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

/// <summary>The two writable roots that belong to a selected Minecraft instance.</summary>
public enum MinecraftPathRoot
{
    MinecraftDirectory,
    GameDirectory
}

/// <summary>
/// A path inside an instance root. <see cref="RelativePath"/> must never be
/// absolute and must not contain traversal that escapes its selected root.
/// </summary>
public readonly record struct MinecraftInstancePath(
    MinecraftPathRoot Root,
    string RelativePath);

public sealed record MinecraftFileEntry
{
    public required MinecraftInstancePath Path { get; init; }

    public bool IsDirectory { get; init; }

    public long Length { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }
}

/// <summary>Read-only instance file access shared by commands and launch contributors.</summary>
public interface IMinecraftInstanceFiles
{
    ValueTask<bool> ExistsAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenReadAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<MinecraftFileEntry> EnumerateAsync(
        MinecraftInstancePath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken cancellationToken = default);
}

public enum MinecraftFileWriteMode
{
    CreateNew,
    ReplaceExisting,
    CreateOrReplace
}

/// <summary>
/// A host-owned transaction for persistent instance changes. Writes and deletes
/// are staged; <see cref="CommitAsync"/> publishes them together. Disposing a
/// session that has not committed rolls every staged operation back.
/// </summary>
public interface IMinecraftEditSession : IMinecraftInstanceFiles, IAsyncDisposable
{
    MinecraftInstanceDescriptor Instance { get; }

    ValueTask WriteFileAsync(
        MinecraftInstancePath path,
        Stream content,
        MinecraftFileWriteMode mode = MinecraftFileWriteMode.CreateOrReplace,
        CancellationToken cancellationToken = default);

    ValueTask DeleteFileAsync(
        MinecraftInstancePath path,
        CancellationToken cancellationToken = default);

    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>A user-visible, explicit command that may persistently modify an instance.</summary>
public sealed record MinecraftInstanceActionDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Glyph { get; init; } = "◇";

    public bool IsDestructive { get; init; }

    public string? ConfirmationMessage { get; init; }
}

/// <summary>Context for one user-approved persistent instance command.</summary>
public sealed record MinecraftInstanceActionContext
{
    public required string ActionId { get; init; }

    public required MinecraftInstanceDescriptor Instance { get; init; }

    public required IMinecraftEditSession EditSession { get; init; }

    public IReadOnlyDictionary<string, string> Arguments { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

public sealed record MinecraftInstanceActionResult(
    bool Success,
    string? Message = null)
{
    public static MinecraftInstanceActionResult Completed(string? message = null) =>
        new(true, message);

    public static MinecraftInstanceActionResult Failed(string message) =>
        new(false, message);
}

/// <summary>
/// Adds explicit commands to an instance details page. This is the API for
/// durable changes such as installing loader files or replacing a loading-screen
/// asset. It is deliberately separate from per-launch contributions below.
/// </summary>
public interface IMinecraftInstanceExtension
{
    string Id { get; }

    IReadOnlyList<MinecraftInstanceActionDefinition> Actions { get; }

    ValueTask<MinecraftInstanceActionResult> InvokeAsync(
        MinecraftInstanceActionContext context,
        CancellationToken cancellationToken);
}

/// <summary>The launch plan visible to a contributor before its changes are merged.</summary>
public sealed record MinecraftLaunchPlanSnapshot
{
    /// <summary>
    /// Distinguishes an explicit empty replacement from an unknown vanilla
    /// classpath, which is intentionally not exposed before Core resolves it.
    /// </summary>
    public bool IsClasspathReplaced { get; init; }

    public IReadOnlyList<string> Classpath { get; init; } = [];

    public string? MainClass { get; init; }

    public string? JavaExecutable { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> JvmArguments { get; init; } = [];

    public IReadOnlyList<string> GameArguments { get; init; } = [];

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

/// <summary>Read-only context supplied while constructing one launch.</summary>
public sealed record MinecraftLaunchContext
{
    public required MinecraftInstanceDescriptor Instance { get; init; }

    public required IMinecraftInstanceFiles Files { get; init; }

    public required MinecraftLaunchPlanSnapshot CurrentPlan { get; init; }
}

/// <summary>
/// Declarative changes applied to one launch only. They do not alter instance
/// files and are discarded after the game process has been prepared. Classpath
/// replacement, when non-null, runs before exact removals, prepends and appends.
/// A null environment value removes that variable for the child process.
/// Main class, Java executable and working directory are exclusive replacements;
/// the host reports a conflict instead of silently choosing between plugins.
/// </summary>
public sealed record MinecraftLaunchContribution
{
    /// <summary>Null preserves the current classpath; an empty list clears it.</summary>
    public IReadOnlyList<string>? ReplaceClasspath { get; init; }

    /// <summary>Replaces one exact classpath entry without changing its position.</summary>
    public IReadOnlyList<MinecraftClasspathEntryReplacement> ReplaceClasspathEntries { get; init; } = [];

    public IReadOnlyList<string> RemoveClasspath { get; init; } = [];

    public IReadOnlyList<string> PrependClasspath { get; init; } = [];

    public IReadOnlyList<string> AppendClasspath { get; init; } = [];

    public string? MainClass { get; init; }

    public string? JavaExecutable { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> PrependJvmArguments { get; init; } = [];

    public IReadOnlyList<string> AppendJvmArguments { get; init; } = [];

    public IReadOnlyList<string> PrependGameArguments { get; init; } = [];

    public IReadOnlyList<string> AppendGameArguments { get; init; } = [];

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());

    public static MinecraftLaunchContribution Empty { get; } = new();
}

public sealed record MinecraftClasspathEntryReplacement(
    string ExistingPath,
    string ReplacementPath);

/// <summary>
/// Produces temporary changes for every launch. Use an
/// <see cref="IMinecraftInstanceExtension"/> command instead when files must be
/// installed or edited permanently.
/// </summary>
public interface IMinecraftLaunchContributor
{
    string Id { get; }

    /// <summary>Lower values are merged first; hosts use plugin and contributor IDs as tie-breakers.</summary>
    int Order { get; }

    ValueTask<MinecraftLaunchContribution> BuildAsync(
        MinecraftLaunchContext context,
        CancellationToken cancellationToken);
}
