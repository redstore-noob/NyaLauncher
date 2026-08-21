using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Config;

public sealed record GlobalLaunchSettings(
    int WindowWidth,
    int WindowHeight,
    string JavaExecutable,
    string[] AdditionalJvmArguments,
    string[] AdditionalGameArguments);

/// <summary>
/// 持久化并加载全局高级启动设置（窗口大小、Java 路径、JVM/游戏参数）。
/// </summary>
public static class GlobalLaunchSettingsStore
{
    private const string WindowWidthKey = "globalLaunchWindowWidth";
    private const string WindowHeightKey = "globalLaunchWindowHeight";
    private const string JavaExecutableKey = "globalLaunchJavaExecutable";
    private const string JvmArgumentsKey = "globalLaunchJvmArguments";
    private const string GameArgumentsKey = "globalLaunchGameArguments";
    private const string AutomaticJavaValue = "$auto";

    public static GlobalLaunchSettings Load() => new(
        ReadInt(WindowWidthKey, 854, 320),
        ReadInt(WindowHeightKey, 480, 240),
        ReadJavaExecutable(),
        ReadArguments(JvmArgumentsKey),
        ReadArguments(GameArgumentsKey));

    public static bool Save(GlobalLaunchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.WindowWidth < 320 || settings.WindowHeight < 240)
            return false;

        var normalized = settings with
        {
            JavaExecutable = settings.JavaExecutable.Trim(),
            AdditionalJvmArguments = NormalizeArguments(settings.AdditionalJvmArguments),
            AdditionalGameArguments = NormalizeArguments(settings.AdditionalGameArguments)
        };
        return LauncherConfig.SetValue(WindowWidthKey, normalized.WindowWidth.ToString()) &&
               LauncherConfig.SetValue(WindowHeightKey, normalized.WindowHeight.ToString()) &&
               LauncherConfig.SetValue(
                   JavaExecutableKey,
                   string.IsNullOrWhiteSpace(normalized.JavaExecutable)
                       ? AutomaticJavaValue
                       : normalized.JavaExecutable) &&
               LauncherConfig.SetValue(
                   JvmArgumentsKey,
                   JsonSerializer.Serialize(normalized.AdditionalJvmArguments)) &&
               LauncherConfig.SetValue(
                   GameArgumentsKey,
                   JsonSerializer.Serialize(normalized.AdditionalGameArguments));
    }

    private static int ReadInt(string key, int fallback, int minimum) =>
        int.TryParse(LauncherConfig.GetValue(key), out var value) && value >= minimum
            ? value
            : fallback;

    private static string ReadJavaExecutable()
    {
        var configured = LauncherConfig.GetValue(JavaExecutableKey);
        if (string.Equals(configured, AutomaticJavaValue, StringComparison.Ordinal))
            return string.Empty;
        return configured ?? LauncherConfig.JavaExecutable ?? string.Empty;
    }

    private static string[] ReadArguments(string key)
    {
        var value = LauncherConfig.GetValue(key);
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return NormalizeArguments(JsonSerializer.Deserialize<string[]>(value));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string[] NormalizeArguments(IEnumerable<string>? arguments) =>
        arguments?
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .Select(argument => argument.Trim())
            .ToArray() ?? [];
}
