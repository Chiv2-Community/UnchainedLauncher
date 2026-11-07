using LanguageExt;
using Moq;
using System.Security.Cryptography;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public sealed class LaunchPersistenceTests(HomeLaunchUi ui) : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LaunchPersistence_" + Guid.NewGuid().ToString("N"));

    private static LaunchOptions Options(bool server) => new([], "", "", false, false, Prelude.None,
        server ? Option<ServerLaunchOptions>.Some(new(true, "Server", "", false, Prelude.None, "Map", 7777, 7778, 7779, 7780,
            null, null, null, null, null, null, Prelude.None, [], Prelude.None, false, false, CensorArg.None)) : Prelude.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SavesCurrentMetadataAndSettingsBeforePluginChecksRecoveryAndDownloadsOnEveryLaunch(bool server) => ui.RunAsync(async () => {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Paks"));
        var config = Path.Combine(_root, "mods.json");
        var settingsPath = Path.Combine(_root, "settings.json");
        var registry = new Mock<IModRegistry>();
        var release = new Release("v1", Convert.ToHexString(SHA512.HashData(new byte[] { 1, 2, 3 })), "GiantSlayers.pak", DateTime.UtcNow,
            new("https://github.com/Org/GiantSlayers", "GiantSlayers", "new metadata", null, null, [], [], []),
            new AssetCollections([], [], [], [], []), "");
        var mod = new Mod(release.Info, [release]);
        var manager = new ModManager(registry.Object, new Core.Services.Mods.PakDir(Path.Combine(_root, "Paks"), []),
            [ReleaseCoordinates.FromRelease(release)]);
        var persistence = new ModManagerPersistence(manager, config);
        persistence.Save();
        registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], [mod]));
        await manager.UpdateModsList(); // No selection event: launch must save refreshed metadata too.
        var settings = new SettingsVM(null!, null!, InstallationType.Steam, true, false, "BeforeActor", "https://example.test", false, false, "-before");
        var codec = new SettingsCodec(null!, null!, null!, "");
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
        var saves = 0;
        IChivalry2LaunchPreparer<LaunchOptions> checkpoint = new SavePreferencesPreparer(() => {
            persistence.Save();
            codec.SerializeFile(settingsPath, settings);
            saves++;
        }, dialogue.Object);
        void AssertSaved() {
            Assert.Single(new ModManagerCodec(registry.Object).Deserialize(File.ReadAllText(config)).Result!.Mods);
            Assert.Contains(settings.AdditionalModActors, File.ReadAllText(settingsPath));
            Assert.True(saves > 0);
        }
        var checks = 0;
        var plugin = Chivalry2LaunchPreparer.Create<LaunchOptions>(options => {
            AssertSaved();
            checks++;
            return Option<LaunchOptions>.Some(options);
        });
        registry.Setup(r => r.DownloadPak(It.IsAny<ReleaseCoordinates>(), It.IsAny<string>()))
            .Returns((ReleaseCoordinates _, string path) => {
                AssertSaved();
                return Prelude.RightAsync<ModPakStreamAcquisitionFailure, FileWriter>(new FileWriter(path, new MemoryStream([1, 2, 3]), 3));
            });
        var pakSettings = Mock.Of<IUnmanagedPakSettings>(s => s.AutomaticallyDownloadPakDependencies == false);
        var composed = checkpoint.AndThen(plugin).AndThen(new Chivalry2ModsInstaller(manager, dialogue.Object, pakSettings));
        var options = Options(server);
        Assert.True((await composed.PrepareLaunch(options)).IsSome);
        settings.AdditionalModActors = "ChangedActor";
        Assert.True((await composed.PrepareLaunch(options)).IsSome); // Same production path used by retries/restarts.
        Assert.Equal(2, saves);
        Assert.Equal(2, checks);
        registry.Verify(r => r.DownloadPak(It.IsAny<ReleaseCoordinates>(), It.IsAny<string>()), Times.Once);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedCheckpointCancelsBeforePluginChecksAndUnknownPakRecoveryThenCanRetry(bool server) => ui.RunAsync(async () => {
        var paks = Path.Combine(_root, "Paks");
        Directory.CreateDirectory(paks);
        var unknown = Path.Combine(paks, "unknown.pak");
        File.WriteAllText(unknown, "leave untouched");
        var registry = new Mock<IModRegistry>(MockBehavior.Strict);
        var manager = new ModManager(registry.Object, new Core.Services.Mods.PakDir(paks, []), []);
        var config = Path.Combine(_root, "mods.json");
        var persistence = new ModManagerPersistence(manager, config);
        persistence.Save();
        var previous = File.ReadAllBytes(config);
        var dialogue = new Mock<IUserDialogueSpawner>();
        IChivalry2LaunchPreparer<LaunchOptions> checkpoint = new SavePreferencesPreparer(persistence.Save, dialogue.Object);
        var checks = 0;
        var plugin = Chivalry2LaunchPreparer.Create<LaunchOptions>(options => {
            checks++;
            return Option<LaunchOptions>.Some(options);
        });
        var composed = checkpoint.AndThen(plugin).AndThen(new Chivalry2ModsInstaller(manager, dialogue.Object));
        using (var locked = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Assert.True((await composed.PrepareLaunch(Options(server))).IsNone);
            Assert.Equal(0, checks);
            Assert.Equal(previous, File.ReadAllBytes(config));
            Assert.Equal("leave untouched", File.ReadAllText(unknown));
            Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
            dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("preferences") && s.Contains("cancelled"))), Times.Once);
            dialogue.Verify(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Never);
            dialogue.Verify(d => d.DisplayProgress(It.IsAny<MemoryProgress>()), Times.Never);
            registry.VerifyNoOtherCalls();
        }
        Assert.True((await checkpoint.PrepareLaunch(Options(server))).IsSome);
    });

    [Fact]
    public Task EmptyServerTabCanBeSavedBeforeAClientLaunch() => ui.RunAsync(async () => {
        Directory.CreateDirectory(_root);
        var settings = new SettingsVM(null!, null!, InstallationType.Steam, true, false, "", "", false, false, "");
        var manager = new ModManager(Mock.Of<IModRegistry>(), new Core.Services.Mods.PakDir(Path.Combine(_root, "Paks"), []), []);
        var scan = new ModScanTabVM();
        var available = new AvailableModsAndMapsService(manager, scan, settings);
        var dialogue = new Mock<IUserDialogueSpawner>();
        var launcher = Mock.Of<IChivalry2Launcher>();
        var watcher = Mock.Of<IChivalryProcessWatcher>();
        using var servers = new ServersTabVM(settings, manager, scan, dialogue.Object, launcher, [], watcher, available);
        servers.RemoveConfiguration();
        Assert.Empty(servers.ServerConfigs);
        var codec = new ServerTabCodec(settings, manager, scan, dialogue.Object, launcher, servers.ServerConfigs, watcher, available);
        var path = Path.Combine(_root, "servers.json");
        var checkpoint = new SavePreferencesPreparer(() => codec.SerializeFile(path, servers), dialogue.Object);
        Assert.True((await checkpoint.PrepareLaunch(Options(false))).IsSome);
        Assert.Equal("", ServerTabCodec.ToJsonType(servers).SelectedConfigurationName);
        Assert.True(File.Exists(path));
        dialogue.Verify(d => d.DisplayMessage(It.IsAny<string>()), Times.Never);
    });

    public void Dispose() => Directory.Delete(_root, true);
}