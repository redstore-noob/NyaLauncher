using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Copies plugin-owned runtime state into bounded launcher-owned snapshots.
/// It validates only state/menu boundaries; applying state remains the view's job.
/// </summary>
internal sealed class ComponentStateSnapshotter
{
    private const int MaximumStateEntriesToInspect = 1024;
    private const int MaximumMenuItems = 128;
    private const int MaximumMenuArguments = 16;
    private const int MaximumMenuArgumentLength = 1024;
    private const int MaximumMenuTextLength = 256;
    private const int MaximumMenuSecondaryTextLength = 512;
    private const int MaximumMenuGlyphLength = 32;

    private readonly HashSet<string> _knownElementIds;
    private readonly HashSet<string> _knownActionIds;

    internal ComponentStateSnapshotter(
        IEnumerable<string> elementIds,
        IEnumerable<string> actionIds)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        ArgumentNullException.ThrowIfNull(actionIds);

        _knownElementIds = elementIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _knownActionIds = actionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal ComponentStateSnapshot Snapshot(ComponentStateSnapshot? state)
    {
        if (state is null)
            return ComponentStateSnapshot.Empty;

        var elements = new Dictionary<string, ComponentElementState>(
            StringComparer.OrdinalIgnoreCase);
        if (state.Elements is not null)
        {
            var inspected = 0;
            foreach (var (id, value) in state.Elements)
            {
                if (++inspected > MaximumStateEntriesToInspect)
                    break;
                if (!string.IsNullOrWhiteSpace(id) && value is not null &&
                    _knownElementIds.Contains(id))
                {
                    elements[id] = value with
                    {
                        ImageSource = ComponentImageLoader.SnapshotSource(value.ImageSource),
                        MenuItems = SnapshotMenuItems(value.MenuItems)
                    };
                }
            }
        }

        return new ComponentStateSnapshot
        {
            Revision = state.Revision,
            Elements = elements
        };
    }

    private IReadOnlyList<ComponentMenuItem>? SnapshotMenuItems(
        IReadOnlyList<ComponentMenuItem>? source)
    {
        if (source is null)
            return null;

        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ComponentMenuItem>(Math.Min(source.Count, MaximumMenuItems));
        var count = Math.Min(source.Count, MaximumMenuItems);
        for (var index = 0; index < count; index++)
        {
            var item = source[index];
            if (item is null || string.IsNullOrWhiteSpace(item.Id) ||
                item.Id.Length > 64 ||
                string.IsNullOrWhiteSpace(item.Text) ||
                item.Text.Length > MaximumMenuTextLength ||
                item.SecondaryText?.Length > MaximumMenuSecondaryTextLength ||
                item.Glyph?.Length > MaximumMenuGlyphLength ||
                string.IsNullOrWhiteSpace(item.ActionId) ||
                !knownIds.Add(item.Id) || !_knownActionIds.Contains(item.ActionId))
            {
                continue;
            }

            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.Arguments is not null)
            {
                var inspected = 0;
                foreach (var (key, value) in item.Arguments)
                {
                    if (++inspected > MaximumMenuArguments)
                        break;
                    if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || value is null ||
                        value.Length > MaximumMenuArgumentLength)
                    {
                        continue;
                    }

                    arguments[key] = value;
                }
            }

            items.Add(item with
            {
                IconSource = ComponentImageLoader.SnapshotSource(item.IconSource),
                Arguments = new ReadOnlyDictionary<string, string>(arguments)
            });
        }

        return items.AsReadOnly();
    }
}
