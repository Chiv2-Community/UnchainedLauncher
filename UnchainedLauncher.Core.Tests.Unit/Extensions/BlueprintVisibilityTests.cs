using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Extensions;

public class BlueprintVisibilityTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedLegacyMarkerDoesNotHideScannedActor(bool hasMarker) {
        var scan = new ModScanResult();
        var pak = new PakScanResult();
        scan.Paks.TryAdd("test.pak", pak);
        var actor = new GenericAssetEntry { AssetPath = "/Game/Mods/Test.Test_C", AssetHash = "hash" };
        actor.Properties["bShowInGUI"] = true;
        pak.AddGenericEntry(actor, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
        if (hasMarker) pak.GenericMarkers.TryAdd("DA_ModMarker_C", new() {
            ["marker"] = new GenericMarkerEntry { AssetPath = "/Game/Mods/OtherMarker", AssetHash = "hash" }
        });

        var manifest = Assert.Single(ModManifestConverter.ProcessModScan(scan).Paks).Inventory;

        Assert.Equal(actor.AssetPath, Assert.Single(manifest.RelevantBlueprints()).Path);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void RequiresExplicitShowInGui(bool? showInGui, bool expected) {
        var actor = new BlueprintDto("/Game/Mods/Test.Test_C", "hash", null, null,
            "Test", "1.0.0", null, null, null, null, null, showInGui, null, null, null, null);

        Assert.Equal(expected, actor.IsRelevant());
        var manifest = new AssetCollections([], [actor], [], [], []);
        Assert.Equal(expected ? new[] { actor } : [], manifest.RelevantBlueprints());
    }
}
