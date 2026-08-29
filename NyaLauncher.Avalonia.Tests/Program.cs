using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Plugins;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Tests;

internal static class Program
{
    private const string TestLineageId = "abcdefab-cdef-5abc-8def-abcdefabcdef";

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--unsafe-shutdown-child", var storage])
            return await RunUnsafeShutdownChildAsync(storage);

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("SemanticVersion ordering", TestSemanticVersionAsync),
            ("Plugin catalog prerelease compatibility", TestPluginCatalogPrereleaseCompatibilityAsync),
            ("Repository index validation", TestRepositoryIndexAsync),
            ("Repository rename history validation", TestRepositoryRenameHistoryValidationAsync),
            ("Repository v1 fallback is 404-only", TestRepositoryV1FallbackAsync),
            ("Malformed repository v2 fails closed", TestMalformedV2DoesNotFallbackAsync),
            ("Repository historical release selection", TestRepositoryHistoricalReleasesAsync),
            ("Repository hidden withdrawal visibility", TestRepositoryWithdrawalVisibilityAsync),
            ("Repository index strict compatibility", TestStrictRepositoryContractAsync),
            ("Verified repository review", TestVerifiedRepositoryReviewAsync),
            ("Review hash mismatch rejection", TestReviewHashMismatchAsync),
            ("Unreviewed install confirmation policy", TestReviewConfirmationPolicyAsync),
            ("SHA-256 mismatch cleanup", TestHashMismatchAsync),
            ("Download cancellation cleanup", TestDownloadCancellationAsync),
            ("Valid package installation", TestValidInstallationAsync),
            ("Committed package rollback", TestInstallationRollbackAsync),
            ("Update rollback requires old backup", TestUpdateRollbackRequiresBackupAsync),
            ("Interrupted update recovery", TestInterruptedUpdateRecoveryAsync),
            ("Prepared update recovery prefers backup", TestPreparedUpdateRecoveryAsync),
            ("Prepared update before move keeps target", TestPreparedUpdateBeforeMoveAsync),
            ("Prepared new install recovery removes target", TestPreparedNewInstallRecoveryAsync),
            ("Committed update recovery keeps target", TestCommittedUpdateRecoveryAsync),
            ("Prepared uninstall recovery restores package", TestPreparedUninstallRecoveryAsync),
            ("Committed uninstall recovery removes backup", TestCommittedUninstallRecoveryAsync),
            ("Staged uninstall rollback restores state", TestStagedUninstallRollbackAsync),
            ("Invalid uninstall state journals fail closed", TestInvalidUninstallStateJournalAsync),
            ("New install clears stale plugin trust", TestNewInstallClearsStaleStateAsync),
            ("Repository downgrade requires confirmation", TestRepositoryDowngradeAsync),
            ("Repository preview release installation", TestRepositoryPreviewInstallAsync),
            ("Repository install uses canonical release", TestCanonicalRepositoryReleaseAsync),
            ("Repository origin blocks identity replacement", TestRepositoryIdentityReplacementAsync),
            ("Repository rename preserves numeric identity", TestRepositoryRenameIdentityAsync),
            ("Repository generation replacement isolates data", TestRepositoryGenerationIsolationAsync),
            ("Legacy local plugin cannot auto-bind", TestLegacyLocalPluginCannotAutoBindAsync),
            ("Legacy v1 origin cannot auto-bind v2", TestLegacyV1OriginCannotAutoBindV2Async),
            ("Plugin components start in library", TestPluginComponentsStartInLibraryAsync),
            ("Built-in component catalog and layered avatar", TestBuiltInComponentCatalogAndAvatarAsync),
            ("Polygon component host theme inheritance", TestPolygonComponentThemeInheritanceAsync),
            ("Plugin area removal persists", TestPluginAreaRemovalAsync),
            ("All workspace areas can be removed", TestAllWorkspaceAreasCanBeRemovedAsync),
            ("Component scale snapshot validation", TestComponentScaleSnapshotAsync),
            ("Plugin storage is single-manager", TestPluginStorageSingleManagerAsync),
            ("Unsafe shutdown retains manager lock", TestUnsafeShutdownRetainsManagerLockAsync),
            ("Unresolved recovery blocks catalog scan", TestRecoveryFailureBlocksRefreshAsync),
            ("ZIP traversal rejection", TestTraversalRejectionAsync),
            ("ZIP Windows device name rejection", TestWindowsDeviceNameRejectionAsync),
            ("ZIP install origin spoofing rejection", TestInstallOriginSpoofingRejectionAsync)
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
        Assert(SemanticVersion.TryParse("0.1.0-ppre2+commit", out var launcherPreview),
            "launcher informational version with build metadata parses");
        Assert(SemanticVersion.TryParse("0.1.0-ppre2", out var previewMinimum),
            "matching preview minimum parses");
        Assert(SemanticVersion.TryParse("0.1.0", out var stableMinimum),
            "stable minimum parses");
        Assert(launcherPreview.CompareTo(previewMinimum) == 0,
            "matching prerelease minimum is accepted regardless of build metadata");
        Assert(launcherPreview.CompareTo(stableMinimum) < 0,
            "prerelease launcher is lower than the stable version with the same core");
        Assert(
            SemanticVersion.TryParse(SemanticVersion.LauncherVersion.ToString(), out var reparsed) &&
            reparsed.CompareTo(SemanticVersion.LauncherVersion) == 0,
            "runtime launcher version round-trips as strict informational SemVer");
        Assert(!SemanticVersion.TryParse("1.2", out _), "two-part version is rejected");
        Assert(!SemanticVersion.TryParse("1.2.3-01", out _), "numeric prerelease leading zero is rejected");
        Assert(
            SemanticVersion.TryParse("1.2.3-2147483648", out var largeNumericPreview) &&
            SemanticVersion.TryParse("1.2.3-10000000000", out var largerNumericPreview) &&
            largeNumericPreview.CompareTo(largerNumericPreview) < 0,
            "numeric prerelease identifiers retain arbitrary-precision SemVer ordering");
        Assert(
            SemanticVersion.TryParse("1.2.3-999999999999", out var numericPreview) &&
            SemanticVersion.TryParse("1.2.3-0alpha", out var alphanumericPreview) &&
            numericPreview.CompareTo(alphanumericPreview) < 0,
            "numeric prerelease identifiers sort before alphanumeric identifiers regardless of size");
        return Task.CompletedTask;
    }

    private static Task TestPluginCatalogPrereleaseCompatibilityAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var packageDirectory = Path.Combine(catalog.PackagesDirectory, "dev.example.preview");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllBytes(Path.Combine(packageDirectory, "TestPlugin.dll"), [0]);

            var manifest = new PluginManifest
            {
                Id = "dev.example.preview",
                Name = "Preview Compatibility Test",
                Version = "1.0.0",
                MinimumLauncherVersion = "0.1.0",
                EntryAssembly = "TestPlugin.dll",
                EntryType = "Dev.Example.PreviewPlugin"
            };
            var manifestPath = Path.Combine(packageDirectory, "plugin.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

            var incompatible = catalog.Scan().Single();
            Assert(incompatible.Status == PluginStatus.Incompatible,
                "0.1.0-ppre2 does not satisfy a stable 0.1.0 minimum");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest with
                {
                    MinimumLauncherVersion = "0.1.0-ppre2"
                }));
            var compatible = catalog.Scan().Single();
            Assert(compatible.Status == PluginStatus.Disabled,
                "0.1.0-ppre2 satisfies the matching prerelease minimum");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
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

    private static Task TestBuiltInComponentCatalogAndAvatarAsync()
    {
        var provider = new BuiltInFeatureAreaProvider(
            _ => { },
            new MinecraftProfileService(),
            new NyaLauncher.Core.Launch.GameLaunchService(),
            _ => { });
        var areas = provider.GetFeatureAreas().ToArray();
        var actionIds = areas
            .SelectMany(area => area.Actions)
            .Select(action => action.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registrations = areas
            .SelectMany(area => area.PolygonComponents
                .Concat(area.Actions
                    .Select(action => action.PolygonComponent)
                    .Where(registration => registration is not null)))
            .Cast<PolygonComponentRegistration>()
            .ToArray();
        var polygonIds = registrations
            .Select(registration => registration.Definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredId in new[]
                 {
                     "select-instance",
                     "instances",
                     "downloads",
                     "tasks",
                     "settings",
                     "runtime",
                     "music-player"
                 })
        {
            Assert(actionIds.Contains(requiredId),
                $"built-in action '{requiredId}' remains registered");
        }

        Assert(!polygonIds.Contains("nyalauncher.builtin/download-task-progress"),
            "download progress demo stays out of the catalog");
        Assert(polygonIds.Contains("nyalauncher.builtin/music-player"),
            "portable music component remains registered");
        Assert(polygonIds.Contains(BuiltInGameInstanceSelectorComponent.ComponentId),
            "functional game instance selector remains registered");
        Assert(polygonIds.Contains(BuiltInPluginListComponent.ComponentId),
            "functional plugin list remains registered");

        var avatarDefinition = registrations.Single(registration =>
            string.Equals(
                registration.Definition.Id,
                BuiltInSkinCapeComponent.ComponentId,
                StringComparison.OrdinalIgnoreCase)).Definition;
        var face = (ImageElementDefinition)avatarDefinition.Elements.Single(element =>
            string.Equals(element.Id, "skin-face", StringComparison.OrdinalIgnoreCase));
        var hat = (ImageElementDefinition)avatarDefinition.Elements.Single(element =>
            string.Equals(element.Id, "skin-hat", StringComparison.OrdinalIgnoreCase));

        Assert(face.SourcePixelRect == new ComponentPixelRect(8, 8, 8, 8),
            "avatar base layer uses the Minecraft face UV");
        Assert(hat.SourcePixelRect == new ComponentPixelRect(40, 8, 8, 8),
            "avatar outer layer uses the Minecraft hat UV");
        Assert(face.Pixelated && hat.Pixelated,
            "both avatar layers keep nearest-neighbor rendering");
        Assert(hat.Bounds.Width > face.Bounds.Width && hat.Bounds.Height > face.Bounds.Height,
            "hat layer expands beyond the base face for visible depth");
        Assert(face.CornerRadius == 0 && hat.CornerRadius == 0,
            "avatar pixels are not rounded away");

        var defaults = WorkspaceDefaultProfile.Create();
        var defaultIds = defaults.Areas
            .SelectMany(area => area.ActionIds)
            .Concat(defaults.ComponentPlacements.Select(placement => placement.ComponentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(!defaultIds.Contains("downloads"),
            "first-run workspace contains no removed download action");
        return Task.CompletedTask;
    }

    private static Task TestPolygonComponentThemeInheritanceAsync()
    {
        var inherited = new PolygonComponentBuilder(
                "io.github.example/inherited-theme",
                "Inherited theme")
            .Build()
            .Theme;

        Assert(
            inherited.Variant == ComponentThemeVariant.Default,
            "default theme inherits the host neutral variant");
        Assert(
            inherited.BorderThickness > 0,
            "default theme keeps a positive border thickness");

        var customizedDefinition = new PolygonComponentBuilder(
                "io.github.example/custom-theme",
                "Custom theme")
            .WithTheme(new PolygonComponentTheme
            {
                Variant = ComponentThemeVariant.Launch,
                BorderThickness = 2
            })
            .Build();
        var customized = customizedDefinition.Theme;

        Assert(
            customized.Variant == ComponentThemeVariant.Launch,
            "explicit accent variant is preserved");
        Assert(
            Math.Abs(customized.BorderThickness - 2) < 0.001,
            "explicit border thickness is preserved");

        var slotResourceKey = typeof(PolygonComponentView).GetMethod(
            "SlotResourceKey",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        Assert(slotResourceKey is not null, "theme slot resolver remains available");
        var definitionField = typeof(PolygonComponentView).GetField(
            "_definition",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        Assert(definitionField is not null, "component view keeps its definition field");

        // SlotResourceKey 只读取 _definition.Theme.Variant，无需完整的 Avalonia 平台。
        var view = (PolygonComponentView)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(PolygonComponentView));

        definitionField!.SetValue(view, new PolygonComponentBuilder(
            "io.github.example/inherited-view",
            "Inherited view").Build());
        var inheritedSurface = (string)slotResourceKey!.Invoke(
            view,
            ["Surface"])!;
        Assert(
            inheritedSurface == "ComponentBgBrush",
            "default variant resolves the host neutral surface resource");

        definitionField!.SetValue(view, customizedDefinition);
        var launchSurface = (string)slotResourceKey!.Invoke(
            view,
            ["Surface"])!;
        Assert(
            launchSurface == "ComponentPrimaryBgBrush",
            "launch variant resolves the host accent fill resource");
        var launchText = (string)slotResourceKey!.Invoke(
            view,
            ["TextPrimary"])!;
        Assert(
            launchText == "WhiteBrush",
            "launch variant resolves the accent foreground resource");
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
        var plugin = index.Plugins.Single();
        Assert(plugin.Generation == 1, "v2 current generation parsed");
        Assert(plugin.LineageId == TestLineageId, "v2 lineage UUID parsed");
        Assert(plugin.Publisher?.RepositoryId == 1001, "v2 numeric repository identity parsed");
        Assert(parsedRelease?.Generation == 1, "release generation parsed");
    }

    private static async Task TestRepositoryRenameHistoryValidationAsync()
    {
        const string oldRepositoryUrl = "https://github.com/example/test-plugin";
        const string renamedRepositoryUrl = "https://github.com/example/renamed-plugin";
        var release = CreateRelease(CreatePackage(includeTraversal: false));
        var original = CreateRepositoryIndexJson(release);

        static string Mutate(
            string json,
            Action<JsonObject, JsonObject, JsonObject> mutation)
        {
            var root = JsonNode.Parse(json)?.AsObject() ??
                       throw new InvalidOperationException("test registry JSON did not parse");
            var plugin = root["plugins"]?.AsArray()[0]?.AsObject() ??
                         throw new InvalidOperationException("test plugin was missing");
            var binding = plugin["generations"]?.AsArray()[0]?.AsObject() ??
                          throw new InvalidOperationException("test generation was missing");
            var releaseNode = plugin["releases"]?.AsArray()[0]?.AsObject() ??
                              throw new InvalidOperationException("test release was missing");
            mutation(plugin, binding, releaseNode);
            return root.ToJsonString();
        }

        var renamed = Mutate(original, (plugin, binding, _) =>
        {
            plugin["repositoryUrl"] = renamedRepositoryUrl;
            binding["repositoryUrl"] = renamedRepositoryUrl;
            binding["repositoryUrlHistory"] = new JsonArray(
                JsonValue.Create(oldRepositoryUrl),
                JsonValue.Create(renamedRepositoryUrl));
        });
        using (var http = new HttpClient(
                   new PayloadHandler(Encoding.UTF8.GetBytes(renamed))))
        using (var client = new PluginRepositoryClient(http))
        {
            var index = await client.LoadIndexAsync();
            var binding = index.Plugins.Single().Generations.Single();
            Assert(
                binding.RepositoryUrlHistory.SequenceEqual(
                    [oldRepositoryUrl, renamedRepositoryUrl],
                    StringComparer.Ordinal),
                "old release URLs remain valid after an identity-preserving rename");
        }

        var differentlyCasedReleaseUrls = Mutate(original, (_, _, releaseNode) =>
        {
            releaseNode["releaseNotesUrl"] =
                "https://github.com/EXAMPLE/TEST-PLUGIN/releases/tag/v1.0.0";
            releaseNode["download"]!.AsObject()["url"] =
                "https://github.com/EXAMPLE/TEST-PLUGIN/releases/download/v1.0.0/test-plugin.zip";
        });
        using (var http = new HttpClient(
                   new PayloadHandler(Encoding.UTF8.GetBytes(differentlyCasedReleaseUrls))))
        using (var client = new PluginRepositoryClient(http))
        {
            _ = await client.LoadIndexAsync();
        }

        var invalidCases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["missing history"] = Mutate(original, (_, binding, _) =>
                binding.Remove("repositoryUrlHistory")),
            ["empty history"] = Mutate(original, (_, binding, _) =>
                binding["repositoryUrlHistory"] = new JsonArray()),
            ["case-insensitive duplicate"] = Mutate(original, (_, binding, _) =>
                binding["repositoryUrlHistory"] = new JsonArray(
                    JsonValue.Create("https://github.com/EXAMPLE/TEST-PLUGIN"),
                    JsonValue.Create(oldRepositoryUrl))),
            ["current URL is not last"] = Mutate(original, (_, binding, _) =>
                binding["repositoryUrlHistory"] = new JsonArray(
                    JsonValue.Create(oldRepositoryUrl),
                    JsonValue.Create(renamedRepositoryUrl))),
            ["non-canonical trailing slash"] = Mutate(original, (plugin, binding, _) =>
            {
                plugin["repositoryUrl"] = oldRepositoryUrl + "/";
                binding["repositoryUrl"] = oldRepositoryUrl + "/";
                binding["repositoryUrlHistory"] = new JsonArray(
                    JsonValue.Create(oldRepositoryUrl + "/"));
            }),
            ["child path alias in history"] = Mutate(original, (_, binding, _) =>
                binding["repositoryUrlHistory"] = new JsonArray(
                    JsonValue.Create("https://github.com/example/test-plugin/issues"),
                    JsonValue.Create(oldRepositoryUrl))),
            ["download repository alias"] = Mutate(original, (_, _, releaseNode) =>
                releaseNode["download"]!.AsObject()["url"] =
                    "https://github.com/example/test-plugin-evil/releases/download/" +
                    "v1.0.0/test-plugin.zip"),
            ["notes repository alias"] = Mutate(original, (_, _, releaseNode) =>
                releaseNode["releaseNotesUrl"] =
                    "https://github.com/example/test-plugin-evil/releases/tag/v1.0.0"),
            ["notes route alias"] = Mutate(original, (_, _, releaseNode) =>
                releaseNode["releaseNotesUrl"] =
                    oldRepositoryUrl + "/releases/compare/v1.0.0")
        };

        foreach (var (name, invalid) in invalidCases)
        {
            using var http = new HttpClient(
                new PayloadHandler(Encoding.UTF8.GetBytes(invalid)));
            using var client = new PluginRepositoryClient(http);
            try
            {
                await AssertThrowsAsync<InvalidDataException>(() => client.LoadIndexAsync());
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"repository history case '{name}' was accepted",
                    exception);
            }
        }
    }

    private static async Task TestRepositoryV1FallbackAsync()
    {
        var package = CreatePackage(includeTraversal: false);
        var legacy = CreateLegacyRepositoryIndexJson(CreateRelease(package));
        var handler = new VersionedIndexHandler(
            v2Status: HttpStatusCode.NotFound,
            v2Payload: null,
            v1Payload: Encoding.UTF8.GetBytes(legacy));
        using var http = new HttpClient(handler);
        using var client = new PluginRepositoryClient(http);

        var index = await client.LoadIndexAsync();

        Assert(index.SchemaVersion == 1, "a v2 404 falls back to the immutable v1 contract");
        Assert(index.Plugins.Single().LineageId is null, "v1 does not invent a bound lineage");
        Assert(index.Plugins.Single().Publisher is null, "v1 does not invent numeric publisher identity");
        Assert(handler.V1RequestCount == 1, "legacy endpoint is requested after the explicit v2 404");
    }

    private static async Task TestMalformedV2DoesNotFallbackAsync()
    {
        var package = CreatePackage(includeTraversal: false);
        var legacy = CreateLegacyRepositoryIndexJson(CreateRelease(package));
        var handler = new VersionedIndexHandler(
            v2Status: HttpStatusCode.OK,
            v2Payload: Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"plugins\":[]}"),
            v1Payload: Encoding.UTF8.GetBytes(legacy));
        using var http = new HttpClient(handler);
        using var client = new PluginRepositoryClient(http);

        await AssertThrowsAsync<InvalidDataException>(() => client.LoadIndexAsync());
        Assert(handler.V1RequestCount == 0, "malformed v2 never triggers a downgrade request");
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

    private static Task TestRepositoryWithdrawalVisibilityAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var withdrawn = CreateRelease(payload) with
        {
            Yanked = true,
            YankReason = "retired"
        };
        var plugin = CreateV2Plugin(withdrawn) with
        {
            LifecycleStatus = "retired",
            Visibility = "hidden",
            Generations =
            [
                CreateGenerationBinding(1, 1001, 101, "retired")
            ]
        };

        Assert(!RepositoryCatalogPolicy.ShouldDisplay(plugin, installed: null),
            "fully withdrawn uninstalled plugin is hidden from discovery");
        var installed = new PluginSnapshot
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = withdrawn.Version,
            PackageDirectory = "test",
            InstallOrigin = PluginCatalog.CreateInstallOrigin(
                CreateV2Plugin(withdrawn),
                withdrawn)
        };
        Assert(RepositoryCatalogPolicy.ShouldDisplay(plugin, installed),
            "fully withdrawn plugin remains visible to an installed user");
        Assert(!RepositoryCatalogPolicy.IsCurrentGenerationInstallable(plugin),
            "retired plugin has no installable current generation");
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
        var invalidCases = new[]
                 {
                     valid.Replace(
                         "\"apiVersion\": \"1.0\"",
                         "\"apiVersion\": \"1.preview\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                          "2026-08-13T00:00:00Z",
                          "2026-08-13T00:00:00+08:00",
                          StringComparison.Ordinal),
                     valid.Replace(
                         "\"minimumLauncherVersion\": \"0.1.0-ppre2\"",
                         "\"minimumLauncherVersion\": \"999.0.0\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"minimumLauncherVersion\": \"0.1.0-ppre2\"",
                         "\"minimumLauncherVersion\": \"0.1.0\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         TestLineageId,
                         TestLineageId.ToUpperInvariant(),
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"visibility\": \"listed\"",
                         "\"visibility\": \"hidden\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"description\": \"Test\"",
                         "\"description\": \" \"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"authors\": [\"Tests\"]",
                         "\"authors\": [\"Tests\", \"tests\"]",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"authors\": [\"Tests\"]",
                         "\"authors\": null",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"authors\": [\"Tests\"]",
                         "\"authors\": []",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"maintainers\": [\"example\"]",
                         "\"maintainers\": [\"example\", \"EXAMPLE\"]",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"maintainers\": [\"example\"]",
                         "\"maintainers\": [\"-invalid\"]",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"categories\": [\"utilities\"]",
                         "\"categories\": [\"utilities\", \"UTILITIES\"]",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"categories\": [\"utilities\"]",
                         "\"categories\": [\"unknown\"]",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"license\": \"MIT\"",
                         "\"license\": \"\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"repositoryUrl\": \"https://github.com/example/test-plugin\"",
                         "\"repositoryUrl\": \"https://github.com/example/test-plugin/issues\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"repositoryUrl\": \"https://github.com/example/test-plugin\"",
                         "\"repositoryUrl\": \"https://github.com/example/test-plugin?owner=other\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"repositoryUrl\": \"https://github.com/example/test-plugin\"",
                         "\"repositoryUrl\": \"https://github.com/bad_owner/test-plugin\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"sourceUrl\": \"https://github.com/redstore-noob/NyaLauncher-Plugins\"",
                         "\"sourceUrl\": \"https://example.com/registry\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "test-plugin.zip\"",
                         "test-plugin.exe\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "test-plugin.zip\"",
                         "test-plugin.zip?token=secret\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "test-plugin.zip\"",
                         "nested/test-plugin.zip\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"requiredCapabilities\": []",
                         "\"requiredCapabilities\": null",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"yanked\": false",
                         "\"yanked\": false, \"yankReason\": \"not actually withdrawn\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"yanked\": false",
                         "\"yanked\": true, \"yankReason\": \"" +
                         new string('x', 1025) + "\"",
                         StringComparison.Ordinal),
                     valid.Replace(
                         "\"minimumLauncherVersion\": \"0.1.0-ppre2\"",
                         "\"minimumLauncherVersion\": \"0.1.0-ppre2\", " +
                         "\"maximumLauncherVersionExclusive\": \"0.1.0-ppre2\"",
                         StringComparison.Ordinal)
                  };
        for (var index = 0; index < invalidCases.Length; index++)
        {
            var invalid = invalidCases[index];
            using var http = new HttpClient(new PayloadHandler(Encoding.UTF8.GetBytes(invalid)));
            using var client = new PluginRepositoryClient(http);
            try
            {
                await AssertThrowsAsync<InvalidDataException>(() => client.LoadIndexAsync());
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"strict repository contract case {index} was accepted",
                    exception);
            }
        }

        var legacy = CreateLegacyRepositoryIndexJson(release);
        var invalidLegacyCases = new[]
        {
            legacy.Replace(
                "\"authors\": [\"Tests\"]",
                "\"authors\": [\"Tests\", \"tests\"]",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"authors\": [\"Tests\"]",
                "\"authors\": []",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"maintainers\": [\"example\"]",
                "\"maintainers\": [\"bad login\"]",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"categories\": [\"utilities\"]",
                "\"categories\": [\"unknown\"]",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"license\": \"MIT\"",
                "\"license\": \" \"",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"repositoryUrl\": \"https://github.com/example/test-plugin\"",
                "\"repositoryUrl\": \"https://github.com/example/test-plugin/releases\"",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"optionalCapabilities\": []",
                "\"optionalCapabilities\": null",
                StringComparison.Ordinal),
            legacy.Replace(
                "\"yanked\": false",
                "\"yanked\": null",
                StringComparison.Ordinal)
        };
        for (var index = 0; index < invalidLegacyCases.Length; index++)
        {
            var handler = new VersionedIndexHandler(
                HttpStatusCode.NotFound,
                v2Payload: null,
                Encoding.UTF8.GetBytes(invalidLegacyCases[index]));
            using var http = new HttpClient(handler);
            using var client = new PluginRepositoryClient(http);
            try
            {
                await AssertThrowsAsync<InvalidDataException>(() => client.LoadIndexAsync());
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"strict legacy repository contract case {index} was accepted",
                    exception);
            }
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
            Assert(File.Exists(Path.Combine(
                    result.PackageDirectory,
                    PluginCatalog.InstallOriginFileName)),
                "launcher-owned install origin is committed with the package");
            var scanned = catalog.Scan();
            Assert(scanned.Count == 1 && scanned[0].Manifest?.Version == release.Version, "installed package scans");
            Assert(scanned[0].InstallOrigin is
                {
                    SourceIndexSchemaVersion: 1,
                    Generation: 1,
                    RepositoryId: null,
                    OwnerId: null
                }, "legacy v1 package origin remains explicitly unbound");
            Assert(scanned[0].InstallOrigin?.Sha256 == release.Download.Sha256,
                "package origin persists the exact release ZIP hash");
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

    private static async Task TestInstallOriginSpoofingRejectionAsync()
    {
        var payload = CreatePackage(
            includeTraversal: false,
            extraEntry: PluginCatalog.InstallOriginFileName);
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
                    CreatePlugin(),
                    CreateRelease(payload),
                    null,
                    null,
                    CancellationToken.None));
            Assert(catalog.Scan().Count == 0,
                "publisher cannot pre-seed launcher-owned install origin metadata");
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

    private static async Task TestUpdateRollbackRequiresBackupAsync()
    {
        var oldPayload = CreatePackage(includeTraversal: false, version: "1.0.0");
        var newPayload = CreatePackage(includeTraversal: false, version: "2.0.0");
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            using var oldHttp = new HttpClient(new PayloadHandler(oldPayload));
            using var oldClient = new PluginRepositoryClient(oldHttp);
            var oldInstall = await PluginPackageInstaller.InstallAsync(
                catalog,
                oldClient,
                CreatePlugin(),
                CreateRelease(oldPayload, "1.0.0"),
                null,
                null,
                CancellationToken.None);
            Assert(oldInstall.Complete() is null, "initial package transaction commits");

            using var firstUpdateHttp = new HttpClient(new PayloadHandler(newPayload));
            using var firstUpdateClient = new PluginRepositoryClient(firstUpdateHttp);
            var firstUpdate = await PluginPackageInstaller.InstallAsync(
                catalog,
                firstUpdateClient,
                CreatePlugin(),
                CreateRelease(newPayload, "2.0.0"),
                oldInstall.PackageDirectory,
                null,
                CancellationToken.None);
            Assert(firstUpdate.Rollback() is null, "update rollback restores an available old backup");
            Assert(
                catalog.Scan().Single().Manifest?.Version == "1.0.0",
                "successful update rollback restores the old package");

            using var missingBackupHttp = new HttpClient(new PayloadHandler(newPayload));
            using var missingBackupClient = new PluginRepositoryClient(missingBackupHttp);
            var missingBackupUpdate = await PluginPackageInstaller.InstallAsync(
                catalog,
                missingBackupClient,
                CreatePlugin(),
                CreateRelease(newPayload, "2.0.0"),
                oldInstall.PackageDirectory,
                null,
                CancellationToken.None);
            var transaction = Directory.EnumerateDirectories(
                    Path.Combine(catalog.RootDirectory, "repository", "transactions"))
                .Single();
            Directory.Delete(Path.Combine(transaction, "backup"), recursive: true);

            var rollbackError = missingBackupUpdate.Rollback();
            Assert(rollbackError is not null, "missing old backup makes update rollback fail closed");
            Assert(
                catalog.Scan().Single().Manifest?.Version == "2.0.0",
                "failed rollback preserves the only remaining package instead of deleting it");
            Assert(Directory.Exists(transaction), "failed rollback preserves its recovery journal");
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

    private static Task TestPreparedUninstallRecoveryAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(transaction, "backup");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "marker.txt"), "removed package");
            var release = CreateRelease(CreatePackage(includeTraversal: false));
            var plugin = CreateV2Plugin(release);
            var expectedOrigin = PluginCatalog.CreateInstallOrigin(plugin, release);
            catalog.UpdateState(plugin.Id, entry =>
            {
                entry.Enabled = true;
                entry.GrantedCapabilities = ["network.http"];
                entry.LastError = "state before uninstall";
                entry.InstallOrigin = expectedOrigin;
            });
            Assert(catalog.TryGetState(plugin.Id, out var previousState),
                "installed plugin has a state entry before uninstall");
            // Exact crash window: package has moved and launcher-owned state was
            // removed, but the prepared journal was not committed yet.
            catalog.RemoveState(plugin.Id);
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                JsonSerializer.Serialize(new
                {
                    Version = 2,
                    Operation = "remove",
                    TargetDirectoryName = plugin.Id,
                    HadExistingTarget = true,
                    Phase = "prepared",
                    PluginId = plugin.Id,
                    HadPreviousState = true,
                    PreviousState = previousState
                }));

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);

            Assert(error is null, $"recovery error: {error}");
            Assert(File.Exists(Path.Combine(target, "marker.txt")),
                "unconfirmed uninstall restores the original package");
            Assert(catalog.TryGetState(plugin.Id, out var restoredState),
                "prepared uninstall recovery restores the removed state entry");
            Assert(restoredState.Enabled &&
                   restoredState.GrantedCapabilities.SequenceEqual(["network.http"]) &&
                   restoredState.LastError == "state before uninstall" &&
                   Equals(restoredState.InstallOrigin, expectedOrigin),
                "prepared uninstall restores enabled state, grants, diagnostics, and origin exactly");
            Assert(!Directory.Exists(transaction), "prepared uninstall transaction is cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestCommittedUninstallRecoveryAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var target = Path.Combine(catalog.PackagesDirectory, "dev.example.test");
            var transaction = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions",
                Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(transaction, "backup");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "marker.txt"), "removed package");
            catalog.UpdateState("dev.example.test", entry =>
            {
                entry.Enabled = true;
                entry.GrantedCapabilities = ["network.http"];
            });
            catalog.RemoveState("dev.example.test");
            File.WriteAllText(
                Path.Combine(transaction, "journal.json"),
                "{\"Version\":2,\"Operation\":\"remove\",\"TargetDirectoryName\":" +
                "\"dev.example.test\",\"HadExistingTarget\":true,\"Phase\":\"committed\"," +
                "\"PluginId\":\"dev.example.test\"}");

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);

            Assert(error is null, $"recovery error: {error}");
            Assert(!Directory.Exists(target), "committed uninstall does not restore the removed package");
            Assert(!catalog.TryGetState("dev.example.test", out _),
                "committed uninstall never restores the removed launcher state");
            Assert(!Directory.Exists(transaction), "committed uninstall backup is safely cleaned");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestStagedUninstallRollbackAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var pluginId = "dev.example.test";
            var target = Path.Combine(catalog.PackagesDirectory, pluginId);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "marker.txt"), "installed package");
            catalog.UpdateState(pluginId, entry =>
            {
                entry.Enabled = true;
                entry.GrantedCapabilities = ["network.http"];
                entry.LastError = "before rollback";
            });
            Assert(catalog.TryGetState(pluginId, out var previousState),
                "state exists before staging an uninstall");

            var removal = PluginPackageInstaller.StageRemoval(
                catalog,
                target,
                pluginId,
                hadPreviousState: true,
                previousState: previousState);
            catalog.RemoveState(pluginId);

            Assert(removal.Rollback() is null, "live uninstall rollback succeeds");
            Assert(File.Exists(Path.Combine(target, "marker.txt")),
                "live rollback restores the package before ending the journal");
            Assert(catalog.TryGetState(pluginId, out var restored) &&
                   restored.Enabled &&
                   restored.GrantedCapabilities.SequenceEqual(["network.http"]) &&
                   restored.LastError == "before rollback",
                "live rollback restores the exact launcher-owned state before deleting its journal");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static Task TestInvalidUninstallStateJournalAsync()
    {
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var transactionsRoot = Path.Combine(
                catalog.RootDirectory,
                "repository",
                "transactions");
            var invalidStateTransaction = Path.Combine(
                transactionsRoot,
                Guid.NewGuid().ToString("N"));
            var oversizedTransaction = Path.Combine(
                transactionsRoot,
                Guid.NewGuid().ToString("N"));
            foreach (var transaction in new[] { invalidStateTransaction, oversizedTransaction })
            {
                var backup = Path.Combine(transaction, "backup");
                Directory.CreateDirectory(backup);
                File.WriteAllText(Path.Combine(backup, "marker.txt"), "preserve me");
            }

            File.WriteAllText(
                Path.Combine(invalidStateTransaction, "journal.json"),
                JsonSerializer.Serialize(new
                {
                    Version = 2,
                    Operation = "remove",
                    TargetDirectoryName = "dev.example.test",
                    HadExistingTarget = true,
                    Phase = "prepared",
                    PluginId = "dev.example.test",
                    HadPreviousState = true,
                    PreviousState = new
                    {
                        Enabled = true,
                        GrantedCapabilities = Enumerable.Range(0, 65)
                            .Select(index => $"capability-{index}")
                            .ToArray(),
                        LastError = (string?)null,
                        InstallOrigin = (PluginInstallOrigin?)null
                    }
                }));
            File.WriteAllText(
                Path.Combine(oversizedTransaction, "journal.json"),
                JsonSerializer.Serialize(new
                {
                    Version = 2,
                    Operation = "remove",
                    TargetDirectoryName = "dev.example.test",
                    HadExistingTarget = true,
                    Phase = "prepared",
                    PluginId = "dev.example.test",
                    HadPreviousState = true,
                    PreviousState = new
                    {
                        Enabled = true,
                        GrantedCapabilities = Array.Empty<string>(),
                        LastError = new string('x', 40 * 1024),
                        InstallOrigin = (PluginInstallOrigin?)null
                    }
                }));

            var error = PluginPackageInstaller.RecoverInterruptedTransactions(catalog);

            Assert(!string.IsNullOrWhiteSpace(error),
                "invalid and oversized uninstall state journals block recovery");
            Assert(Directory.Exists(Path.Combine(invalidStateTransaction, "backup")) &&
                   Directory.Exists(Path.Combine(oversizedTransaction, "backup")),
                "fail-closed journal validation preserves both package backups for diagnosis");
            Assert(!catalog.TryGetState("dev.example.test", out _),
                "invalid journal data is never written into launcher-owned state");
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
            Assert(persisted.InstallOrigin?.Version == release.Version,
                "state caches the package-owned install origin after scan");
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

    private static async Task TestRepositoryIdentityReplacementAsync()
    {
        var firstPayload = CreatePackage(includeTraversal: false, version: "1.0.0");
        var nextPayload = CreatePackage(includeTraversal: false, version: "2.0.0");
        var firstRelease = CreateRelease(firstPayload, "1.0.0");
        var nextRelease = CreateRelease(nextPayload, "2.0.0");
        var trusted = CreateV2Plugin(firstRelease, nextRelease);
        var impostor = trusted with
        {
            Publisher = new RepositoryPublisherIdentity
            {
                RepositoryId = 9001,
                OwnerId = 901
            },
            Generations =
            [
                CreateGenerationBinding(1, 9001, 901, "active")
            ]
        };
        var storage = CreateTemporaryDirectory();
        try
        {
            using var firstHttp = new HttpClient(new PayloadHandler(firstPayload));
            using var firstClient = new PluginRepositoryClient(firstHttp);
            using var nextHttp = new HttpClient(new PayloadHandler(nextPayload));
            using var nextClient = new PluginRepositoryClient(nextHttp);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var installed = await manager.InstallFromRepositoryAsync(
                firstClient,
                trusted,
                firstRelease);
            Assert(installed.Success, $"trusted publisher installs: {installed.Message}");
            var refused = await manager.InstallFromRepositoryAsync(
                nextClient,
                impostor,
                nextRelease);

            Assert(!refused.Success, "same ID/generation from a different numeric publisher is refused");
            Assert(refused.Message.Contains("数字发布者", StringComparison.Ordinal),
                "publisher replacement reports an identity warning");
            Assert(manager.Current.Plugins.Single().Version == "1.0.0",
                "publisher replacement leaves installed bytes unchanged");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestRepositoryRenameIdentityAsync()
    {
        const string oldRepositoryUrl = "https://github.com/example/test-plugin";
        const string renamedRepositoryUrl = "https://github.com/example/renamed-plugin";
        var oldPayload = CreatePackage(includeTraversal: false, version: "1.0.0");
        var newPayload = CreatePackage(includeTraversal: false, version: "1.1.0");
        var oldRelease = CreateRelease(oldPayload, "1.0.0");
        var newRelease = CreateRelease(newPayload, "1.1.0") with
        {
            ReleaseNotesUrl = renamedRepositoryUrl + "/releases/tag/v1.1.0",
            Download = new RepositoryDownload
            {
                Url = renamedRepositoryUrl + "/releases/download/v1.1.0/test-plugin.zip",
                Sha256 = Convert.ToHexString(SHA256.HashData(newPayload)).ToLowerInvariant(),
                Size = newPayload.Length
            }
        };
        var beforeRename = CreateV2Plugin(oldRelease);
        var installedOrigin = PluginCatalog.CreateInstallOrigin(beforeRename, oldRelease);
        var renamedBinding = CreateGenerationBinding(
            1,
            1001,
            101,
            "active",
            renamedRepositoryUrl,
            [oldRepositoryUrl, renamedRepositoryUrl]);
        var afterRename = CreateV2Plugin(oldRelease, newRelease) with
        {
            RepositoryUrl = renamedRepositoryUrl,
            Generations = [renamedBinding]
        };

        Assert(
            RepositoryIdentityPolicy.Compare(afterRename, newRelease, installedOrigin) ==
            RepositoryIdentityMatch.Match,
            "same numeric publisher and generation remains a safe update after a GitHub rename");
        var caseOnlyRename = beforeRename with
        {
            RepositoryUrl = "https://github.com/EXAMPLE/TEST-PLUGIN",
            Generations =
            [
                CreateGenerationBinding(
                    1,
                    1001,
                    101,
                    "active",
                    "https://github.com/EXAMPLE/TEST-PLUGIN")
            ]
        };
        Assert(
            RepositoryIdentityPolicy.Compare(caseOnlyRename, oldRelease, installedOrigin) ==
            RepositoryIdentityMatch.Match,
            "GitHub path casing does not break numeric repository identity continuity");
        var rewrittenHistory = afterRename with
        {
            Generations =
            [
                renamedBinding with
                {
                    RepositoryUrlHistory = [renamedRepositoryUrl]
                }
            ]
        };
        Assert(
            RepositoryIdentityPolicy.Compare(rewrittenHistory, newRelease, installedOrigin) ==
            RepositoryIdentityMatch.InvalidRepositoryHistory,
            "a rename that erases the installed repository URL fails closed");
        Assert(
            RepositoryIdentityPolicy.Compare(
                afterRename,
                newRelease,
                installedOrigin with { SourceIndexSchemaVersion = 1 }) ==
            RepositoryIdentityMatch.LegacyV1NeedsReinstall,
            "legacy v1 provenance cannot use repository history to acquire numeric identity");

        var storage = CreateTemporaryDirectory();
        try
        {
            using var oldHttp = new HttpClient(new PayloadHandler(oldPayload));
            using var oldClient = new PluginRepositoryClient(oldHttp);
            using var newHttp = new HttpClient(new PayloadHandler(newPayload));
            using var newClient = new PluginRepositoryClient(newHttp);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var installed = await manager.InstallFromRepositoryAsync(
                oldClient,
                beforeRename,
                oldRelease);
            Assert(installed.Success, $"pre-rename package installs: {installed.Message}");
            var updated = await manager.InstallFromRepositoryAsync(
                newClient,
                afterRename,
                newRelease);

            Assert(updated.Success, $"same numeric repository updates after rename: {updated.Message}");
            var snapshot = manager.Current.Plugins.Single();
            Assert(snapshot.Version == "1.1.0", "renamed repository update replaces the package");
            Assert(snapshot.InstallOrigin?.RepositoryUrl == renamedRepositoryUrl,
                "successful rename update snapshots the new canonical repository URL");
            Assert(snapshot.InstallOrigin?.RepositoryId == 1001 &&
                   snapshot.InstallOrigin.OwnerId == 101,
                "successful rename update preserves numeric publisher identity");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestRepositoryGenerationIsolationAsync()
    {
        var firstPayload = CreatePackage(
            includeTraversal: false,
            version: "1.0.0",
            includeOptionalNetwork: true);
        var updatedPayload = CreatePackage(
            includeTraversal: false,
            version: "1.1.0",
            includeOptionalNetwork: true);
        var firstRelease = CreateRelease(firstPayload, "1.0.0") with
        {
            OptionalCapabilities = ["network.http"]
        };
        var updatedRelease = CreateRelease(updatedPayload, "1.1.0") with
        {
            OptionalCapabilities = ["network.http"]
        };
        var generationOne = CreateV2Plugin(firstRelease, updatedRelease);
        var replacementRelease = updatedRelease with { Generation = 2 };
        var withdrawnOldRelease = updatedRelease with
        {
            Yanked = true,
            YankReason = "transferred"
        };
        var generationTwo = CreateV2Plugin(withdrawnOldRelease, replacementRelease) with
        {
            Generation = 2,
            Publisher = new RepositoryPublisherIdentity
            {
                RepositoryId = 2002,
                OwnerId = 202
            },
            Generations =
            [
                CreateGenerationBinding(1, 1001, 101, "transferred"),
                CreateGenerationBinding(2, 2002, 202, "active")
            ]
        };
        var storage = CreateTemporaryDirectory();
        try
        {
            using var firstHttp = new HttpClient(new PayloadHandler(firstPayload));
            using var firstClient = new PluginRepositoryClient(firstHttp);
            using var updatedHttp = new HttpClient(new PayloadHandler(updatedPayload));
            using var updatedClient = new PluginRepositoryClient(updatedHttp);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var install = await manager.InstallFromRepositoryAsync(
                firstClient,
                generationOne,
                firstRelease);
            Assert(install.Success, $"generation one installs: {install.Message}");
            var ordinaryUpdate = await manager.InstallFromRepositoryAsync(
                updatedClient,
                generationOne,
                updatedRelease);
            Assert(ordinaryUpdate.Success, $"same-generation update succeeds: {ordinaryUpdate.Message}");
            var grant = await manager.SetOptionalCapabilitiesAsync(
                generationOne.Id,
                ["network.http"]);
            Assert(grant.Success, $"old generation capability grant is saved: {grant.Message}");

            var catalog = new PluginCatalog(storage);
            var oldDataDirectory = catalog.GetPluginDataDirectory(generationOne.Id);
            Directory.CreateDirectory(oldDataDirectory);
            File.WriteAllText(Path.Combine(oldDataDirectory, "old-generation.txt"), "private");

            var refused = await manager.InstallFromRepositoryAsync(
                updatedClient,
                generationTwo,
                replacementRelease);
            Assert(!refused.Success, "cross-generation same-version replacement is refused");
            Assert(refused.Message.Contains("新的发布代际", StringComparison.Ordinal),
                "cross-generation refusal explains the boundary");

            var packageDirectory = manager.Current.Plugins.Single().PackageDirectory;
            var uninstall = await manager.UninstallAsync(generationOne.Id);
            Assert(uninstall.Success, $"explicit launcher uninstall succeeds: {uninstall.Message}");
            Assert(manager.Current.Plugins.Count == 0, "uninstall removes the old package from the catalog");
            Assert(!Directory.Exists(packageDirectory), "uninstall commits removal of the package directory");
            var uninstalledState = new PluginCatalog(storage).GetState(generationOne.Id);
            Assert(uninstalledState.GrantedCapabilities.Count == 0,
                "uninstall revokes capability grants from the removed installation");
            Assert(uninstalledState.InstallOrigin is null,
                "uninstall clears the cached launcher-owned origin");
            Assert(!File.ReadAllText(new PluginCatalog(storage).StateFilePath).Contains(
                    generationOne.Id,
                    StringComparison.Ordinal),
                "uninstall removes the launcher-owned state entry instead of leaving an ID reservation");
            Assert(File.Exists(Path.Combine(oldDataDirectory, "old-generation.txt")),
                "uninstall preserves old generation private data for recovery/audit");

            var replacement = await manager.InstallFromRepositoryAsync(
                updatedClient,
                generationTwo,
                replacementRelease);
            Assert(replacement.Success, $"new generation installs only after removal: {replacement.Message}");
            var newCatalog = new PluginCatalog(storage);
            var newDataDirectory = newCatalog.GetPluginDataDirectory(generationTwo.Id);
            Assert(!string.Equals(oldDataDirectory, newDataDirectory, StringComparison.OrdinalIgnoreCase),
                "different generations receive different private data directories");
            Assert(!File.Exists(Path.Combine(newDataDirectory, "old-generation.txt")),
                "new generation cannot read old generation private data by default");
            Assert(newCatalog.GetState(generationTwo.Id).GrantedCapabilities.Count == 0,
                "new generation does not inherit the old generation capability grant");
            var origin = manager.Current.Plugins.Single().InstallOrigin;
            Assert(origin?.Generation == 2 && origin.RepositoryId == 2002 && origin.OwnerId == 202,
                "replacement package persists the new numeric origin");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestLegacyLocalPluginCannotAutoBindAsync()
    {
        var payload = CreatePackage(includeTraversal: false);
        var release = CreateRelease(payload);
        var plugin = CreateV2Plugin(release);
        var storage = CreateTemporaryDirectory();
        try
        {
            var catalog = new PluginCatalog(storage);
            var packageDirectory = Path.Combine(catalog.PackagesDirectory, plugin.Id);
            Directory.CreateDirectory(packageDirectory);
            using (var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read))
                archive.ExtractToDirectory(packageDirectory);

            using var http = new HttpClient(new PayloadHandler(payload));
            using var client = new PluginRepositoryClient(http);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();
            Assert(manager.Current.Plugins.Single().InstallOrigin is null,
                "manual package has no launcher-owned origin");

            var result = await manager.InstallFromRepositoryAsync(client, plugin, release);

            Assert(!result.Success, "manual/legacy package cannot auto-bind to a repository identity");
            Assert(result.Message.Contains("没有可信来源快照", StringComparison.Ordinal),
                "legacy refusal explains the missing provenance");
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private static async Task TestLegacyV1OriginCannotAutoBindV2Async()
    {
        var firstPayload = CreatePackage(includeTraversal: false, version: "1.0.0");
        var nextPayload = CreatePackage(includeTraversal: false, version: "1.1.0");
        var firstRelease = CreateRelease(firstPayload, "1.0.0");
        var nextRelease = CreateRelease(nextPayload, "1.1.0");
        var legacyPlugin = CreatePlugin() with { Releases = [firstRelease] };
        var v2Plugin = CreateV2Plugin(firstRelease, nextRelease);
        var storage = CreateTemporaryDirectory();
        try
        {
            using var firstHttp = new HttpClient(new PayloadHandler(firstPayload));
            using var firstClient = new PluginRepositoryClient(firstHttp);
            using var nextHttp = new HttpClient(new PayloadHandler(nextPayload));
            using var nextClient = new PluginRepositoryClient(nextHttp);
            await using var manager = new PluginManager(storage, new FeatureAreaRegistry());
            await manager.InitializeAsync();

            var install = await manager.InstallFromRepositoryAsync(
                firstClient,
                legacyPlugin,
                firstRelease);
            Assert(install.Success, $"v1 package installs with unbound origin: {install.Message}");
            Assert(manager.Current.Plugins.Single().InstallOrigin?.SourceIndexSchemaVersion == 1,
                "installed origin records the legacy source contract");

            var result = await manager.InstallFromRepositoryAsync(
                nextClient,
                v2Plugin,
                nextRelease);

            Assert(!result.Success, "same URL cannot silently bind a v1 origin to v2 identity");
            Assert(result.Message.Contains("旧版 v1", StringComparison.Ordinal),
                "v1-to-v2 refusal requires uninstall and reinstall");
            Assert(manager.Current.Plugins.Single().Version == "1.0.0",
                "failed binding leaves the legacy package untouched");
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

    private static RepositoryPlugin CreateV2Plugin(params RepositoryRelease[] releases) =>
        CreatePlugin() with
        {
            LineageId = TestLineageId,
            Generation = 1,
            LifecycleStatus = "active",
            Visibility = "listed",
            Publisher = new RepositoryPublisherIdentity
            {
                RepositoryId = 1001,
                OwnerId = 101
            },
            Generations =
            [
                CreateGenerationBinding(1, 1001, 101, "active")
            ],
            Releases = releases
        };

    private static RepositoryGenerationBinding CreateGenerationBinding(
        int generation,
        long repositoryId,
        long ownerId,
        string status,
        string repositoryUrl = "https://github.com/example/test-plugin",
        IReadOnlyList<string>? repositoryUrlHistory = null) => new()
    {
        Generation = generation,
        RepositoryUrl = repositoryUrl,
        RepositoryUrlHistory = repositoryUrlHistory ?? [repositoryUrl],
        Publisher = new RepositoryPublisherIdentity
        {
            RepositoryId = repositoryId,
            OwnerId = ownerId
        },
        Status = status
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
                MinimumLauncherVersion = "0.1.0-ppre2"
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
          "schemaVersion": 2,
          "name": "NyaLauncher Plugins",
          "sourceUrl": "https://github.com/redstore-noob/NyaLauncher-Plugins",
          "minimumLauncherVersion": "0.1.0-ppre2",
          "plugins": [
            {
              "id": "dev.example.test",
              "name": "Test Plugin",
              "description": "Test",
              "authors": ["Tests"],
              "repositoryUrl": "https://github.com/example/test-plugin",
              "lineageId": "{{TestLineageId}}",
              "generation": 1,
              "lifecycleStatus": "active",
              "visibility": "listed",
              "publisher": {
                "repositoryId": 1001,
                "ownerId": 101
              },
              "generations": [
                {
                  "generation": 1,
                  "repositoryUrl": "https://github.com/example/test-plugin",
                  "repositoryUrlHistory": [
                    "https://github.com/example/test-plugin"
                  ],
                  "publisher": {
                    "repositoryId": 1001,
                    "ownerId": 101
                  },
                  "status": "active"
                }
              ],
              "maintainers": ["example"],
              "categories": ["utilities"],
              "license": "MIT",
              "releases": [
                {
                  "generation": 1,
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
                    "minimumLauncherVersion": "0.1.0-ppre2"
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

    private static string CreateLegacyRepositoryIndexJson(RepositoryRelease release) => $$"""
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
                "minimumLauncherVersion": "0.1.0-ppre2"
              },
              "requiredCapabilities": [],
              "optionalCapabilities": [],
              "yanked": false
            }
          ]
        }
      ]
    }
    """;

    private static byte[] CreatePackage(
        bool includeTraversal,
        string version = "1.0.0",
        string? extraEntry = null,
        bool includeOptionalNetwork = false)
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
              "minimumLauncherVersion": "0.1.0-ppre2",
              "description": "Test",
              "authors": ["Tests"],
              "license": "MIT",
              "entryAssembly": "TestPlugin.dll",
              "entryType": "Tests.PluginEntry",
              "requiredCapabilities": [],
              "optionalCapabilities": [{{OPTIONAL_CAPABILITIES}}],
              "settings": []
            }
            """
                .Replace("{{VERSION}}", version, StringComparison.Ordinal)
                .Replace(
                    "{{OPTIONAL_CAPABILITIES}}",
                    includeOptionalNetwork ? "\"network.http\"" : string.Empty,
                    StringComparison.Ordinal));
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

    private sealed class VersionedIndexHandler(
        HttpStatusCode v2Status,
        byte[]? v2Payload,
        byte[] v1Payload) : HttpMessageHandler
    {
        public int V1RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isV2 = request.RequestUri?.AbsolutePath.Contains(
                "/public/v2/",
                StringComparison.Ordinal) == true;
            if (!isV2)
                V1RequestCount++;
            var payload = isV2 ? v2Payload : v1Payload;
            var response = new HttpResponseMessage(isV2 ? v2Status : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload ?? [])
            };
            return Task.FromResult(response);
        }
    }
}
