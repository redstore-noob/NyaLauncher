using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Avalonia.Pages;

internal static class GameVersionRenameService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static Task<string> RenameAsync(
        string minecraftDirectory,
        string oldVersionId,
        string requestedVersionId,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Rename(minecraftDirectory, oldVersionId, requestedVersionId, cancellationToken),
            cancellationToken);

    private static string Rename(
        string minecraftDirectory,
        string oldVersionId,
        string requestedVersionId,
        CancellationToken cancellationToken)
    {
        var newVersionId = ValidateVersionId(requestedVersionId);
        if (string.Equals(oldVersionId, newVersionId, StringComparison.Ordinal))
            return oldVersionId;

        var versionsDirectory = Path.Combine(Path.GetFullPath(minecraftDirectory), "versions");
        var oldDirectory = ResolveContainedDirectory(versionsDirectory, oldVersionId);
        var newDirectory = ResolveContainedDirectory(versionsDirectory, newVersionId);
        if (!Directory.Exists(oldDirectory))
            throw new DirectoryNotFoundException($"原版本文件夹不存在：{oldDirectory}");
        if ((Directory.Exists(newDirectory) || File.Exists(newDirectory)) &&
            !PathsEqual(oldDirectory, newDirectory))
            throw new IOException($"版本名称“{newVersionId}”已存在。");

        var oldJsonPath = Path.Combine(oldDirectory, $"{oldVersionId}.json");
        if (!File.Exists(oldJsonPath))
            throw new FileNotFoundException("原版本 JSON 不存在。", oldJsonPath);

        cancellationToken.ThrowIfCancellationRequested();
        var mutations = ReadMutations(
            versionsDirectory,
            oldJsonPath,
            oldVersionId,
            newVersionId,
            cancellationToken);

        MoveVersionDirectory(oldDirectory, newDirectory, versionsDirectory);
        try
        {
            var movedOldJson = Path.Combine(newDirectory, $"{oldVersionId}.json");
            var newJson = Path.Combine(newDirectory, $"{newVersionId}.json");
            File.Move(movedOldJson, newJson);

            var movedOldJar = Path.Combine(newDirectory, $"{oldVersionId}.jar");
            var newJar = Path.Combine(newDirectory, $"{newVersionId}.jar");
            if (File.Exists(movedOldJar))
                File.Move(movedOldJar, newJar);

            foreach (var mutation in mutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = string.Equals(
                    mutation.Path,
                    oldJsonPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal)
                    ? newJson
                    : mutation.Path;
                WriteJsonAtomically(destination, mutation.Document);
            }

            GameVersionProfileStore.MigrateRenamedVersion(
                minecraftDirectory,
                oldVersionId,
                newVersionId,
                oldDirectory,
                newDirectory);
            return newVersionId;
        }
        catch
        {
            TryRollbackDirectory(newDirectory, oldDirectory, oldVersionId, newVersionId);
            throw;
        }
    }

    private static IReadOnlyList<JsonMutation> ReadMutations(
        string versionsDirectory,
        string selectedJsonPath,
        string oldVersionId,
        string newVersionId,
        CancellationToken cancellationToken)
    {
        var mutations = new List<JsonMutation>();
        foreach (var versionDirectory in Directory.EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryName = Path.GetFileName(versionDirectory);
            var path = Path.Combine(versionDirectory, $"{directoryName}.json");
            if (!File.Exists(path))
                continue;
            JsonNode? document;
            try
            {
                document = JsonNode.Parse(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                continue;
            }
            if (document is not JsonObject root)
                continue;

            var changed = false;
            if (PathsEqual(path, selectedJsonPath))
            {
                root["id"] = newVersionId;
                changed = true;
            }
            if (string.Equals(root["inheritsFrom"]?.GetValue<string>(), oldVersionId,
                    StringComparison.Ordinal))
            {
                root["inheritsFrom"] = newVersionId;
                changed = true;
            }
            if (string.Equals(root["jar"]?.GetValue<string>(), oldVersionId,
                    StringComparison.Ordinal))
            {
                root["jar"] = newVersionId;
                changed = true;
            }
            if (changed)
                mutations.Add(new JsonMutation(path, root));
        }

        if (!mutations.Any(mutation => PathsEqual(mutation.Path, selectedJsonPath)))
            throw new InvalidDataException("无法读取所选版本 JSON。");
        return mutations;
    }

    private static string ValidateVersionId(string requested)
    {
        var value = requested.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.EndsWith('.') ||
            value.EndsWith(' ') ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("版本名称为空或包含文件系统不允许的字符。");
        }
        return value;
    }

    private static string ResolveContainedDirectory(string versionsDirectory, string versionId)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionsDirectory));
        var target = Path.GetFullPath(Path.Combine(root, versionId));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("版本路径超出 versions 文件夹。");
        return target;
    }

    private static void WriteJsonAtomically(string path, JsonNode document)
    {
        var temporary = $"{path}.nya-rename";
        try
        {
            File.WriteAllText(temporary, document.ToJsonString(SerializerOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
            }
        }
    }

    private static void MoveVersionDirectory(
        string oldDirectory,
        string newDirectory,
        string versionsDirectory)
    {
        if (!PathsEqual(oldDirectory, newDirectory))
        {
            Directory.Move(oldDirectory, newDirectory);
            return;
        }

        // Case-only renames need an intermediate name on case-insensitive filesystems.
        var intermediate = Path.Combine(
            versionsDirectory,
            $".nya-rename-{Guid.NewGuid():N}");
        Directory.Move(oldDirectory, intermediate);
        try
        {
            Directory.Move(intermediate, newDirectory);
        }
        catch
        {
            if (Directory.Exists(intermediate) && !Directory.Exists(oldDirectory))
                Directory.Move(intermediate, oldDirectory);
            throw;
        }
    }

    private static void TryRollbackDirectory(
        string newDirectory,
        string oldDirectory,
        string oldVersionId,
        string newVersionId)
    {
        try
        {
            var newJson = Path.Combine(newDirectory, $"{newVersionId}.json");
            var oldJson = Path.Combine(newDirectory, $"{oldVersionId}.json");
            if (File.Exists(newJson) && !File.Exists(oldJson))
                File.Move(newJson, oldJson);
            var newJar = Path.Combine(newDirectory, $"{newVersionId}.jar");
            var oldJar = Path.Combine(newDirectory, $"{oldVersionId}.jar");
            if (File.Exists(newJar) && !File.Exists(oldJar))
                File.Move(newJar, oldJar);
            if (Directory.Exists(newDirectory) && !Directory.Exists(oldDirectory))
                Directory.Move(newDirectory, oldDirectory);
        }
        catch
        {
            // The original exception is more useful; files stay recoverable on disk.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        NyaLauncher.Core.Tools.PathUtil.PathsEqual(left, right);

    private sealed record JsonMutation(string Path, JsonNode Document);
}
