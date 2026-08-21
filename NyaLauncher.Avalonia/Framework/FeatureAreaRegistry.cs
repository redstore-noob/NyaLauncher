using System;
using System.Collections.Generic;
using System.Linq;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Runtime registry shared by built-in features and Polygon components.
/// Source definitions form a global action catalog; personalized areas are
/// projected from that catalog and can freely select their displayed actions.
/// </summary>
public sealed class FeatureAreaRegistry
{
    public const double MinimumComponentScale = 0.65;
    public const double MaximumComponentScale = 1.6;

    private readonly List<FeatureAreaDefinition> _sourceAreas = [];
    private readonly List<FeatureAreaDefinition> _areas = [];
    private readonly Dictionary<string, FeatureAreaPreference> _preferences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _userAreaIds = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public IReadOnlyList<FeatureAreaDefinition> Areas => _areas;

    public IReadOnlyList<FeatureAreaDefinition> SourceAreas => _sourceAreas;

    public IReadOnlySet<string> UserAreaIds => _userAreaIds;

    public double GlobalComponentScale { get; private set; } = 1;

    public IReadOnlyList<FeatureAreaAction> AvailableActions => _sourceAreas
        .SelectMany(area => area.Actions)
        .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();

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

    public void Register(IFeatureAreaProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var area in provider.GetFeatureAreas())
            Register(area);
    }

    /// <summary>
    /// Convenience entry point for a third-party polygon component provider.
    /// Contributions are merged into an existing area when possible; otherwise
    /// a new source area is created from the supplied metadata.
    /// </summary>
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

    public void SetGlobalComponentScale(double scale)
    {
        GlobalComponentScale = Math.Clamp(
            double.IsFinite(scale) ? scale : 1,
            MinimumComponentScale,
            MaximumComponentScale);
    }

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

        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
    /// Adds a component from the library or moves an existing component
    /// between feature areas. Returns false when the drop changes nothing.
    /// </summary>
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

        ApplyPersonalization(profile.Areas);
        return true;
    }

    public bool Unregister(string id)
    {
        var removed = _sourceAreas.RemoveAll(area =>
            string.Equals(area.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

        if (!removed)
            return false;

        _preferences.Remove(id);
        _userAreaIds.Remove(id);
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

    /// <summary>
    /// Converts a framework-neutral Polygon registration into the workspace's
    /// existing action catalog while keeping the validated definition snapshot.
    /// </summary>
    private static FeatureAreaAction CreatePolygonAction(
        PolygonComponentRegistration? registration)
    {
        if (registration?.Definition is null)
            throw CreateNullRegistrationException();

        var definition = PolygonComponentValidator.ValidateAndSnapshot(
            registration.Definition);
        var registrationSnapshot = new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = registration.Factory
        };
        return new FeatureAreaAction(
            definition.Id,
            definition.Title,
            definition.Description,
            definition.Glyph)
        {
            BaseWidth = definition.PreferredSize.Width,
            BaseHeight = definition.PreferredSize.Height,
            PolygonComponent = registrationSnapshot
        };
    }

    private static ComponentDefinitionException CreateNullRegistrationException() =>
        new([
            new ComponentValidationError(
                "registration.null",
                "$.polygonComponents",
                "多边形组件注册不能为空。")
        ]);

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
            Glyph = string.IsNullOrWhiteSpace(area.Glyph) ? "◇" : area.Glyph,
            IconPath = area.IconPath,
            Actions = [],
            PolygonComponents = []
        };
    }
}
