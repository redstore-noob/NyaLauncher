using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Core.Content;

/// <summary>
/// 存档的导出、备份与删除操作。存档在磁盘上是一个目录，
/// 导出/备份都会将该目录打包为 .zip（丢弃会话锁文件）。
/// </summary>
public static class GameSaveService
{
    private const string SessionLockName = "session.lock";

    /// <summary>
    /// 将指定存档目录打包到目标 .zip 路径。目标已存在时覆盖。
    /// 失败返回 null。
    /// </summary>
    public static async Task<string?> ExportAsync(
        string saveDirectory,
        string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory) ||
            string.IsNullOrWhiteSpace(destinationZipPath) ||
            !Directory.Exists(saveDirectory))
            return null;

        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveDirectory));
            var saveName = Path.GetFileName(normalized);
            var parent = Path.GetDirectoryName(destinationZipPath);
            if (string.IsNullOrWhiteSpace(parent))
                return null;
            Directory.CreateDirectory(parent);

            var temporary = destinationZipPath + ".nya-pack";
            await Task.Run(() => CreateArchive(normalized, saveName, temporary, cancellationToken),
                cancellationToken);
            File.Move(temporary, destinationZipPath, overwrite: true);
            return destinationZipPath;
        }
        catch (OperationCanceledException)
        {
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
    }

    /// <summary>
    /// 在存档同级目录生成 <c>{存档名}-备份-{时间戳}.zip</c>，失败返回 null。
    /// </summary>
    public static async Task<string?> BackupAsync(
        string saveDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory) || !Directory.Exists(saveDirectory))
            return null;

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveDirectory));
        var parent = Path.GetDirectoryName(normalized);
        var saveName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(saveName))
            return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(parent, $"{saveName}-备份-{stamp}.zip");
        return await ExportAsync(normalized, destination, cancellationToken);
    }

    /// <summary>递归删除存档目录；成功或目录已不存在返回 true。</summary>
    public static bool Delete(string saveDirectory)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
            return false;
        if (!Directory.Exists(saveDirectory))
            return true;
        try
        {
            Directory.Delete(Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveDirectory)), true);
            return true;
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

    private static void CreateArchive(
        string sourceDirectory,
        string rootEntryName,
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, file);
            if (string.Equals(Path.GetFileName(file), SessionLockName, StringComparison.OrdinalIgnoreCase))
                continue;
            var entry = archive.CreateEntry(Path.Combine(rootEntryName, relative), CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fileStream.CopyTo(entryStream);
        }
    }
}