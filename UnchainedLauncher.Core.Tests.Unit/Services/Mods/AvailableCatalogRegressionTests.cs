using Moq;
using CUE4Parse.UE4.VirtualFileSystem;
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
            name, "1", "", "", "", true, false, true, false, false, true, false, false)],
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
        public void ScanReplacementAndModDisablePreserveOtherSourcesAndVanillaMaps() {
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
            Assert.Single(catalog.AvailableMaps.Where(map => shared.Maps.Contains(map)));
            Assert.Single(catalog.AvailableServerModBlueprints);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(release));
            Assert.All(shared.Maps, map => Assert.Contains(map, catalog.AvailableMaps));
            Assert.Empty(catalog.AvailableServerModBlueprints);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
            PublishScan(scan, new ModScanResult());
            AssertAssets(catalog, shared, true);
            manager.DisableModRelease(ReleaseCoordinates.FromRelease(release));
            AssertAssets(catalog, shared, false);
            PublishScan(scan, file.Scan);
            PublishScan(scan, null);
            AssertAssets(catalog, shared, false);
            Assert.Equal(vanilla, catalog.AvailableMaps);
        }

        [Fact]
        public void CatalogWithoutScanDoesNotReadPakInventory() {
            var manager = new Mock<IModManager>();
            manager.SetupGet(m => m.PakDir).Throws(new InvalidOperationException("No scan needs a pak inventory"));
            var catalog = new AvailableModsAndMapsService(manager.Object, new ModScanTabVM());
            catalog.RefreshCatalog();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            Assert.NotEmpty(catalog.AvailableMaps);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScannerPakIdentityPublishesActorsOutsideWorkingDirectory(bool scanBeforeCatalog) {
            using var file = new ScannedPak();
            Assert.NotEqual(Path.GetFullPath(Path.GetFileName(file.Result.PakPath)), file.Result.PakPath);
            var scan = new ModScanTabVM();
            if (scanBeforeCatalog) PublishScan(scan, file.Scan);
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            if (!scanBeforeCatalog) PublishScan(scan, file.Scan);
            Assert.Equal("Scanned", Assert.Single(catalog.AvailableServerModBlueprints).ModName);
            catalog.RefreshCatalog();
            Assert.Single(catalog.AvailableServerModBlueprints);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LaunchRefreshRejectsDeletedOrReplacedScannedPak(bool replace) {
            using var file = new ScannedPak();
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, file.Scan);
            Assert.Single(catalog.AvailableServerModBlueprints);
            if (replace) {
                var modified = File.GetLastWriteTimeUtc(file.Result.PakPath);
                File.WriteAllText(file.Result.PakPath, "altered");
                File.SetLastWriteTimeUtc(file.Result.PakPath, modified);
            }
            else File.Delete(file.Result.PakPath);
            catalog.RefreshCatalog();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            PublishScan(scan, file.Scan);
            Assert.Empty(catalog.AvailableServerModBlueprints);
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
            Assert.Single(catalog.AvailableServerModBlueprints);
            using (var locked = File.Open(file.Result.PakPath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                await manager.UpdateModsList();
                Assert.Single(catalog.AvailableServerModBlueprints);
                catalog.RefreshCatalog();
                Assert.Empty(catalog.AvailableServerModBlueprints);
            }
            catalog.RefreshCatalog();
            Assert.Single(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public void ManagedScanCannotResurrectActorWithoutEnabledRegistryProvider() {
            using var file = new ScannedPak();
            var paks = new Mock<IPakDir>();
            paks.SetupGet(p => p.DirPath).Returns(Path.GetDirectoryName(file.Result.PakPath)!);
            paks.SetupGet(p => p.ManagedPaks).Returns([new ManagedPak(new("Org", "Managed", "v1"), Path.GetFileName(file.Result.PakPath), 0)]);
            var manager = new ModManager(Mock.Of<IModRegistry>(), paks.Object, []);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            PublishScan(scan, file.Scan);
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
                Scan.Paks[path] = Result;
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
