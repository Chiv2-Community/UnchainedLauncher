using Moq;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using ManagedPakDir = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server {
    public class ServerSelectionMigrationTests {
        private static BlueprintDto Blueprint(string name) => new($"{name}.uasset", "hash", $"/Game/{name}.{name}_C", null,
            name, "1", null, null, null, false, false, true, false, false, true, false, false);

        private static Mod Mod(string name, params BlueprintDto[] blueprints) {
            var release = new Release("v1.0.0", "hash", "mod.pak", DateTime.UtcNow,
                new($"Chiv2-Community/{name}", name, "", null, null, [], [], []), new([], blueprints.ToList(), [], [], []), "");
            var newer = release with { Tag = "v2.0.0", Manifest = new([], [Blueprint("NewVersion")], [], [], []) };
            return new Mod(release.Info, [release, newer]);
        }

        private static ServerConfigurationCodec Codec(IModManager manager) {
            var scan = new ModScanTabVM();
            return new ServerConfigurationCodec(manager, scan, new AvailableModsAndMapsService(manager, scan));
        }

        private static ObservableCollection<ServerConfigurationVM> Decode(ServerConfigurationCodec codec, string json) {
            var result = codec.Deserialize(json);
            Assert.True(result.Success, result.Exception?.ToString());
            return result.Result!;
        }

        private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "LegacyServerSelections.json"));

        private static JsonElement[] SavedEntries(string json) {
            using var document = JsonDocument.Parse(json);
            return document.RootElement[0].GetProperty("EnabledServerModList").EnumerateArray().Select(entry => entry.Clone()).ToArray();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyCoordinatesResolveExactReleaseAndSurviveSaveReload(bool metadataInitiallyAvailable) {
            RunSta(() => {
                var first = Blueprint("FirstActor");
                var second = Blueprint("SecondActor");
                var mod = Mod("ExampleMod", first, second);
                var registry = new Mock<IModRegistry>();
                registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], [mod]));
                var manager = new ModManager(registry.Object, Mock.Of<IPakDir>(), [], metadataInitiallyAvailable ? [mod] : []);
                var codec = Codec(manager);
                var configs = Decode(codec, Fixture());
                // Hold the actual UI-bound collection: refresh must update it, not just a later getter.
                var selected = configs[0].EnabledServerModList;
                if (!metadataInitiallyAvailable) {
                    Assert.Empty(selected);
                    var savedBeforeRefresh = codec.Serialize(configs);
                    Assert.Equal(SavedEntries(Fixture()).Select(e => JsonSerializer.Serialize(e)), SavedEntries(savedBeforeRefresh).Select(e => JsonSerializer.Serialize(e)));
                    configs = Decode(codec, savedBeforeRefresh);
                    selected = configs[0].EnabledServerModList;
                    manager.UpdateModsList().GetAwaiter().GetResult();
                }
                Assert.Equal([first, second], selected);
                var saved = codec.Serialize(configs);
                Assert.Equal("v3.0.0", Assert.Single(SavedEntries(saved).Where(e => e.TryGetProperty("Org", out _))).GetProperty("Version").GetString());
                var reloaded = Decode(codec, saved);
                Assert.Equal([first, second], reloaded[0].EnabledServerModList);
                Assert.Equal(SavedEntries(saved).Select(e => JsonSerializer.Serialize(e)), SavedEntries(codec.Serialize(reloaded)).Select(e => JsonSerializer.Serialize(e)));
                reloaded[0].DisableServerBlueprintMod(first);
                var edited = Decode(codec, codec.Serialize(reloaded));
                Assert.Equal([second], edited[0].EnabledServerModList);
                Assert.Single(SavedEntries(codec.Serialize(edited)).Where(e => e.TryGetProperty("Org", out _)));
            });
        }

        [Fact]
        public void OfflineV3UpgradeRefreshRestoresCatalogsAndSelectionsInPlace() {
            RunSta(() => {
                var pakDir = Path.Combine(Path.GetTempPath(), "offline-upgrade-" + Guid.NewGuid());
                Directory.CreateDirectory(pakDir);
                try {
                    File.WriteAllBytes(Path.Combine(pakDir, "ExampleMod.pak"), []);
                    var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ModManagerV3.json"))
                        .Replace("Unchained-Mods", "ExampleMod"))!;
                    json["PakDir"]!["DirPath"] = pakDir;
                    var actor = Blueprint("UpgradeActor");
                    var map = new MapDto("TBL/Content/Maps/Upgrade.umap", "hash", null, null, null, "Upgrade", null, null, null, "FFA", null);
                    var mod = Mod("ExampleMod", actor);
                    var release = mod.Releases.First() with { Tag = "v0.0.2", Manifest = new([], [actor], [map], [], []) };
                    mod = mod with { Releases = [release] };
                    var coordinates = ReleaseCoordinates.FromRelease(release);
                    var registry = new Mock<IModRegistry>();
                    registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult(
                        [RegistryMetadataException.PackageListRetrieval("offline", LanguageExt.Prelude.None)], []));
                    var decoded = new ModManagerCodec(registry.Object).Deserialize(json.ToJsonString());
                    Assert.True(decoded.Success, decoded.Exception?.ToString());
                    var manager = decoded.Result!;
                    Assert.Empty(manager.Mods);
                    Assert.Equal([coordinates], manager.EnabledModReleaseCoordinates);
                    var installed = ((ManagedPakDir)manager.PakDir).ManagedPaks.ToList();
                    Assert.Single(installed);

                    // App startup awaits this refresh before constructing the catalog and saved servers.
                    Assert.True(manager.UpdateModsList().GetAwaiter().GetResult().HasErrors);
                    Assert.True(manager.NeedsMetadataRefresh);
                    var scan = new ModScanTabVM();
                    var catalog = new AvailableModsAndMapsService(manager, scan);
                    var codec = new ServerConfigurationCodec(manager, scan, catalog);
                    var savedSelections = Fixture().Replace("v1.0.0", "v0.0.2");
                    var configs = Decode(codec, savedSelections);
                    var selected = configs[0].EnabledServerModList;
                    var actors = catalog.AvailableServerModBlueprints;
                    var maps = catalog.AvailableMaps;
                    Assert.Empty(selected);
                    Assert.Empty(actors);
                    Assert.DoesNotContain(map, maps);
                    Assert.Equal(SavedEntries(savedSelections).Select(e => JsonSerializer.Serialize(e)),
                        SavedEntries(codec.Serialize(configs)).Select(e => JsonSerializer.Serialize(e)));

                    // Reconnect through the production refresh boundary, without a toggle, scan, or restart.
                    registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], [mod]));
                    for (var i = 0; i < 3; i++) {
                        Assert.False(manager.UpdateModsList().GetAwaiter().GetResult().HasErrors);
                        Assert.False(manager.NeedsMetadataRefresh);
                        Assert.Same(actors, catalog.AvailableServerModBlueprints);
                        Assert.Same(maps, catalog.AvailableMaps);
                        Assert.Same(selected, configs[0].EnabledServerModList);
                        Assert.Equal(actor, Assert.Single(selected));
                        Assert.Equal(actor, Assert.Single(actors));
                        Assert.Single(maps.Where(m => m == map));
                        Assert.Equal(maps.Count, maps.Distinct().Count());
                        Assert.Equal([coordinates], manager.EnabledModReleaseCoordinates);
                        Assert.Equal(installed, ((ManagedPakDir)manager.PakDir).ManagedPaks);
                        Assert.False(manager.EnableModRelease(coordinates));
                    }
                    // The restored actor is available to deselect, and refresh does not reselect it.
                    configs[0].DisableServerBlueprintMod(actor);
                    manager.UpdateModsList().GetAwaiter().GetResult();
                    Assert.Empty(selected);
                    Assert.Equal(actor, Assert.Single(actors));
                    Assert.Contains(map, maps);
                    Assert.Single(SavedEntries(codec.Serialize(configs)).Where(e => e.TryGetProperty("Org", out _)));
                }
                finally { Directory.Delete(pakDir, recursive: true); }
            });
        }

        [Fact]
        public void MissingSelectedVersionAndEmptyManifestRemainLossless() {
            RunSta(() => {
                var mod = Mod("ExampleMod");
                var manager = new ModManager(Mock.Of<IModRegistry>(), Mock.Of<IPakDir>(), [], [mod]);
                var codec = Codec(manager);
                var configs = Decode(codec, Fixture());
                Assert.Empty(configs[0].EnabledServerModList);
                var saved = codec.Serialize(configs);
                Assert.Equal(SavedEntries(Fixture()).Select(e => JsonSerializer.Serialize(e)), SavedEntries(saved).Select(e => JsonSerializer.Serialize(e)));
                var missingVersion = Fixture().Replace("v1.0.0", "v9.0.0");
                configs = Decode(codec, missingVersion);
                Assert.Empty(configs[0].EnabledServerModList);
                Assert.Equal(SavedEntries(missingVersion).Select(e => JsonSerializer.Serialize(e)), SavedEntries(codec.Serialize(configs)).Select(e => JsonSerializer.Serialize(e)));
            });
        }

        [Fact]
        public void MixedBlueprintAndUnresolvedCoordinateSelectionsSurviveRepeatedRoundTrips() {
            RunSta(() => {
                var codec = Codec(new ModManager(Mock.Of<IModRegistry>(), Mock.Of<IPakDir>(), []));
                var configs = Decode(codec, Fixture());
                var blueprint = Blueprint("ExistingActor") with { ModName = null };
                configs[0].EnableServerBlueprintMod(blueprint);
                var saved = codec.Serialize(configs);
                for (var i = 0; i < 3; i++) {
                    configs = Decode(codec, saved);
                    Assert.Equal(blueprint, Assert.Single(configs[0].EnabledServerModList));
                    var next = codec.Serialize(configs);
                    Assert.Equal(SavedEntries(saved).Select(e => JsonSerializer.Serialize(e)), SavedEntries(next).Select(e => JsonSerializer.Serialize(e)));
                    Assert.Equal(3, SavedEntries(next).Length);
                    saved = next;
                }
            });
        }

        private static void RunSta(Action action) {
            if (!OperatingSystem.IsWindows()) { action(); return; }
            Exception? error = null;
            var thread = new Thread(() => {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}