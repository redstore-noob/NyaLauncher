using System;
using System.Collections.Generic;
using System.Linq;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Upgrades persisted workspace profiles and normalizes their in-memory shape.
/// Version-specific migrations use fixed target versions so adding a future
/// schema version cannot accidentally replay an older migration.
/// </summary>
internal static class WorkspaceProfileMigrator
{
    private const int CanonicalAreaIdsVersion = 2;
    private const int PolygonComponentsVersion = 3;
    private const int GameInstanceSelectorVersion = 4;
    private const int GameLaunchComponentVersion = 5;
    private const int VersionManagerComponentVersion = 6;
    private const int PluginListComponentVersion = 7;

    public static WorkspaceProfile Migrate(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Version > WorkspaceProfile.CurrentVersion)
        {
            throw new NotSupportedException(
                $"工作区配置版本 {profile.Version} 高于当前支持的版本 " +
                $"{WorkspaceProfile.CurrentVersion}；请使用更新版本的 NyaLauncher 打开该配置。");
        }

        NormalizeShape(profile);

        if (profile.Version < CanonicalAreaIdsVersion)
        {
            MigrateLegacyAreaIds(profile);
            profile.Version = CanonicalAreaIdsVersion;
        }

        if (profile.Version < PolygonComponentsVersion)
        {
            MigratePolygonComponents(profile);
            profile.Version = PolygonComponentsVersion;
        }

        if (profile.Version < GameInstanceSelectorVersion)
        {
            AddGameInstanceSelector(profile);
            profile.Version = GameInstanceSelectorVersion;
        }

        if (profile.Version < GameLaunchComponentVersion)
        {
            MigrateGameLaunchComponent(profile);
            profile.Version = GameLaunchComponentVersion;
        }

        if (profile.Version < VersionManagerComponentVersion)
        {
            AddVersionManagerComponent(profile);
            profile.Version = VersionManagerComponentVersion;
        }

        if (profile.Version < PluginListComponentVersion)
        {
            MigratePluginListComponent(profile);
            profile.Version = PluginListComponentVersion;
        }

        if (profile.Version != WorkspaceProfile.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"工作区配置缺少从版本 {profile.Version} 到版本 " +
                $"{WorkspaceProfile.CurrentVersion} 的迁移步骤。");
        }

        RestorePlacedGameLaunchMembership(profile);
        NormalizeValues(profile);
        return profile;
    }

    private static void MigrateLegacyAreaIds(WorkspaceProfile profile)
    {
        var legacyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["launch"] = "area-001",
            ["resources"] = "area-002",
            ["launcher"] = "area-003"
        };

        foreach (var area in profile.Areas)
        {
            if (legacyIds.TryGetValue(area.AreaId, out var migratedId))
                area.AreaId = migratedId;
        }

        foreach (var sidebar in profile.Sidebars)
        {
            if (legacyIds.TryGetValue(sidebar.AreaId, out var migratedId))
                sidebar.AreaId = migratedId;
        }

        foreach (var placement in profile.ComponentPlacements)
        {
            if (legacyIds.TryGetValue(placement.AreaId, out var migratedId))
                placement.AreaId = migratedId;
        }

        MigrateLayoutAreaIds(profile.Layout, legacyIds);
    }

    private static void MigratePolygonComponents(WorkspaceProfile profile)
    {
        ReplaceComponentId(
            profile,
            "account",
            BuiltInAccountSelectorComponent.ComponentId);
        AddSkinCapeComponent(profile);
    }

    private static void AddSkinCapeComponent(WorkspaceProfile profile)
    {
        EnsureComponent(
            profile,
            "area-001",
            BuiltInSkinCapeComponent.ComponentId,
            relativeX: 0.25,
            relativeY: 0.43,
            insertAtStart: true);
    }

    private static void AddGameInstanceSelector(WorkspaceProfile profile)
    {
        EnsureComponent(
            profile,
            "area-001",
            BuiltInGameInstanceSelectorComponent.ComponentId,
            relativeX: 0.5,
            relativeY: 0.9,
            insertAtStart: true);
    }

    private static void MigrateGameLaunchComponent(WorkspaceProfile profile)
    {
        ReplaceComponentId(
            profile,
            "launch",
            BuiltInGameLaunchComponent.ComponentId);
    }

    private static void AddVersionManagerComponent(WorkspaceProfile profile)
    {
        EnsureComponent(
            profile,
            "area-002",
            BuiltInVersionManagerComponent.ComponentId,
            relativeX: 0.5,
            relativeY: 0.38,
            insertAtStart: true);
    }

    private static void MigratePluginListComponent(WorkspaceProfile profile)
    {
        // v6 exposed a legacy rectangular "plugins" action. Preserve any user
        // placement while moving it onto the built-in polygon component id.
        ReplaceComponentId(
            profile,
            "plugins",
            BuiltInPluginListComponent.ComponentId);
        EnsureComponent(
            profile,
            "area-003",
            BuiltInPluginListComponent.ComponentId,
            relativeX: 0.5,
            relativeY: 0.27,
            insertAtStart: false);
    }

    private static void ReplaceComponentId(
        WorkspaceProfile profile,
        string previousId,
        string currentId)
    {
        foreach (var area in profile.Areas)
        {
            for (var index = 0; index < area.ActionIds.Count; index++)
            {
                if (string.Equals(
                        area.ActionIds[index],
                        previousId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    area.ActionIds[index] = currentId;
                }
            }

            area.ActionIds = area.ActionIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var placement in profile.ComponentPlacements)
        {
            if (string.Equals(
                    placement.ComponentId,
                    previousId,
                    StringComparison.OrdinalIgnoreCase))
            {
                placement.ComponentId = currentId;
            }
        }
    }

    private static void EnsureComponent(
        WorkspaceProfile profile,
        string areaId,
        string componentId,
        double relativeX,
        double relativeY,
        bool insertAtStart)
    {
        var targetArea = profile.Areas.FirstOrDefault(area => string.Equals(
            area.AreaId,
            areaId,
            StringComparison.OrdinalIgnoreCase));
        if (targetArea is null)
            return;

        var assignedArea = profile.Areas.FirstOrDefault(area => area.ActionIds.Any(id =>
            string.Equals(id, componentId, StringComparison.OrdinalIgnoreCase)));
        if (assignedArea is null)
        {
            if (insertAtStart)
                targetArea.ActionIds.Insert(0, componentId);
            else
                targetArea.ActionIds.Add(componentId);
            assignedArea = targetArea;
        }

        var alreadyPlaced = profile.ComponentPlacements.Any(placement => string.Equals(
            placement.ComponentId,
            componentId,
            StringComparison.OrdinalIgnoreCase));
        if (alreadyPlaced)
            return;

        var highestZIndex = profile.ComponentPlacements.Count == 0
            ? 0
            : profile.ComponentPlacements.Max(placement => placement.ZIndex);
        profile.ComponentPlacements.Add(new ComponentPlacementProfile
        {
            AreaId = assignedArea.AreaId,
            ComponentId = componentId,
            RelativeX = relativeX,
            RelativeY = relativeY,
            ZIndex = highestZIndex == int.MaxValue ? int.MaxValue : highestZIndex + 1
        });
    }

    private static void RestorePlacedGameLaunchMembership(WorkspaceProfile profile)
    {
        var placedAreaIds = profile.ComponentPlacements
            .Where(placement => string.Equals(
                placement.ComponentId,
                BuiltInGameLaunchComponent.ComponentId,
                StringComparison.OrdinalIgnoreCase))
            .Select(placement => placement.AreaId)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var areaId in placedAreaIds)
        {
            var area = profile.Areas.FirstOrDefault(candidate => string.Equals(
                candidate.AreaId,
                areaId,
                StringComparison.OrdinalIgnoreCase));
            if (area is not null && !area.ActionIds.Any(id => string.Equals(
                    id,
                    BuiltInGameLaunchComponent.ComponentId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                area.ActionIds.Add(BuiltInGameLaunchComponent.ComponentId);
            }
        }
    }

    private static void NormalizeShape(WorkspaceProfile profile)
    {
        profile.Areas = profile.Areas?
            .Where(area => area is not null)
            .ToList() ?? [];
        foreach (var area in profile.Areas)
        {
            area.AreaId ??= string.Empty;
            area.DisplayName ??= string.Empty;
            area.Description ??= string.Empty;
            area.IconGlyph ??= string.Empty;
            area.ActionIds = area.ActionIds?
                .Where(actionId => actionId is not null)
                .ToList() ?? [];
        }

        profile.CustomAreas = profile.CustomAreas?
            .Where(area => area is not null)
            .ToList() ?? [];
        foreach (var area in profile.CustomAreas)
        {
            area.Id ??= string.Empty;
            area.Title ??= string.Empty;
            area.Subtitle ??= string.Empty;
            area.Glyph ??= string.Empty;
        }

        profile.Sidebars = profile.Sidebars?
            .Where(sidebar => sidebar is not null)
            .ToList() ?? [];
        foreach (var sidebar in profile.Sidebars)
            sidebar.AreaId ??= string.Empty;

        profile.ComponentPlacements = profile.ComponentPlacements?
            .Where(placement => placement is not null)
            .ToList() ?? [];
        foreach (var placement in profile.ComponentPlacements)
        {
            placement.AreaId ??= string.Empty;
            placement.ComponentId ??= string.Empty;
        }

        NormalizeLayoutShape(profile.Layout);
    }

    private static void NormalizeLayoutShape(DockLayoutProfile? node)
    {
        if (node is null)
            return;

        node.Children = node.Children?
            .Where(child => child is not null)
            .ToList() ?? [];
        node.Weights ??= [];

        foreach (var child in node.Children)
            NormalizeLayoutShape(child);
    }

    private static void NormalizeValues(WorkspaceProfile profile)
    {
        profile.GlobalComponentScale = Math.Clamp(
            double.IsFinite(profile.GlobalComponentScale)
                ? profile.GlobalComponentScale
                : 1,
            FeatureAreaRegistry.MinimumComponentScale,
            FeatureAreaRegistry.MaximumComponentScale);

        profile.ComponentPlacements = profile.ComponentPlacements
            .Where(placement =>
                !string.IsNullOrWhiteSpace(placement.AreaId) &&
                !string.IsNullOrWhiteSpace(placement.ComponentId))
            .GroupBy(
                placement => $"{placement.AreaId}\0{placement.ComponentId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        foreach (var placement in profile.ComponentPlacements)
        {
            placement.RelativeX = Math.Clamp(
                double.IsFinite(placement.RelativeX) ? placement.RelativeX : 0.5,
                0,
                1);
            placement.RelativeY = Math.Clamp(
                double.IsFinite(placement.RelativeY) ? placement.RelativeY : 0.5,
                0,
                1);
            placement.ZIndex = Math.Max(0, placement.ZIndex);
        }
    }

    private static void MigrateLayoutAreaIds(
        DockLayoutProfile? node,
        IReadOnlyDictionary<string, string> legacyIds)
    {
        if (node is null)
            return;

        if (node.AreaId is not null && legacyIds.TryGetValue(node.AreaId, out var migratedId))
            node.AreaId = migratedId;

        foreach (var child in node.Children)
            MigrateLayoutAreaIds(child, legacyIds);
    }
}
