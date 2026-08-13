namespace NyaLauncher.Core.Launch;

/// <summary>解析后的 Minecraft 根目录、首选版本和实例目录。</summary>
public sealed record MinecraftInstallationLocation(
    string MinecraftDirectory,
    string? PreferredVersionId,
    string? GameDirectory);

/// <summary>定位、校验 Minecraft 安装目录并枚举本地版本。</summary>
public static class MinecraftDirectoryLocator
{
    /// <summary>获取官方启动器在当前系统使用的默认目录。</summary>
    public static string GetDefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
        }

        var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(userDirectory, "Library", "Application Support", "minecraft")
            : Path.Combine(userDirectory, ".minecraft");
    }

    /// <summary>
    /// 接受 Minecraft 根目录、versions/&lt;版本号&gt; 实例目录，或包含
    /// minecraft/.minecraft 游戏根目录的第三方启动器实例目录，并返回统一结果。
    /// </summary>
    public static MinecraftInstallationLocation ResolveInstallationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new MinecraftLaunchException("Minecraft 路径不能为空。");

        var expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var fullPath = Path.GetFullPath(expandedPath);
        if (!Directory.Exists(fullPath))
            throw new MinecraftLaunchException($"Minecraft 路径不存在：{fullPath}");

        // versions/<id> 只有在同名 JSON 存在时才视为完整实例。
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

        if (HasVersionsDirectory(fullPath))
            return new MinecraftInstallationLocation(fullPath, null, null);

        // MultiMC/Prism 及部分整合包管理器会在实例目录下再放置
        // minecraft 或 .minecraft。只有嵌套目录仍包含标准 versions
        // 元数据时才作为可由当前启动核心直接启动的 Minecraft 根目录。
        foreach (var nestedName in new[] { ".minecraft", "minecraft" })
        {
            var nestedRoot = Path.Combine(fullPath, nestedName);
            if (HasVersionsDirectory(nestedRoot))
                return new MinecraftInstallationLocation(nestedRoot, null, null);
        }

        ValidateRootDirectory(fullPath);
        return new MinecraftInstallationLocation(fullPath, null, null);
    }

    /// <summary>枚举包含同名版本 JSON 的完整版本，按名称降序返回。</summary>
    public static IReadOnlyList<string> GetInstalledVersionIds(string minecraftDirectory)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return [];

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
        if (!HasVersionsDirectory(root))
        {
            throw new MinecraftLaunchException(
                $"该路径不是有效的 Minecraft 根目录，缺少 versions 文件夹：{root}");
        }
    }

    private static bool HasVersionsDirectory(string root) =>
        Directory.Exists(Path.Combine(root, "versions"));
}
