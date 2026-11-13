using Newtonsoft.Json.Linq;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Extensions;

public class BlueprintVisibilityTests {
    public static IEnumerable<object?[]> ScannedVisibilityCases() {
        object?[] values = [null, true, false, "true", "false", "invalid", new JValue(true), new JValue(false), JValue.CreateNull()];
        foreach (var markerChild in new[] { false, true }) {
            yield return [markerChild, false, null, null];
            foreach (var value in values) {
                bool? expected = value switch {
                    bool flag => flag,
                    string text when bool.TryParse(text, out var flag) => flag,
                    JValue { Type: JTokenType.Boolean } token => token.Value<bool>(),
                    _ => null
                };
                yield return [markerChild, true, value, expected];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScannedVisibilityCases))]
    public void ScannedVisibilityPreservesNullableFlag(bool markerChild, bool hasProperty, object? value, bool? expected) {
        var actor = new GenericAssetEntry { AssetPath = "/Game/Mods/Test.Test_C", AssetHash = "hash" };
        if (hasProperty) actor.Properties["bShowInGUI"] = value;

        var blueprint = ConvertActor(actor, markerChild);

        Assert.Equal(expected, blueprint.bShowInGUI);
        Assert.Equal(expected != false, blueprint.IsRelevant());
        Assert.False(blueprint.bClientside);
        Assert.False(blueprint.bAllowOnFrontend);
        Assert.False(blueprint.bEnableByDefault);
        Assert.False(blueprint.bSilentLoad);
        Assert.False(blueprint.bOnlineOnly);
        Assert.False(blueprint.bHostOnly);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void ConvertedActorsRetainOtherFlagSemantics(bool markerChild, bool clientSide, bool frontend) {
        var actor = new GenericAssetEntry { AssetPath = "/Game/Mods/Test.Test_C", AssetHash = "hash" };
        actor.Properties["bClientside"] = clientSide;
        actor.Properties["bAllowOnFrontend"] = frontend;
        actor.Properties["bEnableByDefault"] = true;
        actor.Properties["bSilentLoad"] = "true";
        actor.Properties["bOnlineOnly"] = new JValue(true);
        actor.Properties["bHostOnly"] = "invalid";

        var blueprint = ConvertActor(actor, markerChild);

        Assert.Null(blueprint.bShowInGUI);
        Assert.Equal(clientSide, blueprint.bClientside);
        Assert.Equal(frontend, blueprint.bAllowOnFrontend);
        Assert.True(blueprint.bEnableByDefault);
        Assert.True(blueprint.bSilentLoad);
        Assert.True(blueprint.bOnlineOnly);
        Assert.False(blueprint.bHostOnly);
        Assert.False(blueprint.IsRelevant());
    }

    [Fact]
    public void LegacyManifestUnknownHiddenFlagIsIgnoredByBothSerializers() {
        const string json = """{"path":"/Game/Mods/Test.Test_C","hash":"hash","is_hidden":true}""";

        var systemActor = System.Text.Json.JsonSerializer.Deserialize<BlueprintDto>(json)!;
        var newtonsoftActor = Newtonsoft.Json.JsonConvert.DeserializeObject<BlueprintDto>(json)!;

        Assert.Null(systemActor.bShowInGUI);
        Assert.Null(newtonsoftActor.bShowInGUI);
        Assert.True(systemActor.IsRelevant());
        Assert.True(newtonsoftActor.IsRelevant());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ProductionConfigsExtractExplicitFalse(int configIndex, bool markerChild) {
        var game = GameScanOptions.Chivalry2;
        List<PropertyConfig>[] configs = [
            game.CdoProcessors[0].Properties,
            new CdoProcessorConfig().Properties,
            game.MarkerProcessors[0].ReferencedBlueprintProperties,
            new MarkerDiscoveryConfig().ReferencedBlueprintProperties
        ];
        var properties = configs[configIndex];
        Assert.Equal(EExtractionMode.Raw, Assert.Single(properties, p => p.Name == "bShowInGUI").Mode);
        var raw = new Dictionary<string, object?> { ["bShowInGUI"] = false };
        var actor = new GenericAssetEntry { AssetPath = "/Game/Mods/Test.Test_C", AssetHash = "hash" };
        foreach (var property in properties) {
            if (raw.TryGetValue(property.Name, out var value)) actor.Properties[property.Name] = value;
        }

        var blueprint = ConvertActor(actor, markerChild);

        Assert.False(blueprint.bShowInGUI);
        Assert.False(blueprint.IsRelevant());
    }

    private static BlueprintDto ConvertActor(GenericAssetEntry actor, bool markerChild) {
        var scan = new ModScanResult();
        var pak = new PakScanResult();
        scan.Paks.TryAdd("test.pak", pak);
        if (markerChild) {
            var marker = new GenericMarkerEntry { AssetPath = "/Game/Mods/Marker", AssetHash = "hash" };
            marker.AddGenericEntry(actor);
            pak.GenericMarkers.TryAdd("DA_ModMarker_C", new() { ["marker"] = marker });
        }
        else {
            pak.AddGenericEntry(actor, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
        }

        return Assert.Single(Assert.Single(ModManifestConverter.ProcessModScan(scan).Paks).Inventory.Blueprints);
    }

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
    [InlineData(null, true)]
    public void OnlyExplicitFalseShowInGuiHidesActor(bool? showInGui, bool expected) {
        var actor = new BlueprintDto("/Game/Mods/Test.Test_C", "hash", null, null,
            "Test", "1.0.0", null, null, null, null, null, showInGui, null, null, null, null);

        Assert.Equal(expected, actor.IsRelevant());
        var manifest = new AssetCollections([], [actor], [], [], []);
        Assert.Equal(expected ? new[] { actor } : [], manifest.RelevantBlueprints());
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(null, true, false)]
    [InlineData(null, false, true)]
    public void ShowInGuiDoesNotOverrideClientSideOrFrontendExclusions(bool? showInGui, bool clientSide, bool frontend) {
        var actor = new BlueprintDto("/Game/Mods/Test.Test_C", "hash", null, null,
            "Test", "1.0.0", null, null, null, null, null, showInGui, clientSide, null, null, frontend);

        Assert.False(actor.IsRelevant());
        var manifest = new AssetCollections([], [actor], [], [], []);
        Assert.Empty(manifest.RelevantBlueprints());
    }
}