namespace NyaLauncher.Plugin.Abstractions.Plugins;

/// <summary>The concrete light/dark variant currently rendered by the launcher.</summary>
public enum PluginThemeMode
{
    Light,
    Dark
}

/// <summary>The user's theme-mode preference before a system preference is resolved.</summary>
public enum PluginThemePreference
{
    Light,
    Dark,
    System
}

/// <summary>
/// Framework-neutral ARGB color. Keeping Avalonia types out of the SDK lets a
/// plugin react to the launcher palette without referencing the UI framework.
/// </summary>
public readonly record struct PluginThemeColor(
    byte Alpha,
    byte Red,
    byte Green,
    byte Blue)
{
    public uint Argb =>
        (uint)(Alpha << 24 | Red << 16 | Green << 8 | Blue);

    public static PluginThemeColor FromArgb(uint argb) => new(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);

    public override string ToString() => $"#{Argb:X8}";
}

/// <summary>
/// Stable semantic colors from the active launcher palette. These are value
/// snapshots, not mutable framework brushes.
/// </summary>
public sealed record PluginThemePalette
{
    public PluginThemeColor Accent { get; init; } =
        PluginThemeColor.FromArgb(0xFF3EC9A0);

    public PluginThemeColor AccentText { get; init; } =
        PluginThemeColor.FromArgb(0xFF3EC9A0);

    public PluginThemeColor WindowBackground { get; init; } =
        PluginThemeColor.FromArgb(0xFF101914);

    public PluginThemeColor SurfaceBackground { get; init; } =
        PluginThemeColor.FromArgb(0xFF192520);

    public PluginThemeColor CardBackground { get; init; } =
        PluginThemeColor.FromArgb(0xFF1B2822);

    public PluginThemeColor ControlBackground { get; init; } =
        PluginThemeColor.FromArgb(0xFF1E2E27);

    public PluginThemeColor PrimaryText { get; init; } =
        PluginThemeColor.FromArgb(0xFFF0F7F4);

    public PluginThemeColor SecondaryText { get; init; } =
        PluginThemeColor.FromArgb(0xFFE0ECE6);

    public PluginThemeColor Border { get; init; } =
        PluginThemeColor.FromArgb(0xFF203028);

    public PluginThemeColor Success { get; init; } =
        PluginThemeColor.FromArgb(0xFF3EC97A);

    public PluginThemeColor Warning { get; init; } =
        PluginThemeColor.FromArgb(0xFFF0A83C);

    public PluginThemeColor Error { get; init; } =
        PluginThemeColor.FromArgb(0xFFF05B5B);

    public PluginThemeColor Info { get; init; } =
        PluginThemeColor.FromArgb(0xFF3C9CF0);
}

/// <summary>An immutable snapshot of the launcher theme at one revision.</summary>
public sealed record PluginThemeSnapshot
{
    public long Revision { get; init; }

    /// <summary>The configured theme family, for example <c>HatsuneMiku</c>.</summary>
    public string Family { get; init; } = "HatsuneMiku";

    public PluginThemePreference Preference { get; init; } = PluginThemePreference.Dark;

    /// <summary>The concrete variant after resolving a System preference.</summary>
    public PluginThemeMode EffectiveMode { get; init; } = PluginThemeMode.Dark;

    public PluginThemePalette Palette { get; init; } = new();

    public static PluginThemeSnapshot Default { get; } = new();
}

public sealed class PluginThemeChangedEventArgs(PluginThemeSnapshot theme) : EventArgs
{
    public PluginThemeSnapshot Theme { get; } =
        theme ?? throw new ArgumentNullException(nameof(theme));
}

/// <summary>
/// Read-only access to the current launcher theme. Obtain it through
/// <see cref="IPluginContext.GetService{TService}"/>. No capability is required.
/// The <see cref="Changed"/> event is serialized on a worker thread and the
/// host automatically detaches all handlers when the plugin runtime is retired.
/// Polygon components already use semantic dynamic resources and normally do
/// not need to rebuild themselves when this event fires.
/// </summary>
public interface IPluginTheme
{
    PluginThemeSnapshot Current { get; }

    event EventHandler<PluginThemeChangedEventArgs>? Changed;
}
