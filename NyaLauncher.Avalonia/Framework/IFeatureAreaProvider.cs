using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Supplies one or more workspace feature areas.
/// </summary>
public interface IFeatureAreaProvider
{
    IEnumerable<FeatureAreaDefinition> GetFeatureAreas();
}
