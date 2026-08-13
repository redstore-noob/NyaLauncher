using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Plugin publication, suspension, and cold-profile placeholder handling for
/// <see cref="FeatureAreaRegistry"/>.
/// </summary>
public sealed partial class FeatureAreaRegistry
{
    private readonly Dictionary<string, PluginPublication> _pluginPublications =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pluginAreaOwners =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hydratedAreaIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hydratedActionIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Atomically publishes one plugin's current feature areas. Component ids
    /// are owned by the manifest id and must use the stable "plugin/id" form.
    /// Re-publishing replaces dormant or older registrations without changing
    /// the user's personalized membership order.
    /// </summary>
    public void PublishPlugin(string pluginId, IFeatureAreaProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        PublishPlugin(
            pluginId,
            provider.GetFeatureAreas() ?? throw new ArgumentException(
                "Feature area provider returned null.",
                nameof(provider)));
    }

    public void PublishPlugin(
        string pluginId,
        IEnumerable<FeatureAreaDefinition> featureAreas)
    {
        ArgumentNullException.ThrowIfNull(featureAreas);
        pluginId = NormalizePluginId(pluginId);

        // Snapshot and validate every contribution before touching live state;
        // one malformed component must not leave a partially published plugin.
        var publishedAreas = SnapshotPluginAreas(pluginId, featureAreas);
        ValidatePluginPublication(pluginId, publishedAreas);
        var ownedAreaIds = ResolveOwnedPluginAreas(pluginId, publishedAreas);

        SuspendPluginCore(pluginId);

        var publishedActionIds = publishedAreas
            .SelectMany(area => area.Actions)
            .Select(action => action.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RemoveActionsFromSources(publishedActionIds);
        _hydratedActionIds.ExceptWith(publishedActionIds);

        foreach (var published in publishedAreas)
            MergePublishedArea(pluginId, published, ownedAreaIds.Contains(published.Id));

        var contentAreaIds = publishedAreas
            .Where(area => ownedAreaIds.Contains(area.Id) && area.ContentFactory is not null)
            .Select(area => area.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _pluginPublications[pluginId] = new PluginPublication(
            ownedAreaIds,
            contentAreaIds);

        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adapter for the framework-neutral registration contract exposed by the
    /// plugin SDK. Keeping Avalonia types here prevents them from leaking into
    /// the public abstractions assembly.
    /// </summary>
    public void PublishPluginComponents(
        string pluginId,
        IEnumerable<PluginComponentArea> componentAreas)
    {
        ArgumentNullException.ThrowIfNull(componentAreas);
        PublishPlugin(pluginId, componentAreas.Select(area =>
        {
            ArgumentNullException.ThrowIfNull(area);
            return new FeatureAreaDefinition
            {
                Id = area.Id,
                Title = area.Title,
                Subtitle = area.Subtitle,
                Glyph = area.Glyph,
                IconPath = area.Icon,
                PolygonComponents = area.Components
            };
        }));
    }

    /// <summary>
    /// Suspends runtime code owned by a plugin while retaining launcher-owned
    /// declarative placeholders. This is intentionally different from user
    /// removal: area layout, action membership, and component coordinates stay
    /// valid and are reused when the same ids are published again.
    /// </summary>
    public bool SuspendPlugin(string pluginId)
    {
        pluginId = NormalizePluginId(pluginId);
        if (!SuspendPluginCore(pluginId))
            return false;

        RebuildPersonalizedAreas();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static string NormalizePluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalized = pluginId.Trim();
        if (normalized.Contains('/'))
        {
            throw new ArgumentException(
                "Plugin id cannot contain '/'; component ids use 'plugin/component'.",
                nameof(pluginId));
        }

        return normalized;
    }

    private static IReadOnlyList<FeatureAreaDefinition> SnapshotPluginAreas(
        string pluginId,
        IEnumerable<FeatureAreaDefinition> featureAreas)
    {
        var areaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FeatureAreaDefinition>();

        foreach (var source in featureAreas)
        {
            if (source is null)
                throw new ArgumentException("Plugin feature area cannot be null.", nameof(featureAreas));
            if (string.IsNullOrWhiteSpace(source.Id))
                throw new ArgumentException("Plugin feature area id cannot be empty.", nameof(featureAreas));
            if (!areaIds.Add(source.Id))
            {
                throw new InvalidOperationException(
                    $"Plugin '{pluginId}' published feature area '{source.Id}' more than once.");
            }

            var actions = new List<FeatureAreaAction>();
            foreach (var action in source.Actions ?? [])
            {
                if (action is null)
                    throw new ArgumentException("Plugin component cannot be null.", nameof(featureAreas));

                ValidatePluginComponentId(pluginId, action.Id);
                var snapshot = SnapshotPluginAction(pluginId, action);
                if (!componentIds.Add(snapshot.Id))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{pluginId}' published component '{snapshot.Id}' more than once.");
                }
                actions.Add(snapshot);
            }

            foreach (var registration in source.PolygonComponents ?? [])
            {
                var action = CreatePolygonAction(registration, pluginId);
                ValidatePluginComponentId(pluginId, action.Id);
                if (!componentIds.Add(action.Id))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{pluginId}' published component '{action.Id}' more than once.");
                }
                actions.Add(action);
            }

            result.Add(new FeatureAreaDefinition
            {
                Id = source.Id,
                Title = source.Title,
                Subtitle = source.Subtitle,
                Glyph = source.Glyph,
                IconPath = source.IconPath,
                ContentFactory = source.ContentFactory,
                Actions = actions.ToArray(),
                PolygonComponents = []
            });
        }

        return result;
    }

    private static FeatureAreaAction SnapshotPluginAction(
        string pluginId,
        FeatureAreaAction action)
    {
        PolygonComponentRegistration? registrationSnapshot = null;
        if (action.PolygonComponent is { } registration)
        {
            if (registration.Definition is null)
                throw CreateNullRegistrationException();

            var definition = PolygonComponentValidator.ValidateAndSnapshot(
                registration.Definition);
            if (!string.Equals(definition.Id, action.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Component action '{action.Id}' does not match polygon definition '{definition.Id}'.");
            }

            registrationSnapshot = new PolygonComponentRegistration
            {
                Definition = definition,
                Factory = registration.Factory
            };
        }

        return action with
        {
            OwnerPluginId = pluginId,
            IsDormant = false,
            PolygonComponent = registrationSnapshot
        };
    }

    private static FeatureAreaAction CreatePolygonAction(
        PolygonComponentRegistration? registration,
        string? ownerPluginId = null)
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
            PolygonComponent = registrationSnapshot,
            OwnerPluginId = ownerPluginId
        };
    }

    private static ComponentDefinitionException CreateNullRegistrationException() =>
        new([
            new ComponentValidationError(
                "registration.null",
                "$.polygonComponents",
                "多边形组件注册不能为空。")
        ]);

    private static void ValidatePluginComponentId(string pluginId, string componentId)
    {
        if (string.IsNullOrWhiteSpace(componentId) ||
            !componentId.StartsWith($"{pluginId}/", StringComparison.OrdinalIgnoreCase) ||
            componentId.Length <= pluginId.Length + 1)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' component id '{componentId}' must start with " +
                $"'{pluginId}/' and include a non-empty local id.");
        }
    }

    private void ValidatePluginPublication(
        string pluginId,
        IReadOnlyList<FeatureAreaDefinition> publishedAreas)
    {
        var existing = AvailableActions.ToDictionary(
            action => action.Id,
            StringComparer.OrdinalIgnoreCase);
        foreach (var action in publishedAreas.SelectMany(area => area.Actions))
        {
            if (existing.TryGetValue(action.Id, out var current) &&
                !string.Equals(
                    current.OwnerPluginId,
                    pluginId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Component id '{action.Id}' is already owned by " +
                    $"'{current.OwnerPluginId ?? "the launcher"}'.");
            }
        }
    }

    private HashSet<string> ResolveOwnedPluginAreas(
        string pluginId,
        IReadOnlyList<FeatureAreaDefinition> publishedAreas)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var published in publishedAreas)
        {
            var existing = _sourceAreas.FirstOrDefault(area => string.Equals(
                area.Id,
                published.Id,
                StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                owned.Add(published.Id);
                continue;
            }

            if (_pluginAreaOwners.TryGetValue(published.Id, out var currentOwner))
            {
                if (!string.Equals(currentOwner, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Feature area '{published.Id}' is owned by plugin '{currentOwner}'.");
                }

                owned.Add(published.Id);
                continue;
            }

            if (_hydratedAreaIds.Contains(published.Id) &&
                CanClaimHydratedArea(pluginId, published.Id))
            {
                owned.Add(published.Id);
                continue;
            }

            if (published.ContentFactory is not null)
            {
                throw new InvalidOperationException(
                    $"Plugin '{pluginId}' cannot replace content of existing area '{published.Id}'.");
            }
        }

        return owned;
    }

    private bool CanClaimHydratedArea(string pluginId, string areaId)
    {
        if (!_preferences.TryGetValue(areaId, out var preference) ||
            preference.ActionIds.Count == 0)
        {
            return true;
        }

        return preference.ActionIds.All(id => string.Equals(
            InferPluginOwner(id),
            pluginId,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool SuspendPluginCore(string pluginId)
    {
        var changed = _pluginPublications.Remove(pluginId, out var publication);
        var contentAreaIds = publication?.ContentAreaIds ??
                             new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < _sourceAreas.Count; index++)
        {
            var source = _sourceAreas[index];
            var actionsChanged = false;
            var actions = source.Actions.Select(action =>
            {
                if (action.IsDormant || !string.Equals(
                        action.OwnerPluginId,
                        pluginId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return action;
                }

                actionsChanged = true;
                return CreateDormantAction(action);
            }).ToArray();
            var clearContent = contentAreaIds.Contains(source.Id) &&
                               source.ContentFactory is not null;
            if (!actionsChanged && !clearContent)
                continue;

            _sourceAreas[index] = CloneArea(
                source,
                actions,
                clearContent ? null : source.ContentFactory);
            changed = true;
        }

        return changed;
    }

    private static FeatureAreaAction CreateDormantAction(FeatureAreaAction action)
    {
        var registration = action.PolygonComponent is null
            ? null
            : new PolygonComponentRegistration
            {
                Definition = action.PolygonComponent.Definition,
                Factory = null
            };
        var description = string.IsNullOrWhiteSpace(action.Description)
            ? "插件当前未启用；组件位置已保留"
            : $"{action.Description} · 插件当前未启用";
        return action with
        {
            Description = description,
            Execute = null,
            PolygonComponent = registration,
            IsDormant = true
        };
    }

    private void RemoveActionsFromSources(IReadOnlySet<string> actionIds)
    {
        if (actionIds.Count == 0)
            return;

        for (var index = 0; index < _sourceAreas.Count; index++)
        {
            var source = _sourceAreas[index];
            var actions = source.Actions
                .Where(action => !actionIds.Contains(action.Id))
                .ToArray();
            if (actions.Length != source.Actions.Count)
                _sourceAreas[index] = CloneArea(source, actions, source.ContentFactory);
        }
    }

    private void MergePublishedArea(
        string pluginId,
        FeatureAreaDefinition published,
        bool ownsArea)
    {
        var existingIndex = _sourceAreas.FindIndex(area => string.Equals(
            area.Id,
            published.Id,
            StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            _sourceAreas.Add(published);
            _pluginAreaOwners[published.Id] = pluginId;
            return;
        }

        var existing = _sourceAreas[existingIndex];
        var actions = existing.Actions.Concat(published.Actions).ToArray();
        if (ownsArea)
        {
            _sourceAreas[existingIndex] = new FeatureAreaDefinition
            {
                Id = published.Id,
                Title = published.Title,
                Subtitle = published.Subtitle,
                Glyph = published.Glyph,
                IconPath = published.IconPath,
                ContentFactory = published.ContentFactory,
                Actions = actions,
                PolygonComponents = []
            };
            _pluginAreaOwners[published.Id] = pluginId;
            _hydratedAreaIds.Remove(published.Id);
            return;
        }

        _sourceAreas[existingIndex] = CloneArea(
            existing,
            actions,
            existing.ContentFactory);
    }

    private void HydrateDormantProfileEntries()
    {
        var referencedAreaIds = _preferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedActionIds = _preferences.Values
            .SelectMany(preference => preference.ActionIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var staleActions = AvailableActions
            .Where(action =>
                action.IsDormant &&
                action.OwnerPluginId is not null &&
                !referencedActionIds.Contains(action.Id))
            .Select(action => action.Id)
            .Concat(_hydratedActionIds.Where(id => !referencedActionIds.Contains(id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RemoveActionsFromSources(staleActions);
        _hydratedActionIds.ExceptWith(staleActions);

        var activeAreaIds = _pluginPublications.Values
            .SelectMany(publication => publication.OwnedAreaIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var areaId in _sourceAreas
                     .Where(area =>
                         !referencedAreaIds.Contains(area.Id) &&
                         !activeAreaIds.Contains(area.Id) &&
                         (_hydratedAreaIds.Contains(area.Id) ||
                          _pluginAreaOwners.ContainsKey(area.Id) &&
                          area.ContentFactory is null &&
                          area.Actions.All(action => action.IsDormant)))
                     .Select(area => area.Id)
                     .ToArray())
        {
            _sourceAreas.RemoveAll(area => string.Equals(
                area.Id,
                areaId,
                StringComparison.OrdinalIgnoreCase));
            _hydratedAreaIds.Remove(areaId);
            _pluginAreaOwners.Remove(areaId);
        }

        var catalog = AvailableActions.ToDictionary(
            action => action.Id,
            StringComparer.OrdinalIgnoreCase);
        foreach (var preference in _preferences.Values)
        {
            var sourceIndex = _sourceAreas.FindIndex(area => string.Equals(
                area.Id,
                preference.AreaId,
                StringComparison.OrdinalIgnoreCase));
            if (sourceIndex < 0)
            {
                _sourceAreas.Add(CreateHydratedArea(preference));
                sourceIndex = _sourceAreas.Count - 1;
                _hydratedAreaIds.Add(preference.AreaId);

                var inferredOwners = preference.ActionIds
                    .Select(InferPluginOwner)
                    .Where(owner => owner is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (inferredOwners.Length == 1 && preference.ActionIds.All(id =>
                        string.Equals(
                            InferPluginOwner(id),
                            inferredOwners[0],
                            StringComparison.OrdinalIgnoreCase)))
                {
                    _pluginAreaOwners[preference.AreaId] = inferredOwners[0]!;
                }
            }

            var missing = preference.ActionIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && !catalog.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(CreateDormantProfileAction)
                .ToArray();
            if (missing.Length == 0)
                continue;

            var source = _sourceAreas[sourceIndex];
            _sourceAreas[sourceIndex] = CloneArea(
                source,
                source.Actions.Concat(missing).ToArray(),
                source.ContentFactory);
            foreach (var action in missing)
            {
                catalog[action.Id] = action;
                _hydratedActionIds.Add(action.Id);
            }
        }
    }

    private static FeatureAreaDefinition CreateHydratedArea(FeatureAreaPreference preference) =>
        new()
        {
            Id = preference.AreaId,
            Title = string.IsNullOrWhiteSpace(preference.DisplayName)
                ? preference.AreaId
                : preference.DisplayName.Trim(),
            Subtitle = string.IsNullOrWhiteSpace(preference.Description)
                ? "插件功能区当前不可用；布局已保留"
                : preference.Description.Trim(),
            Glyph = string.IsNullOrWhiteSpace(preference.IconGlyph) ? "◇" : preference.IconGlyph,
            IconPath = preference.IconPath,
            Actions = [],
            PolygonComponents = []
        };

    private static FeatureAreaAction CreateDormantProfileAction(string componentId)
    {
        var separator = componentId.IndexOf('/');
        var localId = separator >= 0 && separator + 1 < componentId.Length
            ? componentId[(separator + 1)..]
            : componentId;
        return new FeatureAreaAction(
            componentId,
            string.IsNullOrWhiteSpace(localId) ? componentId : localId,
            "插件当前未启用；组件位置已保留",
            "◇")
        {
            OwnerPluginId = InferPluginOwner(componentId),
            IsDormant = true
        };
    }

    private static string? InferPluginOwner(string componentId)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            return null;
        var separator = componentId.IndexOf('/');
        if (separator <= 0)
            return null;

        var owner = componentId[..separator];
        return string.Equals(owner, "nyalauncher.builtin", StringComparison.OrdinalIgnoreCase)
            ? null
            : owner;
    }

    private static FeatureAreaDefinition CloneArea(
        FeatureAreaDefinition source,
        IReadOnlyList<FeatureAreaAction> actions,
        Func<Control>? contentFactory) =>
        new()
        {
            Id = source.Id,
            Title = source.Title,
            Subtitle = source.Subtitle,
            Glyph = source.Glyph,
            IconPath = source.IconPath,
            ContentFactory = contentFactory,
            Actions = actions,
            PolygonComponents = []
        };

    private sealed record PluginPublication(
        IReadOnlySet<string> OwnedAreaIds,
        IReadOnlySet<string> ContentAreaIds);
}
