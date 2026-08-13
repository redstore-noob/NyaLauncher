using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

public sealed class WorkspaceProfile
{
    public const int CurrentVersion = 6;

    // Zero is reserved for profiles whose JSON predates (or omits) the
    // version field. New profiles must be created through a current-profile
    // factory, all of which assign CurrentVersion explicitly.
    public int Version { get; set; }

    public double GlobalComponentScale { get; set; } = 1;

    public List<FeatureAreaPreference> Areas { get; set; } = [];

    public List<UserFeatureAreaProfile> CustomAreas { get; set; } = [];

    public DockLayoutProfile? Layout { get; set; }

    public List<SidebarProfile> Sidebars { get; set; } = [];

    public List<ComponentPlacementProfile> ComponentPlacements { get; set; } = [];
}

public sealed class ComponentPlacementProfile
{
    public string AreaId { get; set; } = string.Empty;

    public string ComponentId { get; set; } = string.Empty;

    public double RelativeX { get; set; } = 0.5;

    public double RelativeY { get; set; } = 0.5;

    public int ZIndex { get; set; }
}

public sealed class UserFeatureAreaProfile
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = "用户创建的功能区";

    public string Glyph { get; set; } = "◇";

    public string? IconPath { get; set; }
}

public sealed class SidebarProfile
{
    public string AreaId { get; set; } = string.Empty;

    public DockEdge Edge { get; set; }

    public double RevealSize { get; set; }
}

public enum DockEdge
{
    Left,
    Right,
    Top,
    Bottom
}

public sealed class DockLayoutProfile
{
    public string? AreaId { get; set; }

    public DockSplitDirection? Direction { get; set; }

    public List<DockLayoutProfile> Children { get; set; } = [];

    public List<double> Weights { get; set; } = [];
}

public enum DockSplitDirection
{
    Horizontal,
    Vertical
}
