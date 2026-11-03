using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnrealModScanner.Models;
using Xunit;

namespace UnchainedLauncher.PakScannerApp.Tests;

public class ConcurrentClassificationTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SpecializedExclusionDoesNotDependOnInsertionOrder(bool excludeFirst) {
        var result = new PakScanResult();
        var specialized = Asset("Specialized");
        var unrelated = Asset("Unrelated");
        if (excludeFirst) result.RemoveSpecializedAsset(specialized);
        result.ArbitraryAssets.Add(specialized);
        result.ArbitraryAssets.Add(unrelated);
        result.AddGenericEntry(specialized, "Class");
        result.AddGenericEntry(unrelated, "Class");
        if (!excludeFirst) result.RemoveSpecializedAsset(specialized);

        result.FinalizeClassification();
        result.FinalizeClassification(); // Idempotent; exclusions remain in force.

        Assert.Equal([unrelated.AssetPath], result.ArbitraryAssets.Select(asset => asset.AssetPath));
        Assert.Equal([unrelated.AssetPath], result.GenericEntries["Class"].Select(asset => asset.AssetPath));
    }

    [Fact]
    public void ConcurrentClassificationPreservesEveryUnrelatedAsset() {
        var result = new PakScanResult();
        const int count = 2048;
        Parallel.For(0, count, index => {
            var entry = Asset(index.ToString("D4"));
            if (index % 4 == 0) result.RemoveSpecializedAsset(entry);
            result.ArbitraryAssets.Add(entry);
            result.AddGenericEntry(entry, "Class");
            if (index % 4 == 2) result.RemoveSpecializedAsset(entry);
        });
        result.FinalizeClassification();

        var expected = Enumerable.Range(0, count).Where(index => index % 2 == 1)
            .Select(index => Asset(index.ToString("D4")).AssetPath).ToArray();
        Assert.Equal(expected, result.ArbitraryAssets.Select(asset => asset.AssetPath).OrderBy(path => path, StringComparer.Ordinal));
        Assert.Equal(expected, result.GenericEntries["Class"].Select(asset => asset.AssetPath).OrderBy(path => path, StringComparer.Ordinal));
    }

    [Fact]
    public void GenericAndArbitraryExclusionsRemainIndependent() {
        var result = new PakScanResult();
        var genericOnly = Asset("GenericOnly");
        var arbitraryOnly = Asset("ArbitraryOnly");
        foreach (var entry in new[] { genericOnly, arbitraryOnly }) {
            result.ArbitraryAssets.Add(entry);
            result.AddGenericEntry(entry, "Class");
        }
        result.RemoveEntryGlobal(arbitraryOnly.AssetPath);
        result.RemoveArbitraryAsset(genericOnly);
        result.FinalizeClassification();

        Assert.Equal([arbitraryOnly.AssetPath], result.ArbitraryAssets.Select(asset => asset.AssetPath));
        Assert.Equal([genericOnly.AssetPath], result.GenericEntries["Class"].Select(asset => asset.AssetPath));
    }

    [Fact]
    public void ManifestCollectionsHaveStableOrdinalPathOrder() {
        var scan = new ModScanResult();
        foreach (var pakName in new[] { "z.pak", "a.pak" }) {
            var result = new PakScanResult();
            scan.Paks[pakName] = result;
            foreach (var name in new[] { "z", "a", "B" }) {
                result.ArbitraryAssets.Add(Asset(name));
                result._AssetReplacements.Add(Asset(name));
                var marker = new GenericMarkerEntry { AssetPath = Asset(name).AssetPath };
                foreach (var child in new[] { "z", "a", "B" })
                    marker.AddGenericEntry(Asset(name + "/" + child));
                result.AddGenericMarker(marker, "Marker");
            }
        }
        var manifest = ModManifestConverter.ProcessModScan(scan);
        Assert.Equal(["a.pak", "z.pak"], manifest.Paks.Select(pak => pak.PakName));
        foreach (var pak in manifest.Paks) {
            var expected = new[] { Asset("B").AssetPath, Asset("a").AssetPath, Asset("z").AssetPath };
            Assert.Equal(expected, pak.Inventory.Arbitrary.Select(asset => asset.Path));
            Assert.Equal(expected, pak.Inventory.Replacements.Select(asset => asset.Path));
            Assert.Equal(expected, pak.Inventory.Markers.Select(asset => asset.Path));
            var paths = pak.Inventory.Blueprints.Select(asset => asset.Path).ToArray();
            Assert.Equal(paths.OrderBy(path => path, StringComparer.Ordinal), paths);
            foreach (var marker in pak.Inventory.Markers)
                Assert.Equal(marker.AssociatedBlueprints.OrderBy(path => path, StringComparer.Ordinal), marker.AssociatedBlueprints);
        }
    }

    private static GenericAssetEntry Asset(string name) => new() { AssetPath = $"TBL/Content/Mods/{name}.uasset" };
}