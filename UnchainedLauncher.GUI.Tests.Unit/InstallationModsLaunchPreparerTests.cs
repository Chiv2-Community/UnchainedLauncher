using LanguageExt;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

// GUI paks belong to the installation, not to the launch's Saved profile.
public sealed class InstallationModsLaunchPreparerTests : IDisposable {
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(),
        "gui-ownership-" + Guid.NewGuid().ToString("N"));
    private string Paks => Path.GetFullPath(Path.Combine(_root, FilePaths.PakDir));
    private string Config => Path.Combine(_root, FilePaths.ModManagerConfigPath);
    private readonly LocalModRegistry _registry;
    private readonly Mock<IUserDialogueSpawner> _dialogue = new();
    private readonly Release _selected;
    private readonly Release _dependency;
    private readonly Release _other;

    public InstallationModsLaunchPreparerTests() {
        Directory.CreateDirectory(Paks);
        File.WriteAllText(Path.Combine(Paks, "pakchunk0-WindowsNoEditor.sig"), "base signature");
        File.WriteAllText(Path.Combine(Paks, "user.pak"), "unknown payload");
        File.WriteAllText(Path.Combine(Paks, "user.sig"), "unknown signature");
        var source = Path.Combine(_root, "payload.pak");
        File.WriteAllText(source, "approved payload");
        var hash = FileHelpers.Sha512(source).Match(Right: h => h, Left: e => throw new Exception(e.Message));
        Release Release(string name, params Dependency[] dependencies) => new("v1.0.0", hash, "mod.pak", DateTime.UnixEpoch,
            new($"https://github.com/org/{name}", name, "test", null, null, [], [], dependencies.ToList()),
            new([], [], [], [], []), null);
        _dependency = Release("Dependency");
        _selected = Release("Selected", new Dependency("https://github.com/org/Dependency", "v1.0.0"));
        _other = Release("Other");
        _registry = new LocalModRegistry(Path.Combine(_root, "registry"));
        foreach (var release in new[] { _dependency, _selected, _other })
            _registry.AddRelease(release, source).GetAwaiter().GetResult();
        _dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => () => { });
    }

    private ModManager Startup() => LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Paks).Manager;
    private static LaunchOptions Options(string? suffix) => new([], Option<string>.None, "", false, false,
        suffix == null ? Option<string>.None : suffix, Option<ServerLaunchOptions>.None);
    private async Task<bool> Prepare(ModManager manager, string? suffix) {
        Assert.False((await manager.UpdateModsList()).HasErrors);
        var settings = Mock.Of<IUnmanagedPakSettings>(s => s.AlwaysKeepUnmanagedPaks && s.AutomaticallyDownloadPakDependencies);
        IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(manager, _dialogue.Object, settings);
        return (await installer
            .Sub(SigPreparer.CreateForProfile(manager.PakDir, _dialogue.Object)).PrepareLaunch(Options(suffix))).IsSome;
    }
    private ManagedPak[] Owned(ModManager manager) => ((PakDir)manager.PakDir).ManagedPaks.ToArray();
    private void AssertUnknown() {
        Assert.Equal("unknown payload", File.ReadAllText(Path.Combine(Paks, "user.pak")));
        Assert.Equal("unknown signature", File.ReadAllText(Path.Combine(Paks, "user.sig")));
    }
    private void AssertDurable(ModManager manager) =>
        Assert.Equal(Owned(manager), Owned(new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!));

    [Theory]
    [InlineData(null, "success")]
    [InlineData("unchained", "success")]
    [InlineData("Other", "success")]
    [InlineData("Other", "cancel")]
    [InlineData("Other", "error")]
    [InlineData("Other", "throw-cancel")]
    public async Task AsyncDllPreparationKeepsInstallationManagerAndDoesNotUseCliProfiles(string? suffix, string outcome) {
        var manager = Startup();
        await manager.UpdateModsList();
        var assets = new ProfileAssetPaths(suffix ?? "unchained", _root);
        using var profileLease = assets.AcquireLease();
        var plugins = Path.GetFullPath(Path.Combine(_root, FilePaths.PluginDir));
        var release = new UnchainedLauncher.Core.Services.Dlls.DllRelease("org", "plugin", "1.0.0",
            "https://example.test/plugin.dll", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3])).ToLowerInvariant(), 3);
        var cache = Path.Combine(assets.DllCache, release.Sha256, "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllBytes(cache, [1, 2, 3]);
        using var http = new HttpClient();
        var dll = UnchainedLauncher.Core.Services.Dlls.DllManager.CreateForLaunch(
            plugins, [release], [release], http, () => [], assets.DllCache);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception error = outcome == "throw-cancel" ? new OperationCanceledException("original cancellation")
            : new InvalidOperationException("original DLL error");
        IChivalry2LaunchPreparer<LaunchOptions> plugin = new CallbackPreparer(async options => {
            entered.SetResult();
            await finish.Task;
            if (outcome is "error" or "throw-cancel") throw error;
            if (outcome == "cancel") return Option<LaunchOptions>.None;
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(_other));
            return await new DllLaunchPreparer(dll, _dialogue.Object).PrepareLaunch(options);
        });
        var settings = Mock.Of<IUnmanagedPakSettings>(s => s.AlwaysKeepUnmanagedPaks && s.AutomaticallyDownloadPakDependencies);
        IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(manager, _dialogue.Object, settings);
        var pending = plugin.AndThen(installer.Sub(SigPreparer.CreateForProfile(manager.PakDir, _dialogue.Object)))
            .PrepareLaunch(Options(suffix));
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(Owned(manager));
            Assert.Single(Directory.GetFiles(Paks, "*.pak"));
            Assert.False(Directory.Exists(assets.Paks));
            finish.SetResult();
            if (outcome is "error" or "throw-cancel")
                Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => pending));
            else Assert.Equal(outcome == "success", (await pending).IsSome);
            if (outcome == "success") {
                Assert.Equal(ReleaseCoordinates.FromRelease(_other), Assert.Single(Owned(manager)).Coordinates);
                AssertDurable(manager);
                var injection = dll.OpenForInjection();
                try {
                    var file = Assert.Single(injection);
                    Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file.Path));
                    Assert.StartsWith(OperatingSystem.IsLinux() ? assets.DllCache : plugins, file.Path);
                }
                finally { foreach (var file in injection) file.Dispose(); }
            }
            else {
                Assert.Empty(Owned(manager));
                Assert.False(Directory.Exists(plugins));
            }
            AssertUnknown();
        }
        finally {
            finish.TrySetResult();
            try { await pending; } catch { }
        }
    }

    private sealed class CallbackPreparer(Func<LaunchOptions, Task<Option<LaunchOptions>>> prepare)
        : IChivalry2LaunchPreparer<LaunchOptions> {
        public Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) => prepare(options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SigningPreservesLegacySweepContractAndFailsClosed(bool managedOnly) {
        var directory = Path.Combine(_root, "signing");
        Directory.CreateDirectory(directory);
        var orphan = Path.Combine(directory, "UserBackup.sig");
        File.WriteAllText(orphan, "unknown signature");
        var paks = new PakDir(directory, []);
        var preparer = managedOnly ? SigPreparer.CreateForProfile(paks, _dialogue.Object) : SigPreparer.Create(paks, _dialogue.Object);
        Assert.True((await preparer.PrepareLaunch(LanguageExt.Unit.Default)).IsSome);
        Assert.Equal(managedOnly, File.Exists(orphan));
        File.WriteAllText(Path.Combine(directory, "unknown.pak"), "payload");
        Assert.True((await preparer.PrepareLaunch(LanguageExt.Unit.Default)).IsNone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Courtyard")]
    [InlineData("Court_yard_1")]
    [InlineData("Court-yard")]
    public async Task ExistingSignedInstallationOwnershipSurvivesLaunchDisableRestartAndCleanup(string? suffix) {
        // This is an existing v1 installation/config, not an empty profile manager.
        var coordinate = ReleaseCoordinates.FromRelease(_selected);
        var legacy = new ManagedPak(coordinate, "legacy-selected.pak", 7);
        File.Copy(Path.Combine(_root, "payload.pak"), Path.Combine(Paks, legacy.PakFileName));
        File.WriteAllText(Path.Combine(Paks, "legacy-selected.sig"), "base signature");
        var existing = new ModManager(_registry, new PakDir(Paks, [legacy]), [coordinate]);
        new ModManagerCodec(_registry).SerializeFile(Config, existing);
        var manager = Startup();
        Assert.Equal(legacy, Assert.Single(Owned(manager)));
        using var heldCliProfile = new ProfileAssetPaths(suffix ?? "unchained", _root).AcquireLease();
        Assert.True(await Prepare(manager, suffix));
        Assert.Equal(Paks, manager.PakDir.DirPath);
        Assert.False(File.Exists(Path.Combine(Paks, legacy.PakFileName)));
        Assert.False(File.Exists(Path.Combine(Paks, "legacy-selected.sig")));
        Assert.Equal(2, Owned(manager).Length);
        AssertDurable(manager);
        Assert.True(await Prepare(manager, suffix));
        Assert.Equal(3, Directory.GetFiles(Paks, "*.pak").Length);
        manager.DisableModRelease(coordinate);
        manager = Startup();
        Assert.Empty(manager.EnabledModReleaseCoordinates);
        Assert.True(await Prepare(manager, suffix));
        Assert.Empty(Owned(manager));
        AssertDurable(manager);
        Assert.Empty(Owned(Startup()));
        AssertUnknown();
        Assert.Single(Directory.GetFiles(Paks, "*.pak"));
        Assert.Equal(2, Directory.GetFiles(Paks, "*.sig").Length); // base and unknown
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Server")]
    public async Task MutableSelectionAndUnknownSignatureCollisionUseActualPersistentManager(string? suffix) {
        File.WriteAllText(Path.Combine(Paks, "Selected_1_P.sig"), "reserved signature");
        var manager = Startup();
        await manager.UpdateModsList();
        manager.EnableModRelease(ReleaseCoordinates.FromRelease(_selected));
        Assert.True(await Prepare(manager, suffix));
        var owned = Owned(manager);
        Assert.Equal("Selected (1)_1_P.pak", Assert.Single(owned,
            p => p.Coordinates == ReleaseCoordinates.FromRelease(_selected)).PakFileName);
        AssertDurable(manager);
        manager.DisableModRelease(ReleaseCoordinates.FromRelease(_selected));
        manager.EnableModRelease(ReleaseCoordinates.FromRelease(_other));
        Assert.True(await Prepare(manager, suffix));
        Assert.Equal(ReleaseCoordinates.FromRelease(_other), Assert.Single(Owned(manager)).Coordinates);
        AssertDurable(manager);
        Assert.Equal("reserved signature", File.ReadAllText(Path.Combine(Paks, "Selected_1_P.sig")));
        AssertUnknown();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedConfigPublicationCancelsWithoutOrphanAndRetrySurvivesRestart(bool failAfterPayload) {
        var manager = Startup();
        await manager.UpdateModsList();
        manager.EnableModRelease(ReleaseCoordinates.FromRelease(_other));
        var before = File.ReadAllBytes(Config);
        File.Delete(Config);
        Directory.CreateDirectory(Config); // File.Move cannot replace a directory on either OS.
        if (failAfterPayload) {
            Directory.Delete(Config);
            File.WriteAllBytes(Config, before);
        }
        if (!failAfterPayload) {
            Assert.False(await Prepare(manager, null));
            Assert.Empty(Owned(manager));
            Assert.Single(Directory.GetFiles(Paks, "*.pak"));
        }
        else {
            // A failed final checkpoint retains intent, not confirmed ownership.
            var original = Config + ".before";
            var request = new ModInstallRequest(ReleaseCoordinates.FromRelease(_other),
                path => {
                    File.Move(Config, original);
                    Directory.CreateDirectory(Config);
                    var bytes = File.ReadAllBytes(Path.Combine(_root, "payload.pak"));
                    return new FileWriter(path, new MemoryStream(bytes), bytes.Length);
                });
            var failure = await Record.ExceptionAsync(async () => {
                await foreach (var _ in manager.PakDir.InstallModSet([request], Option<AccumulatedMemoryProgress>.None)) { }
            });
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "No failure");
            Assert.Single(Owned(manager));
            Assert.Empty(Owned(new ModManagerCodec(_registry).Deserialize(File.ReadAllText(original)).Result!));
            Assert.Contains("PendingPublications", File.ReadAllText(original));
            // Restore the last successful disk publication, as a repaired mount would.
            Directory.Delete(Config);
            File.Move(original, Config);
        }
        if (Directory.Exists(Config)) {
            Directory.Delete(Config);
            File.WriteAllBytes(Config, before);
        }
        manager = Startup();
        if (failAfterPayload) {
            Assert.False(await Prepare(manager, null)); // AlwaysKeep is not recovery authorization.
            _dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel",
                It.IsAny<IEnumerable<DependencyUpdate>>())).Returns(UserDialogueChoice.Yes);
        }
        Assert.True(await Prepare(manager, null));
        Assert.Single(Owned(manager));
        Assert.Equal(2, Directory.GetFiles(Paks, "*.pak").Length);
        manager.DisableModRelease(ReleaseCoordinates.FromRelease(_other));
        manager = Startup();
        Assert.True(await Prepare(manager, null));
        Assert.Empty(Owned(manager));
        AssertUnknown();
    }

    [Fact]
    public async Task VanillaUnsignsInstallationWithoutTouchingCliProfileOrPayloads() {
        var profile = new ProfileAssetPaths("unchained", _root);
        Directory.CreateDirectory(profile.Paks);
        var unknown = Path.Combine(profile.Paks, "unknown.sig");
        File.WriteAllText(unknown, "profile signature");
        using var held = profile.AcquireLease();
        Assert.True((await NoSigPreparer.CreateForInstallation(_root, _dialogue.Object)
            .PrepareLaunch(LanguageExt.Unit.Default)).IsSome);
        Assert.False(File.Exists(Path.Combine(Paks, "user.sig")));
        Assert.Equal("unknown payload", File.ReadAllText(Path.Combine(Paks, "user.pak")));
        Assert.Equal("profile signature", File.ReadAllText(unknown));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
