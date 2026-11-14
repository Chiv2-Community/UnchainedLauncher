using CUE4Parse.UE4.VirtualFileSystem;
using Moq;
using Newtonsoft.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnchainedLauncher.UnrealModScanner.ViewModels;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {
    public class AvailableCatalogRegressionTests {
        private static AssetCollections Assets(string name) => new([], [new($"{name}.uasset", "hash", null, null,
            name, "1", "", "", "", true, false, true, false, false, true, false)],
            [new($"TBL/Content/Maps/{name}.umap", "hash", null, null, null, name, null, null, null, "FFA", null)], [], []);

        private static Release Release(string name, AssetCollections assets, params Dependency[] dependencies) =>
            new("v1.0.0", "hash", "mod.pak", DateTime.UtcNow,
                new($"Org/{name}", name, "", null, null, [], [], dependencies.ToList()), assets, "");

        private static ModManager Manager(params Release[] releases) => new(Mock.Of<IModRegistry>(), Mock.Of<IPakDir>(), [],
            releases.GroupBy(release => release.Info.RepoUrl).Select(group => new Mod(group.First().Info, group.ToList())));

        private static void AssertAssets(AvailableModsAndMapsService catalog, AssetCollections assets, bool present) {
            Assert.All(assets.Maps, map => Assert.Equal(present, catalog.AvailableMaps.Contains(map)));
            Assert.All(assets.Blueprints, blueprint => Assert.Equal(present, catalog.AvailableServerModBlueprints.Contains(blueprint)));
        }

        [Theory]
        [InlineData(true, false, false, true)]
        [InlineData(false, false, false, false)]
        [InlineData(null, false, false, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(null, true, false, false)]
        [InlineData(null, false, true, false)]
        public void ServerActorCatalogHonorsExplicitShowInGuiFalseAndExistingExclusions(
            bool? showInGui, bool clientSide, bool frontend, bool expected) {
            var assets = Assets("Visibility");
            assets.Blueprints[0] = assets.Blueprints[0] with {
                bShowInGUI = showInGui,
                bClientside = clientSide,
                bAllowOnFrontend = frontend
            };
            var release = Release("Visibility", assets);
            var manager = Manager(release);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));

            var catalog = new AvailableModsAndMapsService(manager, new ModScanTabVM());

            Assert.Equal(expected, catalog.AvailableServerModBlueprints.Contains(assets.Blueprints[0]));
        }

        [Fact]
        public void LiveEnableAndDisableMatchStartupDependencyCatalog() {
            var dependency = Release("Dependency", Assets("Dependency"));
            var parent = Release("Parent", Assets("Parent"), new Dependency("Org/Dependency", dependency.Tag));
            var manager = Manager(parent, dependency);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            AssertAssets(catalog, dependency.Manifest, false);
            Assert.True(manager.EnableModRelease(ReleaseCoordinates.FromRelease(parent)));
            AssertAssets(catalog, dependency.Manifest, true);
            AssertAssets(catalog, parent.Manifest, true);
            var restarted = new AvailableModsAndMapsService(manager, scan);
            Assert.Equal(catalog.AvailableMaps.OrderBy(map => map.Path), restarted.AvailableMaps.OrderBy(map => map.Path));
            Assert.Equal(catalog.AvailableServerModBlueprints, restarted.AvailableServerModBlueprints);
            Assert.True(manager.DisableModRelease(ReleaseCoordinates.FromRelease(parent)));
            AssertAssets(catalog, dependency.Manifest, false);
            AssertAssets(restarted, dependency.Manifest, false);
            AssertAssets(catalog, parent.Manifest, false);
        }

        [Fact]
        public void SharedDependencyAndDuplicateAssetsSurviveUntilLastProviderIsDisabled() {
            var shared = Assets("Shared");
            var dependency = Release("Dependency", shared);
            var first = Release("First", shared, new Dependency("Org/Dependency", dependency.Tag));
            var second = Release("Second", shared, new Dependency("Org/Dependency", dependency.Tag));
            var manager = Manager(first, second, dependency);
            var catalog = new AvailableModsAndMapsService(manager, new ModScanTabVM());
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(first));
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(second));
            Assert.Single(catalog.AvailableMaps.Where(map => shared.Maps.Contains(map)));
            Assert.Single(catalog.AvailableServerModBlueprints);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(first));
            AssertAssets(catalog, shared, true);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(second));
            AssertAssets(catalog, shared, false);
        }

        [Fact]
        public void VersionSwitchReconcilesTransitiveDependenciesAndPreservesSurvivingEntries() {
            var leaf = Release("Leaf", Assets("Leaf"));
            var dependency = Release("Dependency", Assets("Dependency"), new Dependency("Org/Leaf", leaf.Tag));
            var parent = Release("Parent", Assets("Parent"), new Dependency("Org/Dependency", dependency.Tag));
            var replacement = parent with { Tag = "v2.0.0", Manifest = Assets("ParentV2") };
            var manager = Manager(parent, replacement, dependency, leaf);
            var catalog = new AvailableModsAndMapsService(manager, new ModScanTabVM());
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(parent));
            AssertAssets(catalog, leaf.Manifest, true);
            var removals = new List<object?>();
            catalog.AvailableMaps.CollectionChanged += (_, args) => {
                if (args.OldItems != null) removals.AddRange(args.OldItems.Cast<object>());
            };
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(replacement));
            AssertAssets(catalog, dependency.Manifest, true);
            AssertAssets(catalog, leaf.Manifest, true);
            AssertAssets(catalog, parent.Manifest, false);
            AssertAssets(catalog, replacement.Manifest, true);
            Assert.DoesNotContain(leaf.Manifest.Maps[0], removals);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(replacement));
            AssertAssets(catalog, leaf.Manifest, false);
            AssertAssets(catalog, dependency.Manifest, false);
        }

        [Fact]
        public async Task ScanReplacementAndModDisablePreserveOtherSourcesAndVanillaMaps() {
            using var file = new ScannedPak("Shared");
            file.Result._Maps.Add(JsonConvert.DeserializeObject<GameMapInfo>("""
                {"asset_path":"TBL/Content/Maps/Shared.umap","asset_hash":"hash",
                 "settings":{"Map":{"MapName":"Shared","GamemodeType":"FFA"}}}
                """)!);
            var shared = ModManifestConverter.ProcessModScan(file.Scan).Paks.Single().Inventory;
            var release = Release("Shared", shared);
            var manager = Manager(release);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            var vanilla = catalog.AvailableMaps.ToArray();
            Assert.NotEmpty(vanilla);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Single(catalog.AvailableMaps.Where(map => shared.Maps.Contains(map)));
            // Registry and scan fixtures refer to different physical pak paths.
            Assert.Equal(2, catalog.AvailableServerModBlueprints.Count);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(release));
            await catalog.WaitForCatalogAsync();
            Assert.All(shared.Maps, map => Assert.Contains(map, catalog.AvailableMaps));
            Assert.Empty(catalog.AvailableServerModBlueprints);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
            PublishScan(scan, new ModScanResult());
            await catalog.WaitForCatalogAsync();
            AssertAssets(catalog, shared, true);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(release));
            AssertAssets(catalog, shared, false);
            PublishScan(scan, file.Scan);
            PublishScan(scan, null);
            await catalog.WaitForCatalogAsync();
            AssertAssets(catalog, shared, false);
            Assert.Equal(vanilla, catalog.AvailableMaps);
        }

        [Fact]
        public async Task CatalogWithoutScanDoesNotReadPakInventory() {
            var manager = new Mock<IModManager>();
            manager.SetupGet(m => m.PakDir).Throws(new InvalidOperationException("No scan needs a pak inventory"));
            var catalog = new AvailableModsAndMapsService(manager.Object, new ModScanTabVM());
            await catalog.RefreshCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            Assert.NotEmpty(catalog.AvailableMaps);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ScannerPakIdentityPublishesActorsOutsideWorkingDirectory(bool scanBeforeCatalog) {
            using var file = new ScannedPak();
            Assert.NotEqual(Path.GetFullPath(Path.GetFileName(file.Result.PakPath)), file.Result.PakPath);
            var scan = new ModScanTabVM();
            if (scanBeforeCatalog) PublishScan(scan, file.Scan);
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            if (!scanBeforeCatalog) PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Equal("Scanned", Assert.Single(catalog.AvailableServerModBlueprints).ModName);
            await catalog.RefreshCatalogAsync();
            Assert.Single(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public async Task SameActorAndPakPathDeduplicateDespiteDifferentMetadataAndCasing() {
            using var file = new ScannedPak();
            var duplicate = new GenericAssetEntry { AssetPath = "SCANNED.uasset", AssetHash = "different hash" };
            duplicate.Properties["ModName"] = "Different display name";
            duplicate.Properties["bShowInGUI"] = true;
            file.Result.AddGenericEntry(duplicate, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            var actor = Assert.Single(catalog.AvailableServerModBlueprints);
            PublishScan(scan, file.Scan);
            await catalog.RefreshCatalogAsync();
            Assert.Same(actor, Assert.Single(catalog.AvailableServerModBlueprints));
        }

        [Fact]
        public async Task SameActorInDifferentPaksRemainsDistinctAndSurvivorIsPreserved() {
            using var first = new ScannedPak();
            using var second = new ScannedPak();
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, first.Scan);
            await catalog.WaitForCatalogAsync();
            var survivor = Assert.Single(catalog.AvailableServerModBlueprints);
            first.Scan.Paks[second.Result.PakPath] = second.Result;
            PublishScan(scan, first.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Equal(2, catalog.AvailableServerModBlueprints.Count);
            Assert.Contains(catalog.AvailableServerModBlueprints, actor => ReferenceEquals(actor, survivor));
            File.Delete(second.Result.PakPath);
            await catalog.RefreshCatalogAsync();
            Assert.Same(survivor, Assert.Single(catalog.AvailableServerModBlueprints));
        }

        [Fact]
        public async Task DifferentActorPathsInSamePakRemainDistinct() {
            using var file = new ScannedPak();
            var other = new GenericAssetEntry { AssetPath = "Other.uasset", AssetHash = "hash" };
            other.Properties["ModName"] = "Scanned";
            other.Properties["bShowInGUI"] = true;
            file.Result.AddGenericEntry(other, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Equal(2, catalog.AvailableServerModBlueprints.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LaunchRefreshRejectsDeletedOrReplacedScannedPak(bool replace) {
            using var file = new ScannedPak();
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Single(catalog.AvailableServerModBlueprints);
            if (replace) {
                var modified = File.GetLastWriteTimeUtc(file.Result.PakPath);
                File.WriteAllText(file.Result.PakPath, "altered");
                File.SetLastWriteTimeUtc(file.Result.PakPath, modified);
            }
            else File.Delete(file.Result.PakPath);
            await catalog.RefreshCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public async Task ChangedScanHashRevalidatesEvenWhenFileMetadataIsUnchanged() {
            using var file = new ScannedPak();
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Single(catalog.AvailableServerModBlueprints);

            var modified = File.GetLastWriteTimeUtc(file.Result.PakPath);
            File.WriteAllText(file.Result.PakPath, "changed");
            File.SetLastWriteTimeUtc(file.Result.PakPath, modified);
            file.Result.PakHash = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(file.Result.PakPath)));
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Single(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public async Task RepeatedScanReusesUnchangedValidationButLaunchStillReadsBytes() {
            using var file = new ScannedPak();
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            var actor = Assert.Single(catalog.AvailableServerModBlueprints);

            using (File.Open(file.Result.PakPath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                PublishScan(scan, file.Scan);
                await catalog.WaitForCatalogAsync();
                Assert.Same(actor, Assert.Single(catalog.AvailableServerModBlueprints));
                await catalog.RefreshCatalogAsync();
                Assert.Empty(catalog.AvailableServerModBlueprints);
            }
        }

        [Fact]
        public void PendingValidationCannotPublishOlderScanOrToggleStateAndPublishesOnUiContext() {
            RunOnUiContext(async () => {
                using var first = new ScannedPak("First");
                using var second = new ScannedPak("Second");
                var release = Release("Registry", Assets("Registry"));
                var manager = Manager(release);
                manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
                var scan = new ModScanTabVM();
                var catalog = new AvailableModsAndMapsService(manager, scan);
                var uiThread = Environment.CurrentManagedThreadId;
                var published = new List<string?>();
                catalog.AvailableServerModBlueprints.CollectionChanged += (_, args) => {
                    Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                    if (args.NewItems != null)
                        published.AddRange(args.NewItems.Cast<BlueprintDto>().Select(actor => actor.ModName));
                };
                PublishScan(scan, first.Scan);
                var launchValidation = catalog.RefreshCatalogAsync();
                PublishScan(scan, second.Scan);
                manager.DisableModRelease(ReleaseCoordinates.FromRelease(release));
                await launchValidation;
                await catalog.WaitForCatalogAsync();
                Assert.Equal("Second", Assert.Single(catalog.AvailableServerModBlueprints).ModName);
                Assert.DoesNotContain("First", published);
                Assert.DoesNotContain("Registry", published);

                var modified = File.GetLastWriteTimeUtc(second.Result.PakPath);
                File.WriteAllText(second.Result.PakPath, "changed");
                File.SetLastWriteTimeUtc(second.Result.PakPath, modified);
                var firstLaunch = catalog.RefreshCatalogAsync();
                var secondLaunch = catalog.RefreshCatalogAsync();
                PublishScan(scan, second.Scan);
                await Task.WhenAll(firstLaunch, secondLaunch);
                Assert.Empty(catalog.AvailableServerModBlueprints);

                PublishScan(scan, first.Scan);
                var obsoleteValidation = catalog.WaitForCatalogAsync();
                PublishScan(scan, null);
                await obsoleteValidation;
                await catalog.WaitForCatalogAsync();
                Assert.Empty(catalog.AvailableServerModBlueprints);
            });
        }

        // Exercise the production notification/continuation boundary without a WPF dispatcher.
        private static void RunOnUiContext(Func<Task> test) {
            var previous = SynchronizationContext.Current;
            var context = new TestUiContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try {
                var task = test();
                while (!task.IsCompleted) context.RunNext();
                task.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private sealed class TestUiContext : SynchronizationContext {
            private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
            public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
            public void RunNext() {
                Assert.True(_queue.TryTake(out var work, TimeSpan.FromSeconds(10)), "Catalog validation did not resume");
                work.Callback(work.State);
            }
        }

        [Fact]
        public async Task MetadataRefreshReusesValidationButLaunchHashesUnchangedScanAgain() {
            using var file = new ScannedPak();
            var registry = new Mock<IModRegistry>();
            registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], []));
            var manager = new ModManager(registry.Object, Mock.Of<IPakDir>(), []);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Single(catalog.AvailableServerModBlueprints);
            using (var locked = File.Open(file.Result.PakPath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                await manager.UpdateModsList();
                await catalog.WaitForCatalogAsync();
                Assert.Single(catalog.AvailableServerModBlueprints);
                await catalog.RefreshCatalogAsync();
                Assert.Empty(catalog.AvailableServerModBlueprints);
            }
            await catalog.RefreshCatalogAsync();
            Assert.Single(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public async Task ManagedScanCannotResurrectActorWithoutEnabledRegistryProvider() {
            using var file = new ScannedPak();
            var paks = new Mock<IPakDir>();
            paks.SetupGet(p => p.DirPath).Returns(Path.GetDirectoryName(file.Result.PakPath)!);
            paks.SetupGet(p => p.ManagedPaks).Returns([new ManagedPak(new("Org", "Managed", "v1"), Path.GetFileName(file.Result.PakPath), 0)]);
            var manager = new ModManager(Mock.Of<IModRegistry>(), paks.Object, []);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task ReplacedManagedPakUsesFreshScanInsteadOfDisabledOrStaleRegistryMetadata(bool enabled, bool scanBeforeCatalog) {
            using var file = new ScannedPak("Menu");
            file.Result._Maps.Add(JsonConvert.DeserializeObject<GameMapInfo>("""
                {"asset_path":"TBL/Content/Maps/Managed.umap","asset_hash":"hash"}
                """)!);
            var original = ModManifestConverter.ProcessModScan(file.Scan).Paks.Single().Inventory;
            original.Blueprints[0] = original.Blueprints[0] with { ModName = "Registry menu", Version = "0.0.1" };
            var release = Release("Managed", original) with { ReleaseHash = file.Result.PakHash! };
            var registry = new Mock<IModRegistry>();
            var mod = new Mod(release.Info, [release]);
            registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], [mod]));
            var paks = new Mock<IPakDir>();
            paks.SetupGet(p => p.DirPath).Returns(Path.GetDirectoryName(file.Result.PakPath)!);
            paks.SetupGet(p => p.ManagedPaks).Returns([new ManagedPak(ReleaseCoordinates.FromRelease(release), Path.GetFileName(file.Result.PakPath), 0)]);
            paks.Setup(p => p.GetManagedPakFilePath(ReleaseCoordinates.FromRelease(release))).Returns(file.Result.PakPath);
            var manager = new ModManager(registry.Object, paks.Object, [], [mod]);
            if (enabled) manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
            var scan = new ModScanTabVM();
            if (scanBeforeCatalog) PublishScan(scan, file.Scan);
            var catalog = new AvailableModsAndMapsService(manager, scan);
            if (!scanBeforeCatalog) PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            Assert.Equal(enabled ? 1 : 0, catalog.AvailableServerModBlueprints.Count);
            if (enabled) Assert.Equal("Registry menu", catalog.AvailableServerModBlueprints.Single().ModName);

            // Same length, new modification time: a metadata event must invalidate the cached hash.
            var modified = File.GetLastWriteTimeUtc(file.Result.PakPath);
            File.WriteAllText(file.Result.PakPath, "changed");
            File.SetLastWriteTimeUtc(file.Result.PakPath, modified.AddSeconds(2));
            await manager.UpdateModsList();
            await catalog.WaitForCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            Assert.DoesNotContain(original.Maps[0], catalog.AvailableMaps);

            file.Result.PakHash = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(file.Result.PakPath)));
            var actor = file.Result.GenericEntries.Values.Single().Single();
            actor.Properties["ModName"] = "Scanned menu";
            actor.Properties["ModVersion"] = "0.2.1";
            PublishScan(scan, file.Scan);
            await catalog.WaitForCatalogAsync();
            var current = Assert.Single(catalog.AvailableServerModBlueprints);
            Assert.Equal("Scanned menu", current.ModName);
            Assert.Equal("0.2.1", current.Version);
            Assert.Empty(catalog.GetConfirmedInactiveSelections([current]));
            await catalog.RefreshCatalogAsync();
            Assert.Same(current, Assert.Single(catalog.AvailableServerModBlueprints));
            var restarted = new AvailableModsAndMapsService(manager, scan);
            await restarted.WaitForCatalogAsync();
            Assert.Equal("0.2.1", Assert.Single(restarted.AvailableServerModBlueprints).Version);

            // Returning to the registered bytes restores the normal enable/disable policy.
            File.WriteAllText(file.Result.PakPath, "scanned");
            File.SetLastWriteTimeUtc(file.Result.PakPath, modified.AddSeconds(2));
            await catalog.RefreshCatalogAsync();
            Assert.Equal(enabled ? 1 : 0, catalog.AvailableServerModBlueprints.Count);
            if (enabled) Assert.Equal("Registry menu", catalog.AvailableServerModBlueprints.Single().ModName);
            File.Delete(file.Result.PakPath);
            await catalog.RefreshCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InactiveDetectionUsesDependencyHelpersLatestReleaseAndVersionRangePolicy(bool dependencyAvailable) {
            var owner = Release("InactiveOwner", Assets("InactiveActor"));
            var parent = Release("Parent", Assets("Parent"));
            var latestParent = parent with {
                Tag = "v2.0.0",
                Info = parent.Info with { Dependencies = [new Dependency("Org/Middle", "v9.0.0")] }
            };
            var middle = Release("Middle", Assets("Middle"));
            var manager = dependencyAvailable ? Manager(owner, parent, latestParent, middle) : Manager(owner, parent, latestParent);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(parent));
            var catalog = new AvailableModsAndMapsService(manager, new ModScanTabVM());
            Assert.Equal(dependencyAvailable, manager.GetEnabledAndDependencyReleases().Contains(middle));
            Assert.Equal(dependencyAvailable, catalog.GetConfirmedInactiveSelections(owner.Manifest.Blueprints).Length == 1);
        }

        [Fact]
        public void InactiveSelectionBatchReadsStateOnceAndPreservesSelectionOrder() {
            var first = Release("First", Assets("First"));
            var second = Release("Second", Assets("Second"));
            var active = Release("Active", Assets("Active"));
            var manager = new Mock<IModManager>();
            manager.SetupGet(m => m.PakDir).Returns(Mock.Of<IPakDir>());
            manager.SetupGet(m => m.Mods).Returns(new[] { first, second, active }
                .Select(release => new Mod(release.Info, [release])).ToArray());
            manager.SetupGet(m => m.EnabledModReleaseCoordinates).Returns([ReleaseCoordinates.FromRelease(active)]);
            var catalog = new AvailableModsAndMapsService(manager.Object, new ModScanTabVM());
            var firstActor = first.Manifest.Blueprints.Single();
            var secondActor = second.Manifest.Blueprints.Single();
            manager.Invocations.Clear();
            Assert.Equal(firstActor, Assert.Single(catalog.GetConfirmedInactiveSelections([firstActor])));
            var singleStateReads = manager.Invocations.Count;
            manager.Invocations.Clear();
            var selections = new[] { secondActor, active.Manifest.Blueprints.Single(), Assets("Unknown").Blueprints.Single(), firstActor };
            Assert.Equal(new[] { secondActor, firstActor }, catalog.GetConfirmedInactiveSelections(selections));
            Assert.Equal(singleStateReads, manager.Invocations.Count);
            manager.VerifyGet(m => m.EnabledModReleaseCoordinates, Times.Exactly(2));
            Assert.Equal(secondActor, selections[0]);
        }

        private sealed class ScannedPak : IDisposable {
            public PakScanResult Result { get; }
            public ModScanResult Scan { get; } = new();
            public ScannedPak(string name = "Scanned") {
                var path = Path.GetTempFileName();
                File.WriteAllText(path, "scanned");
                Result = Scan.GetOrAddPak(Mock.Of<IVfsReader>(reader => reader.Path == path && reader.Name == Path.GetFileName(path)));
                var blueprint = new GenericAssetEntry { AssetPath = $"{name}.uasset", AssetHash = "hash" };
                blueprint.Properties["ModName"] = name;
                blueprint.Properties["bShowInGUI"] = true;
                Result.AddGenericEntry(blueprint, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            }
            public void Dispose() => File.Delete(Result.PakPath);
        }

        private static void PublishScan(ModScanTabVM scan, ModScanResult? result) {
            // Feed deterministic scan data through the production completion notification.
            typeof(ModScanTabVM).GetProperty(nameof(scan.LastScanResult))!.SetValue(scan, result);
            scan.ResultsVisual = new PakScanResultVM();
        }
    }
}