namespace NyaLauncher.Core.Launch;

public sealed class MinecraftLaunchException : Exception
{
    public MinecraftLaunchException(string message)
        : base(message)
    {
    }

    public MinecraftLaunchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
