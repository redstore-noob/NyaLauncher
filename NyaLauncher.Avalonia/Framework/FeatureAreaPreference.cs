using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// User-owned presentation settings for one feature area.
/// Action ids reference the global action catalog rather than fixed area content.
/// </summary>
public sealed class FeatureAreaPreference
{
    public string AreaId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string IconGlyph { get; set; } = "◇";

    public string? IconPath { get; set; }

    public List<string> ActionIds { get; set; } = [];
}
