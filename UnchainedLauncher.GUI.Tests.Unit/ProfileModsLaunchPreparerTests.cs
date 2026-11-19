using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using System.Diagnostics;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class ProfileModsLaunchPreparerTests {
    [Fact]
    public async Task VanillaLaunchOnlyUnsignsInstallationEvenWhileProfileIsLeased() {
        var root = Scratch("gui-vanilla-boundary-");
        try {
            var installationPaks = Path.Combine(root, FilePaths.PakDir);
            Directory.CreateDirectory(installationPaks);
            var baseSig = Path.Combine(installationPaks, "pakchunk0-WindowsNoEditor.sig");
            var legacySig = Path.Combine(installationPaks, "legacy.sig");
            File.WriteAllText(baseSig, "base signature");
            File.WriteAllText(legacySig, "legacy signature");
            File.WriteAllText(Path.ChangeExtension(legacySig, ".pak"), "legacy payload");
            var assets = new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix, root);
            Directory.CreateDirectory(assets.Paks);
            File.WriteAllText(Path.Combine(assets.Paks, "unknown.sig"), "profile signature");
            File.WriteAllText(Path.Combine(assets.Paks, "unknown.pak"), "profile payload");
            File.WriteAllText(Path.Combine(assets.Paks, FilePaths.ManagedPakInventoryFileName), "profile inventory");
            var before = Directory.GetFiles(assets.Paks).ToDictionary(path => path, File.ReadAllBytes);
            using var held = assets.AcquireLease();
            var dialogue = new Dialogue();
            var process = new CapturingProcessLauncher();
            var launcher = new Chivalry2Launcher(NoSigPreparer.CreateForInstallation(root, dialogue)
                .IgnoreOptions<LaunchOptions>(), process, root);

            Assert.True((await launcher.Launch(Options(null))).IsLeft);
            Assert.Equal(1, process.Calls);
            Assert.False(File.Exists(legacySig));
            Assert.Equal("legacy payload", File.ReadAllText(Path.ChangeExtension(legacySig, ".pak")));
            Assert.Equal("base signature", File.ReadAllText(baseSig));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(assets.Paks).Order());
            foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            Assert.Empty(dialogue.Messages);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("AAA", true)]
    [InlineData("AAA", false)]
    [InlineData("zzz", true)]
    [InlineData("zzz", false)]
    public async Task CombinedPipelineLocksBothProfilesBeforeEnteringRealDllPreparer(string? suffix, bool holdShared) {
        var root = Scratch("gui-combined-held-");
        try {
            var shared = new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix, root);
            var selected = new ProfileAssetPaths(suffix ?? ProfileAssetPaths.SharedSuffix, root);
            var manager = new ModManager(new LocalModRegistry(Path.Combine(root, "registry")), shared.CreatePakDir(root), []);
            Directory.CreateDirectory(selected.Paks);
            File.WriteAllText(Path.Combine(selected.Paks, "unknown.sig"), "profile signature");
            var dialogue = new Dialogue();
            using var http = new HttpClient();
            var (dll, installed) = CachedDll(shared, http);
            var entered = 0;
            var preceding = new CallbackPreparer(async options => {
                entered++;
                return await new DllLaunchPreparer(dll, dialogue).PrepareLaunch(options);
            });
            var preparer = new ProfileModsLaunchPreparer(manager, dialogue, root, root, preceding);
            var heldAssets = holdShared ? shared : selected;
            using (heldAssets.AcquireLease()) {
                var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => !path.EndsWith(".instance.lock")).ToDictionary(path => path, File.ReadAllBytes);
                var process = new CapturingProcessLauncher();
                var launcher = new UnchainedChivalry2Launcher(preparer, process, root, new NullInjector());
                Assert.True((await launcher.Launch(Options(suffix))).IsLeft);
                Assert.Equal(0, process.Calls);
                Assert.Equal(0, entered);
                Assert.False(File.Exists(installed));
                Assert.Contains("another launcher", Assert.Single(dialogue.Messages));
                Assert.Equal(before.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => !path.EndsWith(".instance.lock")).Order());
                foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
                // The lower-sorted profile is acquired first, regardless of which one holds DLLs.
                if (shared.Root != selected.Root) {
                    var first = new[] { shared, selected }.OrderBy(p => p.Root, StringComparer.OrdinalIgnoreCase).First();
                    var second = first == shared ? selected : shared;
                    if (heldAssets == second) {
                        Assert.True(File.Exists(Path.Combine(first.Root, ".instance.lock")));
                        using (first.AcquireLease()) { }
                    }
                    else Assert.False(File.Exists(Path.Combine(second.Root, ".instance.lock")));
                }
            }
            Assert.True((await preparer.PrepareLaunch(Options(suffix))).IsSome);
            Assert.Equal(1, entered);
            AssertSelectedDll(dll, installed);
            Assert.Empty(ReadInventory(selected.Paks));
            Assert.Equal("profile signature", File.ReadAllText(Path.Combine(selected.Paks, "unknown.sig")));
            using (shared.AcquireLease()) { }
            using (selected.AcquireLease()) { }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null, "success")]
    [InlineData("un-chained", "success")]
    [InlineData("Other", "success")]
    [InlineData("Other", "cancel")]
    [InlineData("Other", "error")]
    [InlineData("Other", "throw-cancel")]
    public async Task CombinedPipelineHoldsLeasesAcrossAsyncDllPreparationAndRebindsAfterSelection(string? suffix, string outcome) {
        var root = Scratch("gui-combined-async-");
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Option<LaunchOptions>>? pending = null;
        try {
            var shared = new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix, root);
            var selected = new ProfileAssetPaths(suffix ?? ProfileAssetPaths.SharedSuffix, root);
            var installationPaks = Path.Combine(root, FilePaths.PakDir);
            Directory.CreateDirectory(installationPaks);
            File.WriteAllText(Path.Combine(installationPaks, "pakchunk0-WindowsNoEditor.sig"), "signature");
            var payload = Path.Combine(root, "source.pak");
            File.WriteAllText(payload, "pak payload");
            var hash = FileHelpers.Sha512(payload).Match(value => value, error => throw new Exception(error.Message));
            var release = new Release("v1.0.0", hash, "mod.pak", DateTime.UnixEpoch,
                new ModInfo("https://github.com/org/mod", "mod", "test", null, null, [], [], []),
                new AssetCollections([], [], [], [], []), null);
            var registry = new LocalModRegistry(Path.Combine(root, "registry"));
            await registry.AddRelease(release, payload);
            var manager = new ModManager(registry, shared.CreatePakDir(root), []);
            Assert.False((await manager.UpdateModsList()).HasErrors);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialogue = new Dialogue();
            using var http = new HttpClient();
            var (dll, installed) = CachedDll(shared, http);
            Exception error = outcome == "throw-cancel" ? new OperationCanceledException("original cancellation")
                : new InvalidOperationException("original DLL error");
            dialogue.ProgressStarted = () => {
                AssertSelectedDll(dll, installed);
                Assert.Throws<IOException>(() => shared.AcquireLease());
                Assert.Throws<IOException>(() => selected.AcquireLease());
            };
            var preceding = new CallbackPreparer(async options => {
                Assert.Empty(Directory.GetFiles(selected.Paks, "*.pak"));
                started.SetResult();
                await finish.Task;
                if (outcome is "error" or "throw-cancel") throw error;
                if (outcome == "cancel") return Option<LaunchOptions>.None;
                Assert.True(manager.EnableModRelease(ReleaseCoordinates.FromRelease(release)));
                return await new DllLaunchPreparer(dll, dialogue).PrepareLaunch(options);
            });
            var preparer = new ProfileModsLaunchPreparer(manager, dialogue, root, root, preceding);
            pending = preparer.PrepareLaunch(Options(suffix));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Throws<IOException>(() => shared.AcquireLease());
            Assert.Throws<IOException>(() => selected.AcquireLease());
            Assert.False(File.Exists(installed));
            finish.SetResult();
            if (outcome is "error" or "throw-cancel") Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => pending));
            else Assert.Equal(outcome == "success", (await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsSome);
            using (shared.AcquireLease()) { }
            using (selected.AcquireLease()) { }
            if (outcome == "success") {
                AssertSelectedDll(dll, installed);
                Assert.Equal(ReleaseCoordinates.FromRelease(release), Assert.Single(ReadInventory(selected.Paks)).Coordinates);
                Assert.Single(manager.EnabledModReleaseCoordinates);
            }
            else {
                Assert.False(File.Exists(installed));
                Assert.Empty(Directory.GetFiles(selected.Paks));
            }
            Assert.Empty(dialogue.Messages);
        }
        finally {
            finish.TrySetResult();
            if (pending is not null) try { await pending; } catch { }
            Directory.Delete(root, true);
        }
    }

    private static void AssertSelectedDll(UnchainedLauncher.Core.Services.Dlls.DllManager manager, string installed) {
        var files = manager.OpenForInjection();
        try {
            var file = Assert.Single(files);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file.Path));
            if (OperatingSystem.IsLinux()) {
                Assert.False(File.Exists(installed));
                Assert.Contains(Path.Combine("cache", "dlls") + Path.DirectorySeparatorChar, file.Path);
            }
            else {
                Assert.Equal(installed, file.Path);
                Assert.True(File.Exists(installed));
            }
        }
        finally { foreach (var file in files) file.Dispose(); }
    }

    private static (UnchainedLauncher.Core.Services.Dlls.DllManager Manager, string Installed) CachedDll(ProfileAssetPaths shared, HttpClient http) {
        var release = new UnchainedLauncher.Core.Services.Dlls.DllRelease("org", "plugin", "1.0.0",
            "https://example.test/plugin.dll", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3])).ToLowerInvariant(), 3);
        var cache = Path.Combine(shared.DllCache, release.Sha256, "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllBytes(cache, [1, 2, 3]);
        return (UnchainedLauncher.Core.Services.Dlls.DllManager.CreateForLaunch(shared.Plugins, [release], [release], http, () => [], shared.DllCache),
            Path.Combine(shared.Plugins, "Managed", "org", "plugin", "plugin.dll"));
    }

    private sealed class CallbackPreparer(Func<LaunchOptions, Task<Option<LaunchOptions>>> prepare) : IChivalry2LaunchPreparer<LaunchOptions> {
        public Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) => prepare(options);
    }

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
            var profilePaks = new ProfileAssetPaths(expectedSuffix, root).Paks;
            Directory.CreateDirectory(profilePaks);
            var unknownSigs = new[] { "UserBackup.sig", "Selected_1_P.sig" };
            foreach (var name in unknownSigs) File.WriteAllText(Path.Combine(profilePaks, name), "unknown " + name);
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
            Assert.Equal("Selected (1)_1_P.pak", Assert.Single(inventory, pak => pak.Coordinates == ReleaseCoordinates.FromRelease(selected)).PakFileName);
            Assert.True((await preparer.PrepareLaunch(options)).IsSome);
            Assert.Equal(inventory, ReadInventory(paks));
            foreach (var name in unknownSigs) Assert.Equal("unknown " + name, File.ReadAllText(Path.Combine(paks, name)));
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
            Assert.Equal(3, Directory.GetFiles(paks, "*.sig").Length);
            foreach (var name in unknownSigs) Assert.Equal("unknown " + name, File.ReadAllText(Path.Combine(paks, name)));
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
            var entered = false;
            var preceding = new CallbackPreparer(options => {
                entered = true;
                return Task.FromResult(Option<LaunchOptions>.Some(options));
            });
            var result = await new ProfileModsLaunchPreparer(manager, dialogue, root, root, preceding).PrepareLaunch(Options(suffix));
            Assert.False(entered);
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

    [Theory]
    [InlineData(null)]
    [InlineData("Target")]
    public async Task ObstructedProfilePaksCancelsLaunchAndAllowsRetry(string? suffix) {
        var root = Scratch("gui-obstructed-profile-");
        try {
            var assets = new ProfileAssetPaths(suffix ?? ProfileAssetPaths.SharedSuffix, root);
            var source = new ProfileAssetPaths("Source", root);
            var manager = new ModManager(new LocalModRegistry(Path.Combine(root, "registry")), source.CreatePakDir(root), []);
            Directory.CreateDirectory(Path.GetDirectoryName(assets.Paks)!);
            byte[] obstruction = [0, 1, 255, 42];
            File.WriteAllBytes(assets.Paks, obstruction);
            var dialogue = new Dialogue();
            var entered = 0;
            var preceding = new CallbackPreparer(options => {
                entered++;
                return Task.FromResult(Option<LaunchOptions>.Some(options));
            });
            var preparer = new ProfileModsLaunchPreparer(manager, dialogue, root, root, preceding);
            var process = new CapturingProcessLauncher();
            var launcher = new UnchainedChivalry2Launcher(preparer, process, root, new NullInjector());

            Assert.True((await preparer.PrepareLaunch(Options(suffix))).IsNone);
            Assert.True((await launcher.Launch(Options(suffix))).IsLeft);
            Assert.Equal(0, process.Calls);
            Assert.Equal(0, entered);
            Assert.Equal(obstruction, File.ReadAllBytes(assets.Paks));
            Assert.Equal(2, dialogue.Messages.Count);
            Assert.All(dialogue.Messages, message => {
                Assert.Contains("Could not prepare profile assets", message);
                Assert.Contains("Launch cancelled", message);
            });
            Assert.Equal(0, dialogue.ProgressClosed);
            using (assets.AcquireLease()) { }

            File.Delete(assets.Paks);
            dialogue.Messages.Clear();
            Assert.True((await preparer.PrepareLaunch(Options(suffix))).IsSome);
            Assert.True((await launcher.Launch(Options(suffix))).IsLeft); // Stop at the OS process boundary.
            Assert.Equal(1, process.Calls);
            Assert.Equal(2, entered);
            Assert.Empty(dialogue.Messages);
            Assert.Equal(2, dialogue.ProgressClosed);
            using (assets.AcquireLease()) { }
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Scratch(string prefix) => Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ??
        (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), prefix + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task InventoryIoFailureCancelsLaunchAndReleasesLease(bool gui, bool lockedRead) {
        var root = Scratch("gui-inventory-io-");
        try {
            var assets = new ProfileAssetPaths("unchained", root);
            var basePaks = Path.Combine(root, FilePaths.PakDir);
            Directory.CreateDirectory(basePaks);
            File.WriteAllText(Path.Combine(basePaks, "pakchunk0-WindowsNoEditor.sig"), "signature");
            var payload = Path.Combine(root, "source.pak");
            File.WriteAllText(payload, "payload");
            var hash = FileHelpers.Sha512(payload).Match(value => value, error => throw new Exception(error.Message));
            var release = new Release("v1.0.0", hash, "mod.pak", DateTime.UnixEpoch,
                new ModInfo("https://github.com/org/mod", "mod", "test", null, null, [], [], []),
                new AssetCollections([], [], [], [], []), null);
            var registry = new LocalModRegistry(Path.Combine(root, "registry"));
            await registry.AddRelease(release, payload);
            var manager = new ModManager(registry, assets.CreatePakDir(root), [ReleaseCoordinates.FromRelease(release)]);
            Assert.False((await manager.UpdateModsList()).HasErrors);
            var path = Path.Combine(assets.Paks, FilePaths.ManagedPakInventoryFileName);
            File.WriteAllText(path, "{\"ManagedPaks\":[]}");
            if (!lockedRead) Directory.CreateDirectory(path + ".tmp");
            var dialogue = new Dialogue();
            IChivalry2LaunchPreparer<LaunchOptions> preparer = gui
                ? new ProfileModsLaunchPreparer(manager, dialogue, root, root)
                : ((IChivalry2LaunchPreparer<LaunchOptions>)new Chivalry2ModsInstaller(manager, dialogue))
                    .Sub(SigPreparer.CreateForProfile(manager.PakDir, dialogue));
            using (var held = lockedRead ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null) {
                var process = new CapturingProcessLauncher();
                var launcher = new UnchainedChivalry2Launcher(preparer, process, root, new NullInjector());
                Assert.True((await launcher.Launch(Options(null))).IsLeft);
                Assert.Equal("", process.Arguments);
                Assert.Contains("Could not reconcile managed pak inventory", Assert.Single(dialogue.Messages));
                Assert.Contains("Launch cancelled", dialogue.Messages[0]);
                Assert.Equal(lockedRead ? 0 : 1, Directory.GetFiles(assets.Paks, "*.pak").Length);
                // Installation itself signs downloads before the final inventory save.
                Assert.Equal(lockedRead ? 0 : 1, Directory.GetFiles(assets.Paks, "*.sig").Length);
                Assert.Equal(lockedRead ? 0 : 1, dialogue.ProgressClosed);
                using var available = assets.AcquireLease();
            }
            Assert.Equal("{\"ManagedPaks\":[]}", File.ReadAllText(path));
            if (!lockedRead) Directory.Delete(path + ".tmp");
            Assert.True((await preparer.PrepareLaunch(Options(null))).IsSome);
            Assert.Single(ReadInventory(assets.Paks));
            // A failed save leaves the first download unowned, so retry must preserve it.
            Assert.Equal(lockedRead ? 1 : 2, Directory.GetFiles(assets.Paks, "*.sig").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    private static LaunchOptions Options(string? suffix) => new([], Option<string>.None, "", false, false,
        suffix is null ? Option<string>.None : Option<string>.Some(suffix), Option<ServerLaunchOptions>.None);

    [Fact]
    public async Task EmptyProfilePreparationPreservesUnknownSignatures() {
        var root = Scratch("gui-empty-signatures-");
        try {
            var assets = new ProfileAssetPaths("unchained", root);
            var manager = new ModManager(new LocalModRegistry(Path.Combine(root, "registry")), assets.CreatePakDir(root), []);
            var path = Path.Combine(assets.Paks, "UserBackup.sig");
            File.WriteAllBytes(path, [0, 1, 255, 42]);
            var preparer = new ProfileModsLaunchPreparer(manager, new Dialogue(), root, root);
            for (var i = 0; i < 2; i++) {
                Assert.True((await preparer.PrepareLaunch(Options(null))).IsSome);
                Assert.Equal(new byte[] { 0, 1, 255, 42 }, File.ReadAllBytes(path));
                Assert.Empty(ReadInventory(assets.Paks));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileSigningPreservesLegacySweepContractAndFailsClosed(bool profile) {
        var root = Scratch("signing-contract-");
        try {
            Directory.CreateDirectory(root);
            var orphan = Path.Combine(root, "UserBackup.sig");
            File.WriteAllText(orphan, "unknown signature");
            var paks = new PakDir(root, []);
            var dialogue = new Dialogue();
            var preparer = profile ? SigPreparer.CreateForProfile(paks, dialogue) : SigPreparer.Create(paks, dialogue);
            Assert.True((await preparer.PrepareLaunch(LanguageExt.Unit.Default)).IsSome);
            Assert.Equal(profile, File.Exists(orphan));
            File.WriteAllText(Path.Combine(root, "unknown.pak"), "payload");
            Assert.True((await preparer.PrepareLaunch(LanguageExt.Unit.Default)).IsNone);
            Assert.Contains("errors while installing", Assert.Single(dialogue.Messages));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreparationExceptionsStillEscapeAfterInventorySaveAttempt(bool cancelled, bool saveBlocked) {
        var root = Scratch("inventory-original-error-");
        try {
            var assets = new ProfileAssetPaths("unchained", root);
            var manager = new ModManager(new LocalModRegistry(Path.Combine(root, "registry")), assets.CreatePakDir(root), []);
            var path = Path.Combine(assets.Paks, FilePaths.ManagedPakInventoryFileName);
            if (saveBlocked) Directory.CreateDirectory(path + ".tmp");
            Exception error = cancelled ? new OperationCanceledException("original") : new InvalidOperationException("original");
            var dialogue = new Dialogue { ProgressError = error };
            var preparer = new ProfileModsLaunchPreparer(manager, dialogue, root, root);
            Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => preparer.PrepareLaunch(Options(null))));
            using var available = assets.AcquireLease();
            if (saveBlocked) Assert.Contains("Launch cancelled", Assert.Single(dialogue.Messages));
            else {
                Assert.Empty(dialogue.Messages);
                Assert.Empty(ReadInventory(assets.Paks));
            }
        }
        finally { Directory.Delete(root, true); }
    }

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
        public int ProgressClosed { get; private set; }
        public Exception? ProgressError { get; init; }
        public Action? ProgressStarted { get; set; }
        public void DisplayMessage(string message) => Messages.Add(message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) => throw new NotSupportedException();
        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => throw new NotSupportedException();
        public Action DisplayProgress(MemoryProgress progress) {
            ProgressStarted?.Invoke();
            if (ProgressError is not null) throw ProgressError;
            return () => { ProgressClosed++; };
        }
    }

    private sealed class CapturingProcessLauncher : IProcessLauncher {
        public string Executable => "test-game.exe";
        public string Arguments { get; private set; } = "";
        public int Calls { get; private set; }
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args) {
            Calls++;
            Arguments = args;
            return new LaunchFailed(Executable, args, LanguageExt.Common.Error.New("Stopped at the OS boundary."));
        }
    }
}
