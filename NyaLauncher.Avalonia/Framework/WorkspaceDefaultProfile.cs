namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Shipped first-run workspace.  Always returns a fresh object graph so user
/// changes can never mutate the defaults held by the application.
/// </summary>
public static class WorkspaceDefaultProfile
{
    public static WorkspaceProfile Create()
    {
        return new WorkspaceProfile
        {
            Version = 2,
            GlobalComponentScale = 1,
            Areas =
            [
                new FeatureAreaPreference
                {
                    AreaId = "area-001",
                    DisplayName = "启动页",
                    Description = "启动游戏功能的区域",
                    IconGlyph = "\u25B6",
                    IconPath = null,
                    ActionIds = ["launch"]
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-002",
                    DisplayName = "自定义",
                    Description = "自定义的功能区",
                    IconGlyph = "\u25C6",
                    IconPath = null,
                    ActionIds = []
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-003",
                    DisplayName = "多功能区",
                    Description = "设置与下载",
                    IconGlyph = "\u2699",
                    IconPath = null,
                    ActionIds = ["downloads", "settings"]
                }
            ],
            CustomAreas = [],
            Layout = new DockLayoutProfile
            {
                Direction = DockSplitDirection.Horizontal,
                Children =
                [
                    new DockLayoutProfile { AreaId = "area-002" },
                    new DockLayoutProfile { AreaId = "area-001" }
                ],
                Weights = [883.2, 354.4]
            },
            Sidebars =
            [
                new SidebarProfile
                {
                    AreaId = "area-003",
                    Edge = DockEdge.Left,
                    RevealSize = 180
                }
            ],
            ComponentPlacements =
            [
                new ComponentPlacementProfile
                {
                    AreaId = "area-001",
                    ComponentId = "launch",
                    RelativeX = 0.5115384615384608,
                    RelativeY = 0.46466721446179143,
                    ZIndex = 86
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-003",
                    ComponentId = "downloads",
                    RelativeX = 0.5,
                    RelativeY = 0,
                    ZIndex = 23
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-003",
                    ComponentId = "settings",
                    RelativeX = 0.5,
                    RelativeY = 0.13121783876500862,
                    ZIndex = 26
                }
            ]
        };
    }
}
