using LanguageExt;
using LanguageExt.Common;
using Moq;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels.Registry;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class OwnershipPersistenceTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ownership_" + Guid.NewGuid().ToString("N"));
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private string Paks => Path.Combine(_root, "TBL", "Content", "Paks");
    private string Config => Path.Combine(_root, ".mod_cache", "mod_manager_config.json");
    private string Inventory => Path.Combine(Paks, ".unchained", "inventory.json");
    private static ReleaseCoordinates Coordinates(string name = "Mod") => new("Org", name, "v1");
    private static Release Release(string name) => new("v1", "hash", name + ".pak", DateTime.UtcNow,
        new("https://github.com/Org/" + name, name, "", null, null, [], [], []),
        new([], [], [], [], []), "");

    public OwnershipPersistenceTests() {
        Directory.CreateDirectory(Paks);
        File.WriteAllText(Path.Combine(Paks, "pakchunk0-WindowsNoEditor.sig"), "sig");
    }

    private ModManager Manager(params ManagedPak[] paks) =>
        new(_registry, new PakDir(Paks, paks), [Coordinates()], [new Mod(Release("Mod").Info, [Release("Mod")])]);

    private ModManager Reload() => new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!;
    private ModManager Startup() => LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Paks).Manager;
    private void WriteInventory(string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(Inventory)!);
        File.WriteAllText(Inventory, content);
    }
    private ManagedPak Owned(string name = "Mod") {
        File.WriteAllText(Path.Combine(Paks, name + ".pak"), name);
        return new(Coordinates(name), name + ".pak", 42);
    }
    private void Save(ModManager manager) => new ModManagerCodec(_registry).SerializeFile(Config, manager);
    private static ModInstallRequest Install(string name) => new(Coordinates(name),
        path => new FileWriter(path, new MemoryStream([1, 2, 3]), 3));

    [Theory]
    [InlineData("Local#Team", "Mod", false)]
    [InlineData("LocalTeam", "Mod#1", false)]
    [InlineData("Local#Team", "Mod", true)]
    [InlineData("LocalTeam", "Mod#1", true)]
    [InlineData("Local Team.é_+@!$%&'()=-[]{};,", "Mod.é_+@!$%&'()=-[]{};,", false)]
    [InlineData("Local Team.é_+@!$%&'()=-[]{};,", "Mod.é_+@!$%&'()=-[]{};,", true)]
    public async Task LocalRegistryIdentitySurvivesInstallAndStartup(string organization, string module, bool convertInventory) {
        var source = Path.Combine(_root, "source.pak");
        File.WriteAllBytes(source, [1, 2, 3]);
        var form = new RegistryReleaseFormVM(Release("Mod"), source) { Organization = organization, Name = module };
        var (release, pakPath) = form.ToRelease();
        var coordinates = new ReleaseCoordinates(organization, module, "v1");
        Assert.Equal(coordinates, ReleaseCoordinates.FromRelease(release));
        var registry = new LocalModRegistry(Path.Combine(_root, "registry"));
        await registry.AddRelease(release, pakPath);
        Assert.True((await registry.GetModRelease(coordinates)).IsRight);
        var (manager, persistence) = LegacyOwnershipMigration.Load(Config, registry, installationPakDirectory: Paks);
        persistence.Save();
        var catalog = await manager.UpdateModsList();
        Assert.Empty(catalog.Errors);
        Assert.Single(catalog.Mods);
        Assert.True(manager.EnableModRelease(coordinates));
        var results = 0;
        await foreach (var result in manager.InstallMods([release], Option<AccumulatedMemoryProgress>.None)) {
            Assert.True(result.IsRight);
            results++;
        }
        Assert.Equal(1, results);
        var owned = Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
        Assert.Equal(coordinates, owned.Coordinates);
        Assert.Equal(module + "_0_P.pak", owned.PakFileName);
        Assert.Equal(0, owned.Priority);
        var codec = new ModManagerCodec(registry);
        Assert.Equal(owned, Assert.Single(((PakDir)codec.Deserialize(File.ReadAllText(Config)).Result!.PakDir).ManagedPaks));
        Assert.False(File.Exists(Inventory));
        if (convertInventory) {
            File.WriteAllText(Config, codec.Serialize(new ModManager(registry, new PakDir(Paks, []), [coordinates])));
            WriteInventory(JsonSerializer.Serialize(new[] { owned }));
        }
        var configBefore = File.ReadAllBytes(Config);
        var inventoryBefore = convertInventory ? File.ReadAllBytes(Inventory) : null;
        var restarted = LegacyOwnershipMigration.Load(Config, registry, installationPakDirectory: Paks).Manager;
        Assert.Equal(owned, Assert.Single(((PakDir)restarted.PakDir).ManagedPaks));
        Assert.Equal(coordinates, Assert.Single(restarted.EnabledModReleaseCoordinates));
        Assert.False(File.Exists(Inventory));
        if (convertInventory) {
            var backup = Assert.Single(Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(Config), "inventory.json", SearchOption.AllDirectories));
            Assert.Equal(inventoryBefore, File.ReadAllBytes(backup));
            var configBackup = Assert.Single(Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(Config), "mod_manager_config.json", SearchOption.AllDirectories));
            Assert.Equal(configBefore, File.ReadAllBytes(configBackup));
        }
        else Assert.Equal(configBefore, File.ReadAllBytes(Config));
        Assert.Equal(owned, Assert.Single(((PakDir)LegacyOwnershipMigration.Load(Config, registry, installationPakDirectory: Paks).Manager.PakDir).ManagedPaks));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Paks, owned.PakFileName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:stream")]
    [InlineData("a?b")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    public void UnsafeOrBlankIdentityCannotLoadSnapshotOrInventory(string? value) {
        foreach (var organization in new[] { true, false }) {
            var coordinates = organization ? Coordinates() with { Org = value! } : Coordinates() with { ModuleName = value! };
            var owned = Owned() with { Coordinates = coordinates };
            Save(Manager(owned));
            var configBefore = File.ReadAllBytes(Config);
            Assert.Throws<InvalidDataException>(Startup);
            Assert.Equal(configBefore, File.ReadAllBytes(Config));
            Save(Manager());
            configBefore = File.ReadAllBytes(Config);
            WriteInventory(JsonSerializer.Serialize(new[] { owned }));
            var inventoryBefore = File.ReadAllBytes(Inventory);
            Assert.Throws<InvalidDataException>(Startup);
            Assert.Equal(configBefore, File.ReadAllBytes(Config));
            Assert.Equal(inventoryBefore, File.ReadAllBytes(Inventory));
            Assert.Equal("Mod", File.ReadAllText(Path.Combine(Paks, owned.PakFileName)));
            File.Delete(Inventory);
        }
    }

    [Theory]
    [InlineData("release/v1.0.0")]
    [InlineData("local build #1?")]
    public async Task InstalledReleaseTagSurvivesPersistAndRestart(string tag) {
        var (manager, persistence) = LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Paks);
        persistence.Save();
        var coordinates = Coordinates() with { Version = tag };
        var request = new ModInstallRequest(coordinates,
            path => new FileWriter(path, new MemoryStream([1, 2, 3]), 3));
        var results = 0;
        await foreach (var result in manager.PakDir.InstallModSet([request], Option<AccumulatedMemoryProgress>.None)) {
            Assert.True(result.IsRight);
            results++;
        }
        Assert.Equal(1, results);
        var owned = Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
        Assert.Equal(coordinates, owned.Coordinates);
        Assert.Equal(owned, Assert.Single(((PakDir)Reload().PakDir).ManagedPaks));
        Assert.False(File.Exists(Inventory));
        var before = File.ReadAllBytes(Config);
        Assert.Equal(owned, Assert.Single(((PakDir)Startup().PakDir).ManagedPaks));
        Assert.Equal(before, File.ReadAllBytes(Config));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Paks, owned.PakFileName)));
    }

    [Theory]
    [InlineData("release/v1.0.0")]
    [InlineData("local build #1?")]
    public void CleanInventoryPreservesExactReleaseTag(string tag) {
        Save(Manager());
        var owned = Owned() with { Coordinates = Coordinates() with { Version = tag } };
        WriteInventory(JsonSerializer.Serialize(new[] { owned }));
        var inventoryBefore = File.ReadAllBytes(Inventory);
        Assert.Equal(owned, Assert.Single(((PakDir)Startup().PakDir).ManagedPaks));
        Assert.False(File.Exists(Inventory));
        Assert.Equal(owned, Assert.Single(((PakDir)Reload().PakDir).ManagedPaks));
        Assert.Equal(owned, Assert.Single(((PakDir)Startup().PakDir).ManagedPaks));
        var backup = Assert.Single(Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(Config), "inventory.json", SearchOption.AllDirectories));
        Assert.Equal(inventoryBefore, File.ReadAllBytes(backup));
        Assert.Equal("Mod", File.ReadAllText(Path.Combine(Paks, owned.PakFileName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void BlankReleaseTagCannotLoadSnapshotOrInventory(string? tag) {
        var owned = Owned() with { Coordinates = Coordinates() with { Version = tag! } };
        Save(Manager(owned));
        var before = File.ReadAllBytes(Config);
        Assert.Throws<InvalidDataException>(Startup);
        Assert.Equal(before, File.ReadAllBytes(Config));
        Save(Manager());
        before = File.ReadAllBytes(Config);
        WriteInventory(JsonSerializer.Serialize(new[] { owned }));
        var inventoryBefore = File.ReadAllBytes(Inventory);
        Assert.Throws<InvalidDataException>(Startup);
        Assert.Equal(before, File.ReadAllBytes(Config));
        Assert.Equal(inventoryBefore, File.ReadAllBytes(Inventory));
        Assert.Equal("Mod", File.ReadAllText(Path.Combine(Paks, owned.PakFileName)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanInventorySupersedesStaleConfigIncludingEmpty(bool empty) {
        Save(Manager(Owned("Stale")));
        var current = Owned();
        WriteInventory(JsonSerializer.Serialize(empty ? Array.Empty<ManagedPak>() : [current]));
        var before = File.ReadAllBytes(Config);
        var inventoryBefore = File.ReadAllBytes(Inventory);
        var manager = Startup();
        Assert.Equal(empty ? [] : new[] { current }, ((PakDir)manager.PakDir).ManagedPaks);
        Assert.Equal(new[] { Coordinates() }, manager.EnabledModReleaseCoordinates);
        Assert.Single(manager.Mods);
        Assert.False(File.Exists(Inventory));
        Assert.Equal(((PakDir)manager.PakDir).ManagedPaks, ((PakDir)Reload().PakDir).ManagedPaks);
        var backups = Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(Config), "*", SearchOption.AllDirectories);
        Assert.Contains(backups, path => File.ReadAllBytes(path).SequenceEqual(before));
        Assert.Contains(backups, path => File.ReadAllBytes(path).SequenceEqual(inventoryBefore));
        Assert.Equal(((PakDir)manager.PakDir).ManagedPaks, ((PakDir)Startup().PakDir).ManagedPaks);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("lock-only")]
    public void MissingInventoryUsesConfigAndNeverOpensLock(string kind) {
        var owned = Owned();
        Save(Manager(owned));
        var lockPath = Path.Combine(Paks, ".unchained", "inventory.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        using var held = kind == "lock-only"
            ? new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None) : null;
        Assert.Equal(owned, Assert.Single(((PakDir)Startup().PakDir).ManagedPaks));
        Assert.False(File.Exists(Inventory));
    }

    [Theory]
    [InlineData("pending", "")]
    [InlineData("inventory.tmp", "[]")]
    [InlineData("inventory.json", "")]
    [InlineData("inventory.json", "null")]
    [InlineData("inventory.json", "{}")]
    [InlineData("inventory.json", "[")]
    public void UncertainLegacyEvidenceCannotFallbackOrOverwriteConfig(string name, string content) {
        Save(Manager(Owned()));
        var before = File.ReadAllBytes(Config);
        Directory.CreateDirectory(Path.GetDirectoryName(Inventory)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Inventory)!, name), content);
        Assert.ThrowsAny<Exception>(Startup);
        Assert.Equal(before, File.ReadAllBytes(Config));
        Assert.Equal(content, File.ReadAllText(Path.Combine(Path.GetDirectoryName(Inventory)!, name)));
    }

    [Theory]
    [InlineData("../escape.pak")]
    [InlineData("a\\escape.pak")]
    [InlineData("stream:evil.pak")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a\0b.pak")]
    [InlineData("a\nb.pak")]
    [InlineData("a?b.pak")]
    [InlineData("a*b.pak")]
    [InlineData("a<b.pak")]
    [InlineData("a>b.pak")]
    [InlineData("a\"b.pak")]
    [InlineData("a|b.pak")]
    [InlineData("pakchunk0-WindowsNoEditor.pak")]
    public void InvalidInventoryNamesAreRejected(string name) {
        Save(Manager());
        WriteInventory(JsonSerializer.Serialize(new[] { new ManagedPak(new("Local#Team", "Mod#1", "v1"), name, 0) }));
        Assert.Throws<InvalidDataException>(Startup);
        Assert.True(File.Exists(Inventory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateNamesAndCoordinatesAreRejected(bool duplicateCoordinates) {
        Save(Manager());
        WriteInventory(JsonSerializer.Serialize(new[] {
            new ManagedPak(Coordinates(), "a.pak", 0),
            new ManagedPak(Coordinates(duplicateCoordinates ? "Mod" : "Other"), duplicateCoordinates ? "b.pak" : "A.pak", 1)
        }));
        Assert.Throws<InvalidDataException>(Startup);
    }

    [Fact]
    public void ConversionPreservesCacheConsentAndDropsOnlyMissingLiveRecords() {
        Save(Manager());
        WriteInventory(JsonSerializer.Serialize(new[] { Owned(), new ManagedPak(Coordinates("Missing"), "missing.pak", 1) }));
        var cache = Path.Combine(Paks, ".unchained", "pak-read-cache.json");
        var consent = Path.Combine(Paks, ".unchained", "kept-unmanaged-paks.json");
        var payload = new FileInfo(Path.Combine(Paks, "Mod.pak"));
        // Synthetic released hash-only schema; full production-writer coverage is in PakScanCacheTests.
        File.WriteAllText(cache, JsonSerializer.Serialize(new Dictionary<string, object> {
            ["Mod.pak"] = new {
                payload.Length,
                LastWriteTicks = payload.LastWriteTimeUtc.Ticks,
                Hash = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(payload.FullName))),
                VersionRead = false,
                Version = (string?)null
            }
        }));
        var cacheBytes = File.ReadAllBytes(cache);
        File.WriteAllText(consent, "consent-evidence");
        File.WriteAllText(Path.Combine(Paks, ".unchained", "inventory.tmp"), "stale-temporary");
        Assert.Single(((PakDir)Startup().PakDir).ManagedPaks);
        Assert.Equal(cacheBytes, File.ReadAllBytes(cache));
        Assert.Equal("consent-evidence", File.ReadAllText(consent));
        Assert.False(File.Exists(Path.Combine(Paks, ".unchained", "inventory.tmp")));
    }

    [Fact]
    public void FailedBackupPreservesOperationalEvidenceAndConfig() {
        Save(Manager());
        WriteInventory("[]");
        var before = File.ReadAllBytes(Config);
        File.WriteAllText(LegacyOwnershipMigration.BackupRoot(Config), "block-directory");
        Assert.ThrowsAny<IOException>(Startup);
        Assert.Equal(before, File.ReadAllBytes(Config));
        Assert.Equal("[]", File.ReadAllText(Inventory));
    }

    [Fact]
    public void FailedInitialConfigSaveCannotRetireInventory() {
        WriteInventory(JsonSerializer.Serialize(new[] { Owned() }));
        Directory.CreateDirectory(Config);
        Assert.True(Record.Exception(() => Startup()) is IOException or UnauthorizedAccessException);
        Assert.True(File.Exists(Inventory));
        Assert.NotEmpty(Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(Config), "inventory.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void InventoryImportsWhenConfigIsMissingButCannotMaskCorruptConfig() {
        var owned = Owned();
        WriteInventory(JsonSerializer.Serialize(new[] { owned }));
        Assert.Equal(owned, Assert.Single(((PakDir)Startup().PakDir).ManagedPaks));
        WriteInventory("[]");
        File.WriteAllText(Config, "{");
        Assert.Throws<InvalidDataException>(Startup);
        Assert.Equal("{", File.ReadAllText(Config));
        Assert.True(File.Exists(Inventory));
    }

    [Fact]
    public void LinkedInventoryOrOwnedPayloadCannotBeImported() {
        Save(Manager());
        var external = Path.Combine(_root, "outside.json");
        File.WriteAllText(external, "[]");
        Directory.CreateDirectory(Path.GetDirectoryName(Inventory)!);
        File.CreateSymbolicLink(Inventory, external);
        Assert.Throws<IOException>(Startup);
        File.Delete(Inventory);
        var payload = Path.Combine(Paks, "link.pak");
        File.CreateSymbolicLink(payload, external);
        WriteInventory(JsonSerializer.Serialize(new[] { new ManagedPak(Coordinates(), "link.pak", 0) }));
        Assert.Throws<IOException>(Startup);
        Assert.Equal("[]", File.ReadAllText(external));
    }

    [Fact]
    public void CustomDirectoryConversionDoesNotRebindOrSweepInstallationSavedPaks() {
        var custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(Path.Combine(custom, ".unchained"));
        File.WriteAllText(Path.Combine(custom, "custom.pak"), "custom");
        File.WriteAllText(Path.Combine(custom, ".unchained", "inventory.json"), "[]");
        var source = Path.Combine(_root, "TBL", "Saved", "Paks");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "other-installation.pak"), "preserved");
        Save(new ModManager(_registry, new PakDir(custom, [new(Coordinates(), "custom.pak", 0)]), []));
        var converted = Startup();
        Assert.Equal(custom, converted.PakDir.DirPath);
        Assert.Empty(((PakDir)converted.PakDir).ManagedPaks);
        Assert.True(File.Exists(Path.Combine(custom, "custom.pak")));
        Assert.True(File.Exists(Path.Combine(source, "other-installation.pak")));
        Assert.False(File.Exists(Path.Combine(custom, ".unchained", "inventory.json")));
    }

    [Fact]
    public async Task BatchPartialFailureAndEarlyDisposalSaveOwnershipOnce() {
        var manager = Manager();
        new ModManagerPersistence(manager, Config).Save();
        var paks = (PakDir)manager.PakDir;
        var notifications = 0;
        paks.OwnershipChanged += (_, _) => notifications++;
        await using (var results = paks.InstallModSet([Install("First"), Install("Never")], Option<AccumulatedMemoryProgress>.None).GetAsyncEnumerator()) {
            Assert.True(await results.MoveNextAsync());
            Assert.True(results.Current.IsRight);
            Assert.Empty(((PakDir)Reload().PakDir).ManagedPaks);
        }
        Assert.Equal(1, notifications);
        Assert.Equal(Coordinates("First"), Assert.Single(((PakDir)Reload().PakDir).ManagedPaks).Coordinates);
        var failed = new ModInstallRequest(Coordinates("Failed"), _ => Error.New("failure"));
        await foreach (var _ in paks.InstallModSet([Install("First"), Install("Second"), failed], Option<AccumulatedMemoryProgress>.None)) { }
        Assert.Equal(2, notifications);
        Assert.Equal(2, ((PakDir)Reload().PakDir).ManagedPaks.Count());
        Assert.False(Directory.Exists(Path.Combine(Paks, ".unchained")));
    }

    [Fact]
    public async Task ReorderStandaloneRemovalAndResetSaveImmediately() {
        var manager = Manager();
        new ModManagerPersistence(manager, Config).Save();
        foreach (var names in new[] { new[] { "First", "Second" }, new[] { "Second", "First" } }) {
            await foreach (var result in manager.PakDir.InstallModSet(names.Select(Install), Option<AccumulatedMemoryProgress>.None))
                Assert.True(result.IsRight);
            Assert.Equal(((PakDir)manager.PakDir).ManagedPaks, ((PakDir)Reload().PakDir).ManagedPaks);
        }
        Assert.True((await manager.PakDir.Uninstall(Coordinates("First"))).IsRight);
        Assert.Single(((PakDir)Reload().PakDir).ManagedPaks);
        Assert.True((await manager.PakDir.Reset()).IsRight);
        Assert.Empty(((PakDir)Reload().PakDir).ManagedPaks);
    }

    [Fact]
    public void SelectionChangesSaveWithoutShutdown() {
        var release = Release("Mod");
        var manager = new ModManager(_registry, new PakDir(Paks, []), [], [new Mod(release.Info, [release])]);
        _ = new ModManagerPersistence(manager, Config);
        Assert.True(manager.EnableModRelease(release));
        Assert.Equal(Coordinates(), Assert.Single(Reload().EnabledModReleaseCoordinates));
        Assert.True(manager.DisableModRelease(release));
        Assert.Empty(Reload().EnabledModReleaseCoordinates);
    }

    [Fact]
    public async Task HashVerifiedAdoptionSavesWithoutInstallOrShutdown() {
        var content = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(Path.Combine(Paks, "arbitrary-name.pak"), content);
        var release = Release("Mod") with {
            ReleaseHash = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(content))
        };
        var manager = Manager();
        new ModManagerPersistence(manager, Config).Save();
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
            It.IsAny<IEnumerable<DependencyUpdate>>())).Returns(UserDialogueChoice.Yes);
        Assert.True(await manager.PakDir.RecoverReleases([], [release], dialogue.Object));
        Assert.Equal("arbitrary-name.pak", Assert.Single(((PakDir)Reload().PakDir).ManagedPaks).PakFileName);
        Assert.False(File.Exists(Inventory));
    }

    [Fact]
    public void MainPluginAwareCacheAndCustomRegistrySurviveConversion() {
        var info = Release("Mod").Info with {
            Dependencies = null,
            ModDependencies = [new("https://example.test/custom/org/Dependency", "*")],
            PluginDependencies = [new("https://example.test/custom/org/Plugin", "1.*")]
        };
        var release = Release("Mod") with { Info = info };
        var registry = new GithubModRegistry("https://raw.githubusercontent.com/Org/Registry/refs/heads/custom");
        var manager = new ModManager(registry, new PakDir(Paks, []), [Coordinates()], [new Mod(info, [release])]);
        new ModManagerCodec(registry).SerializeFile(Config, manager);
        WriteInventory("[]");
        var converted = LegacyOwnershipMigration.Load(Config, registry, installationPakDirectory: Paks).Manager;
        var actual = Assert.Single(converted.Mods).LatestReleaseInfo;
        Assert.Null(actual.Dependencies);
        Assert.Equal(info.EffectiveModDependencies, actual.EffectiveModDependencies);
        Assert.Equal(info.EffectivePluginDependencies, actual.EffectivePluginDependencies);
        var registryJson = ModRegistryCodec.Instance.Serialize(registry);
        Assert.Equal(registryJson, ModRegistryCodec.Instance.Serialize(ModRegistryCodec.Instance.Deserialize(registryJson).Result!));
    }

    [Fact]
    public void ReleasedV3FixturePreservesSelectionsOwnershipAndInvalidatesOldCache() {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ModManagerV3.json")))!;
        fixture["PakDir"]!["DirPath"] = Paks;
        File.WriteAllText(Path.Combine(Paks, "Unchained-Mods.pak"), "fixture-payload");
        File.WriteAllText(ConfigDirectory(), fixture.ToJsonString());
        var manager = Startup();
        Assert.True(manager.NeedsMetadataRefresh);
        Assert.Single(manager.EnabledModReleaseCoordinates);
        Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
        new ModManagerPersistence(manager, Config).Save();
        Assert.Equal(manager.EnabledModReleaseCoordinates, Reload().EnabledModReleaseCoordinates);
        Assert.Equal(((PakDir)manager.PakDir).ManagedPaks, ((PakDir)Reload().PakDir).ManagedPaks);
    }

    private string ConfigDirectory() {
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        return Config;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
