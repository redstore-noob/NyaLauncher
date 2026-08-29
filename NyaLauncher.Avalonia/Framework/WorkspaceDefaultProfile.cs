namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 出厂默认工作区：首次启动（或用户选择「恢复默认布局」）时使用。
/// <para>
/// 每次调用都返回一整棵<b>全新的对象图</b>，因此调用方对结果的任何修改
/// 都不会污染应用程序持有的默认值。
/// </para>
/// <para>
/// 内置区域固定占用前三个中性编号：<c>area-001</c> 启动页、<c>area-002</c> 自定义、
/// <c>area-003</c> 多功能区（默认折叠为左侧栏）。用户新建区域从 <c>area-004</c> 开始。
/// </para>
/// </summary>
public static class WorkspaceDefaultProfile
{
    /// <summary>
    /// 创建一份全新的默认档案，版本号为 <see cref="WorkspaceProfile.CurrentVersion"/>。
    /// </summary>
    /// <returns>默认工作区档案；调用方可随意修改返回值。</returns>
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
                    IconGlyph = "material:Play",
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
                    IconGlyph = "material:Diamond",
                    IconPath = null,
                    ActionIds = [BuiltInVersionManagerComponent.ComponentId]
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-003",
                    DisplayName = "多功能区",
                    Description = "设置与下载",
                    IconGlyph = "material:Cog",
                    IconPath = null,
                    ActionIds =
                    [
                        "settings",
                        BuiltInPluginListComponent.ComponentId
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
                    ComponentId = "settings",
                    RelativeX = 0.5,
                    RelativeY = 0.13121783876500862,
                    ZIndex = 26
                },
                new ComponentPlacementProfile
                {
                    AreaId = "area-003",
                    ComponentId = BuiltInPluginListComponent.ComponentId,
                    RelativeX = 0.5,
                    RelativeY = 0.27,
                    ZIndex = 27
                }
            ]
        };
    }
}
