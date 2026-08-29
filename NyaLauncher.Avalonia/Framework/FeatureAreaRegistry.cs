using System;
using System.Collections.Generic;
using System.Linq;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 功能区注册表：内置功能与插件共用同一个注册入口。
/// <para>
/// 注册进来的原始定义（<see cref="SourceAreas"/>）汇总成一份<b>全局动作目录</b>
/// （<see cref="AvailableActions"/>）；界面真正显示的 <see cref="Areas"/> 是
/// 按用户个性化偏好从这份目录投影出来的，因此同一个按钮可以出现在多个区域。
/// </para>
/// <para>
/// 区域增删、个性化变更、组件摆放变化都会触发 <see cref="Changed"/>，
/// 工作区订阅它并自动刷新，无需重启。
/// </para>
/// </summary>
public sealed partial class FeatureAreaRegistry
{
    /// <summary>允许的全局组件缩放下限（0.65 倍）。</summary>
    public const double MinimumComponentScale = 0.65;

    /// <summary>允许的全局组件缩放上限（1.6 倍）。</summary>
    public const double MaximumComponentScale = 1.6;

    private readonly List<FeatureAreaDefinition> _sourceAreas = [];
    private readonly List<FeatureAreaDefinition> _areas = [];
    private readonly Dictionary<string, FeatureAreaPreference> _preferences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _userAreaIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _personalizationApplied;

    /// <summary>
    /// 区域内容发生变化时触发（注册、注销、个性化、用户区域同步、组件摆放等）。
    /// 工作区与组件库都订阅它来重建界面。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>应用用户个性化后、界面实际显示的区域列表。</summary>
    public IReadOnlyList<FeatureAreaDefinition> Areas => _areas;

    /// <summary>注册的原始区域列表（未应用个性化的改名、改图标与按钮筛选）。</summary>
    public IReadOnlyList<FeatureAreaDefinition> SourceAreas => _sourceAreas;

    /// <summary>由用户在个性化窗口中创建、而非代码注册的区域 Id 集合。</summary>
    public IReadOnlySet<string> UserAreaIds => _userAreaIds;

    /// <summary>由插件注册、并带有所有者信息的区域 Id 集合（详见 <c>FeatureAreaRegistry.Plugins</c>）。</summary>
    public IReadOnlySet<string> PluginAreaIds =>
        _pluginAreaOwners.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>全局组件缩放系数，钳制在 <see cref="MinimumComponentScale"/> 与 <see cref="MaximumComponentScale"/> 之间。</summary>
    public double GlobalComponentScale { get; private set; } = 1;

    /// <summary>
    /// 全局动作目录：所有原始区域的动作按 Id 去重后的集合。
    /// 个性化窗口供用户挑选按钮时展示的就是这份目录。
    /// </summary>
    public IReadOnlyList<FeatureAreaAction> AvailableActions => _sourceAreas
        .SelectMany(area => area.Actions)
        .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();

    /// <summary>注册一个功能区。</summary>
    /// <param name="area">区域定义。</param>
    /// <exception cref="ArgumentNullException"><paramref name="area"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">区域 Id 为空，或某条动作的 Id 为空。</exception>
    /// <exception cref="InvalidOperationException">区域 Id 或动作 Id 已被占用。</exception>
    public void Register(FeatureAreaDefinition area)
    {
        ArgumentNullException.ThrowIfNull(area);

        area = NormalizeSourceArea(area);

        if (string.IsNullOrWhiteSpace(area.Id))
            throw new ArgumentException("Feature area id cannot be empty.", nameof(area));

        if (_sourceAreas.Exists(candidate =>
                string.Equals(candidate.Id, area.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Feature area '{area.Id}' is already registered.");
        }

        _sourceAreas.Add(area);
        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 批量注册：把 provider 返回的每个区域逐个交给 <see cref="Register(FeatureAreaDefinition)"/>。
    /// 一个插件要提供多个区域时用这个重载。
    /// </summary>
    /// <param name="provider">区域提供者。</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> 为 <c>null</c>。</exception>
    public void Register(IFeatureAreaProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var area in provider.GetFeatureAreas())
            Register(area);
    }

    /// <summary>
    /// 第三方多边形组件的一站式注册入口。
    /// <para>
    /// 若 <paramref name="areaId"/> 对应的区域<b>已存在</b>，会保留该区域原有的标题、图标、
    /// 内容工厂与旧组件，只把 provider 的组件追加进去；
    /// 若<b>不存在</b>，则用传入的元数据新建一个区域。
    /// </para>
    /// <para>
    /// 已有个性化配置的区域不会被强制改写按钮列表，因此新组件会先出现在组件库，
    /// 由用户自己拖到功能区里。
    /// </para>
    /// </summary>
    /// <param name="areaId">目标区域 Id（稳定不变，建议 <c>publisher.plugin/area</c> 形式）。</param>
    /// <param name="title">区域不存在时用于新建的显示名称。</param>
    /// <param name="subtitle">区域不存在时用于新建的副标题。</param>
    /// <param name="glyph">区域不存在时用于新建的图标字符。</param>
    /// <param name="provider">组件提供者。</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">provider 返回了 <c>null</c> 组件列表。</exception>
    public void RegisterPolygonComponents(
        string areaId,
        string title,
        string subtitle,
        string glyph,
        IPolygonComponentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var registrations = provider.GetPolygonComponents()
            ?? throw new ArgumentException(
                "Polygon component provider returned null.",
                nameof(provider));
        var existingIndex = _sourceAreas.FindIndex(area =>
            string.Equals(area.Id, areaId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            Register(new FeatureAreaDefinition
            {
                Id = areaId,
                Title = title,
                Subtitle = subtitle,
                Glyph = glyph,
                PolygonComponents = registrations
            });
            return;
        }

        var existing = _sourceAreas[existingIndex];
        var merged = NormalizeSourceArea(new FeatureAreaDefinition
        {
            Id = existing.Id,
            Title = existing.Title,
            Subtitle = existing.Subtitle,
            Glyph = existing.Glyph,
            IconPath = existing.IconPath,
            ContentFactory = existing.ContentFactory,
            Actions = existing.Actions,
            PolygonComponents = registrations
        }, replacingAreaId: existing.Id);
        _sourceAreas[existingIndex] = merged;
        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 设置全局组件缩放系数。超出范围会被钳制；非有限值（NaN / Infinity）回退为 1。
    /// </summary>
    /// <param name="scale">缩放系数，有效区间 <see cref="MinimumComponentScale"/> ~ <see cref="MaximumComponentScale"/>。</param>
    public void SetGlobalComponentScale(double scale)
    {
        GlobalComponentScale = Math.Clamp(
            double.IsFinite(scale) ? scale : 1,
            MinimumComponentScale,
            MaximumComponentScale);
    }

    /// <summary>
    /// 应用用户个性化偏好（改名、改图标、筛选该区域显示哪些按钮），并重建显示区域。
    /// <para>
    /// 传进来的偏好会整体替换旧的偏好集合；<c>AreaId</c> 为空的条目会被跳过。
    /// <c>ActionIds</c> 中引用了未知动作 Id 的项会被忽略，不会中断其它条目的应用。
    /// </para>
    /// </summary>
    /// <param name="preferences">按区域 Id 索引的个性化偏好。</param>
    /// <exception cref="ArgumentNullException"><paramref name="preferences"/> 为 <c>null</c>。</exception>
    public void ApplyPersonalization(IEnumerable<FeatureAreaPreference> preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        _preferences.Clear();
        foreach (var preference in preferences)
        {
            if (string.IsNullOrWhiteSpace(preference.AreaId))
                continue;

            _preferences[preference.AreaId] = new FeatureAreaPreference
            {
                AreaId = preference.AreaId,
                DisplayName = preference.DisplayName,
                Description = preference.Description,
                IconGlyph = preference.IconGlyph,
                IconPath = preference.IconPath,
                ActionIds = [.. preference.ActionIds]
            };
        }

        HydrateDormantProfileEntries();
        _personalizationApplied = true;
        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 同步用户在个性化窗口中自建的区域：新增的补进来，已被删除的从注册表移除。
    /// <para>
    /// 只影响 <see cref="UserAreaIds"/> 中记录的区域，代码注册的区域不受影响。
    /// 启动时应<b>先</b>调用本方法恢复区域，<b>再</b>调用
    /// <see cref="ApplyPersonalization"/> 恢复名称、图标与按钮。
    /// </para>
    /// </summary>
    /// <param name="userAreas">来自工作区档案的用户区域列表；Id 为空或重复的条目会被忽略。</param>
    /// <exception cref="ArgumentNullException"><paramref name="userAreas"/> 为 <c>null</c>。</exception>
    public void SynchronizeUserAreas(IEnumerable<UserFeatureAreaProfile> userAreas)
    {
        ArgumentNullException.ThrowIfNull(userAreas);

        var requested = userAreas
            .Where(area => !string.IsNullOrWhiteSpace(area.Id))
            .GroupBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(area => area.Id, StringComparer.OrdinalIgnoreCase);

        _sourceAreas.RemoveAll(area =>
            _userAreaIds.Contains(area.Id) && !requested.ContainsKey(area.Id));
        _userAreaIds.RemoveWhere(id => !requested.ContainsKey(id));

        foreach (var userArea in requested.Values)
        {
            var existingIndex = _sourceAreas.FindIndex(area =>
                string.Equals(area.Id, userArea.Id, StringComparison.OrdinalIgnoreCase));
            var definition = CreateUserDefinition(userArea);

            if (existingIndex >= 0)
            {
                if (!_userAreaIds.Contains(userArea.Id))
                    continue;

                _sourceAreas[existingIndex] = definition;
            }
            else
            {
                _sourceAreas.Add(definition);
            }

            _userAreaIds.Add(userArea.Id);
        }

        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 生成当前工作区档案：基于<b>已应用个性化</b>的 <see cref="Areas"/>，
    /// 保存缩放、各区域的显示名称/图标/按钮 Id 与用户自建区域。用于持久化。
    /// </summary>
    /// <returns>可直接序列化到 <c>workspace.json</c> 的档案。</returns>
    public WorkspaceProfile CreateCurrentProfile()
    {
        return new WorkspaceProfile
        {
            Version = WorkspaceProfile.CurrentVersion,
            GlobalComponentScale = GlobalComponentScale,
            Areas = _areas.Select(area => new FeatureAreaPreference
            {
                AreaId = area.Id,
                DisplayName = area.Title,
                Description = area.Subtitle,
                IconGlyph = area.Glyph,
                IconPath = area.IconPath,
                ActionIds = area.Actions.Select(action => action.Id).ToList()
            }).ToList(),
            CustomAreas = CreateUserAreaProfiles()
        };
    }

    /// <summary>
    /// 生成默认工作区档案：基于<b>原始注册</b>的 <see cref="SourceAreas"/>，
    /// 丢弃全部用户个性化，缩放重置为 1。用于「恢复默认布局」。
    /// </summary>
    /// <returns>默认档案。</returns>
    public WorkspaceProfile CreateDefaultProfile()
    {
        return new WorkspaceProfile
        {
            Version = WorkspaceProfile.CurrentVersion,
            GlobalComponentScale = 1,
            Areas = _sourceAreas.Select(area => new FeatureAreaPreference
            {
                AreaId = area.Id,
                DisplayName = area.Title,
                Description = area.Subtitle,
                IconGlyph = area.Glyph,
                IconPath = area.IconPath,
                ActionIds = area.Actions.Select(action => action.Id).ToList()
            }).ToList(),
            CustomAreas = CreateUserAreaProfiles()
        };
    }

    /// <summary>
    /// 把组件放进目标区域：从组件库拖入时只传目标区域；跨区移动时同时传源区域。
    /// </summary>
    /// <param name="componentId">要摆放的组件（动作）Id。</param>
    /// <param name="targetAreaId">目标区域 Id。</param>
    /// <param name="sourceAreaId">
    /// 来源区域 Id；与 <paramref name="targetAreaId"/> 相同时视为纯拖入，不做移除。
    /// </param>
    /// <returns>
    /// 摆放结果发生变化返回 <c>true</c>；
    /// 组件不在全局目录、区域不存在或目标区域已有该组件时返回 <c>false</c>。
    /// </returns>
    public bool PlaceComponent(
        string componentId,
        string targetAreaId,
        string? sourceAreaId = null)
    {
        if (string.IsNullOrWhiteSpace(componentId) ||
            string.IsNullOrWhiteSpace(targetAreaId) ||
            !AvailableActions.Any(action =>
                string.Equals(action.Id, componentId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var profile = CreateCurrentProfile();
        var target = profile.Areas.FirstOrDefault(area =>
            string.Equals(area.AreaId, targetAreaId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return false;

        var changed = false;
        if (!string.IsNullOrWhiteSpace(sourceAreaId) &&
            !string.Equals(sourceAreaId, targetAreaId, StringComparison.OrdinalIgnoreCase))
        {
            var source = profile.Areas.FirstOrDefault(area =>
                string.Equals(area.AreaId, sourceAreaId, StringComparison.OrdinalIgnoreCase));
            if (source is not null)
            {
                changed |= source.ActionIds.RemoveAll(id =>
                    string.Equals(id, componentId, StringComparison.OrdinalIgnoreCase)) > 0;
            }
        }

        if (!target.ActionIds.Any(id =>
                string.Equals(id, componentId, StringComparison.OrdinalIgnoreCase)))
        {
            target.ActionIds.Add(componentId);
            changed = true;
        }

        if (!changed)
            return false;

        ApplyPersonalization(profile.Areas);
        return true;
    }

    /// <summary>
    /// 从指定区域移除一个组件（从工作区拖回组件库或丢弃时调用）。
    /// <para>注意：这只是把它从该区域的显示列表里摘掉，
    /// 组件本身仍在全局目录中，稍后可以重新拖回来。</para>
    /// </summary>
    /// <param name="componentId">要移除的组件（动作）Id。</param>
    /// <param name="sourceAreaId">来源区域 Id。</param>
    /// <returns>确实移除了返回 <c>true</c>；参数为空、区域不存在或该区域本就没有此组件时返回 <c>false</c>。</returns>
    public bool RemoveComponent(string componentId, string sourceAreaId)
    {
        if (string.IsNullOrWhiteSpace(componentId) ||
            string.IsNullOrWhiteSpace(sourceAreaId))
        {
            return false;
        }

        var profile = CreateCurrentProfile();
        var source = profile.Areas.FirstOrDefault(area =>
            string.Equals(area.AreaId, sourceAreaId, StringComparison.OrdinalIgnoreCase));
        if (source is null ||
            source.ActionIds.RemoveAll(id =>
                string.Equals(id, componentId, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        // A plugin-owned area exists only as a historical/default container.
        // Once its last component is returned to the library, remove its
        // personalization entry so the empty workspace cannot linger or be
        // recreated by the still-enabled plugin.
        if (source.ActionIds.Count == 0 && _pluginAreaOwners.ContainsKey(sourceAreaId))
            profile.Areas.Remove(source);

        ApplyPersonalization(profile.Areas);
        return true;
    }

    /// <summary>
    /// 注销一个区域（插件卸载或动态移除功能时调用），连同其个性化偏好与用户区域标记一起清除。
    /// </summary>
    /// <param name="id">区域 Id，忽略大小写匹配。</param>
    /// <returns>找到并移除返回 <c>true</c>；区域不存在返回 <c>false</c>。</returns>
    public bool Unregister(string id)
    {
        var removed = _sourceAreas.RemoveAll(area =>
            string.Equals(area.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

        if (!removed)
            return false;

        _preferences.Remove(id);
        _userAreaIds.Remove(id);
        _hydratedAreaIds.Remove(id);
        _pluginAreaOwners.Remove(id);
        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RebuildPersonalizedAreas()
    {
        _areas.Clear();

        var catalog = AvailableActions.ToDictionary(
            action => action.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var source in _sourceAreas)
        {
            _preferences.TryGetValue(source.Id, out var preference);

            // Once a profile has been applied, its area list is authoritative.
            // Registered built-in and plugin sources remain in the component
            // catalog, but an omitted source must not recreate a workspace the
            // user removed. Before the first profile is applied, registrations
            // can still project their shipped defaults.
            if (preference is null && _personalizationApplied)
                continue;

            var title = string.IsNullOrWhiteSpace(preference?.DisplayName)
                ? source.Title
                : preference.DisplayName.Trim();
            var subtitle = string.IsNullOrWhiteSpace(preference?.Description)
                ? source.Subtitle
                : preference.Description.Trim();
            var glyph = string.IsNullOrWhiteSpace(preference?.IconGlyph)
                ? source.Glyph
                : preference.IconGlyph;
            var iconPath = string.IsNullOrWhiteSpace(preference?.IconPath)
                ? source.IconPath
                : preference.IconPath;

            IReadOnlyList<FeatureAreaAction> actions;
            if (preference is null)
            {
                actions = source.Actions;
            }
            else
            {
                actions = preference.ActionIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(catalog.ContainsKey)
                    .Select(id => catalog[id])
                    .ToArray();
            }

            _areas.Add(new FeatureAreaDefinition
            {
                Id = source.Id,
                Title = title,
                Subtitle = subtitle,
                Glyph = glyph,
                IconPath = iconPath,
                ContentFactory = source.ContentFactory,
                Actions = actions,
                PolygonComponents = []
            });
        }
    }

    private FeatureAreaDefinition NormalizeSourceArea(
        FeatureAreaDefinition source,
        string? replacingAreaId = null)
    {
        var actions = source.Actions?.ToList() ?? [];
        foreach (var registration in source.PolygonComponents ?? [])
            actions.Add(CreatePolygonAction(registration));

        var knownIds = _sourceAreas
            .Where(area => !string.Equals(
                area.Id,
                replacingAreaId,
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(area => area.Actions)
            .Select(action => action.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
                throw new ArgumentException("Component id cannot be empty.", nameof(source));
            if (!localIds.Add(action.Id) || knownIds.Contains(action.Id))
            {
                throw new InvalidOperationException(
                    $"Component id '{action.Id}' is already registered.");
            }
        }

        return new FeatureAreaDefinition
        {
            Id = source.Id,
            Title = source.Title,
            Subtitle = source.Subtitle,
            Glyph = source.Glyph,
            IconPath = source.IconPath,
            ContentFactory = source.ContentFactory,
            Actions = actions.ToArray(),
            PolygonComponents = []
        };
    }

    private List<UserFeatureAreaProfile> CreateUserAreaProfiles()
    {
        return _sourceAreas
            .Where(area => _userAreaIds.Contains(area.Id))
            .Select(area => new UserFeatureAreaProfile
            {
                Id = area.Id,
                Title = area.Title,
                Subtitle = area.Subtitle,
                Glyph = area.Glyph,
                IconPath = area.IconPath
            })
            .ToList();
    }

    private static FeatureAreaDefinition CreateUserDefinition(UserFeatureAreaProfile area)
    {
        return new FeatureAreaDefinition
        {
            Id = area.Id,
            Title = string.IsNullOrWhiteSpace(area.Title) ? "新功能区" : area.Title.Trim(),
            Subtitle = string.IsNullOrWhiteSpace(area.Subtitle)
                ? "用户创建的功能区"
                : area.Subtitle.Trim(),
            Glyph = string.IsNullOrWhiteSpace(area.Glyph) ? "material:Apps" : area.Glyph,
            IconPath = area.IconPath,
            Actions = [],
            PolygonComponents = []
        };
    }
}
