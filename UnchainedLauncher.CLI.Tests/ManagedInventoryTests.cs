using UnchainedLauncher.CLI.Configuration;
using UnchainedLauncher.CLI.Launching;
using UnchainedLauncher.CLI.Terminal;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

[Collection("Installation directory")]
public class ManagedInventoryTests : IDisposable {
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()),
        "inventory-tests-" + Guid.NewGuid().ToString("N"));

    public ManagedInventoryTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SequentialRequestsLoadOwnershipButNeverCachedSelections(bool server) {
        await InInstallation(async (registry, common, extra, launch) => {
            if (!server) launch = launch with { ServerLaunchOptions = LanguageExt.Option<ServerLaunchOptions>.None };
            var codec = new ModManagerCodec(registry);
            var cli = Manager(registry, []);
            await Prepare(cli, launch);

            // GUI has its own selection and may reorder the shared common pak.
            var selected = ReleaseCoordinates.FromRelease(extra);
            var gui = Manager(registry, [selected]);
            await Prepare(gui, launch);
            var guiConfiguration = File.ReadAllText(OwnerConfig);
            var oldExtra = (await gui.PakDir.GetManagedPakFilePath(selected)).IfNone("");
            Assert.True(File.Exists(oldExtra));

            var secondCli = Manager(registry, []);
            await Prepare(secondCli, launch);
            Assert.False(File.Exists(oldExtra));
            Assert.False(File.Exists(Path.ChangeExtension(oldExtra, ".sig")));
            AssertOnlyCommonAndUnknown(secondCli, common);
            Assert.Empty(secondCli.EnabledModReleaseCoordinates);

            // The saved GUI snapshot now names obsolete files. Shared ownership wins,
            // while GUI's selected release survives and is installed on its next launch.
            var decoded = codec.Deserialize(guiConfiguration);
            Assert.True(decoded.Success);
            gui = Manager(registry, [selected]);
            Assert.Equal(selected, Assert.Single(gui.EnabledModReleaseCoordinates));
            await Prepare(gui, launch);
            Assert.Equal(2, ((PakDir)gui.PakDir).ManagedPaks.Count());
            secondCli = Manager(registry, []);
            await Prepare(secondCli, launch);
            AssertOnlyCommonAndUnknown(secondCli, common);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UnrelatedHostCachesAreNeverImported(bool guiCache, bool cliCache) {
        await InInstallation(async (registry, common, extra, launch) => {
            var codec = new ModManagerCodec(registry);
            var paks = Path.GetFullPath(FilePaths.PakDir);
            var commonCoordinate = ReleaseCoordinates.FromRelease(common);
            var extraCoordinate = ReleaseCoordinates.FromRelease(extra);
            var oldCommon = new ManagedPak(commonCoordinate, "legacy-common.pak", 0);
            var newCommon = new ManagedPak(commonCoordinate, "gui-common.pak", 1);
            var guiOnly = new ManagedPak(extraCoordinate, "gui-only.pak", 0);
            foreach (var pak in new[] { oldCommon, newCommon, guiOnly }) {
                File.Copy(Path.Combine(_root, "payload.pak"), Path.Combine(paks, pak.PakFileName));
                File.WriteAllText(Path.Combine(paks, Path.ChangeExtension(pak.PakFileName, ".sig")), "signature");
            }
            // A single legacy input must be sufficient, including duplicate releases.
            if (guiCache) codec.SerializeFile(Path.GetFullPath(FilePaths.ModManagerConfigPath),
                new ModManager(registry, new PakDir(FilePaths.PakDir, cliCache ? [newCommon, guiOnly] : [oldCommon, newCommon, guiOnly]), [extraCoordinate]));
            if (cliCache) codec.SerializeFile(Path.Combine(FilePaths.ModCachePath, "cli_mod_manager.json"),
                new ModManager(registry, new PakDir(paks, guiCache ? [oldCommon] : [oldCommon, newCommon, guiOnly]), []));
            var beforeGui = guiCache ? File.ReadAllText(FilePaths.ModManagerConfigPath) : null;
            var manager = Manager(registry, []);
            await Prepare(manager, launch);
            Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
            foreach (var pak in new[] { oldCommon, newCommon, guiOnly }) {
                Assert.True(File.Exists(Path.Combine(paks, pak.PakFileName)));
                Assert.True(File.Exists(Path.Combine(paks, Path.ChangeExtension(pak.PakFileName, ".sig"))));
            }
            if (guiCache) Assert.Equal(beforeGui, File.ReadAllText(FilePaths.ModManagerConfigPath));
            // Obsolete caches must not override the post-migration shared inventory.
            await Prepare(Manager(registry, []), launch);
            Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
        });
    }

    private static string OwnerConfig => Path.GetFullPath(Path.Combine(FilePaths.ModCachePath, "test_owner.json"));
    private ModManager Manager(IModRegistry registry, ReleaseCoordinates[] selected) =>
        LegacyOwnershipMigration.LoadProfile(OwnerConfig, registry,
            new PakDir(Path.GetFullPath(FilePaths.PakDir), [],
                Path.GetFullPath(Path.Combine(FilePaths.PakDir, "pakchunk0-WindowsNoEditor.sig")),
                Path.Combine(_root, "cache")), selected).Manager;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPublicationRetryRestartAndDeselectNeverDuplicateOrOrphan(bool afterPayload) {
        await InInstallation(async (registry, common, extra, launch) => {
            var selected = ReleaseCoordinates.FromRelease(extra);
            var manager = Manager(registry, [selected]);
            new ModManagerCodec(registry).SerializeFile(OwnerConfig, manager);
            var evidence = OwnerConfig + ".last-success";
            void BlockPublication() {
                File.Move(OwnerConfig, evidence);
                Directory.CreateDirectory(OwnerConfig);
            }
            if (!afterPayload) BlockPublication();
            var downloads = 0;
            var request = new ModInstallRequest(selected, path => {
                downloads++;
                if (afterPayload) BlockPublication();
                var bytes = File.ReadAllBytes(Path.Combine(_root, "payload.pak"));
                return new FileWriter(path, new MemoryStream(bytes), bytes.Length);
            }, extra.ReleaseHash);
            if (afterPayload) {
                var failure = await Record.ExceptionAsync(async () => {
                    await foreach (var _ in manager.PakDir.InstallModSet([request], LanguageExt.Option<AccumulatedMemoryProgress>.None)) { }
                });
                Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "No failure");
                Assert.Equal(1, downloads);
                Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
            }
            else {
                Assert.True((await new Chivalry2ModsInstaller(manager, new ConsoleDialogue()).PrepareLaunch(launch)).IsNone);
                Assert.Equal(0, downloads);
                Assert.Empty(((PakDir)manager.PakDir).ManagedPaks);
            }
            Directory.Delete(OwnerConfig);
            File.Move(evidence, OwnerConfig);
            var restored = Manager(registry, [selected]);
            Assert.Empty(((PakDir)restored.PakDir).ManagedPaks);
            if (afterPayload) {
                var beforeRecovery = File.ReadAllBytes(OwnerConfig);
                Assert.True((await new Chivalry2ModsInstaller(restored, new ConsoleDialogue()).PrepareLaunch(launch)).IsNone);
                Assert.Equal(beforeRecovery, File.ReadAllBytes(OwnerConfig));
                Assert.True(await restored.PakDir.RecoverReleases([extra], [extra], new KeepRecoveryDialogue()));
                Assert.Equal(new[] { selected }, restored.EnabledModReleaseCoordinates);
            }
            await Prepare(restored, launch);
            var owned = ((PakDir)restored.PakDir).ManagedPaks.ToArray();
            Assert.Equal(2, owned.Length); // requested mod and mandatory common framework
            Assert.Equal(4, Directory.GetFiles(restored.PakDir.DirPath, "*.pak").Length);
            await Prepare(Manager(registry, []), launch);
            var restarted = Manager(registry, []);
            AssertOnlyCommonAndUnknown(restarted, common);
            Assert.Equal(((PakDir)restarted.PakDir).ManagedPaks,
                ((PakDir)new ModManagerCodec(registry).Deserialize(File.ReadAllText(OwnerConfig)).Result!.PakDir).ManagedPaks);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCliInventoryIsMergedOnceWithoutClobberingConfig(bool blockSave) {
        await InInstallation(async (registry, common, extra, launch) => {
            var paks = Path.GetFullPath(FilePaths.PakDir);
            var a = new ManagedPak(ReleaseCoordinates.FromRelease(common), "a.pak", 0);
            var b = new ManagedPak(ReleaseCoordinates.FromRelease(extra), "b.pak", 1);
            foreach (var pak in new[] { a, b }) File.Copy(Path.Combine(_root, "payload.pak"), Path.Combine(paks, pak.PakFileName));
            new ModManagerCodec(registry).SerializeFile(OwnerConfig, new ModManager(registry, new PakDir(paks, [a, b]), [b.Coordinates]));
            var inventory = Path.Combine(paks, FilePaths.ManagedPakInventoryFileName);
            File.WriteAllText(inventory, JsonHelpers.Serialize(new PakDirMetadata(paks, [a])));
            var bytes = File.ReadAllBytes(inventory);
            var config = File.ReadAllBytes(OwnerConfig);
            if (blockSave) {
                File.Delete(OwnerConfig);
                Directory.CreateDirectory(OwnerConfig);
                Assert.ThrowsAny<IOException>(() => Manager(registry, []));
                Assert.Equal(bytes, File.ReadAllBytes(inventory));
                Assert.True(File.Exists(Path.Combine(paks, b.PakFileName)));
                Directory.Delete(OwnerConfig);
                File.WriteAllBytes(OwnerConfig, config);
            }
            var manager = Manager(registry, []);
            Assert.Equal(new[] { a, b }, ((PakDir)manager.PakDir).ManagedPaks);
            Assert.Empty(manager.EnabledModReleaseCoordinates);
            Assert.False(File.Exists(inventory));
            Assert.Contains(Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(OwnerConfig), FilePaths.ManagedPakInventoryFileName,
                SearchOption.AllDirectories), path => File.ReadAllBytes(path).SequenceEqual(bytes));
            Assert.Equal(new[] { a, b }, ((PakDir)Manager(registry, []).PakDir).ManagedPaks);
            await Prepare(manager, launch);
            AssertOnlyCommonAndUnknown(manager, common);
        });
    }

    [Theory]
    [InlineData("config-profile")]
    [InlineData("config-installation")]
    [InlineData("inventory-profile")]
    [InlineData("inventory-installation")]
    [InlineData("inventory-conflict")]
    [InlineData("inventory-pending")]
    public async Task ForeignOrUncertainProfileEvidenceIsPreservedAndNeverImported(string fault) {
        await InInstallation((registry, common, extra, _) => {
            var paks = Path.GetFullPath(FilePaths.PakDir);
            var owned = new ManagedPak(ReleaseCoordinates.FromRelease(common), "owned.pak", 0);
            File.Copy(Path.Combine(_root, "payload.pak"), Path.Combine(paks, owned.PakFileName));
            var metadata = new PakDirMetadata(paks, [owned], Path.Combine(paks, "pakchunk0-WindowsNoEditor.sig"));
            var otherDirectory = Path.Combine(_root, "foreign");
            Directory.CreateDirectory(otherDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(OwnerConfig)!);
            var configMetadata = fault == "config-profile" ? metadata with { DirPath = otherDirectory }
                : fault == "config-installation" ? metadata with { DefaultSigPath = Path.Combine(otherDirectory, "base.sig") } : metadata;
            File.WriteAllText(OwnerConfig, JsonHelpers.Serialize(new StandardModManagerMetadata([], [], configMetadata)));
            var inventory = Path.Combine(paks, FilePaths.ManagedPakInventoryFileName);
            if (fault.StartsWith("inventory-", StringComparison.Ordinal)) {
                var legacy = fault == "inventory-profile" ? metadata with { DirPath = otherDirectory }
                    : fault == "inventory-installation" ? metadata with { DefaultSigPath = Path.Combine(otherDirectory, "base.sig") }
                    : fault == "inventory-conflict" ? metadata with { ManagedPaks = [owned with { Coordinates = ReleaseCoordinates.FromRelease(extra) }] }
                    : metadata;
                File.WriteAllText(inventory, JsonHelpers.Serialize(legacy));
                if (fault == "inventory-pending") File.WriteAllText(inventory + ".tmp", "uncertain");
            }
            var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
            var error = Record.Exception(() => Manager(registry, []));
            Assert.True(error is InvalidDataException or IOException, error?.ToString() ?? "No error");
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
            return Task.CompletedTask;
        });
    }

    public static IEnumerable<object[]> InvalidInventories() {
        yield return ["{"];
        yield return ["null"];
        foreach (var json in new[] { "{}", "{\"ManagedPaks\":null}", "{\"ManagedPaks\":[null]}" })
            yield return [json];
        var valid = new ManagedPak(new ReleaseCoordinates("org", "mod", "v1.0.0"), "owned.pak", 0);
        foreach (var field in new[] { "Coordinates", "PakFileName", "Org", "ModuleName", "Version" }) {
            foreach (var missing in new[] { false, true }) {
                var entry = System.Text.Json.Nodes.JsonNode.Parse(JsonHelpers.Serialize(valid))!;
                var parent = field is "Org" or "ModuleName" or "Version" ? entry["Coordinates"]! : entry;
                if (missing) parent.AsObject().Remove(field);
                else parent[field] = null;
                yield return [$"{{\"ManagedPaks\":[{entry}]}}"];
            }
        }
        var invalid = valid with { PakFileName = "../outside.pak" };
        yield return [JsonHelpers.Serialize(new PakDirMetadata("unused", [invalid]))];
    }

    [Theory]
    [MemberData(nameof(InvalidInventories))]
    public async Task InvalidInventoryCancelsBeforeInstallationWithoutChangingFiles(string inventoryJson) {
        await InInstallation(async (registry, common, extra, launch) => {
            var owned = new ManagedPak(ReleaseCoordinates.FromRelease(common), "owned.pak", 0);
            var paks = Path.GetFullPath(FilePaths.PakDir);
            File.Copy(Path.Combine(_root, "payload.pak"), Path.Combine(paks, owned.PakFileName));
            File.WriteAllText(Path.Combine(paks, "owned.sig"), "owned signature");
            var manager = new ModManager(registry, new PakDir(paks, [owned]), []);
            var inventoryPath = Path.Combine(manager.PakDir.DirPath, FilePaths.ManagedPakInventoryFileName);
            try {
                if (System.Text.Json.Nodes.JsonNode.Parse(inventoryJson) is System.Text.Json.Nodes.JsonObject document) {
                    document["DirPath"] = paks;
                    inventoryJson = document.ToJsonString();
                }
            }
            catch (System.Text.Json.JsonException) { }
            File.WriteAllText(inventoryPath, inventoryJson);
            var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            Assert.Throws<InvalidDataException>(() => Manager(registry, []));
            Assert.Equal(owned, Assert.Single(((PakDir)manager.PakDir).ManagedPaks));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public async Task EmptyInventoryIsAcceptedWithoutAdoptingUnmanagedFiles() {
        await InInstallation(async (registry, common, extra, launch) => {
            var manager = Manager(registry, []);
            var inventoryPath = Path.Combine(manager.PakDir.DirPath, FilePaths.ManagedPakInventoryFileName);
            File.WriteAllText(inventoryPath, JsonHelpers.Serialize(new PakDirMetadata(manager.PakDir.DirPath, [])));
            var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .Where(path => path != inventoryPath).ToDictionary(path => path, File.ReadAllBytes);
            manager = Manager(registry, []);
            await Prepare(manager, launch);
            AssertOnlyCommonAndUnknown(manager, common);
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        });
    }

    private sealed class KeepRecoveryDialogue : IUserDialogueSpawner {
        public void DisplayMessage(string message) { }
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) => UserDialogueChoice.Yes;
        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => UserDialogueChoice.Yes;
        public Action DisplayProgress(MemoryProgress progress) => () => { };
    }

    private sealed class RejectingDialogue : IUserDialogueSpawner {
        public List<string> Messages { get; } = [];
        public void DisplayMessage(string message) => Messages.Add(message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) => throw new InvalidOperationException("Unexpected repair prompt.");
        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => throw new InvalidOperationException("Unexpected update prompt.");
        public Action DisplayProgress(MemoryProgress progress) => throw new InvalidOperationException("Installation must not start.");
    }

    private sealed class RejectingProcessLauncher : IProcessLauncher {
        public string Executable => "test-game.exe";
        public int Calls { get; private set; }
        public LanguageExt.Either<LaunchFailed, System.Diagnostics.Process> Launch(string workingDirectory, string args) {
            Calls++;
            throw new InvalidOperationException("Process launch must not occur.");
        }
    }

    [Fact]
    public async Task ServerInstancesInstallDifferentPakSetsWithoutChangingBaseOrPeerFiles() {
        await InInstallation(async (source, common, extra, _) => {
            var registry = new AggregateModRegistry(source);
            var first = Assert.IsType<ServerOptions>(CliLaunchOptions.Parse([
                "server", "--chivalry2-path", _root, "--platform", "Steam", "--saved-dir-suffix", "One"]));
            var second = Assert.IsType<ServerOptions>(CliLaunchOptions.Parse([
                "server", "--chivalry2-path", _root, "--platform", "Steam", "--saved-dir-suffix", "Two",
                "--mod", $"{extra.Info.Organization}/{extra.Info.RepoName}:{extra.Tag}"]));
            var baseFiles = Directory.GetFiles(Path.GetFullPath(FilePaths.PakDir))
                .ToDictionary(path => path, File.ReadAllBytes);
            first.Shared.Assets = new ProfileAssetPaths(first.Shared.SavedDirSuffix, _root);
            second.Shared.Assets = new ProfileAssetPaths(second.Shared.SavedDirSuffix, _root);
            var firstManager = ServerLauncher.CreateModManager(first, registry);
            await Prepare(firstManager, first.ToLaunchOptions());
            var firstFiles = Directory.GetFiles(first.InstancePaths.Paks).ToDictionary(path => path, File.ReadAllBytes);
            var secondManager = ServerLauncher.CreateModManager(second, registry);
            await Prepare(secondManager, second.ToLaunchOptions());

            Assert.Single(((PakDir)firstManager.PakDir).ManagedPaks);
            Assert.Equal(2, ((PakDir)secondManager.PakDir).ManagedPaks.Count());
            Assert.NotEqual(first.InstancePaths.Paks, second.InstancePaths.Paks);
            Assert.Equal(baseFiles.Keys.Order(), Directory.GetFiles(Path.GetFullPath(FilePaths.PakDir)).Order());
            foreach (var (path, bytes) in baseFiles.Concat(firstFiles)) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.True(File.Exists(first.InstancePaths.ModMetadata));
            Assert.True(File.Exists(second.InstancePaths.ModMetadata));
            Assert.False(File.Exists(Path.Combine(first.InstancePaths.Paks, FilePaths.ManagedPakInventoryFileName)));
            Assert.False(File.Exists(Path.Combine(second.InstancePaths.Paks, FilePaths.ManagedPakInventoryFileName)));

            // Host metadata retains the external signature template when reloaded.
            var codec = new ModManagerCodec(registry);
            codec.SerializeFile(first.InstancePaths.ModMetadata, firstManager);
            var restored = codec.Deserialize(File.ReadAllText(first.InstancePaths.ModMetadata));
            Assert.True(restored.Success);
            var restoredPaks = Assert.IsType<PakDir>(restored.Result!.PakDir);
            Assert.Equal(Path.Combine(Path.GetFullPath(FilePaths.PakDir), "pakchunk0-WindowsNoEditor.sig"),
                Path.GetFullPath(restoredPaks.DefaultSigPath!));
            var managed = Assert.Single(restoredPaks.ManagedPaks);
            File.Delete(Path.ChangeExtension(Path.Combine(restoredPaks.DirPath, managed.PakFileName), ".sig"));
            Assert.True((await restoredPaks.SignAll()).IsRight);
        });
    }

    [Theory]
    [InlineData("client")]
    [InlineData("server")]
    public async Task RealProfileReloadsOwnershipAndMetadataButUsesOnlyCurrentRequestSelection(string command) {
        await InInstallation(async (source, common, extra, _) => {
            var registry = new AggregateModRegistry(source);
            CliLaunchOptions Request(bool selected) {
                var arguments = new List<string> { command, "--chivalry2-path", _root, "--platform", "Steam", "--saved-dir-suffix", "Reload" };
                if (selected) arguments.AddRange(["--mod", $"{extra.Info.Organization}/{extra.Info.RepoName}:{extra.Tag}"]);
                var request = CliLaunchOptions.Parse(arguments.ToArray());
                request.Shared.Assets = new ProfileAssetPaths("Reload", _root);
                return request;
            }
            var first = Request(true);
            using var lease = first.InstancePaths.AcquireLease();
            var manager = ServerLauncher.CreateModManager(first, registry);
            await Prepare(manager, first.ToLaunchOptions());
            var owned = ((PakDir)manager.PakDir).ManagedPaks.ToArray();
            Assert.Equal(2, owned.Length);
            var next = Request(false);
            var restored = ServerLauncher.CreateModManager(next, registry);
            Assert.Equal(owned, ((PakDir)restored.PakDir).ManagedPaks);
            Assert.Equal(JsonHelpers.Serialize(manager.Mods), JsonHelpers.Serialize(restored.Mods));
            Assert.Empty(restored.EnabledModReleaseCoordinates);
            await Prepare(restored, next.ToLaunchOptions());
            Assert.Equal(ReleaseCoordinates.FromRelease(common), Assert.Single(((PakDir)restored.PakDir).ManagedPaks).Coordinates);
            Assert.Single(Directory.GetFiles(next.InstancePaths.Paks, "*.pak"));
            Assert.Single(Directory.GetFiles(next.InstancePaths.Paks, "*.sig"));
            Assert.False(File.Exists(Path.Combine(next.InstancePaths.Paks, FilePaths.ManagedPakInventoryFileName)));
        });
    }

    [Fact]
    public async Task SharedOwnershipPreservesUnmanagedCollisionAndUpdatesPatchOrders() {
        await InInstallation(async (registry, common, extra, launch) => {
            var paks = Path.GetFullPath(FilePaths.PakDir);
            var occupied = Path.Combine(paks, "Unchained-Mods_0_P.pak");
            File.WriteAllText(occupied, "user collision");
            var manager = Manager(registry, []);
            await Prepare(manager, launch);
            var original = Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
            Assert.Equal("Unchained-Mods (1)_0_P.pak", original.PakFileName);
            Assert.True(manager.EnableModRelease(ReleaseCoordinates.FromRelease(extra)));
            await Prepare(manager, launch);

            var owned = ((PakDir)manager.PakDir).ManagedPaks.ToArray();
            var expectedOrder = manager.SortByDependencies(manager.GetEnabledAndDependencyReleases())
                .Select(ReleaseCoordinates.FromRelease).ToArray();
            Assert.Equal(expectedOrder, owned.OrderBy(pak => pak.Priority)
                .Select(pak => pak.Coordinates));
            Assert.True(File.Exists(Path.Combine(paks, original.PakFileName)));
            Assert.Equal(original.PakFileName, Assert.Single(owned, pak => pak.Coordinates == original.Coordinates).PakFileName);
            Assert.Equal("user collision", File.ReadAllText(occupied));
            Assert.Equal(2, owned.Length);
            Assert.All(owned, pak => Assert.True(File.Exists(Path.ChangeExtension(Path.Combine(paks, pak.PakFileName), ".sig"))));
            Assert.All(owned, pak => Assert.EndsWith($"_{pak.Priority}_P.pak", pak.PakFileName));

            await Prepare(manager, launch);
            Assert.Equal(owned, ((PakDir)manager.PakDir).ManagedPaks);
            Assert.Equal(owned, ((PakDir)new ModManagerCodec(registry).Deserialize(File.ReadAllText(OwnerConfig)).Result!.PakDir).ManagedPaks);
            Assert.Equal("user collision", File.ReadAllText(occupied));
        });
    }

    [Fact]
    public async Task FailedDuplicateRemovalPersistsOwnershipAndCanBeRetried() {
        await InInstallation(async (registry, common, extra, launch) => {
            var paks = Path.GetFullPath(FilePaths.PakDir);
            var coords = ReleaseCoordinates.FromRelease(common);
            var first = new ManagedPak(coords, "first.pak", 0);
            var duplicate = new ManagedPak(coords, "duplicate.pak", 1);
            foreach (var pak in new[] { first, duplicate })
                File.Copy(Path.Combine(_root, "payload.pak"), Path.Combine(paks, pak.PakFileName));
            // A directory at the signature path deterministically rejects File.Delete
            // on both Windows and Linux, unlike a platform-dependent file lock.
            var blocked = Path.Combine(paks, "duplicate.sig");
            Directory.CreateDirectory(blocked);
            var manager = new ModManager(registry, new PakDir(paks, [first, duplicate]), []);
            new ModManagerPersistence(manager, OwnerConfig).Save();
            Assert.False((await manager.UpdateModsList()).HasErrors);
            Assert.True((await new Chivalry2ModsInstaller(manager, new ConsoleDialogue()).PrepareLaunch(launch)).IsNone);
            var persisted = new ModManagerCodec(registry).Deserialize(File.ReadAllText(OwnerConfig));
            Assert.True(persisted.Success);
            Assert.Equal(2, ((PakDir)persisted.Result!.PakDir).ManagedPaks.Count());
            Assert.True(File.Exists(Path.Combine(paks, first.PakFileName)));
            Assert.True(File.Exists(Path.Combine(paks, duplicate.PakFileName)));
            Directory.Delete(blocked);
            var retry = Manager(registry, []);
            await Prepare(retry, launch);
            AssertOnlyCommonAndUnknown(retry, common);
        });
    }

    private static async Task Prepare(ModManager manager, LaunchOptions launch) {
        Assert.False((await manager.UpdateModsList()).HasErrors);
        ServerLauncher.ValidateReleases(manager, manager.EnabledModReleaseCoordinates);
        var dialogue = new ConsoleDialogue();
        Assert.True((await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(launch)).IsSome);
        Assert.True((await SigPreparer.Create(manager.PakDir, dialogue).PrepareLaunch(LanguageExt.Unit.Default)).IsSome);
    }

    private static void AssertOnlyCommonAndUnknown(ModManager manager, Release common) {
        var pakDir = (PakDir)manager.PakDir;
        var owned = Assert.Single(pakDir.ManagedPaks);
        Assert.Equal(ReleaseCoordinates.FromRelease(common), owned.Coordinates);
        Assert.EndsWith("_0_P.pak", owned.PakFileName);
        Assert.Equal(3, Directory.GetFiles(pakDir.DirPath, "*.pak").Length); // base, common, unknown
        Assert.Equal("user payload", File.ReadAllText(Path.Combine(pakDir.DirPath, "user.pak")));
        Assert.Equal("base payload", File.ReadAllText(Path.Combine(pakDir.DirPath, "pakchunk0-WindowsNoEditor.pak")));
        Assert.True(File.Exists(Path.ChangeExtension(Path.Combine(pakDir.DirPath, owned.PakFileName), ".sig")));
        Assert.False(File.Exists(Path.Combine(pakDir.DirPath, FilePaths.ManagedPakInventoryFileName)));
    }

    private async Task InInstallation(Func<LocalModRegistry, Release, Release, LaunchOptions, Task> action) {
        var request = CliLaunchOptions.Parse(["server", "--chivalry2-path", _root, "--platform", "Steam"]);
        await ServerLauncher.InInstallationDirectoryAsync(request, async () => {
            Directory.CreateDirectory(FilePaths.PakDir);
            File.WriteAllText(Path.Combine(FilePaths.PakDir, "pakchunk0-WindowsNoEditor.pak"), "base payload");
            File.WriteAllText(Path.Combine(FilePaths.PakDir, "pakchunk0-WindowsNoEditor.sig"), "base signature");
            File.WriteAllText(Path.Combine(FilePaths.PakDir, "user.pak"), "user payload");
            var source = Path.Combine(_root, "payload.pak");
            File.WriteAllText(source, "managed payload");
            var hash = FileHelpers.Sha512(source).Match(Right: h => h, Left: e => throw new Exception(e.Message));
            Release Release(string org, string mod) => new("v1.0.0", hash, "test.pak", DateTime.UtcNow,
                new ModInfo($"https://github.com/{org}/{mod}", mod, "test", null, null, [], [], []),
                new AssetCollections([], [], [], [], []), null);
            var common = Release("Chiv2-Community", "Unchained-Mods");
            var extra = Release("org", "GuiOnlyMod");
            var registry = new LocalModRegistry(Path.Combine(_root, "registry"));
            await registry.AddRelease(common, source);
            await registry.AddRelease(extra, source);
            await action(registry, common, extra, request.ToLaunchOptions());
            foreach (var path in Directory.EnumerateFiles(registry.RegistryPath, "*.pak", SearchOption.AllDirectories)) {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            return 0;
        });
    }
}