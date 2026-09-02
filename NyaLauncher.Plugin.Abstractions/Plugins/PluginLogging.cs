namespace NyaLauncher.Plugin.Abstractions.Plugins;

public enum PluginLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Launcher-owned structured logging for one plugin. Obtain it through
/// <see cref="IPluginContext.GetService{TService}"/>; no capability is required.
/// The host adds the plugin ID, bounds individual records and may rate-limit a
/// noisy plugin. Secrets and personal data must not be written to this service.
/// Calls made after the plugin runtime has been retired are ignored.
/// </summary>
public interface IPluginLogger
{
    void Log(
        PluginLogLevel level,
        string message,
        Exception? exception = null);
}
