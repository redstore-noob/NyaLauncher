using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Content;

/// <summary>
/// 实例自定义图标的持久化存储。图标按「游戏目录 + 版本 id」哈希存放在
/// 启动器存储目录的 instance-icons/custom 下，不污染 Minecraft 游戏目录。
/// <see cref="GameContentMetadataService.ResolveInstanceVisual"/> 优先读取这里的图标。
/// </summary>
public static class CustomInstanceIconStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"
    };

    private const long MaximumIconBytes = 8 * 1024 * 1024;

    /// <summary>解析自定义图标路径；未设置或文件已丢失时返回 null。</summary>
    public static string? GetPath(string? minecraftDirectory, string? versionId)
    {
        var path = BuildTargetPath(minecraftDirectory, versionId, out _);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 为指定实例设置自定义图标：校验扩展名与大小后复制到存储目录。
    /// 成功返回存储路径；参数无效、文件不可读或磁盘写入失败返回 null。
    /// </summary>
    public static string? Set(string? minecraftDirectory, string? versionId, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) ||
            string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(sourcePath))
            return null;

        var extension = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(extension))
            return null;

        FileInfo info;
        try
        {
            info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumIconBytes)
                return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var hash = ComputeHash(minecraftDirectory, versionId);
        var directory = Path.Combine(LauncherConfig.StorageDirectory, "instance-icons", "custom");
        var target = Path.Combine(directory, hash + extension.ToLowerInvariant());
        try
        {
            Directory.CreateDirectory(directory);
            RemoveVariants(directory, hash);
            File.Copy(info.FullName, target, true);
            return target;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>清除指定实例的自定义图标；存在并删除成功返回 true。</summary>
    public static bool Remove(string? minecraftDirectory, string? versionId)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionId))
            return false;

        var hash = ComputeHash(minecraftDirectory, versionId);
        var directory = Path.Combine(LauncherConfig.StorageDirectory, "instance-icons", "custom");
        try
        {
            if (!Directory.Exists(directory))
                return false;
            var removed = RemoveVariants(directory, hash);
            return removed;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? BuildTargetPath(string? minecraftDirectory, string? versionId, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionId))
            return null;
        hash = ComputeHash(minecraftDirectory, versionId);
        return Path.Combine(LauncherConfig.StorageDirectory, "instance-icons", "custom", hash);
    }

    private static bool RemoveVariants(string directory, string hash)
    {
        var removed = false;
        foreach (var existing in Directory.GetFiles(directory, hash + ".*"))
        {
            try
            {
                File.Delete(existing);
                removed = true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    private static string ComputeHash(string minecraftDirectory, string versionId)
    {
        var key = $"{NormalizePath(minecraftDirectory)}\0{versionId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
}
