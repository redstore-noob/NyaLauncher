using System;
using System.Collections.Generic;
using System.Linq;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 工作区档案迁移器：把旧版本的 <c>workspace.json</c> 就地升级到当前版本，
/// 并统一规整内存中的形状（去空值、钳制越界数值、去重摆放项）。
/// <para>
/// 每一步迁移都写死自己的<b>目标版本</b>，而不是简单地递增，
/// 这样将来新增版本时不会误把某一步老迁移重放一遍。
/// </para>
/// </summary>
internal static class WorkspaceProfileMigrator
{
    /// <summary>v2：把业务含义的区域 Id（launch/resources/launcher）换成中性编号 area-00X。</summary>
    private const int CanonicalAreaIdsVersion = 2;

    /// <summary>v3：引入多边形组件（账号选择器、皮肤披风编辑器）。</summary>
    private const int PolygonComponentsVersion = 3;

    /// <summary>v4：引入游戏实例选择器组件。</summary>
    private const int GameInstanceSelectorVersion = 4;

    /// <summary>v5：启动组件改用规范的组件 Id。</summary>
    private const int GameLaunchComponentVersion = 5;

    /// <summary>v6：引入版本管理器组件。</summary>
    private const int VersionManagerComponentVersion = 6;

    // v7 原是插件分支的兼容格式：注册不上的组件 Id 会被注册表丢弃。
    // 合并插件系统后正式采用：v7 把旧版矩形「插件列表」动作迁移为规范组件。
    /// <summary>v7：插件列表组件迁移。</summary>
    private const int PluginListComponentVersion = 7;

    /// <summary>
    /// 把档案迁移到 <see cref="WorkspaceProfile.CurrentVersion"/>。
    /// <para>
    /// 版本<b>高于</b>当前支持的最大版本时直接抛异常（不猜测、不降级覆盖），
    /// 避免出现「新版本配置被旧启动器写坏」的情况。
    /// </para>
    /// </summary>
    /// <param name="profile">读到的档案，会被就地修改。</param>
    /// <returns>同一个档案实例（已迁移并规整）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> 为 <c>null</c>。</exception>
    /// <exception cref="NotSupportedException">档案版本高于当前支持的版本。</exception>
    /// <exception cref="InvalidOperationException">缺少到当前版本的迁移步骤。</exception>
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
