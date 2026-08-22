using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Plugins;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--unsafe-shutdown-child", var storage])
            return await RunUnsafeShutdownChildAsync(storage);

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("SemanticVersion ordering", TestSemanticVersionAsync),
            ("Repository index validation", TestRepositoryIndexAsync),
            ("Repository historical release selection", TestRepositoryHistoricalReleasesAsync),
            ("Repository index strict compatibility", TestStrictRepositoryContractAsync),
            ("Verified repository review", TestVerifiedRepositoryReviewAsync),
            ("Review hash mismatch rejection", TestReviewHashMismatchAsync),
            ("Unreviewed install confirmation policy", TestReviewConfirmationPolicyAsync),
            ("SHA-256 mismatch cleanup", TestHashMismatchAsync),
            ("Download cancellation cleanup", TestDownloadCancellationAsync),
            ("Valid package installation", TestValidInstallationAsync),
            ("Committed package rollback", TestInstallationRollbackAsync),
            ("Interrupted update recovery", TestInterruptedUpdateRecoveryAsync),
            ("Prepared update recovery prefers backup", TestPreparedUpdateRecoveryAsync),
            ("Prepared update before move keeps target", TestPreparedUpdateBeforeMoveAsync),
            ("Prepared new install recovery removes target", TestPreparedNewInstallRecoveryAsync),
            ("Committed update recovery keeps target", TestCommittedUpdateRecoveryAsync),
            ("New install clears stale plugin trust", TestNewInstallClearsStaleStateAsync),
            ("Repository downgrade requires confirmation", TestRepositoryDowngradeAsync),
            ("Repository preview release installation", TestRepositoryPreviewInstallAsync),
            ("Repository install uses canonical release", TestCanonicalRepositoryReleaseAsync),
            ("Plugin components start in library", TestPluginComponentsStartInLibraryAsync),
            ("Polygon component host theme inheritance", TestPolygonComponentThemeInheritanceAsync),
            ("Plugin area removal persists", TestPluginAreaRemovalAsync),
            ("All workspace areas can be removed", TestAllWorkspaceAreasCanBeRemovedAsync),
            ("Component scale snapshot validation", TestComponentScaleSnapshotAsync),
            ("Plugin storage is single-manager", TestPluginStorageSingleManagerAsync),
            ("Unsafe shutdown retains manager lock", TestUnsafeShutdownRetainsManagerLockAsync),
            ("Unresolved recovery blocks catalog scan", TestRecoveryFailureBlocksRefreshAsync),
            ("ZIP traversal rejection", TestTraversalRejectionAsync),
            ("ZIP Windows device name rejection", TestWindowsDeviceNameRejectionAsync)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {test.Name}: {exception}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static Task TestSemanticVersionAsync()
    {
        Assert(SemanticVersion.TryParse("1.2.3-alpha.1", out var preview), "preview parses");
        Assert(SemanticVersion.TryParse("1.2.3", out var stable), "stable parses");
        Assert(SemanticVersion.TryParse("1.3.0", out var next), "next parses");
        Assert(preview.CompareTo(stable) < 0, "preview sorts before stable");
        Assert(stable.CompareTo(next) < 0, "minor version ordering");
        Assert(!SemanticVersion.TryParse("1.2", out _), "two-part version is rejected");
        Assert(!SemanticVersion.TryParse("1.2.3-01", out _), "numeric prerelease leading zero is rejected");
        return Task.CompletedTask;
    }

    private static Task TestPluginComponentsStartInLibraryAsync()
    {
        var registry = CreateRegistryWithWorkspace();
        registry.PublishPluginComponents(
            "io.github.touristh.clock",
            [CreateClockPluginArea()]);

        Assert(
            registry.AvailableActions.Any(action =>
                action.Id == "io.github.touristh.clock/digital-clock"),
            "enabled plugin component is available in the component library");
        Assert(
            registry.Areas.All(area => area.Id != "io.github.touristh.clock.area"),
            "enabled plugin does not create a workspace area without personalization");
        Assert(
            registry.PlaceComponent("io.github.touristh.clock/digital-clock", "area-001"),
            "library component can be placed into an existing workspace");
        Assert(
            registry.Areas.Single(area => area.Id == "area-001").Actions.Any(action =>
                action.Id == "io.github.touristh.clock/digital-clock"),
            "placed plugin component appears in the chosen workspace");
        return Task.CompletedTask;
    }

    private static Task TestPolygonComponentThemeInheritanceAsync()
    {
        var inherited = new PolygonComponentBuilder(
                "io.github.example/inherited-theme",
                "Inherited theme")
            .Build()
            .Theme;

        Assert(string.IsNullOrEmpty(inherited.Surface), "default surface inherits the host theme");
        Assert(string.IsNullOrEmpty(inherited.TextPrimary), "default text inherits the host theme");
        Assert(string.IsNullOrEmpty(inherited.Accent), "default accent inherits the host theme");

        var customizedDefinition = new PolygonComponentBuilder(
                "io.github.example/custom-theme",
                "Custom theme")
            .WithTheme(new PolygonComponentTheme
            {
                Surface = "#112233",
                Accent = "#445566"
            })
            .Build();
        var customized = customizedDefinition.Theme;

        Assert(customized.Surface == "#112233", "explicit surface color is preserved");
        Assert(customized.Accent == "#445566", "explicit accent color is preserved");
        Assert(
            customized.TextPrimary == "#F6F7FF",
            "explicit legacy themes retain their original defaults");

        var partial = PolygonComponentTheme.InheritHost with { Accent = "#778899" };
        Assert(partial.Accent == "#778899", "host theme can be selectively overridden");
        Assert(
            string.IsNullOrEmpty(partial.TextPrimary),
            "unmodified host-theme slots continue to inherit");

        var applyThemeBrush = typeof(PolygonComponentView).GetMethod(
            "ApplyThemeBrush",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);
        Assert(applyThemeBrush is not null, "theme resource bridge remains available");

        var inheritedTarget = new global::Avalonia.Controls.Border();
        applyThemeBrush!.Invoke(
            null,
            [
                inheritedTarget,
                global::Avalonia.Controls.Border.BackgroundProperty,
                null,
                "ComponentBgBrush",
                "#1B2822"
            ]);
        var firstHostSurface = new global::Avalonia.Media.SolidColorBrush(
            global::Avalonia.Media.Color.Parse("#123456"));
        inheritedTarget.Resources["ComponentBgBrush"] = firstHostSurface;
        Assert(
            ReferenceEquals(inheritedTarget.Background, firstHostSurface),
            "inherited surface resolves the host resource");

        var secondHostSurface = new global::Avalonia.Media.SolidColorBrush(
            global::Avalonia.Media.Color.Parse("#654321"));
        inheritedTarget.Resources["ComponentBgBrush"] = secondHostSurface;
        Assert(
            ReferenceEquals(inheritedTarget.Background, secondHostSurface),
            "inherited surface follows host resource changes");

        var customizedTarget = new global::Avalonia.Controls.Border();
        applyThemeBrush.Invoke(
            null,
            [
                customizedTarget,
                global::Avalonia.Controls.Border.BackgroundProperty,
                customized.Surface,
                "ComponentBgBrush",
                "#1B2822"
            ]);
        customizedTarget.Resources["ComponentBgBrush"] = firstHostSurface;
        Assert(
            customizedTarget.Background is global::Avalonia.Media.ISolidColorBrush customBrush &&
            customBrush.Color == global::Avalonia.Media.Color.Parse("#112233"),
            "explicit surface color does not follow host resource changes");
        return Task.CompletedTask;
    }

    private static Task TestPluginAreaRemovalAsync()
    {
        var registry = CreateRegistryWithWorkspace(
            new FeatureAreaPreference
            {
                AreaId = "io.github.touristh.clock.area",
                DisplayName = "电子时钟",
                ActionIds = ["io.github.touristh.clock/digital-clock"]
            });
        registry.PublishPluginComponents(
            "io.github.touristh.clock",
            [CreateClockPluginArea()]);

        Assert(
            registry.Areas.Any(area => area.Id == "io.github.touristh.clock.area"),
            "historical plugin workspace is restored before removal");
        Assert(
            registry.RemoveComponent(
                "io.github.touristh.clock/digital-clock",
                "io.github.touristh.clock.area"),
            "last plugin component can be returned to the library");
        Assert(
            registry.Areas.All(area => area.Id != "io.github.touristh.clock.area"),
            "empty plugin workspace is removed immediately");
        Assert(
            registry.CreateCurrentProfile().Areas.All(area =>
                area.AreaId != "io.github.touristh.clock.area"),
            "removed plugin workspace is absent from persisted personalization");
        Assert(
            registry.AvailableActions.Any(action =>
                action.Id == "io.github.touristh.clock/digital-clock"),
            "removed workspace leaves the plugin component in the library");
        return Task.CompletedTask;
    }

    private static Task TestAllWorkspaceAreasCanBeRemovedAsync()
    {
        var registry = new FeatureAreaRegistry();
        registry.Register(new FeatureAreaDefinition
        {
            Id = "area-001",
            Title = "启动页",
            Actions = []
        });
        registry.Register(new FeatureAreaDefinition
        {
            Id = "area-002",
            Title = "设置页",
            Actions = []
        });
        registry.ApplyPersonalization([
            new FeatureAreaPreference { AreaId = "area-001", DisplayName = "启动页" },
            new FeatureAreaPreference { AreaId = "area-002", DisplayName = "设置页" }
        ]);
        Assert(registry.Areas.Count == 2, "both built-in workspaces initially exist");

        registry.ApplyPersonalization([]);
        Assert(registry.Areas.Count == 0, "an empty profile removes every built-in workspace");
        Assert(registry.CreateCurrentProfile().Areas.Count == 0, "empty workspace persists as empty");
        Assert(registry.SourceAreas.Count == 2, "removed workspaces leave component sources registered");

        registry.Register(new FeatureAreaDefinition
        {
            Id = "plugin.area",
            Title = "插件来源",
            Actions = []
        });
        Assert(registry.Areas.Count == 0, "new sources do not recreate a removed workspace");
        return Task.CompletedTask;
    }

    private static Task TestComponentScaleSnapshotAsync()
    {
        var snapshotter = new ComponentStateSnapshotter([], []);
        var valid = snapshotter.Snapshot(new ComponentStateSnapshot
        {
            Revision = 1,
            Scale = 1.35
        });
        var invalid = snapshotter.Snapshot(new ComponentStateSnapshot
        {
            Revision = 2,
            Scale = double.PositiveInfinity
        });

        Assert(valid.Scale == 1.35, "positive finite component scale is preserved");
        Assert(invalid.Scale is null, "non-finite component scale is rejected");
        return Task.CompletedTask;
    }

    private static FeatureAreaRegistry CreateRegistryWithWorkspace(
        FeatureAreaPreference? extraPreference = null)
    {
        var registry = new FeatureAreaRegistry();
        registry.Register(new FeatureAreaDefinition
        {
            Id = "area-001",
            Title = "启动页",
            Actions = []
        });
        var preferences = new List<FeatureAreaPreference>
        {
            new() { AreaId = "area-001", DisplayName = "启动页", ActionIds = [] }
        };
        if (extraPreference is not null)
            preferences.Add(extraPreference);
        registry.ApplyPersonalization(preferences);
        return registry;
    }

    private static PluginComponentArea CreateClockPluginArea() => new()
    {
        Id = "io.github.touristh.clock.area",
        Title = "电子时钟",
        Components =
        [
            new PolygonComponentRegistration
            {
                Definition = new PolygonComponentBuilder(
                        "io.github.touristh.clock/digital-clock",
                        "电子时钟")
                    .Build()
            }
        ]
    };

    private static async Task TestRepositoryIndexAsync()
    {
        var package = CreatePackage(includeTraversal: false);
        var release = CreateRelease(package);
        var json = CreateRepositoryIndexJson(release);
        using var http = new HttpClient(new PayloadHandler(Encoding.UTF8.GetBytes(json)));
        using var client = new PluginRepositoryClient(http);
        var index = await client.LoadIndexAsync();
        Assert(index.Plugins.Count == 1, "one plugin parsed");
        var parsedRelease = client.GetLatestCompatibleRelease(index.Plugins[0]);
        Assert(parsedRelease?.Version == "1.0.0", "latest release selected");
        Assert(parsedRelease is not null &&
               RepositoryReviewPolicy.RequiresInstallConfirmation(parsedRelease),
            "an index release without review requires explicit confirmation");
    }

    private static Task TestRepositoryHistoricalReleasesAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var oldStable = CreateRelease(payload, "1.0.0");
        var preview = CreateRelease(payload, "1.5.0-beta.1", "preview");
        var newStable = CreateRelease(payload, "2.0.0");
        var yanked = CreateRelease(payload, "1.4.0") with
        {
            Yanked = true,
            YankReason = "security issue"
        };
        var incompatible = CreateRelease(payload, "3.0.0") with
        {
            Compatibility = CreateRelease(payload).Compatibility with
            {
                MinimumLauncherVersion = "999.0.0"
            }
        };
        var plugin = CreatePlugin() with
        {
            Releases = [oldStable, yanked, incompatible, preview, newStable]
        };
        using var http = new HttpClient(new PayloadHandler(payload));
        using var client = new PluginRepositoryClient(http);

        var releases = client.GetCompatibleReleases(plugin);
        Assert(
            releases.Select(release => release.Version).SequenceEqual(
                ["2.0.0", "1.5.0-beta.1", "1.0.0"],
                StringComparer.Ordinal),
            "every compatible non-yanked historical release is selectable in semantic order");
        Assert(
            client.GetLatestCompatibleRelease(plugin)?.Version == "2.0.0",
            "latest stable release remains the default selection");
        return Task.CompletedTask;
    }

    private static async Task TestVerifiedRepositoryReviewAsync()
    {
        var release = CreateRelease(CreatePackage(includeTraversal: false));
        var json = CreateRepositoryIndexJson(
            release,
            includeReview: true,
            reviewSha256: release.Download.Sha256);
        using var http = new HttpClient(new PayloadHandler(Encoding.UTF8.GetBytes(json)));
        using var client = new PluginRepositoryClient(http);

        var index = await client.LoadIndexAsync();
        var parsedRelease = index.Plugins.Single().Releases.Single();
        Assert(parsedRelease.Review?.Status == "verified", "verified status parsed");
        Assert(parsedRelease.Review?.ReviewedBy == "registry-admin", "reviewer parsed");
        Assert(parsedRelease.Review?.ReviewedAt == "2026-08-13T08:00:00Z", "review time parsed");
        Assert(parsedRelease.Review?.Notes == "Release asset reviewed.", "review notes parsed");
        Assert(!RepositoryReviewPolicy.RequiresInstallConfirmation(parsedRelease),
            "a validated administrator review bypasses the unreviewed warning");
    }

    private static async Task TestStrictRepositoryContractAsync()
    {
        var release = CreateRelease(CreatePackage(includeTraversal: false));
        var valid = CreateRepositoryIndexJson(release);
        foreach (var invalid in new[]
                 {
                     valid.Replace(
                         "\"apiVersion\": \"1.0\"",
                         "\"apiVersion\": \"1.preview\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "2026-08-13T00:00:00Z",
                         "2026-08-13T00:00:00+08:00",
                         StringComparison.Ordinal)
                 })
        {
            using var http = new HttpClient(new PayloadHandler(Encoding.UTF8.GetBytes(invalid)));
            using var client = new PluginRepositoryClient(http);
            await AssertThrowsAsync<InvalidDataException>(() => client.LoadIndexAsync());
        }
    }

    private static async Task TestReviewHashMismatchAsync()
    {
        var release = CreateRelease(CreatePackage(includeTraversal: false));
        var json = CreateRepositoryIndexJson(
            release,
            includeReview: true,
            reviewSha256: new string('0', 64));
        using var http = new HttpClient(new PayloadHandler(Encoding.UTF8.GetBytes(json)));
        using var client = new PluginRepositoryClient(http);

        await AssertThrowsAsync<InvalidDataException>(() => client.LoadIndexAsync());
    }

    private static Task TestReviewConfirmationPolicyAsync()
    {
        var release = CreateRelease(Encoding.UTF8.GetBytes("review-policy"));
        Assert(RepositoryReviewPolicy.RequiresInstallConfirmation(release),
            "an unreviewed release requires confirmation");

        var reviewed = release with
        {
            Review = new RepositoryReleaseReview
            {
                Status = "verified",
                ReviewedBy = "registry-admin",
                ReviewedAt = "2026-08-13T08:00:00Z",
                Sha256 = release.Download.Sha256,
                Notes = null
            }
        };
        Assert(!RepositoryReviewPolicy.RequiresInstallConfirmation(reviewed),
            "a reviewed release does not require the unreviewed confirmation");

        var pending = reviewed with
        {
            Review = reviewed.Review! with { Status = "pending" }
        };
        Assert(RepositoryReviewPolicy.RequiresInstallConfirmation(pending),
            "a non-verified review cannot bypass confirmation");

        var mismatched = reviewed with
        {
            Review = reviewed.Review! with { Sha256 = new string('0', 64) }
        };
        Assert(RepositoryReviewPolicy.RequiresInstallConfirmation(mismatched),
            "a review for different bytes cannot bypass confirmation");
        return Task.CompletedTask;
    }

    private static async Task TestHashMismatchAsync()
    {
        var payload = Encoding.UTF8.GetBytes("not the expected plugin");
        var plugin = CreatePlugin();
        var release = CreateRelease(payload) with
        {
            Download = CreateRelease(payload).Download with
            {
                Sha256 = new string('0', 64)
            }
        };
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "package.zip");
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            await AssertThrowsAsync<InvalidDataException>(() =>
                client.DownloadPackageAsync(plugin, release, destination));
            Assert(!File.Exists(destination), "failed download is removed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task TestDownloadCancellationAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "package.zip");
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await AssertThrowsAsync<OperationCanceledException>(() =>
                client.DownloadPackageAsync(
                    CreatePlugin(),
                    CreateRelease(payload),
                    destination,
                    cancellationToken: cancellation.Token));
            Assert(!File.Exists(destination), "cancelled download is removed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task TestValidInstallationAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var plugin = CreatePlugin();
        var release = CreateRelease(payload);
        var storage = CreateTemporaryDirectory();
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            var catalog = new PluginCatalog(storage);
            var result = await PluginPackageInstaller.InstallAsync(
                catalog,
                client,
                plugin,
                release,
                null,
                null,
                CancellationToken.None);
            Assert(result.Manifest.Id == plugin.Id, "manifest id matches");
            Assert(File.Exists(Path.Combine(result.PackageDirectory, "plugin.json")), "manifest committed");
            Assert(File.Exists(Path.Combine(result.PackageDirectory, "TestPlugin.dll")), "assembly committed");
            var scanned = catalog.Scan();
            Assert(scanned.Count == 1 && scanned[0].Manifest?.Version == release.Version, "installed package scans");
            Assert(result.Complete() is null, "installation transaction completes");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestTraversalRejectionAsync()
    {
        var payload = CreatePackage(includeTraversal: true);
        var plugin = CreatePlugin();
        var release = CreateRelease(payload);
        var storage = CreateTemporaryDirectory();
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            var catalog = new PluginCatalog(storage);
            await AssertThrowsAsync<InvalidDataException>(() =>
                PluginPackageInstaller.InstallAsync(
                    catalog,
                    client,
                    plugin,
                    release,
                    null,
                    null,
                    CancellationToken.None));
            Assert(!Directory.EnumerateFiles(storage, "escape.txt", SearchOption.AllDirectories).Any(),
                "traversal target was never created");
            Assert(catalog.Scan().Count == 0, "malicious package was not installed");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestWindowsDeviceNameRejectionAsync()
    {
        var payload = CreatePackage(includeTraversal: false, extraEntry: "COM¹/payload.dll");
        var plugin = CreatePlugin();
        var release = CreateRelease(payload);
        var storage = CreateTemporaryDirectory();
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            var catalog = new PluginCatalog(storage);
            await AssertThrowsAsync<InvalidDataException>(() =>
                PluginPackageInstaller.InstallAsync(
                    catalog,
                    client,
                    plugin,
                    release,
                    null,
                    null,
                    CancellationToken.None));
            Assert(catalog.Scan().Count == 0, "reserved Windows device path was not installed");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestInstallationRollbackAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var storage = CreateTemporaryDirectory();
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            var catalog = new PluginCatalog(storage);
            var result = await PluginPackageInstaller.InstallAsync(
                catalog,
                client,
                CreatePlugin(),
                CreateRelease(payload),
                null,
                null,
                CancellationToken.None);
            Assert(Directory.Exists(result.PackageDirectory), "new package committed before refresh");
            Assert(result.Rollback() is null, "rollback succeeds");
            Assert(!Directory.Exists(result.PackageDirectory), "new package removed by rollback");
            Assert(catalog.Scan().Count == 0, "catalog returned to pre-install state");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestInterruptedUpdateRecoveryAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(transaction, "backup");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "marker.txt"), "old package");
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                "{\"Version\":2,\"TargetDirectoryName\":\"dev.example.test\",\"HadExistingTarget\":true,\"Phase\":\"prepared\"}");

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);
            Assert(error is null, $"recovery error: {error}");
            Assert(
                File.Exists(Path.Combine(
                    catalog.PackagesDirectory,
                    "dev.example.test",
                    "marker.txt")),
                "old package backup restored");
            Assert(!Directory.Exists(transaction), "completed transaction cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestPreparedUpdateRecoveryAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "new package");
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(transaction, "backup");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "marker.txt"), "old package");
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                "{\"Version\":2,\"TargetDirectoryName\":\"dev.example.test\",\"HadExistingTarget\":true,\"Phase\":\"prepared\"}");

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);
            Assert(error is null, $"recovery error: {error}");
            Assert(
                File.ReadAllText(Path.Combine(target, "marker.txt")) == "old package",
                "old backup wins over unconfirmed new target");
            Assert(!Directory.Exists(transaction), "completed recovery transaction cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestCommittedUpdateRecoveryAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "new package");
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(transaction, "backup");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "marker.txt"), "old package");
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                "{\"Version\":2,\"TargetDirectoryName\":\"dev.example.test\",\"HadExistingTarget\":true,\"Phase\":\"committed\"}");

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);
            Assert(error is null, $"recovery error: {error}");
            Assert(
                File.ReadAllText(Path.Combine(target, "marker.txt")) == "new package",
                "confirmed new target wins over old backup");
            Assert(!Directory.Exists(transaction), "committed recovery transaction cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestPreparedNewInstallRecoveryAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "unconfirmed new package");
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transaction);
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                "{\"Version\":2,\"TargetDirectoryName\":\"dev.example.test\",\"HadExistingTarget\":false,\"Phase\":\"prepared\"}");

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);
            Assert(error is null, $"recovery error: {error}");
            Assert(!Directory.Exists(target), "unconfirmed new package is removed");
            Assert(!Directory.Exists(transaction), "prepared transaction is cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestPreparedUpdateBeforeMoveAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "original package");
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transaction);
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                "{\"Version\":2,\"TargetDirectoryName\":\"dev.example.test\",\"HadExistingTarget\":true,\"Phase\":\"prepared\"}");

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);
            Assert(error is null, $"recovery error: {error}");
            Assert(
                File.ReadAllText(Path.Combine(target, "marker.txt")) == "original package",
                "existing package is preserved before its first move");
            Assert(!Directory.Exists(transaction), "prepared transaction is cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestNewInstallClearsStaleStateAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var release = CreateRelease(payload);
        var plugin = CreatePlugin() with { Releases = [release] };
        var storage = CreateTemporaryDirectory();
        try
        {
            var stateCatalog = new PluginCatalog(storage);
            stateCatalog.UpdateState(plugin.Id, entry =>
            {
                entry.Enabled = true;
                entry.GrantedCapabilities = ["network.http"];
                entry.LastError = "stale state";
            });

            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();
            var result = await manager.InstallFromRepositoryAsync(client, plugin, release);

            Assert(result.Success, $"repository installation succeeds: {result.Message}");
            var installed = manager.Current.Plugins.Single(item => item.Id == plugin.Id);
            Assert(!installed.IsEnabled, "newly installed package remains disabled");
            var persisted = new PluginCatalog(storage).GetState(plugin.Id);
            Assert(!persisted.Enabled, "stale enabled flag is cleared");
            Assert(persisted.GrantedCapabilities.Count == 0, "stale capability grants are cleared");
            Assert(persisted.LastError is null, "stale plugin error is cleared");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestRepositoryDowngradeAsync()
    {
        var oldPayload = CreatePackage(includeTraversal: false, version: "1.0.0");
        var newPayload = CreatePackage(includeTraversal: false, version: "2.0.0");
        var oldRelease = CreateRelease(oldPayload, "1.0.0");
        var newRelease = CreateRelease(newPayload, "2.0.0");
        var plugin = CreatePlugin() with { Releases = [oldRelease, newRelease] };
        var storage = CreateTemporaryDirectory();
        try
        {
            using var oldHttp = new HttpClient(new PayloadHandler(oldPayload));
            using var oldClient = new PluginRepositoryClient(oldHttp);
            using var newHttp = new HttpClient(new PayloadHandler(newPayload));
            using var newClient = new PluginRepositoryClient(newHttp);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var install = await manager.InstallFromRepositoryAsync(
                newClient,
                plugin,
                newRelease);
            Assert(install.Success, $"new version installs: {install.Message}");
            var refused = await manager.InstallFromRepositoryAsync(
                oldClient,
                plugin,
                oldRelease);
            Assert(!refused.Success, "downgrade without explicit confirmation is refused");
            Assert(
                manager.Current.Plugins.Single().Version == "2.0.0",
                "refused downgrade leaves the installed package untouched");
            var staleConfirmation = await manager.InstallFromRepositoryAsync(
                oldClient,
                plugin,
                oldRelease,
                confirmedDowngradeFromVersion: "1.9.0");
            Assert(
                !staleConfirmation.Success,
                "confirmation for a stale installed version cannot authorize a downgrade");

            var downgraded = await manager.InstallFromRepositoryAsync(
                oldClient,
                plugin,
                oldRelease,
                confirmedDowngradeFromVersion: "2.0.0");
            Assert(downgraded.Success, $"confirmed downgrade succeeds: {downgraded.Message}");
            Assert(
                manager.Current.Plugins.Single().Version == "1.0.0",
                "confirmed downgrade installs the selected historical release");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestRepositoryPreviewInstallAsync()
    {
        const string version = "2.0.0-beta.1";
        var payload = CreatePackage(includeTraversal: false, version);
        var release = CreateRelease(payload, version, "preview");
        var plugin = CreatePlugin() with { Releases = [release] };
        var storage = CreateTemporaryDirectory();
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var result = await manager.InstallFromRepositoryAsync(client, plugin, release);
            Assert(result.Success, $"selected preview release installs: {result.Message}");
            Assert(
                manager.Current.Plugins.Single().Version == version,
                "installed preview version matches the selected historical release");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestCanonicalRepositoryReleaseAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var canonical = CreateRelease(payload) with
        {
            Yanked = true,
            YankReason = "Withdrawn by the repository administrator."
        };
        var suppliedClone = canonical with { Yanked = false, YankReason = null };
        var plugin = CreatePlugin() with { Releases = [canonical] };
        var storage = CreateTemporaryDirectory();
        try
        {
            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var result = await manager.InstallFromRepositoryAsync(
                client,
                plugin,
                suppliedClone);

            Assert(!result.Success, "a cloned DTO cannot clear the canonical yank state");
            Assert(manager.Current.Plugins.Count == 0, "withdrawn canonical release is not installed");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestRecoveryFailureBlocksRefreshAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            Directory.CreateDirectory(target);
            using (var archive = new ZipArchive(
                       new MemoryStream(CreatePackage(includeTraversal: false)),
                       ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(target);
            }

            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(transaction, "backup"));
            File.WriteAllText(Path.Combine(transaction, "backup", "marker.txt"), "old package");

            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();
            Assert(manager.Current.Plugins.Count == 0, "catalog scan is blocked before plugin code can load");
            Assert(!string.IsNullOrWhiteSpace(manager.Current.Error), "recovery failure is surfaced");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestPluginStorageSingleManagerAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var stateCatalog = new PluginCatalog(storage);
            var packageDirectory = Path.Combine(
                stateCatalog.PackagesDirectory,
                "dev.example.test");
            Directory.CreateDirectory(packageDirectory);
            using (var archive = new ZipArchive(
                       new MemoryStream(CreatePackage(includeTraversal: false)),
                       ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(packageDirectory);
            }
            stateCatalog.UpdateState("dev.example.test", entry => entry.Enabled = true);

            await using var first = new PluginManager(storage, new FeatureAreaRegistry());
            Assert(
                !File.Exists(Path.Combine(
                    new PluginCatalog(storage).RootDirectory,
                    "repository",
                    ".manager.lock")),
                "active manager lock stays outside the migratable plugin tree");
            await using var second = new PluginManager(storage, new FeatureAreaRegistry());
            await second.InitializeAsync();
            Assert(second.Current.Plugins.Count == 0, "second manager does not scan shared package storage");
            Assert(
                second.Current.Error?.Contains("另一个 NyaLauncher 进程", StringComparison.Ordinal) == true,
                "second manager reports the exclusive storage lock");
            stateCatalog.UpdateState("dev.example.test", entry =>
            {
                entry.Enabled = false;
                entry.GrantedCapabilities.Clear();
            });
            await first.DisposeAsync();
            await second.RefreshAsync();
            Assert(second.Current.Error is null, "waiting manager can take ownership after release");
            Assert(
                second.Current.Plugins.Single().Status == PluginStatus.Disabled,
                "new owner reloads revoked state before scanning or starting plugin code");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestUnsafeShutdownRetainsManagerLockAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
            startInfo.ArgumentList.Add("--unsafe-shutdown-child");
            startInfo.ArgumentList.Add(storage);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start shutdown test process.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert(
                process.ExitCode == 0,
                $"shutdown test process failed: {await standardOutput} {await standardError}");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task<int> RunUnsafeShutdownChildAsync(string storage)
    {
        try
        {
            var catalog = new PluginCatalog(storage);
            var packageDirectory = Path.Combine(
                catalog.PackagesDirectory,
                "dev.example.test");
            Directory.CreateDirectory(packageDirectory);
            using (var archive = new ZipArchive(
                       new MemoryStream(CreatePackage(includeTraversal: false)),
                       ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(packageDirectory);
            }
            File.Copy(
                typeof(global::Tests.PluginEntry).Assembly.Location,
                Path.Combine(packageDirectory, "TestPlugin.dll"),
                overwrite: true);
            catalog.UpdateState("dev.example.test", entry => entry.Enabled = true);

            var manager = new PluginManager(
                storage,
                new FeatureAreaRegistry(),
                (_, _) => Task.FromException(
                    new InvalidOperationException("Simulated component drain failure.")));
            await manager.InitializeAsync();
            Assert(
                manager.Current.Plugins.Single().Status == PluginStatus.Enabled,
                "test plugin is running before shutdown");
            await manager.DisposeAsync();

            try
            {
                using var unexpectedLock = PluginPackageInstaller.AcquireManagerLock(catalog);
                throw new InvalidOperationException(
                    "manager lock was released after a plugin drain failure");
            }
            catch (IOException)
            {
                return 0;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static RepositoryPlugin CreatePlugin() => new()
    {
        Id = "dev.example.test",
        Name = "Test Plugin",
        Description = "Test",
        Authors = ["Tests"],
        RepositoryUrl = "https://github.com/example/test-plugin",
        Maintainers = ["example"],
        Categories = ["utilities"],
        License = "MIT"
    };

    private static RepositoryRelease CreateRelease(
        byte[] payload,
        string version = "1.0.0",
        string channel = "stable")
    {
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return new RepositoryRelease
        {
            Version = version,
            Channel = channel,
            PublishedAt = "2026-08-13T00:00:00Z",
            ReleaseNotesUrl = $"https://github.com/example/test-plugin/releases/tag/v{version}",
            Download = new RepositoryDownload
            {
                Url = $"https://github.com/example/test-plugin/releases/download/v{version}/test-plugin.zip",
                Sha256 = hash,
                Size = payload.Length
            },
            Compatibility = new RepositoryCompatibility
            {
                ManifestVersion = 1,
                ApiVersion = "1.0",
                MinimumLauncherVersion = "0.1.0"
            },
            RequiredCapabilities = [],
            OptionalCapabilities = [],
            Yanked = false
        };
    }

    private static string CreateRepositoryIndexJson(
        RepositoryRelease release,
        bool includeReview = false,
        string? reviewSha256 = null)
    {
        var review = includeReview
            ? $$"""
              ,
                                "review": {
                                  "status": "verified",
                                  "reviewedBy": "registry-admin",
                                  "reviewedAt": "2026-08-13T08:00:00Z",
                                  "sha256": "{{reviewSha256 ?? release.Download.Sha256}}",
                                  "notes": "Release asset reviewed."
                                }
              """
            : string.Empty;
        return $$"""
        {
          "schemaVersion": 1,
          "name": "NyaLauncher Plugins",
          "sourceUrl": "https://github.com/redstore-noob/NyaLauncher-Plugins",
          "plugins": [
            {
              "id": "dev.example.test",
              "name": "Test Plugin",
              "description": "Test",
              "authors": ["Tests"],
              "repositoryUrl": "https://github.com/example/test-plugin",
              "maintainers": ["example"],
              "categories": ["utilities"],
              "license": "MIT",
              "releases": [
                {
                  "version": "{{release.Version}}",
                  "channel": "stable",
                  "publishedAt": "2026-08-13T00:00:00Z",
                  "releaseNotesUrl": "https://github.com/example/test-plugin/releases/tag/v1.0.0",
                  "download": {
                    "url": "{{release.Download.Url}}",
                    "sha256": "{{release.Download.Sha256}}",
                    "size": {{release.Download.Size}}
                  },
                  "compatibility": {
                    "manifestVersion": 1,
                    "apiVersion": "1.0",
                    "minimumLauncherVersion": "0.1.0"
                  },
                  "requiredCapabilities": [],
                  "optionalCapabilities": [],
                  "yanked": false{{review}}
                }
              ]
            }
          ]
        }
        """;
    }

    private static byte[] CreatePackage(
        bool includeTraversal,
        string version = "1.0.0",
        string? extraEntry = null)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "plugin.json", """
            {
              "manifestVersion": 1,
              "id": "dev.example.test",
              "name": "Test Plugin",
              "version": "{{VERSION}}",
              "apiVersion": "1.0",
              "minimumLauncherVersion": "0.1.0",
              "description": "Test",
              "authors": ["Tests"],
              "license": "MIT",
              "entryAssembly": "TestPlugin.dll",
              "entryType": "Tests.PluginEntry",
              "requiredCapabilities": [],
              "optionalCapabilities": [],
              "settings": []
            }
            """.Replace("{{VERSION}}", version, StringComparison.Ordinal));
            WriteEntry(archive, "TestPlugin.dll", "not loaded during package validation");
            if (includeTraversal)
                WriteEntry(archive, "../escape.txt", "must not escape");
            if (extraEntry is not null)
                WriteEntry(archive, extraEntry, "must not install");
        }
        return memory.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NyaLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(response);
        }
    }
}
