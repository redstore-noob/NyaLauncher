namespace NyaLauncher.Core.Launch;

internal sealed record MinecraftLaunchPlan(
    string JavaExecutable,
    string WorkingDirectory,
    string NativeDirectory,
    int? RequiredJavaMajorVersion,
    IReadOnlyList<string> Arguments);
