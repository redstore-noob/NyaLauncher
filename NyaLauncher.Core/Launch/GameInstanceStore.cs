using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Launch;

public sealed record GameInstanceSnapshot(
    string SourcePath,
    string MinecraftDirectory,
    string? GameDirectory,
    IReadOnlyList<string> VersionIds,
    string? SelectedVersionId,
    bool UsesVersionDirectoryAsGameDirectory,
    bool IsLoading,
    string? ErrorMessage)
{
    public static GameInstanceSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        null,
        [],
        null,
        false,
        true,
        null);
}

/// <summary>
/// Shared installed-instance state used by both the launch page and the
/// polygon selector. Disk enumeration runs on the thread pool; published
/// snapshots are immutable and stale scans cannot replace a newer request.
/// </summary>
public static class GameInstanceStore
{
    private const string SelectedVersionConfigKey = "selectedGameInstance";
    private static readonly object Gate = new();
    private static GameInstanceSnapshot _current = GameInstanceSnapshot.Empty;
    private static long _latestRefreshId;

    public static GameInstanceSnapshot Current
    {
        get
        {
            lock (Gate)
                return _current;
        }
    }

    public static event Action<GameInstanceSnapshot>? Changed;

    public static async Task<GameInstanceSnapshot> RefreshAsync(
        string? sourcePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = sourcePath?.Trim() ?? string.Empty;
        GameInstanceSnapshot previous;
        GameInstanceSnapshot loading;
        long refreshId;
        lock (Gate)
        {
            previous = _current;
            refreshId = ++_latestRefreshId;
            loading = new GameInstanceSnapshot(
                normalizedSource,
                string.Empty,
                null,
                [],
                null,
                false,
                true,
                null);
            _current = loading;
        }

        RaiseChanged(loading);

        try
        {
            var scanned = await Task.Run(
                    () => Scan(normalizedSource, previous),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryPublish(refreshId, scanned))
                return Current;

            if (!string.IsNullOrWhiteSpace(scanned.SelectedVersionId))
            {
                LauncherConfig.SetValue(
                    SelectedVersionConfigKey,
                    scanned.SelectedVersionId);
            }

            RaiseChanged(scanned);
            return scanned;
        }
        catch (OperationCanceledException)
        {
            return Current;
        }
        catch (Exception exception)
        {
            var failed = new GameInstanceSnapshot(
                normalizedSource,
                string.Empty,
                null,
                [],
                null,
                false,
                false,
                exception.Message);
            if (TryPublish(refreshId, failed))
                RaiseChanged(failed);
            return Current;
        }
    }

    public static bool Select(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return false;

        GameInstanceSnapshot next;
        lock (Gate)
        {
            var current = _current;
            if (current.IsLoading || current.ErrorMessage is not null ||
                !current.VersionIds.Contains(versionId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var selected = current.VersionIds.First(id => string.Equals(
                id,
                versionId,
                StringComparison.OrdinalIgnoreCase));
            if (string.Equals(
                    current.SelectedVersionId,
                    selected,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var layout = GameVersionIsolation.Resolve(current, selected);
            next = current with
            {
                SelectedVersionId = selected,
                UsesVersionDirectoryAsGameDirectory = layout.IsIsolated,
                GameDirectory = layout.IsIsolated ? layout.ContentDirectory : null
            };
            _current = next;
        }

        LauncherConfig.SetValue(SelectedVersionConfigKey, next.SelectedVersionId!);
        RaiseChanged(next);
        return true;
    }

    public static bool CanResolveSource(string sourcePath)
    {
        try
        {
            _ = MinecraftDirectoryLocator.ResolveInstallationPath(sourcePath);
            return true;
        }
        catch
        {
            return GameInstanceLayoutResolver.TryResolveExternalInstance(sourcePath, out _);
        }
    }

    private static GameInstanceSnapshot Scan(
        string sourcePath,
        GameInstanceSnapshot previous)
    {
        MinecraftInstallationLocation location;
        try
        {
            location = MinecraftDirectoryLocator.ResolveInstallationPath(sourcePath);
        }
        catch (MinecraftLaunchException) when (
            GameInstanceLayoutResolver.TryResolveExternalInstance(sourcePath, out var external))
        {
            return new GameInstanceSnapshot(
                sourcePath,
                external.LauncherRoot,
                external.ContentDirectory,
                Array.AsReadOnly(new[] { external.InstanceId }),
                external.InstanceId,
                true,
                false,
                null);
        }

        var versions = MinecraftDirectoryLocator
            .GetInstalledVersionIds(location.MinecraftDirectory)
            .ToArray();
        var savedSelection = LauncherConfig.GetValue(SelectedVersionConfigKey);
        var previousSelection = PathsEqual(
                previous.MinecraftDirectory,
                location.MinecraftDirectory)
            ? previous.SelectedVersionId
            : null;
        var selected = FindInstalled(versions, previousSelection) ??
                       FindInstalled(versions, savedSelection) ??
                       FindInstalled(versions, location.PreferredVersionId) ??
                       versions.FirstOrDefault();
        var provisional = new GameInstanceSnapshot(
            sourcePath,
            location.MinecraftDirectory,
            null,
            Array.AsReadOnly(versions),
            selected,
            location.GameDirectory is not null,
            false,
            null);
        var layout = selected is null
            ? null
            : GameVersionIsolation.Resolve(provisional, selected);

        return new GameInstanceSnapshot(
            sourcePath,
            location.MinecraftDirectory,
            layout is { IsIsolated: true } ? layout.ContentDirectory : null,
            Array.AsReadOnly(versions),
            selected,
            layout?.IsIsolated == true,
            false,
            null);
    }

    private static string? FindInstalled(
        IReadOnlyList<string> versions,
        string? candidate) =>
        string.IsNullOrWhiteSpace(candidate)
            ? null
            : versions.FirstOrDefault(version => string.Equals(
                version,
                candidate,
                StringComparison.OrdinalIgnoreCase));

    private static bool TryPublish(long refreshId, GameInstanceSnapshot snapshot)
    {
        lock (Gate)
        {
            if (refreshId != _latestRefreshId)
                return false;

            _current = snapshot;
            return true;
        }
    }

    private static bool PathsEqual(string? left, string? right) =>
        NyaLauncher.Core.Tools.PathUtil.PathsEqual(left, right);

    private static void RaiseChanged(GameInstanceSnapshot snapshot)
    {
        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (Action<GameInstanceSnapshot> subscriber in handlers.GetInvocationList())
        {
            try
            {
                subscriber(snapshot);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GameInstanceStore.Changed 订阅者异常：{exception}");
            }
        }
    }
}
