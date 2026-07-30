using System.Runtime.InteropServices;

namespace NyaLauncher.Core.Launch;

public sealed record MinecraftInstallationLocation(
    string MinecraftDirectory,
    string? PreferredVersionId,
    string? GameDirectory);

public static class MinecraftDirectoryLocator
{
    public static string GetDefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "minecraft");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine(home, ".minecraft");
        }

        return Path.Combine(home, ".minecraft");
    }

    /// <summary>
    /// 接受 Minecraft 根目录，或 versions/&lt;版本号&gt; 形式的独立实例目录。
    /// </summary>
    public static MinecraftInstallationLocation ResolveInstallationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new MinecraftLaunchException("Minecraft 路径不能为空。");

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        if (!Directory.Exists(fullPath))
            throw new MinecraftLaunchException($"Minecraft 路径不存在：{fullPath}");

        var directoryName = Path.GetFileName(fullPath);
        var parent = Directory.GetParent(fullPath);
        if (parent is not null &&
            string.Equals(parent.Name, "versions", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(fullPath, $"{directoryName}.json")))
        {
            var root = parent.Parent?.FullName;
            if (root is null)
                throw new MinecraftLaunchException("无法确定版本目录对应的 Minecraft 根目录。");

            ValidateRootDirectory(root);
            return new MinecraftInstallationLocation(root, directoryName, fullPath);
        }

        ValidateRootDirectory(fullPath);
        return new MinecraftInstallationLocation(fullPath, null, null);
    }

    public static IReadOnlyList<string> GetInstalledVersionIds(string minecraftDirectory)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => File.Exists(Path.Combine(versionsDirectory, id!, $"{id}.json")))
            .OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    private static void ValidateRootDirectory(string root)
    {
        if (!Directory.Exists(Path.Combine(root, "versions")))
        {
            throw new MinecraftLaunchException(
                $"该路径不是有效的 Minecraft 根目录，缺少 versions 文件夹：{root}");
        }
    }
}
