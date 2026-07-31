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
            Version = 1,
            Areas =
            [
                new FeatureAreaPreference
                {
                    AreaId = "area-001",
                    DisplayName = "1",
                    Description = "1",
                    IconGlyph = "◇",
                    IconPath = null,
                    ActionIds = ["launch"]
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-002",
                    DisplayName = "2",
                    Description = "2",
                    IconGlyph = "◇",
                    IconPath = null,
                    ActionIds = []
                },
                new FeatureAreaPreference
                {
                    AreaId = "area-003",
                    DisplayName = "3",
                    Description = "3",
                    IconGlyph = "◇",
                    IconPath = null,
                    ActionIds = ["downloads", "tasks", "settings"]
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
                Weights = [932.8, 354.4]
            },
            Sidebars =
            [
                new SidebarProfile
                {
                    AreaId = "area-003",
                    Edge = DockEdge.Left,
                    RevealSize = 260
                }
            ]
        };
    }
}
