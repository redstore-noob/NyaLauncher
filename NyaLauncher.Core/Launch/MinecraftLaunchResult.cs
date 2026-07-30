using System.Diagnostics;

namespace NyaLauncher.Core.Launch;

public sealed record MinecraftLaunchResult(
    Process Process,
    string VersionId,
    string Username,
    int? RequiredJavaMajorVersion);
