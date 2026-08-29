using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Core.Launch.Internal;

internal sealed record NativeLibrary(string ArchivePath, IReadOnlyList<string> Exclusions);

internal sealed class MinecraftLibraryResolver
{
    public (IReadOnlyList<string> Classpath, IReadOnlyList<NativeLibrary> Natives) Resolve(
        MinecraftVersionProfile profile,
        string minecraftDirectory,
        IReadOnlyDictionary<string, bool> features)
    {
        var librariesDirectory = Path.Combine(minecraftDirectory, "libraries");
        var classpath = new List<string>();
        var natives = new List<NativeLibrary>();
        var missingFiles = new List<string>();

        foreach (var library in profile.Libraries)
        {
            if (!MinecraftRuleEvaluator.IsAllowed(library, features))
                continue;

            if (TryGetArtifactPath(library, out var artifactRelativePath))
            {
                var artifactPath = ToAbsoluteLibraryPath(librariesDirectory, artifactRelativePath);
                if (File.Exists(artifactPath))
                    classpath.Add(artifactPath);
                else
                    missingFiles.Add(artifactPath);
            }

            if (!TryGetNativePath(library, out var nativeRelativePath))
                continue;

            var nativePath = ToAbsoluteLibraryPath(librariesDirectory, nativeRelativePath);
            if (File.Exists(nativePath))
                natives.Add(new NativeLibrary(nativePath, GetNativeExclusions(library)));
            else
                missingFiles.Add(nativePath);
        }

        var clientJar = Path.Combine(
            minecraftDirectory,
            "versions",
            profile.ClientJarVersionId,
            $"{profile.ClientJarVersionId}.jar");
        if (File.Exists(clientJar))
            classpath.Add(clientJar);
        else
            missingFiles.Add(clientJar);

        if (missingFiles.Count > 0)
        {
            var preview = string.Join(Environment.NewLine, missingFiles.Take(5));
            var suffix = missingFiles.Count > 5 ? $"{Environment.NewLine}……另有 {missingFiles.Count - 5} 个文件" : string.Empty;
            throw new MinecraftLaunchException(
                $"版本文件不完整，缺少以下依赖：{Environment.NewLine}{preview}{suffix}");
        }

        return (            classpath.Distinct(PathUtil.PathComparer).ToArray(), natives);
    }

    public string ExtractNatives(
        string versionId,
        IEnumerable<NativeLibrary> nativeLibraries,
        string? minecraftDirectory = null)
    {
        var safeVersionId = string.Concat(versionId.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        // 优先解压到 Minecraft 目录下（避开 Linux /tmp 的 noexec 挂载与临时目录泄漏），
        // 未提供目录时回退系统临时目录以保持向后兼容。
        var baseDirectory = !string.IsNullOrWhiteSpace(minecraftDirectory)
            ? Path.Combine(minecraftDirectory, ".nya-natives")
            : Path.Combine(Path.GetTempPath(), "NyaLauncher", "natives");
        CleanupStaleNativeDirectories(baseDirectory);
        var nativeDirectory = Path.Combine(baseDirectory, $"{safeVersionId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(nativeDirectory);

        try
        {
            foreach (var nativeLibrary in nativeLibraries)
            {
                ExtractNativeArchive(nativeLibrary, nativeDirectory);
            }

            return nativeDirectory;
        }
        catch
        {
            TryDeleteDirectory(nativeDirectory);
            throw;
        }
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // 游戏退出后的临时文件清理不应影响主流程。
        }
    }

    /// <summary>
    /// 清理超过 7 天未被修改的旧 natives 解压目录，
    /// 防止启动器被强制终止或断电时 .nya-natives 下无限累积 GUID 目录。
    /// </summary>
    private static void CleanupStaleNativeDirectories(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory))
            return;
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(7);
            foreach (var directory in Directory.EnumerateDirectories(baseDirectory))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                        Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // 单个目录清理失败不影响其余
                }
            }
        }
        catch
        {
            // 清理失败不影响本次解压
        }
    }

    private static void ExtractNativeArchive(NativeLibrary nativeLibrary, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(nativeLibrary.ArchivePath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entry.Name) ||
                normalizedName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) ||
                nativeLibrary.Exclusions.Any(exclusion =>
                    normalizedName.StartsWith(exclusion, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(
                Path.Combine(destinationRoot, normalizedName.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContainedPath(destinationRoot, destinationPath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void EnsureContainedPath(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new MinecraftLaunchException("Native 依赖包含不安全的解压路径。");
        }
    }

    private static bool TryGetArtifactPath(JsonElement library, out string path)
    {
        path = string.Empty;
        if (library.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("artifact", out var artifact) &&
            PathUtil.TryGetString(artifact, "path", out path))
        {
            return true;
        }

        return PathUtil.TryGetString(library, "name", out var name) &&
               TryConvertMavenNameToPath(name, out path);
    }

    private static bool TryGetNativePath(JsonElement library, out string path)
    {
        path = string.Empty;
        if (!library.TryGetProperty("natives", out var natives) ||
            !natives.TryGetProperty(MinecraftRuleEvaluator.GetOperatingSystemName(), out var classifierElement) ||
            classifierElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "64",
            Architecture.X86 => "32",
            _ => Environment.Is64BitOperatingSystem ? "64" : "32"
        };
        var classifier = classifierElement.GetString()!.Replace("${arch}", architecture);

        // 新版本：downloads.classifiers 提供精确路径
        if (library.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("classifiers", out var classifiers) &&
            classifiers.TryGetProperty(classifier, out var nativeArtifact) &&
            PathUtil.TryGetString(nativeArtifact, "path", out path))
        {
            return true;
        }

        // 旧版本（1.7.x 及更早）：版本 JSON 无 downloads 字段，
        // 用 name + classifier 拼出 natives JAR 路径（org.lwjgl:lwjgl-platform:2.9.4 + natives-windows）
        if (PathUtil.TryGetString(library, "name", out var name))
        {
            var parts = name.Split(':');
            if (parts.Length is >= 3 and <= 4 && parts.All(part => !string.IsNullOrWhiteSpace(part)))
            {
                path = Path.Combine(
                    parts[0].Replace('.', Path.DirectorySeparatorChar),
                    parts[1],
                    parts[2],
                    $"{parts[1]}-{parts[2]}-{classifier}.jar");
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetNativeExclusions(JsonElement library)
    {
        if (!library.TryGetProperty("extract", out var extract) ||
            !extract.TryGetProperty("exclude", out var exclusions) ||
            exclusions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return exclusions.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!.Replace('\\', '/'))
            .ToArray();
    }

    private static string ToAbsoluteLibraryPath(string libraryRoot, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolute = Path.GetFullPath(Path.Combine(libraryRoot, normalized));
        EnsureContainedPath(libraryRoot, absolute);
        return absolute;
    }

    private static bool TryConvertMavenNameToPath(string name, out string path)
    {
        path = string.Empty;
        var extension = "jar";
        var coordinate = name;
        var extensionSeparator = name.IndexOf('@');
        if (extensionSeparator >= 0)
        {
            extension = name[(extensionSeparator + 1)..];
            coordinate = name[..extensionSeparator];
        }

        var parts = coordinate.Split(':');
        if (parts.Length is < 3 or > 4)
            return false;

        var classifier = parts.Length == 4 ? $"-{parts[3]}" : string.Empty;
        var fileName = $"{parts[1]}-{parts[2]}{classifier}.{extension}";
        path = Path.Combine(
            parts[0].Replace('.', Path.DirectorySeparatorChar),
            parts[1],
            parts[2],
            fileName);
        return true;
    }
}
