using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Implement this interface in a plugin to contribute one or more workspace areas.
/// </summary>
public interface IFeatureAreaProvider
{
    IEnumerable<FeatureAreaDefinition> GetFeatureAreas();
}
