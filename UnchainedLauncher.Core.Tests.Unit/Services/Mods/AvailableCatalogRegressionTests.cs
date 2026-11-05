using Moq;
using Newtonsoft.Json;
using System.Security.Cryptography;
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
            using var file = new ScannedPak();
            var scanResult = new ModScanResult();
            var pak = file.Result;
            pak._Maps.Add(JsonConvert.DeserializeObject<GameMapInfo>("""
                {"asset_path":"TBL/Content/Maps/Shared.umap","asset_hash":"hash",
                 "settings":{"Map":{"MapName":"Shared","GamemodeType":"FFA"}}}
                """)!);
            var blueprint = new GenericAssetEntry { AssetPath = "Shared.uasset", AssetHash = "hash" };
            blueprint.Properties["ModName"] = "Shared";
            blueprint.Properties["bShowInGUI"] = true;
            pak.AddGenericEntry(blueprint, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            scanResult.Paks["shared.pak"] = pak;
            var shared = ModManifestConverter.ProcessModScan(scanResult).Paks.Single().Inventory;
            var release = Release("Shared", shared);
            var manager = Manager(release);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            var vanilla = catalog.AvailableMaps.ToArray();
            Assert.NotEmpty(vanilla);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
            PublishScan(scan, scanResult);
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
            PublishScan(scan, scanResult);
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
        public void LaunchRefreshRejectsDeletedOrReplacedScannedPak(bool replace) {
            using var file = new ScannedPak();
            var blueprint = new GenericAssetEntry { AssetPath = "Scanned.uasset", AssetHash = "hash" };
            blueprint.Properties["ModName"] = "Scanned";
            blueprint.Properties["bShowInGUI"] = true;
            file.Result.AddGenericEntry(blueprint, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            var result = new ModScanResult();
            result.Paks[file.Result.PakPath] = file.Result;
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(Manager(), scan);
            PublishScan(scan, result);
            Assert.Single(catalog.AvailableServerModBlueprints);
            if (replace) {
                var modified = File.GetLastWriteTimeUtc(file.Result.PakPath);
                File.WriteAllText(file.Result.PakPath, "altered"); // Same length and timestamp as the validated file.
                File.SetLastWriteTimeUtc(file.Result.PakPath, modified);
            }
            else File.Delete(file.Result.PakPath);
            catalog.RefreshCatalog();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            // Republishing a stale result must not make it usable again.
            PublishScan(scan, result);
            Assert.Empty(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public void MetadataRefreshReusesValidationButLaunchHashesUnchangedScanAgain() {
            using var file = new ScannedPak();
            var blueprint = new GenericAssetEntry { AssetPath = "Cached.uasset", AssetHash = "hash" };
            blueprint.Properties["bShowInGUI"] = true;
            file.Result.AddGenericEntry(blueprint, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            var result = new ModScanResult();
            result.Paks[file.Result.PakPath] = file.Result;
            var registry = new Mock<IModRegistry>();
            registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], []));
            var manager = new ModManager(registry.Object, Mock.Of<IPakDir>(), []);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            PublishScan(scan, result);
            Assert.Single(catalog.AvailableServerModBlueprints);
            using (var locked = File.Open(file.Result.PakPath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                manager.UpdateModsList().GetAwaiter().GetResult();
                Assert.Single(catalog.AvailableServerModBlueprints); // No redundant hash read on metadata events.
                catalog.RefreshCatalog();
                Assert.Empty(catalog.AvailableServerModBlueprints); // Launch must validate, not trust the cache.
            }
            catalog.RefreshCatalog();
            Assert.Single(catalog.AvailableServerModBlueprints);
        }

        [Fact]
        public void ManagedScanCannotReintroduceActorWhenRegistryMetadataIsMissing() {
            using var file = new ScannedPak();
            var blueprint = new GenericAssetEntry { AssetPath = "Managed.uasset", AssetHash = "hash" };
            blueprint.Properties["ModName"] = "Managed";
            blueprint.Properties["bShowInGUI"] = true;
            file.Result.AddGenericEntry(blueprint, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            var result = new ModScanResult();
            result.Paks[file.Result.PakPath] = file.Result;
            var pakDir = new Mock<IPakDir>();
            pakDir.SetupGet(p => p.DirPath).Returns(Path.GetDirectoryName(file.Result.PakPath)!);
            pakDir.SetupGet(p => p.ManagedPaks).Returns([new ManagedPak(new("Org", "Managed", "v1"), Path.GetFileName(file.Result.PakPath), 0)]);
            var manager = new ModManager(Mock.Of<IModRegistry>(), pakDir.Object, []);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            PublishScan(scan, result);
            Assert.Empty(catalog.AvailableServerModBlueprints);
            var actor = ModManifestConverter.ProcessModScan(result).Paks.Single().Inventory.Blueprints.Single();
            Assert.False(catalog.IsConfirmedInactive(actor)); // Still retain the unresolved saved preference.
        }

        private sealed class ScannedPak : IDisposable {
            public PakScanResult Result { get; }
            public ScannedPak() {
                var path = Path.GetTempFileName();
                File.WriteAllText(path, "scanned");
                Result = new PakScanResult {
                    PakPath = path,
                    PakHash = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path)))
                };
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
