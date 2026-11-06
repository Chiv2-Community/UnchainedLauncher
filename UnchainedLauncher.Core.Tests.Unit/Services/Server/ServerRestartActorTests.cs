using LanguageExt;
using Moq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Threading;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Tests.Unit.Services.Processes;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

[Collection("Home launch UI")]
public class ServerRestartActorTests(HomeLaunchUi ui) {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RestoredUnmanagedActorReturnsOnLaterRestartWithoutChangingRequestedSnapshot(bool unavailableInitially) => ui.RunAsync(async () => {
        var path = Path.GetTempFileName();
        var name = "RestartActor_" + Guid.NewGuid().ToString("N");
        var profile = FilePaths.Chiv2ConfigPath(ServerConfigurationVM.SavedDirSuffix(name));
        var processes = new List<Process>();
        ServersTabVM? tab = null;
        try {
            File.WriteAllText(path, "original");
            var entry = new GenericAssetEntry {
                AssetPath = "TBL/Content/Mods/Transient.uasset",
                AssetHash = "hash",
                ClassPath = "/Game/Mods/Transient.Transient_C"
            };
            entry.Properties["ModName"] = "Transient";
            entry.Properties["bShowInGUI"] = true;
            var pak = new PakScanResult { PakPath = path, PakHash = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path))) };
            pak.AddGenericEntry(entry, "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C");
            var result = new ModScanResult();
            result.Paks[path] = pak;
            var scan = new ModScanTabVM();
            typeof(ModScanTabVM).GetProperty(nameof(scan.LastScanResult))!.SetValue(scan, result);
            var manager = new ModManager(Mock.Of<IModRegistry>(), Mock.Of<IPakDir>(), []);
            var catalog = new AvailableModsAndMapsService(manager, scan);
            var actor = Assert.Single(catalog.AvailableServerModBlueprints);
            Assert.False(string.IsNullOrWhiteSpace(actor.ClassPath));
            var config = new ServerConfigurationVM(manager, scan, catalog, name: name, localIp: "127.0.0.1");
            config.EnableServerBlueprintMod(actor);
            var launches = new List<LaunchOptions>();
            var launcher = new Mock<IChivalry2Launcher>();
            launcher.Setup(l => l.Launch(It.IsAny<LaunchOptions>())).Returns((LaunchOptions options) => {
                launches.Add(options);
                var process = new Process();
                processes.Add(process);
                return Task.FromResult<Either<LaunchFailed, Process>>(process);
            });
            var exits = new List<Action<int, bool>>();
            var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var watcher = new Mock<IChivalryProcessWatcher>();
            watcher.Setup(w => w.OnExit(It.IsAny<Process>(), It.IsAny<Action<int, bool>>()))
                .Callback<Process, Action<int, bool>>((_, callback) => { exits.Add(callback); attached.TrySetResult(); })
                .ReturnsAsync(true);
            var settings = new SettingsVM(null!, null!, InstallationType.Steam, true, false, "", "https://example.test", false, false, "");
            tab = new ServersTabVM(settings, manager, scan, Mock.Of<IUserDialogueSpawner>(), launcher.Object,
                new ObservableCollection<ServerConfigurationVM>([config]), watcher.Object, catalog);
            if (unavailableInitially) File.WriteAllText(path, "unavailable");
            await tab.LaunchSelected();
            Assert.Equal(unavailableInitially ? [] : new[] { actor.ClassPath }, launches[0].ServerLaunchOptions.ToSeq().Single().ServerMods);
            Assert.Equal(actor, Assert.Single(config.EnabledServerModList));

            async Task Restart(int generation) {
                attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
                exits[generation](42, false);
                await attached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            File.WriteAllText(path, "unavailable");
            await Restart(0);
            Assert.Empty(launches[1].ServerLaunchOptions.ToSeq().Single().ServerMods);
            File.WriteAllText(path, "original");
            await Restart(1);
            Assert.Equal([actor.ClassPath], launches[2].ServerLaunchOptions.ToSeq().Single().ServerMods);
            Assert.Equal(actor, Assert.Single(config.EnabledServerModList));
        }
        finally {
            if (tab != null) {
                foreach (var running in tab.RunningServers) {
                    try { running.live.DisposeServer(); }
                    catch (InvalidOperationException) { } // Fake processes were never started.
                }
            }
            foreach (var process in processes) process.Dispose();
            File.Delete(path);
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
        }
    });
}