using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
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
    public async Task SequentialCliGuiCliSharesOwnershipButNotSelectedReleases(bool server) {
        await InInstallation(async (registry, common, extra, launch) => {
            if (!server) launch = launch with { ServerLaunchOptions = LanguageExt.Option<ServerLaunchOptions>.None };
            var codec = new ModManagerCodec(registry);
            var cliCache = Path.Combine(FilePaths.ModCachePath, "cli_mod_manager.json");
            var cli = Manager(registry, []);
            await Prepare(cli, launch);
            codec.SerializeFile(cliCache, cli);

            // GUI has its own selection and may reorder the shared common pak.
            var selected = ReleaseCoordinates.FromRelease(extra);
            var gui = Manager(registry, [selected]);
            await Prepare(gui, launch);
            codec.SerializeFile(Path.GetFullPath(FilePaths.ModManagerConfigPath), gui);
            var guiConfiguration = File.ReadAllText(FilePaths.ModManagerConfigPath);
            var oldExtra = gui.PakDir.GetManagedPakFilePath(selected).IfNone("");
            Assert.True(File.Exists(oldExtra));

            var secondCli = Manager(registry, []);
            await Prepare(secondCli, launch);
            codec.SerializeFile(cliCache, secondCli);
            Assert.False(File.Exists(oldExtra));
            Assert.False(File.Exists(Path.ChangeExtension(oldExtra, ".sig")));
            AssertOnlyCommonAndUnknown(secondCli, common);
            Assert.Equal(guiConfiguration, File.ReadAllText(FilePaths.ModManagerConfigPath));

            // The saved GUI snapshot now names obsolete files. Shared ownership wins,
            // while GUI's selected release survives and is installed on its next launch.
            var decoded = codec.Deserialize(guiConfiguration);
            Assert.True(decoded.Success);
            gui = decoded.Result!;
            Assert.Equal(selected, Assert.Single(gui.EnabledModReleaseCoordinates));
            await Prepare(gui, launch);
            Assert.Equal(2, ((PakDir)gui.PakDir).ManagedPaks.Count());
            await Prepare(secondCli, launch); // also reconcile an already-created manager
            AssertOnlyCommonAndUnknown(secondCli, common);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LegacyCachesMigrateOwnershipAndRemoveDuplicateCommonPaks(bool guiCache, bool cliCache) {
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
            AssertOnlyCommonAndUnknown(manager, common);
            foreach (var pak in new[] { oldCommon, newCommon, guiOnly }) {
                Assert.False(File.Exists(Path.Combine(paks, pak.PakFileName)));
                Assert.False(File.Exists(Path.Combine(paks, Path.ChangeExtension(pak.PakFileName, ".sig"))));
            }
            if (guiCache) Assert.Equal(beforeGui, File.ReadAllText(FilePaths.ModManagerConfigPath));
            // Obsolete caches must not override the post-migration shared inventory.
            await Prepare(Manager(registry, []), launch);
            AssertOnlyCommonAndUnknown(manager, common);
        });
    }

    private ModManager Manager(IModRegistry registry, ReleaseCoordinates[] selected) =>
        new(registry, new PakDir(Path.GetFullPath(FilePaths.PakDir), []), selected);

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
            Assert.False((await manager.UpdateModsList()).HasErrors);
            Assert.True((await new Chivalry2ModsInstaller(manager, new ConsoleDialogue()).PrepareLaunch(launch)).IsNone);
            var persisted = JsonHelpers.Deserialize<PakDirMetadata>(File.ReadAllText(Path.Combine(paks, FilePaths.ManagedPakInventoryFileName)));
            Assert.True(persisted.Success);
            Assert.Equal(2, persisted.Result!.ManagedPaks.Count());
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
        Assert.Equal(3, Directory.GetFiles(pakDir.DirPath, "*.pak").Length); // base, common, unknown
        Assert.Equal("user payload", File.ReadAllText(Path.Combine(pakDir.DirPath, "user.pak")));
        Assert.Equal("base payload", File.ReadAllText(Path.Combine(pakDir.DirPath, "pakchunk0-WindowsNoEditor.pak")));
        Assert.True(File.Exists(Path.ChangeExtension(Path.Combine(pakDir.DirPath, owned.PakFileName), ".sig")));
        var inventory = JsonHelpers.Deserialize<PakDirMetadata>(File.ReadAllText(Path.Combine(pakDir.DirPath, FilePaths.ManagedPakInventoryFileName)));
        Assert.True(inventory.Success);
        Assert.Equal(owned, Assert.Single(inventory.Result!.ManagedPaks));
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