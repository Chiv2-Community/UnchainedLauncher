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
        var root = Scratch("gui-rebind-");
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
        var root = Scratch("gui-profile-");
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

    [Theory]
    [InlineData("")]
    [InlineData("---")]
    [InlineData("../other")]
    public async Task InvalidExplicitSuffixCancelsBeforeCreatingProfileAssets(string suffix) {
        var root = Scratch("gui-invalid-profile-");
        try {
            var registry = new LocalModRegistry(Path.Combine(root, "registry"));
            var manager = new ModManager(registry, new PakDir(Path.Combine(root, "source"), []), []);
            var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();
            var dialogue = new Dialogue();
            var result = await new ProfileModsLaunchPreparer(manager, dialogue, root, root).PrepareLaunch(Options(suffix));
            Assert.True(result.IsNone);
            Assert.Contains("Invalid saved directory suffix", Assert.Single(dialogue.Messages));
            Assert.Equal(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HeldCliLeaseCancelsGuiPreparationWithoutReconcilingOwnedFiles() {
        var root = Scratch("gui-cli-lease-");
        try {
            var assets = new ProfileAssetPaths("unchained", root);
            var coordinates = new ReleaseCoordinates("org", "owned", "v1.0.0");
            var owned = new ManagedPak(coordinates, "owned_0_P.pak", 0);
            var manager = new ModManager(new LocalModRegistry(Path.Combine(root, "registry")), assets.CreatePakDir(root), []);
            var payload = Path.Combine(assets.Paks, owned.PakFileName);
            File.WriteAllText(payload, "owned payload");
            File.WriteAllText(Path.ChangeExtension(payload, ".sig"), "owned signature");
            File.WriteAllText(Path.Combine(assets.Paks, FilePaths.ManagedPakInventoryFileName),
                JsonHelpers.Serialize(new PakDirMetadata(assets.Paks, [owned])));
            // Exact CLI convention, independently opened rather than a second test-only lock.
            Directory.CreateDirectory(assets.Root);
            using var lease = new FileStream(Path.Combine(assets.Root, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".instance.lock")).ToDictionary(path => path, File.ReadAllText);
            var dialogue = new Dialogue();
            Assert.True((await new ProfileModsLaunchPreparer(manager, dialogue, root, root).PrepareLaunch(Options(null))).IsNone);
            Assert.Contains("another launcher", Assert.Single(dialogue.Messages));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".instance.lock")).Order());
            foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllText(pair.Key));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PendingGuiTransactionRejectsSameProfileButAllowsAnotherProfile() {
        var root = Scratch("gui-concurrent-profile-");
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            var defaultPaks = Path.Combine(root, FilePaths.PakDir);
            Directory.CreateDirectory(defaultPaks);
            File.WriteAllText(Path.Combine(defaultPaks, "pakchunk0-WindowsNoEditor.sig"), "signature");
            var payload = Path.Combine(root, "source.pak");
            File.WriteAllText(payload, "payload");
            var hash = FileHelpers.Sha512(payload).Match(Right: value => value, Left: e => throw new Exception(e.Message));
            var release = new Release("v1.0.0", hash, "mod.pak", DateTime.UnixEpoch,
                new ModInfo("https://github.com/org/mod", "mod", "test", null, null, [], [], []),
                new AssetCollections([], [], [], [], []), null);
            var local = new LocalModRegistry(Path.Combine(root, "registry"));
            await local.AddRelease(release, payload);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var registry = new PausedRegistry(local, started, finish);
            var source = new ProfileAssetPaths("Source", root);
            var manager = new ModManager(registry, source.CreatePakDir(root), [ReleaseCoordinates.FromRelease(release)]);
            Assert.False((await manager.UpdateModsList()).HasErrors);
            var firstDialogue = new Dialogue();
            var preparer = new ProfileModsLaunchPreparer(manager, firstDialogue, root, root);
            var first = preparer.PrepareLaunch(Options("First"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var firstAssets = new ProfileAssetPaths("First", root);
            var before = Directory.GetFiles(firstAssets.Paks).ToDictionary(path => path, File.ReadAllBytes);
            Assert.True((await preparer.PrepareLaunch(Options("First")).WaitAsync(TimeSpan.FromSeconds(10))).IsNone);
            Assert.Contains("another launcher", Assert.Single(firstDialogue.Messages));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(firstAssets.Paks).Order());
            foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            // An empty selection avoids waiting on the intentionally shared immutable cache object.
            var otherManager = new ModManager(local, source.CreatePakDir(root), []);
            var otherDialogue = new Dialogue();
            Assert.True((await new ProfileModsLaunchPreparer(otherManager, otherDialogue, root, root)
                .PrepareLaunch(Options("Other"))).IsSome);
            Assert.False(first.IsCompleted);
            Assert.Empty(otherDialogue.Messages);
            finish.SetResult();
            Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(10))).IsSome);
            Assert.Single(ReadInventory(firstAssets.Paks));
            Assert.True((await preparer.PrepareLaunch(Options("First"))).IsSome);
        }
        finally { finish.TrySetResult(); Directory.Delete(root, true); }
    }

    private static string Scratch(string prefix) => Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ??
        (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), prefix + Guid.NewGuid().ToString("N"));

    private static LaunchOptions Options(string? suffix) => new([], Option<string>.None, "", false, false,
        suffix is null ? Option<string>.None : Option<string>.Some(suffix), Option<ServerLaunchOptions>.None);

    private sealed class PausedRegistry(IModRegistry inner, TaskCompletionSource started, TaskCompletionSource finish) : IModRegistry {
        public string Name => inner.Name;
        public Task<GetAllModsResult> GetAllMods() => inner.GetAllMods();
        public EitherAsync<RegistryMetadataException, Mod> GetMod(ModIdentifier id) => inner.GetMod(id);
        public EitherAsync<RegistryMetadataException, Release> GetModRelease(ReleaseCoordinates coordinates) => inner.GetModRelease(coordinates);
        public Task<long?> GetPakSize(Release release, CancellationToken cancellationToken = default) => inner.GetPakSize(release, cancellationToken);
        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation) =>
            Download(coordinates, outputLocation).ToAsync();
        private async Task<Either<ModPakStreamAcquisitionFailure, FileWriter>> Download(ReleaseCoordinates coordinates, string outputLocation) {
            started.TrySetResult();
            await finish.Task;
            return await inner.DownloadPak(coordinates, outputLocation).ToEither();
        }
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
