using System.Globalization;
using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;
using UnchainedLauncher.UnrealModScanner.Services;
using UnrealModScanner.Models;
using Xunit;

namespace UnchainedLauncher.PakScannerApp.Tests {
    public class ManifestOrderingTests {
        private static readonly string[] Paths = ["I.uasset", "Z.uasset", "a.uasset", "i.uasset"];

        [Fact]
        public void ManifestCollectionsAreOrdinalPathSortedRegardlessOfDiscoveryOrderOrCulture() {
            var originalCulture = CultureInfo.CurrentCulture;
            string? expected = null;
            try {
                foreach (var culture in new[] { "en-US", "tr-TR" }) {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    for (var seed = 0; seed < 8; seed++) {
                        var random = new Random(seed);
                        var scan = new ModScanResult();
                        foreach (var pakPath in new[] { "Z.pak", "a.pak", "I.pak" }.OrderBy(_ => random.Next())) {
                            var pak = new PakScanResult { PakPath = pakPath, PakHash = "pak-hash" };
                            foreach (var path in Paths.OrderBy(_ => random.Next())) {
                                var marker = new GenericMarkerEntry { AssetPath = path, AssetHash = "marker-hash" };
                                foreach (var child in Paths.OrderBy(_ => random.Next())) {
                                    marker.AddGenericEntry(new GenericAssetEntry { AssetPath = child, AssetHash = "child-hash" });
                                }
                                pak.AddGenericMarker(marker, "marker");
                                pak.ArbitraryAssets.Add(new GenericAssetEntry { AssetPath = path, AssetHash = "arbitrary-hash" });
                                pak._AssetReplacements.Add(new GenericAssetEntry { AssetPath = path, AssetHash = "replacement-hash" });
                                var map = (GameMapInfo)Activator.CreateInstance(typeof(GameMapInfo), nonPublic: true)!;
                                map.AssetPath = path;
                                map.AssetHash = "map-hash";
                                pak._Maps.Add(map);
                            }
                            scan.Paks[pakPath] = pak;
                        }

                        var manifest = ModManifestConverter.ProcessModScan(scan);
                        Assert.Equal(new[] { "I.pak", "Z.pak", "a.pak" }, manifest.Paks.Select(p => p.PakPath));
                        foreach (var pak in manifest.Paks) {
                            var assets = pak.Inventory;
                            Assert.Equal(Paths, assets.Markers.Select(a => a.Path));
                            Assert.Equal(Paths, assets.Blueprints.Select(a => a.Path));
                            Assert.Equal(Paths, assets.Maps.Select(a => a.Path));
                            Assert.Equal(Paths, assets.Replacements.Select(a => a.Path));
                            Assert.Equal(Paths, assets.Arbitrary.Select(a => a.Path));
                            Assert.All(assets.Markers, marker => Assert.Equal(Paths, marker.AssociatedBlueprints));
                        }

                        // The envelope records scan time; registry inventories exclude it.
                        var json = JsonConvert.SerializeObject(manifest with { GeneratedAt = default });
                        expected ??= json;
                        Assert.Equal(expected, json);
                    }
                }
            }
            finally {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Fact]
        public void CompletedScanCleanupRemovesAllSpecializedOverlapsWithoutLosingOtherAssets() {
            var scan = new ModScanResult();
            var pak = new PakScanResult { PakPath = "overlaps.pak" };
            scan.Paks["overlaps.pak"] = pak;
            Parallel.For(0, 100, index => {
                var marker = new GenericMarkerEntry { AssetPath = $"marker-{index}" };
                var child = new GenericAssetEntry { AssetPath = $"child-{index}" };
                marker.AddGenericEntry(child);
                pak.AddGenericMarker(marker, "marker");
                pak.ArbitraryAssets.Add(new GenericAssetEntry { AssetPath = marker.AssetPath });
                pak.ArbitraryAssets.Add(child);
                pak.ArbitraryAssets.Add(new GenericAssetEntry { AssetPath = $"other-{index}" });
                pak.AddGenericEntry(child, "base");
            });

            ModScanOrchestrator.RemoveSpecializedAssets(scan);

            Assert.Equal(100, pak.ArbitraryAssets.Count);
            Assert.All(pak.ArbitraryAssets, asset => Assert.StartsWith("other-", asset.AssetPath));
            Assert.Empty(pak.GenericEntries["base"]);
            Assert.Equal(100, pak.GenericMarkers["marker"].Count);
        }

        [Fact]
        public void OrphanBlueprintsArePathSorted() {
            var scan = new ModScanResult();
            var pak = new PakScanResult { PakPath = "orphan.pak" };
            foreach (var path in Paths.Reverse()) {
                pak.AddGenericEntry(new GenericAssetEntry { AssetPath = path },
                    "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            }
            scan.Paks["orphan.pak"] = pak;

            var manifest = ModManifestConverter.ProcessModScan(scan);

            Assert.Equal(Paths, Assert.Single(manifest.Paks).Inventory.Blueprints.Select(a => a.Path));
        }
    }
}
