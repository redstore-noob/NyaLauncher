using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Minecraft;

namespace NyaLauncher.Plugin.Abstractions.Plugins;

/// <summary>
/// Metadata read from <c>plugin.json</c>. The launcher reads this object before
/// loading plugin code, so every path in the manifest must be relative to the
/// plugin package directory.
/// </summary>
public sealed record PluginManifest
{
    public const int CurrentManifestVersion = 1;

    public int ManifestVersion { get; init; } = CurrentManifestVersion;

    /// <summary>A stable, lower-case reverse-domain identifier, for example <c>dev.example.clock</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The plugin's semantic version.</summary>
    public required string Version { get; init; }

    /// <summary>The SDK contract version requested by the plugin, for example <c>1.0</c>.</summary>
    public string ApiVersion { get; init; } = "1.0";

    public string? MinimumLauncherVersion { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? Homepage { get; init; }

    public string? License { get; init; }

    /// <summary>A package-relative icon path. Remote and absolute icon paths are not valid.</summary>
    public string? Icon { get; init; }

    /// <summary>Package-relative path to the assembly containing <see cref="EntryType"/>.</summary>
    public required string EntryAssembly { get; init; }

    /// <summary>Assembly-qualified or full type name implementing <see cref="INyaLauncherPlugin"/>.</summary>
    public required string EntryType { get; init; }

    /// <summary>Capabilities that must be granted before the plugin may start.</summary>
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    /// <summary>Capabilities the plugin can operate without when they are denied or unavailable.</summary>
    public IReadOnlyList<string> OptionalCapabilities { get; init; } = [];

    /// <summary>Declarative settings rendered and validated by the launcher.</summary>
    public IReadOnlyList<PluginSettingDefinition> Settings { get; init; } = [];
}

/// <summary>
/// Well-known capability names. They are strings instead of an enum so a newer
/// launcher can add capabilities without changing the binary contract. A grant
/// controls launcher services and records user consent; it is not a sandbox for
/// plugin assemblies executing inside the launcher process.
/// </summary>
public static class PluginCapabilities
{
    public const string Components = "ui.components";
    public const string NativeUi = "ui.native";
    public const string NetworkHttp = "network.http";
    public const string SystemInformationRead = "system.info.read";
    public const string UserFilesRead = "user-files.read";
    public const string UserFilesWrite = "user-files.write";
    public const string ProcessStart = "process.start";
    public const string MinecraftInstanceRead = "minecraft.instance.read";
    public const string MinecraftInstanceModify = "minecraft.instance.modify";
    public const string MinecraftLaunchModify = "minecraft.launch.modify";
}

/// <summary>The single executable entry point declared by a plugin manifest.</summary>
public interface INyaLauncherPlugin
{
    /// <summary>
    /// Starts the plugin. Registrations must be made through
    /// <see cref="IPluginContext.Registrar"/> during this call; the host publishes
    /// them atomically only after the method completes successfully.
    /// </summary>
    ValueTask StartAsync(
        IPluginContext context,
        CancellationToken cancellationToken);

    /// <summary>Stops background work and releases plugin-owned resources.</summary>
    ValueTask StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Optional base class that keeps the entry-point boilerplate small while
/// ensuring the context is available during start and stop callbacks.
/// </summary>
public abstract class PluginBase : INyaLauncherPlugin
{
    private IPluginContext? _context;
    private int _state;

    protected IPluginContext Context => Volatile.Read(ref _context) ??
        throw new InvalidOperationException("The plugin has not been started.");

    public async ValueTask StartAsync(
        IPluginContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The plugin is already started or stopping.");

        Volatile.Write(ref _context, context);
        try
        {
            await OnStartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Startup can allocate resources before failing. Give derived
            // plugins the same cleanup hook while Context is still available;
            // cleanup errors must not hide the original startup failure.
            Volatile.Write(ref _state, 2);
            try
            {
                await OnStopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                Volatile.Write(ref _context, null);
                Volatile.Write(ref _state, 0);
            }

            throw;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
            return;

        try
        {
            await OnStopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _context, null);
            Volatile.Write(ref _state, 0);
        }
    }

    protected virtual ValueTask OnStartAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    protected virtual ValueTask OnStopAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

/// <summary>Launcher-owned services exposed to one plugin instance.</summary>
public interface IPluginContext
{
    PluginManifest Manifest { get; }

    IPluginStorage Storage { get; }

    IPluginSettings Settings { get; }

    /// <summary>
    /// The registrar accepts calls only while <see cref="INyaLauncherPlugin.StartAsync"/>
    /// is running. Keeping it after start does not permit dynamic registration.
    /// </summary>
    IPluginRegistrar Registrar { get; }

    bool IsCapabilityGranted(string capability);

    /// <summary>
    /// Obtains an optional, SDK-defined host service. Unknown or ungranted
    /// services return <see langword="null"/> rather than exposing host internals.
    /// </summary>
    TService? GetService<TService>() where TService : class;
}

/// <summary>Package and private writable locations assigned to one plugin.</summary>
public interface IPluginStorage
{
    /// <summary>The plugin installation directory; plugins should treat it as read-only.</summary>
    string PackageDirectory { get; }

    /// <summary>Persistent private data that survives plugin package replacement.</summary>
    string DataDirectory { get; }

    /// <summary>Re-creatable private data that the launcher may remove.</summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Resolves a relative path below <see cref="DataDirectory"/>. Implementations
    /// reject absolute paths and paths that escape the private directory.
    /// </summary>
    string GetDataPath(string relativePath);

    string GetCachePath(string relativePath);
}

/// <summary>
/// Collects a plugin's contributions during startup. The host associates every
/// item with the current plugin; callers cannot choose or spoof contribution ownership.
/// </summary>
public interface IPluginRegistrar
{
    void AddComponentArea(PluginComponentArea contribution);

    void AddMinecraftInstanceExtension(IMinecraftInstanceExtension extension);

    void AddMinecraftLaunchContributor(IMinecraftLaunchContributor contributor);
}

/// <summary>A framework-neutral group of declarative workspace components.</summary>
public sealed record PluginComponentArea
{
    /// <summary>A stable plugin-owned area ID.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public string Glyph { get; init; } = "◇";

    public string? Icon { get; init; }

    public IReadOnlyList<PolygonComponentRegistration> Components { get; init; } = [];
}
