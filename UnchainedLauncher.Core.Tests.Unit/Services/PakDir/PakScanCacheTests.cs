using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.Export;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

[Collection("Home launch UI")]
public class PakScanCacheTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pak-scans-" + Guid.NewGuid());
    private string Pak => Path.Combine(directory, "manual.pak");
    private string Cache => Path.Combine(directory, ".unchained", "pak-read-cache.json");
    private int scans;
    private static ScanOptions Options => GameScanOptions.Chivalry2;

    public PakScanCacheTests() {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Pak, "original");
    }

    public void Dispose() => Directory.Delete(directory, true);

    private Task<ModScanResult> Read(IReadOnlyDictionary<string, string> hashes) {
        scans += hashes.Count;
        var result = new ModScanResult();
        foreach (var (path, hash) in hashes) {
            var pak = JsonConvert.DeserializeObject<PakScanResult>("""
                {"generic_assets":{"/Game/Base":[{"asset_path":"orphan.uasset","class_path":null,
                  "properties":{"ModVersion":null,"bShowInGUI":null,"bHostOnly":true}}]},
                 "generic_markers":{"Marker":{"marker.uasset":{"asset_path":"marker.uasset",
                  "children_class_path":"/Game/Base","children":[{"asset_path":"child.uasset",
                  "class_path":"/Game/Child.Child_C","asset_hash":"child-hash","properties":{
                  "ModName":"Child","ModVersion":"2.3.4","bShowInGUI":true,"bEnableByDefault":true,
                  "bSilentLoad":true,"bClientside":true,"bOnlineOnly":true,"bHostOnly":true,
                  "bAllowOnFrontend":true}}]}}},
                 "maps":[{"asset_path":"map.umap","game_mode":"Mode_C","settings":{"Map":{
                   "MapName":"Map","Description":"Description","AttackingFaction":"Agatha",
                   "DefendingFaction":"Mason","GamemodeType":"TO","TBLDefaultGameMode":"Mode"}}}],
                 "asset_replacements":[{"asset_path":"replacement.uasset"}],
                 "arbitrary_assets":[{"asset_path":"other.uasset","properties":{"date":"2026-11-13T00:00:00Z"}}],
                 "markers":[{"asset_path":"legacy.uasset","blueprints":[{"asset_path":"legacy-child.uasset",
                   "mod_name":null,"mod_version":null,"mod_author":null,"is_clientside":true}]}]}
                """, new JsonSerializerSettings {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                DateParseHandling = DateParseHandling.None
            })!;
            pak.PakPath = path;
            pak.PakHash = hash;
            result.Paks[Path.GetFileName(path)] = pak;
        }
        return Task.FromResult(result);
    }

    [Fact]
    public async Task RepeatedScansAndStartupReuseFullRawResultsWithoutOpeningPaks() {
        var first = await PakScanCache.ScanAsync(directory, Options, Read);
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        var second = await PakScanCache.ScanAsync(directory, Options, _ => throw new Exception("Rescan"));
        var startup = await PakScanCache.ScanAsync(directory, Options, cachedOnly: true);
        Assert.Equal(1, scans);
        Assert.True(JToken.DeepEquals(JToken.FromObject(first), JToken.FromObject(second)));
        Assert.True(JToken.DeepEquals(JToken.FromObject(first), JToken.FromObject(startup)));
        Assert.True(JToken.DeepEquals(JToken.Parse(ModScanJsonExporter.ExportToString(first))["ScanResults"],
            JToken.Parse(ModScanJsonExporter.ExportToString(startup))["ScanResults"]));
        var manifest = ModManifestConverter.ProcessModScan(startup).Paks.Single().Inventory;
        var actor = Assert.Single(manifest.Blueprints);
        Assert.True(actor.bHostOnly);
        Assert.True(actor.bShowInGUI);
        Assert.Equal("2.3.4", actor.Version);
        Assert.Equal("/Game/Child.Child_C", actor.ClassPath);
        Assert.Equal("Mode_C", Assert.Single(manifest.Maps).GameMode);
        Assert.Equal("Agatha", Assert.Single(manifest.Maps).AttackingFaction);
        Assert.Equal("child.uasset", Assert.Single(Assert.Single(manifest.Markers).AssociatedBlueprints));
        Assert.Null(Assert.Single(startup.Paks.Values).GenericEntries["/Game/Base"].Single().Properties["bShowInGUI"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataChangesInvalidateDependentScansWithoutRehashingUnchangedPak(bool size) {
        File.WriteAllText(Path.Combine(directory, "other.pak"), "other");
        await PakScanCache.ScanAsync(directory, Options, Read);
        var timestamp = File.GetLastWriteTimeUtc(Pak);
        File.WriteAllText(Pak, size ? "longer replacement" : "modified");
        File.SetLastWriteTimeUtc(Pak, size ? timestamp : timestamp.AddMinutes(1));
        using var locked = File.Open(Path.Combine(directory, "other.pak"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Single((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
        await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Equal(3, scans);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("rename")]
    [InlineData("vanilla")]
    [InlineData("utoc")]
    [InlineData("ucas")]
    public async Task UnrelatedInventoryChangesPreserveSurvivorsAndTheirHashes(string change) {
        var dependency = Path.Combine(directory, "dependency.pak");
        File.WriteAllText(dependency, "dependency");
        await PakScanCache.ScanAsync(directory, Options, Read);
        switch (change) {
            case "remove": File.Delete(dependency); break;
            case "rename": File.Move(dependency, dependency + ".renamed.pak"); break;
            case "vanilla": File.WriteAllText(Path.Combine(directory, Options.VanillaPakNames.First()), "vanilla"); break;
            case "utoc": File.WriteAllText(Path.Combine(directory, "dependency.utoc"), "container"); break;
            case "ucas": File.WriteAllText(Path.Combine(directory, "dependency.ucas"), "container"); break;
            default: File.WriteAllText(Path.Combine(directory, "added.pak"), "added"); break;
        }
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Contains("manual.pak", (await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks.Keys);
        var before = scans;
        var result = await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Equal(before + (change is "add" or "rename" ? 1 : 0), scans);
        Assert.True(JToken.DeepEquals(JToken.FromObject(result),
            JToken.FromObject(await PakScanCache.ScanAsync(directory, Options, _ => throw new Exception("Unchanged inventory")))));
    }

    [Fact]
    public async Task InventoryChangesDuringScanPublishNoNewScanEntries() {
        await Assert.ThrowsAsync<IOException>(() => PakScanCache.ScanAsync(directory, Options, hashes => {
            File.WriteAllText(Path.Combine(directory, "added.pak"), "added");
            return Read(hashes);
        }));
        Assert.Empty((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
        Assert.All(JsonNode.Parse(File.ReadAllText(Cache))!.AsObject(), entry => Assert.Null(entry.Value!["ScanJson"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionsAndRevisionInvalidateScansButKeepHash(bool revision) {
        await PakScanCache.ScanAsync(directory, Options, Read);
        if (revision) {
            var json = JsonNode.Parse(File.ReadAllText(Cache))!;
            json["manual.pak"]!["ScanIdentity"] = "old-revision";
            File.WriteAllText(Cache, json.ToJsonString());
        }
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        await PakScanCache.ScanAsync(directory, revision ? Options : Options with { ScanFilter = new MapsOnlyScanFilter() }, Read);
        Assert.Equal(2, scans);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameRemovalAndEmptyInventoriesPruneBothCaches(bool reinventory) {
        await PakScanCache.ScanAsync(directory, Options, Read);
        var renamed = Path.Combine(directory, "renamed.pak");
        File.Move(Pak, renamed);
        if (reinventory) {
            _ = new UnchainedLauncher.Core.Services.Mods.PakDir(directory, []);
            Assert.Empty(JsonNode.Parse(File.ReadAllText(Cache))!.AsObject());
        }
        var result = await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Equal(2, scans);
        Assert.Equal("renamed.pak", Assert.Single(result.Paks).Key);
        Assert.Equal("renamed.pak", Assert.Single(JsonNode.Parse(File.ReadAllText(Cache))!.AsObject()).Key);
        File.Delete(renamed);
        if (reinventory) {
            _ = new UnchainedLauncher.Core.Services.Mods.PakDir(directory, []);
            Assert.Empty(JsonNode.Parse(File.ReadAllText(Cache))!.AsObject());
        }
        Assert.Empty((await PakScanCache.ScanAsync(directory, Options, Read)).Paks);
        Assert.Empty(JsonNode.Parse(File.ReadAllText(Cache))!.AsObject());
        Assert.Equal(2, scans);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null-collection")]
    [InlineData("wrong-identity")]
    public async Task CorruptScanIsAMissWithoutDiscardingHash(string corruption) {
        await PakScanCache.ScanAsync(directory, Options, Read);
        var json = JsonNode.Parse(File.ReadAllText(Cache))!;
        if (corruption == "wrong-identity") json["manual.pak"]!["ScanIdentity"] = "wrong";
        else {
            var raw = JObject.Parse(json["manual.pak"]!["ScanJson"]!.GetValue<string>());
            raw["maps"] = null;
            json["manual.pak"]!["ScanJson"] = corruption == "invalid-json" ? "{" : raw.ToString();
        }
        File.WriteAllText(Cache, json.ToJsonString());
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task ScannerFailureAndChangingFilesPublishNoPartialScan() {
        await Assert.ThrowsAsync<IOException>(() => PakScanCache.ScanAsync(directory, Options, _ => throw new IOException("scanner failed")));
        Assert.Empty((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
        await Assert.ThrowsAsync<IOException>(() => PakScanCache.ScanAsync(directory, Options, hashes => {
            File.AppendAllText(Pak, "changed");
            return Read(hashes);
        }));
        Assert.Empty((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
        await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Single((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
    }

    [Fact]
    public async Task HydrationDoesNotHashUncachedFilesAndEmptyPakScansAreReusable() {
        using (File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Empty((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
        var empty = await PakScanCache.ScanAsync(directory, Options, _ => Task.FromResult(new ModScanResult()));
        Assert.Single(empty.Paks);
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Single((await PakScanCache.ScanAsync(directory, Options, _ => throw new Exception("Rescan"))).Paks);
    }

    [Fact]
    public async Task ConcurrentHashAndVersionClientDoesNotOverwriteCompletedScan() {
        using (var older = new PakReadCache(directory, _ => Task.FromResult<string?>("1.2.3"))) {
            var hash = await older.Hash(Pak);
            await PakScanCache.ScanAsync(directory, Options, Read);
            await older.Version(Pak, hash);
        }
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Single((await PakScanCache.ScanAsync(directory, Options, _ => throw new Exception("Lost scan"))).Paks);
    }

    [Fact]
    public async Task LegacyHashEntriesRemainUsableWithoutReadingPak() {
        using (var cache = new PakReadCache(directory)) await cache.Hash(Pak);
        var json = JsonNode.Parse(File.ReadAllText(Cache))!;
        json["manual.pak"]!.AsObject().Remove("ScanIdentity");
        json["manual.pak"]!.AsObject().Remove("ScanJson");
        File.WriteAllText(Cache, json.ToJsonString());
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Single((await PakScanCache.ScanAsync(directory, Options, Read)).Paks);
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task VersionClientWithOlderScanDoesNotRestoreObsoleteOptionsResult() {
        await PakScanCache.ScanAsync(directory, Options, Read);
        var revised = Options with { ScanFilter = new MapsOnlyScanFilter() };
        using (var older = new PakReadCache(directory, _ => Task.FromResult<string?>("1.2.3"))) {
            await PakScanCache.ScanAsync(directory, revised, Read);
            await older.Version(Pak, await older.Hash(Pak));
        }
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Single((await PakScanCache.ScanAsync(directory, revised, cachedOnly: true)).Paks);
    }

    [Fact]
    public async Task ReinventoryDropsChangedMetadataWithoutHashingOrScanning() {
        await PakScanCache.ScanAsync(directory, Options, Read);
        File.AppendAllText(Pak, "replacement");
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = new UnchainedLauncher.Core.Services.Mods.PakDir(directory, []);
        Assert.Empty(JsonNode.Parse(File.ReadAllText(Cache))!.AsObject());
        Assert.Empty((await PakScanCache.ScanAsync(directory, Options, cachedOnly: true)).Paks);
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task ConcurrentClientsMergeIndependentPakEntries() {
        var other = Path.Combine(directory, "other.pak");
        File.WriteAllText(other, "other");
        using (var first = new PakReadCache(directory))
        using (var second = new PakReadCache(directory)) {
            await Task.WhenAll(first.Hash(Pak), second.Hash(other));
        }
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(Cache))!.AsObject().Count);
        using var exclusive = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        using var otherExclusive = File.Open(other, FileMode.Open, FileAccess.Read, FileShare.None);
        using var restarted = new PakReadCache(directory, readHash: _ => throw new Exception("Lost hash"));
        await restarted.Hash(Pak);
        await restarted.Hash(other);
    }

    private void SetDependencies(string owner, string[] lookups, params string[] contributors) {
        var json = JsonNode.Parse(File.ReadAllText(Cache))!;
        json[owner]!["ScanDependencies"] = System.Text.Json.JsonSerializer.SerializeToNode(
            new PakScanDependencies(PakScanDependencies.Snapshot(directory), lookups, contributors));
        File.WriteAllText(Cache, json.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionEmptyContainerScansCaptureShadowCandidatesAutomatically(bool overlaps) {
        const string owned = "TBL/Content/Owned.bin";
        WriteIndexPak(Pak, owned);
        await PakScanCache.ScanAsync(directory, Options);
        var saved = JsonNode.Parse(File.ReadAllText(Cache))!;
        Assert.Contains(owned, saved["manual.pak"]!["ScanDependencies"]!["Paths"]!.AsArray().Select(path => path!.GetValue<string>()));
        WriteIndexPak(Path.Combine(directory, "added.pak"), overlaps ? owned : "TBL/Content/Unrelated.bin");
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        var selected = new List<string>();
        await PakScanCache.ScanAsync(directory, Options, hashes => {
            selected.AddRange(hashes.Keys.Select(Path.GetFileName)!);
            return Read(hashes);
        });
        Assert.Equal(overlaps, selected.Contains("manual.pak"));
        Assert.Contains("added.pak", selected);
    }

    [Fact]
    public async Task ProductionScanRemovingUnrelatedContainerReusesLockedSurvivor() {
        WriteIndexPak(Pak, "TBL/Content/Owned.bin");
        var other = Path.Combine(directory, "other.pak");
        WriteIndexPak(other, "TBL/Content/Other.bin");
        await PakScanCache.ScanAsync(directory, Options);
        File.Delete(other);
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        var survivor = await PakScanCache.ScanAsync(directory, Options);
        Assert.Equal("manual.pak", Assert.Single(survivor.Paks).Key);
        var saved = JsonNode.Parse(File.ReadAllText(Cache))!.AsObject();
        Assert.False(saved.ContainsKey("other.pak"));
        Assert.False(saved["manual.pak"]!["ScanDependencies"]!["Inventory"]!.AsObject().ContainsKey("other.pak"));
    }

    [Theory]
    [InlineData("add", false)]
    [InlineData("rename", false)]
    [InlineData("remove", false)]
    [InlineData("change", false)]
    [InlineData("add", true)]
    [InlineData("rename", true)]
    [InlineData("remove", true)]
    [InlineData("change", true)]
    public async Task RealContainerIndexChangesInvalidateOnlyIntersectingLookups(string action, bool related) {
        const string used = "TBL/Content/Shared.uasset";
        var contributor = Path.Combine(directory, "contributor.pak");
        WriteIndexPak(contributor, related ? used : "TBL/Content/Unrelated.bin");
        var independent = Path.Combine(directory, "independent.pak");
        File.WriteAllText(independent, "independent");
        await PakScanCache.ScanAsync(directory, Options, Read);
        SetDependencies("manual.pak", [used], related && action != "add" ? ["contributor.pak"] : []);
        switch (action) {
            case "remove": File.Delete(contributor); break;
            case "rename": File.Move(contributor, Path.Combine(directory, "renamed.pak")); break;
            case "change": WriteIndexPak(contributor, related ? used : "TBL/Content/StillUnrelated.bin"); break;
            default: WriteIndexPak(Path.Combine(directory, "added.pak"), related ? used : "TBL/Content/New.bin"); break;
        }
        using var ownerLock = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        using var independentLock = File.Open(independent, FileMode.Open, FileAccess.Read, FileShare.None);
        if (action == "remove") {
            var hydrated = await PakScanCache.ScanAsync(directory, Options, cachedOnly: true);
            Assert.Equal(!related, hydrated.Paks.ContainsKey("manual.pak"));
            Assert.Contains("independent.pak", hydrated.Paks.Keys);
        }
        var selected = new List<string>();
        await PakScanCache.ScanAsync(directory, Options, hashes => {
            selected.AddRange(hashes.Keys.Select(Path.GetFileName)!);
            return Read(hashes);
        });
        Assert.Equal(related, selected.Contains("manual.pak"));
        Assert.DoesNotContain("independent.pak", selected);
        Assert.Single((await PakScanCache.GetHashesAsync([Pak])).Values);
        var saved = JsonNode.Parse(File.ReadAllText(Cache))!.AsObject();
        if (action is "remove" or "rename") Assert.False(saved.ContainsKey("contributor.pak"));
        Assert.All(saved["manual.pak"]!["ScanDependencies"]!["Inventory"]!.AsObject(),
            entry => Assert.True(File.Exists(Path.Combine(directory, entry.Key))));
    }

    [Fact]
    public async Task MultipleContributorsAndMissingPayloadLookupsAreTrackedIndependently() {
        const string lookup = "TBL/Content/Imported.uexp";
        WriteIndexPak(Path.Combine(directory, "one.pak"), "TBL/Content/Imported.uasset");
        WriteIndexPak(Path.Combine(directory, "two.pak"), "TBL/Content/Other.uasset");
        await PakScanCache.ScanAsync(directory, Options, Read);
        SetDependencies("manual.pak", [lookup], "one.pak", "two.pak");
        WriteIndexPak(Path.Combine(directory, "payload.pak"), lookup);
        var selected = new List<string>();
        await PakScanCache.ScanAsync(directory, Options, hashes => {
            selected.AddRange(hashes.Keys.Select(Path.GetFileName)!);
            return Read(hashes);
        });
        Assert.Equal(["manual.pak", "payload.pak"], selected.Order().ToArray());
    }

    [Fact]
    public async Task BrokenChangedContainerPublishesNoDependencyRefreshAndRetainsHash() {
        await PakScanCache.ScanAsync(directory, Options, Read);
        SetDependencies("manual.pak", ["TBL/Content/Missing.uasset"]);
        var original = JsonNode.Parse(File.ReadAllText(Cache))!["manual.pak"]!.DeepClone();
        File.WriteAllText(Path.Combine(directory, "broken.pak"), "not a pak");
        await Assert.ThrowsAnyAsync<Exception>(() => PakScanCache.ScanAsync(directory, Options, Read));
        var after = JsonNode.Parse(File.ReadAllText(Cache))!["manual.pak"]!;
        Assert.Equal(original.ToJsonString(), after.ToJsonString());
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.NotNull((await PakScanCache.GetHashesAsync([Pak]))[Pak]);
    }

    [Fact]
    public async Task OldFullScanRefreshKeepsExistingHashAndVersion() {
        await PakScanCache.ScanAsync(directory, Options, Read);
        using (var cache = new PakReadCache(directory, _ => Task.FromResult<string?>("1.2.3")))
            await cache.Version(Pak, await cache.Hash(Pak));
        var json = JsonNode.Parse(File.ReadAllText(Cache))!;
        json["manual.pak"]!["ScanIdentity"] = "3:old-inventory-fingerprint";
        json["manual.pak"]!.AsObject().Remove("ScanDependencies");
        File.WriteAllText(Cache, json.ToJsonString());
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        await PakScanCache.ScanAsync(directory, Options, Read);
        await PakScanCache.ScanAsync(directory, Options, _ => throw new Exception("Second refresh"));
        using var version = new PakReadCache(directory, _ => throw new Exception("Version reread"));
        Assert.Equal("1.2.3", await version.Version(Pak, await version.Hash(Pak)));
        Assert.Equal(2, scans);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("null-paths")]
    public async Task DamagedDependenciesRefreshOnlyTheScanAndKeepItsHash(string damage) {
        await PakScanCache.ScanAsync(directory, Options, Read);
        var json = JsonNode.Parse(File.ReadAllText(Cache))!;
        if (damage == "missing") json["manual.pak"]!.AsObject().Remove("ScanDependencies");
        else if (damage == "wrong-type") json["manual.pak"]!["ScanDependencies"] = "invalid";
        else json["manual.pak"]!["ScanDependencies"]!["Paths"] = null;
        File.WriteAllText(Cache, json.ToJsonString());
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Equal(2, scans);
    }

    [Theory]
    [InlineData("one.pak")]
    [InlineData("two.pak")]
    public async Task RemovingEitherRecordedContributorInvalidatesOnlyItsSource(string removed) {
        File.WriteAllText(Path.Combine(directory, "one.pak"), "one");
        File.WriteAllText(Path.Combine(directory, "two.pak"), "two");
        await PakScanCache.ScanAsync(directory, Options, Read);
        SetDependencies("manual.pak", ["TBL/Content/Shared.uasset"], "one.pak", "two.pak");
        File.Delete(Path.Combine(directory, removed));
        using var locked = File.Open(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        var selected = new List<string>();
        await PakScanCache.ScanAsync(directory, Options, hashes => {
            selected.AddRange(hashes.Keys.Select(Path.GetFileName)!);
            return Read(hashes);
        });
        Assert.Equal(["manual.pak"], selected);
    }

    [Fact]
    public void GlobalResolutionInputsAndNumericIoImportsRemainConservative() {
        var inventory = new Dictionary<string, PakScanDependencies.Stamp> { ["owner.pak"] = new(1, 1) };
        var dependencies = new PakScanDependencies(inventory, ["TBL/Content/Used.uasset"], ["owner.pak"], "TBL");
        var addedPak = new Dictionary<string, PakScanDependencies.Stamp>(inventory) { ["added.pak"] = new(2, 2) };
        Assert.True(dependencies.Matches(addedPak, _ => ["TBL/Content/Unrelated.uasset"]));
        Assert.False(dependencies.Matches(addedPak, _ => ["TBL/Plugins/Added.uplugin"]));
        Assert.False(dependencies.Matches(addedPak, _ => ["OtherProject/Content/Unrelated.uasset"]));
        Assert.False(dependencies.Matches(addedPak, _ => null)); // Hydration never opens indexes.
        var addedIo = new Dictionary<string, PakScanDependencies.Stamp>(inventory) { ["added.utoc"] = new(2, 2) };
        Assert.False((dependencies with { UsesIoStore = true }).Matches(addedIo, _ => throw new Exception("Index read")));
        var addedProject = new Dictionary<string, PakScanDependencies.Stamp>(inventory) { ["TBL.uproject"] = new(2, 2) };
        Assert.False(dependencies.Matches(addedProject, _ => throw new Exception("Index read")));
    }

    private static void WriteIndexPak(string path, params string[] names) {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[512]); // CUE4Parse probes several newer footer sizes.
        var offsets = new List<long>();
        foreach (var name in names) {
            offsets.Add(stream.Position);
            Entry(writer, stream.Position);
            writer.Write((byte)0);
        }
        var indexOffset = stream.Position;
        FString(writer, "../../../");
        writer.Write(names.Length);
        for (var n = 0; n < names.Length; n++) { FString(writer, names[n]); Entry(writer, offsets[n]); }
        var indexSize = stream.Position - indexOffset;
        writer.Write(0x5A6F12E1u);
        writer.Write(3);
        writer.Write(indexOffset);
        writer.Write(indexSize);
        writer.Write(new byte[20]);
        static void Entry(BinaryWriter writer, long offset) {
            writer.Write(offset); writer.Write(1L); writer.Write(1L); writer.Write(0);
            writer.Write(SHA1.HashData([0])); writer.Write((byte)0); writer.Write(0u);
        }
        static void FString(BinaryWriter writer, string text) {
            var bytes = Encoding.UTF8.GetBytes(text);
            writer.Write(bytes.Length + 1); writer.Write(bytes); writer.Write((byte)0);
        }
    }
}