using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using System.Diagnostics;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class ProfileModsLaunchPreparerTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RebindingCopiesOwnershipOnlyWithinTheSameProfileAndSnapshotsSelection(bool sameProfile) {
        var root = Path.Combine(Path.GetTempPath(), "gui-rebind-" + Guid.NewGuid().ToString("N"));
        try {
            var source = new ProfileAssetPaths("Source", root);
            var target = sameProfile ? new ProfileAssetPaths("Source", root) : new ProfileAssetPaths("Target", root);
            Directory.CreateDirectory(source.Paks);
            Directory.CreateDirectory(target.Paks);
            var coordinates = new ReleaseCoordinates("org", "mod", "v1.0.0");
            var owned = new ManagedPak(coordinates, "mod_0_P.pak", 0);
            File.WriteAllText(Path.Combine(source.Paks, owned.PakFileName), "source");
            if (!sameProfile) File.WriteAllText(Path.Combine(target.Paks, owned.PakFileName), "unknown target payload");
            var manager = new ModManager(new LocalModRegistry(Path.Combine(root, "registry")),
                source.CreatePakDir(root, [owned]), [coordinates]);
            var rebound = target.Rebind(manager, root);
            Assert.NotSame(manager, rebound);
            Assert.NotSame(manager.PakDir, rebound.PakDir);
            Assert.Same(manager.ModRegistry, rebound.ModRegistry);
            Assert.Equal(new[] { coordinates }, rebound.EnabledModReleaseCoordinates);
            Assert.Equal(sameProfile ? new[] { owned } : [], Assert.IsType<PakDir>(rebound.PakDir).ManagedPaks);
            Assert.Single(Assert.IsType<PakDir>(manager.PakDir).ManagedPaks);
            Assert.IsType<List<ReleaseCoordinates>>(manager.EnabledModReleaseCoordinates).Clear();
            Assert.Single(rebound.EnabledModReleaseCoordinates);
            if (!sameProfile) Assert.Equal("unknown target payload", File.ReadAllText(Path.Combine(target.Paks, owned.PakFileName)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null, "unchained")]
    [InlineData("Courtyard", "Courtyard")]
    [InlineData("Court_yard_1", "Court_yard_1")]
    [InlineData("Court-yard", "Courtyard")]
    public async Task LaunchSuffixSelectsInstalledAndSignedPaks(string? suffix, string expectedSuffix) {
        var root = Path.Combine(Path.GetTempPath(), "gui-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var installation = Path.Combine(root, "game");
            var defaultPaks = Path.Combine(installation, FilePaths.PakDir);
            Directory.CreateDirectory(defaultPaks);
            File.WriteAllText(Path.Combine(defaultPaks, "pakchunk0-WindowsNoEditor.sig"), "base signature");
            var payload = Path.Combine(root, "source.pak");
            File.WriteAllText(payload, "approved payload");
            var hash = FileHelpers.Sha512(payload).Match(Right: value => value, Left: e => throw new Exception(e.Message));
            Release Release(string name, params Dependency[] dependencies) => new("v1.0.0", hash, "mod.pak", DateTime.UnixEpoch,
                new ModInfo($"https://github.com/org/{name}", name, "test", null, null, [], [], dependencies.ToList()),
                new AssetCollections([], [], [], [], []), null);
            var dependency = Release("Dependency");
            var selected = Release("Selected", new Dependency("https://github.com/org/Dependency", "v1.0.0"));
            var unselected = Release("Unselected");
            var registry = new LocalModRegistry(Path.Combine(root, "registry"));
            foreach (var release in new[] { dependency, selected, unselected }) await registry.AddRelease(release, payload);
            var shared = new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix, root);
            var manager = new ModManager(registry, shared.CreatePakDir(installation), [ReleaseCoordinates.FromRelease(selected)]);
            Assert.False((await manager.UpdateModsList()).HasErrors);
            var dialogue = new Dialogue();
            var preparer = new ProfileModsLaunchPreparer(manager, dialogue, installation, root);
            var server = new ServerLaunchOptions(true, "Courtyard", "", false, Option<string>.None, "/Game/Maps/Courtyard",
                7777, 7778, 7779, 7780, null, null, null, null, null, null, Option<string>.None, [],
                Option<DiscordIntegrationLaunchOptions>.None, false, false, CensorArg.None);
            var options = new LaunchOptions(manager.EnabledModReleaseCoordinates, Option<string>.None, "", false, false,
                suffix is null ? Option<string>.None : Option<string>.Some(suffix),
                suffix is null ? Option<ServerLaunchOptions>.None : Option<ServerLaunchOptions>.Some(server));

            var result = await preparer.PrepareLaunch(options);
            Assert.True(result.IsSome);
            var prepared = result.ValueUnsafe();
            Assert.Equal(options.ServerLaunchOptions, prepared.ServerLaunchOptions);
            Assert.Equal(expectedSuffix, prepared.SavedDirSuffix.ValueUnsafe());
            Assert.Contains(prepared.ToCLIArgs(), arg => arg.Rendered == "-saveddirsuffix=" + expectedSuffix);
            var process = new CapturingProcessLauncher();
            var launcher = new UnchainedChivalry2Launcher(preparer, process, installation, new NullInjector());
            Assert.True((await launcher.Launch(options)).IsLeft); // Stop at the OS process boundary.
            Assert.Contains("-saveddirsuffix=" + expectedSuffix, process.Arguments);
            Assert.DoesNotContain("--additional-pak-dir", process.Arguments);
            var paks = Path.Combine(root, "Chivalry 2", "Saved_" + expectedSuffix, "Paks");
            var inventory = ReadInventory(paks);
            Assert.Equal(2, inventory.Length);
            Assert.Contains(inventory, pak => pak.Coordinates == ReleaseCoordinates.FromRelease(selected));
            Assert.Contains(inventory, pak => pak.Coordinates == ReleaseCoordinates.FromRelease(dependency));
            foreach (var pak in inventory) {
                Assert.Equal("approved payload", File.ReadAllText(Path.Combine(paks, pak.PakFileName)));
                Assert.Equal("base signature", File.ReadAllText(Path.ChangeExtension(Path.Combine(paks, pak.PakFileName), ".sig")));
                Assert.EndsWith($"_{pak.Priority}_P.pak", pak.PakFileName);
            }
            if (expectedSuffix != ProfileAssetPaths.SharedSuffix) Assert.Empty(Directory.GetFiles(shared.Paks, "*.pak"));
            Assert.False(Directory.Exists(new ProfileAssetPaths(expectedSuffix, root).Plugins));

            // The next launch must use current GUI selections and reconcile this profile's ownership.
            Assert.True(manager.DisableModRelease(ReleaseCoordinates.FromRelease(selected)));
            Assert.True(manager.EnableModRelease(ReleaseCoordinates.FromRelease(unselected)));
            Assert.True((await preparer.PrepareLaunch(options with { EnabledReleases = manager.EnabledModReleaseCoordinates })).IsSome);
            Assert.Equal(ReleaseCoordinates.FromRelease(unselected), Assert.Single(ReadInventory(paks)).Coordinates);
            Assert.Single(Directory.GetFiles(paks, "*.pak"));
            Assert.Single(Directory.GetFiles(paks, "*.sig"));
            Assert.Equal("approved payload", File.ReadAllText(Path.Combine(shared.PakCache, hash.ToLowerInvariant(), "mod.pak")));
            Assert.Empty(Directory.GetFiles(shared.PakCache, "*.sig", SearchOption.AllDirectories));
            Assert.Empty(dialogue.Messages);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ManagedPak[] ReadInventory(string paks) {
        var result = JsonHelpers.Deserialize<PakDirMetadata>(File.ReadAllText(Path.Combine(paks, FilePaths.ManagedPakInventoryFileName)));
        Assert.True(result.Success);
        return result.Result!.ManagedPaks.ToArray();
    }

    private sealed class Dialogue : IUserDialogueSpawner {
        public List<string> Messages { get; } = [];
        public void DisplayMessage(string message) => Messages.Add(message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) => throw new NotSupportedException();
        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => throw new NotSupportedException();
        public Action DisplayProgress(MemoryProgress progress) => () => { };
    }

    private sealed class CapturingProcessLauncher : IProcessLauncher {
        public string Executable => "test-game.exe";
        public string Arguments { get; private set; } = "";
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args) {
            Arguments = args;
            return new LaunchFailed(Executable, args, LanguageExt.Common.Error.New("Stopped at the OS boundary."));
        }
    }
}
