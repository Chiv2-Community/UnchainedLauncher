using Moq;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using Moq;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class LocalPakDeletionTests(HomeLaunchUi ui) {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ScanPublicationDoesNotEraseDeletionEvidence(bool scanFirst) => ui.RunAsync(async () => {
        var directory = Path.Combine(Path.GetTempPath(), "pak-deletion-order-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try {
            var path = Path.Combine(directory, "local.pak");
            File.WriteAllText(path, "local");
            await CacheSharedActors(directory);
            var manager = new ModManager(Mock.Of<IModRegistry>(), new Core.Services.Mods.PakDir(directory, []), [], []);
            var scanner = new ModScanTabVM { DefaultPakDirectory = directory };
            using var catalog = new AvailableModsAndMapsService(manager, scanner);
            await catalog.WaitForCatalogAsync();
            var selected = Assert.Single(catalog.AvailableServerModBlueprints);
            var unavailable = selected with { Path = "unknown.uasset", ClassPath = "/Game/Unknown.Unknown_C" };
            File.Delete(path);
            if (scanFirst) await scanner.ScanAsync(directory);
            await catalog.RefreshLocalPaksAsync();
            if (!scanFirst) await scanner.ScanAsync(directory);
            await catalog.WaitForCatalogAsync();
            Assert.Empty(catalog.AvailableServerModBlueprints);
            Assert.Equal(new[] { selected }, catalog.GetConfirmedInactiveSelections([selected, unavailable]));
        }
        finally { Directory.Delete(directory, true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SharedActorSelectionSurvivesDeferredDependencyRevalidationOnlyForUnchangedSources(bool replaceSurvivor) => ui.RunAsync(async () => {
        var directory = Path.Combine(Path.GetTempPath(), "pak-shared-deletion-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try {
            var first = Path.Combine(directory, "first.pak");
            var second = Path.Combine(directory, "second.pak");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            await CacheSharedActors(directory);
            // Shadow candidates are contributors even when the owning pak's bytes are unchanged.
            var cachePath = Path.Combine(directory, ".unchained", "pak-read-cache.json");
            var json = JsonNode.Parse(File.ReadAllText(cachePath))!;
            json["second.pak"]!["ScanDependencies"] = System.Text.Json.JsonSerializer.SerializeToNode(
                new PakScanDependencies(PakScanDependencies.Snapshot(directory), ["Actor.uasset"], ["first.pak", "second.pak"]));
            File.WriteAllText(cachePath, json.ToJsonString());
            var manager = new ModManager(Mock.Of<IModRegistry>(), new Core.Services.Mods.PakDir(directory, []), [], []);
            var scanner = new ModScanTabVM { DefaultPakDirectory = directory };
            using var catalog = new AvailableModsAndMapsService(manager, scanner);
            await catalog.WaitForCatalogAsync();
            Assert.Equal(2, catalog.AvailableServerModBlueprints.Count);
            var selected = catalog.AvailableServerModBlueprints.First();
            File.Delete(first);
            if (replaceSurvivor) File.WriteAllText(second, "replacement");
            await catalog.RefreshLocalPaksAsync();
            // Exercise the real dependency-aware cache boundary: stale scans are not published as fresh.
            Assert.Empty((await PakScanCache.ScanAsync(directory, GameScanOptions.Chivalry2, cachedOnly: true)).Paks);
            Assert.Empty(scanner.LastScanResult!.Paks);
            Assert.Empty(catalog.AvailableServerModBlueprints);
            Assert.True(catalog.LocalModScan.CanScan);
            Assert.Equal(0, catalog.LocalModScan.LocalModActorCount);
            Assert.Equal(replaceSurvivor, catalog.GetConfirmedInactiveSelections([selected]).Length == 1);
            if (!replaceSurvivor) {
                await CacheSharedActors(directory);
                await catalog.RefreshLocalPaksAsync();
                Assert.Single(catalog.AvailableServerModBlueprints);
                Assert.Empty(catalog.GetConfirmedInactiveSelections([selected]));
            }
        }
        finally { Directory.Delete(directory, true); }
    });

    private static Task<ModScanResult> CacheSharedActors(string directory) => PakScanCache.ScanAsync(directory, GameScanOptions.Chivalry2, hashes => {
        var result = new ModScanResult();
        foreach (var (path, hash) in hashes) {
            var pak = JsonConvert.DeserializeObject<PakScanResult>("""
                {"generic_assets":{"/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C":[{
                  "asset_path":"Actor.uasset","class_path":"/Game/Actor.Actor_C",
                  "properties":{"ModName":"Actor","bShowInGUI":true}}]}}
                """, new JsonSerializerSettings {
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            })!;
            pak.PakPath = path;
            pak.PakHash = hash;
            result.Paks[Path.GetFileName(path)] = pak;
        }
        return Task.FromResult(result);
    });

    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("remove")]
    public Task PakChangesRefreshAllCollectionsWithoutAnOpenServerView(string change) => ui.RunAsync(async () => {
        var directory = Path.Combine(Path.GetTempPath(), "pak-deletion-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try {
            var first = Path.Combine(directory, "first.pak");
            var second = Path.Combine(directory, "second.pak");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            await PakScanCache.ScanAsync(directory, GameScanOptions.Chivalry2, hashes => {
                var result = new ModScanResult();
                foreach (var (path, hash) in hashes) {
                    var name = Path.GetFileNameWithoutExtension(path);
                    var pak = JsonConvert.DeserializeObject<PakScanResult>("""
                        {"generic_assets":{"/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C":[{
                          "asset_path":"NAME.uasset","class_path":"/Game/NAME.NAME_C",
                          "properties":{"ModName":"NAME","bShowInGUI":true}}]},
                          "maps":[{"asset_path":"NAME.umap","settings":{"Map":{"GamemodeType":"FFA"}}}]}
                        """.Replace("NAME", name), new JsonSerializerSettings {
                        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                        ObjectCreationHandling = ObjectCreationHandling.Replace
                    })!;
                    pak.PakPath = path;
                    pak.PakHash = hash;
                    result.Paks[Path.GetFileName(path)] = pak;
                }
                return Task.FromResult(result);
            });
            var manager = new ModManager(Mock.Of<IModRegistry>(), new Core.Services.Mods.PakDir(directory, []), [], []);
            var scanner = new ModScanTabVM { DefaultPakDirectory = directory };
            using var catalog = new AvailableModsAndMapsService(manager, scanner);
            await catalog.WaitForCatalogAsync();
            await catalog.LocalModScan.RefreshAsync();
            Assert.Equal(2, catalog.AvailableServerModBlueprints.Count);
            Assert.Equal(new[] { "first.pak", "second.pak" }, catalog.AvailableServerModActors.Select(actor => actor.Source).Order().ToArray());
            var survivor = catalog.AvailableServerModBlueprints.Single(actor => actor.ModName == "second");
            var selections = new ObservableCollection<BlueprintDto>(catalog.AvailableServerModBlueprints);
            var unavailable = survivor with { Path = "unknown.uasset", ClassPath = "/Game/Unknown.Unknown_C" };
            selections.Add(unavailable);
            catalog.ModStateChanged += (_, _) => {
                foreach (var actor in catalog.GetConfirmedInactiveSelections(selections)) selections.Remove(actor);
            };
            catalog.StartWatchingLocalPaks();
            if (change == "remove") {
                var dialogue = new Mock<IUserDialogueSpawner>();
                dialogue.Setup(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
                    It.IsAny<IEnumerable<DependencyUpdate>>())).Returns(UserDialogueChoice.No);
                using (var cache = new PakReadCache(directory, _ => Task.FromResult<string?>(null))) {
                    foreach (var path in new[] { first, second }) await cache.Version(path, await cache.Hash(path));
                }
                Assert.True(await manager.PakDir.RecoverReleases([], [], dialogue.Object));
            }
            else if (change == "rename") File.Move(first, Path.Combine(directory, "renamed.pak"));
            else File.Delete(first);

            var expected = change == "remove" ? 0 : 1;
            var timer = Stopwatch.StartNew();
            while (scanner.LastScanResult?.Paks.Count != expected || catalog.AvailableServerModBlueprints.Count != expected
                || catalog.LocalModScan.LocalModActorCount != expected || selections.Count != expected + 1) {
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "Pak deletion did not propagate to every collection.");
                await Task.Delay(25);
            }
            await catalog.WaitForCatalogAsync();
            Assert.Equal(expected, scanner.ResultsVisual.Children.Count);
            Assert.Equal(expected, catalog.AvailableServerModActors.Count);
            Assert.DoesNotContain(catalog.AvailableMaps, map => map.Path == "first.umap");
            Assert.Equal(change != "remove", catalog.LocalModScan.HasUnmanagedPaks);
            Assert.Equal(change == "rename", catalog.LocalModScan.CanScan);
            Assert.Contains(unavailable, selections);
            if (expected > 0) {
                Assert.Same(survivor, Assert.Single(catalog.AvailableServerModBlueprints));
                Assert.Contains(catalog.AvailableMaps, map => map.Path == "second.umap");
            }
            using var persisted = new PakReadCache(directory);
            Assert.Null(persisted.CachedHash(first));
            Assert.Equal(expected, (await PakScanCache.ScanAsync(directory, GameScanOptions.Chivalry2, cachedOnly: true)).Paks.Count);
        }
        finally { Directory.Delete(directory, true); }
    });
}
