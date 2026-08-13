using System.Text.Json;

namespace NyaLauncher.Plugin.Abstractions.Plugins;

/// <summary>Control types supported by the launcher's declarative settings page.</summary>
public enum PluginSettingKind
{
    Boolean,
    Integer,
    Number,
    Text,
    MultilineText,
    Secret,
    Choice,
    File,
    Directory
}

public enum PluginSettingScope
{
    Global,
    MinecraftInstance
}

public sealed record PluginSettingOption(
    string Value,
    string Label)
{
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// One host-rendered setting declared in <c>plugin.json</c>. The host validates
/// stored values against this schema before making them available to plugin code.
/// </summary>
public sealed record PluginSettingDefinition
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public PluginSettingKind Kind { get; init; } = PluginSettingKind.Text;

    public PluginSettingScope Scope { get; init; } = PluginSettingScope.Global;

    public JsonElement? DefaultValue { get; init; }

    public bool Required { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? Step { get; init; }

    public int? MaximumLength { get; init; }

    public string? Pattern { get; init; }

    public string? Placeholder { get; init; }

    public IReadOnlyList<PluginSettingOption> Options { get; init; } = [];

    /// <summary>Allowed file suffixes, including the leading dot, for a file setting.</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = [];
}

/// <summary>
/// Typed access to launcher-owned setting values. Instance-scoped definitions
/// require an instance ID; global definitions must use a null instance ID.
/// </summary>
public interface IPluginSettings
{
    bool TryGet<T>(string key, out T? value, string? instanceId = null);

    T Get<T>(string key, T fallback, string? instanceId = null);

    ValueTask SetAsync<T>(
        string key,
        T value,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    ValueTask ResetAsync(
        string key,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    event EventHandler<PluginSettingChangedEventArgs>? Changed;
}

public sealed class PluginSettingChangedEventArgs(
    string key,
    PluginSettingScope scope,
    string? instanceId) : EventArgs
{
    public string Key { get; } = key;

    public PluginSettingScope Scope { get; } = scope;

    public string? InstanceId { get; } = instanceId;
}
