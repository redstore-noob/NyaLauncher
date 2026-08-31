namespace NyaLauncher.Core;

/// <summary>
/// 启动器版本信息（前端版本显示的唯一来源，发版只改这里）。
/// </summary>
public struct NyaLauncherInfo
{
    public static int MainVersion { get; } = 1;
    public static int SubVersion { get; } = 0;
    public static int FixVersion { get; } = 0;
    public static string Suffix { get; } = "preview2";
    public static Boolean IsUnstable { get; } = false;
    public static string UpdateChannel { get; } = "main";

    /// <summary>纯版本字符串，如 "1.0.0-preview1"；由上方字段拼接而来。</summary>
    public static string Version => $"{MainVersion}.{SubVersion}.{FixVersion}-{Suffix}";

    /// <summary>格式化版本号，如 "NyaLauncher版本号:1.0.0-preview1"。</summary>
    public static string FormatVersionString() =>
        $"NyaLauncher版本号:{MainVersion}.{SubVersion}.{FixVersion}-{Suffix}";
}
