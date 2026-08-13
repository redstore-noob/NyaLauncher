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
            Version = WorkspaceProfile.CurrentVersion,
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
                    ActionIds =
                    [
                        BuiltInAccountSelectorComponent.ComponentId,
                        BuiltInGameInstanceSelectorComponent.ComponentId,
                        BuiltInSkinCapeComponent.ComponentId,
                        BuiltInGameLaunchComponent.ComponentId
                    ]
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-002",
                    DisplayName = "自定义",
                    Description = "自定义的功能区",
                    IconGlyph = "\u25C6",
                    IconPath = null,
                    ActionIds = [BuiltInVersionManagerComponent.ComponentId]
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-003",
                    DisplayName = "多功能区",
                    Description = "设置与下载",
                    IconGlyph = "\u2699",
                    IconPath = null,
                    ActionIds =
                    [
                        "downloads",
                        "settings"
                    ]
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
                    ComponentId = BuiltInAccountSelectorComponent.ComponentId,
                    RelativeX = 0.5,
                    RelativeY = 0.13,
                    ZIndex = 85
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-001",
                    ComponentId = BuiltInGameInstanceSelectorComponent.ComponentId,
                    RelativeX = 0.5,
                    RelativeY = 0.38,
                    ZIndex = 86
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-001",
                    ComponentId = BuiltInSkinCapeComponent.ComponentId,
                    RelativeX = 0.16,
                    RelativeY = 0.68,
                    ZIndex = 87
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-001",
                    ComponentId = BuiltInGameLaunchComponent.ComponentId,
                    RelativeX = 0.65,
                    RelativeY = 0.68,
                    ZIndex = 88
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-002",
                    ComponentId = BuiltInVersionManagerComponent.ComponentId,
                    RelativeX = 0.5,
                    RelativeY = 0.38,
                    ZIndex = 89
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
