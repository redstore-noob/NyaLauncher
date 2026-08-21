namespace NyaLauncher.Core;

/// <summary>
/// 启动器版本信息。
/// </summary>
public struct NyaLauncherInfo
{
    public static int MainVersion { get; } = 0;
    public static int SubVersion { get; } = 1;
    public static int FixVersion { get; } = 0;
    public static string Suffix { get; } = "ppre.1";

    /// <summary>格式化版本号，如 "NyaLauncher版本号:0.1.0-ppre.1"。</summary>
    public static string FormatVersionString() =>
        $"NyaLauncher版本号:{MainVersion}.{SubVersion}.{FixVersion}-{Suffix}";
}
