using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                """, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace,
                    ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor, DateParseHandling = DateParseHandling.None })!;
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
    public async Task MetadataChangesInvalidateOnlyChangedPak(bool size) {
        File.WriteAllText(Path.Combine(directory, "other.pak"), "other");
        await PakScanCache.ScanAsync(directory, Options, Read);
        var timestamp = File.GetLastWriteTimeUtc(Pak);
        File.WriteAllText(Pak, size ? "longer replacement" : "modified");
        File.SetLastWriteTimeUtc(Pak, size ? timestamp : timestamp.AddMinutes(1));
        await PakScanCache.ScanAsync(directory, Options, Read);
        Assert.Equal(3, scans);
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
}
