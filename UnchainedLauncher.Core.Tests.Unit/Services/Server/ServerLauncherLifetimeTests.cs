using LanguageExt;
using Moq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
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
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

[Collection("Home launch UI")]
public class ServerLauncherLifetimeTests(HomeLaunchUi ui) {
    [Theory]
    [InlineData(InstallationType.EpicGamesStore, false, 0, true)]
    [InlineData(InstallationType.EpicGamesStore, true, 0, true)]
    [InlineData(InstallationType.EpicGamesStore, false, 42, false)]
    [InlineData(InstallationType.EpicGamesStore, true, 42, false)]
    [InlineData(InstallationType.EpicGamesStore, false, -67, false)]
    [InlineData(InstallationType.EpicGamesStore, true, -67, false)]
    [InlineData(InstallationType.Steam, false, 0, true)]
    [InlineData(InstallationType.Steam, true, 0, true)]
    public Task LauncherLifetimeFollowsServerExit(InstallationType installationType, bool headless, int code, bool acceptable) =>
        RunLifetime(installationType, headless, code, acceptable, attached: true);

    [Theory]
    [InlineData(InstallationType.EpicGamesStore, false)]
    [InlineData(InstallationType.EpicGamesStore, true)]
    [InlineData(InstallationType.Steam, false)]
    [InlineData(InstallationType.Steam, true)]
    public Task AttachmentFailureKeepsServerTrackedWithoutShutdown(InstallationType installationType, bool headless) =>
        RunLifetime(installationType, headless, 0, true, attached: false);

    private Task RunLifetime(InstallationType installationType, bool headless, int code, bool acceptable, bool attached) => ui.RunAsync(async () => {
        var name = "Lifetime_" + Guid.NewGuid().ToString("N");
        var profile = FilePaths.Chiv2ConfigPath(ServerConfigurationVM.SavedDirSuffix(name));
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        await process.WaitForExitAsync();
        ServersTabVM? tab = null;
        try {
            var manager = new ModManager(Mock.Of<IModRegistry>(), Mock.Of<IPakDir>(), []);
            var scan = new ModScanTabVM();
            var catalog = new AvailableModsAndMapsService(manager, scan);
            var config = new ServerConfigurationVM(manager, scan, catalog, name: name, localIp: "127.0.0.1");
            var settings = new SettingsVM(null!, null!, installationType, true, false, "", "https://example.test", false, false, "");
            var launcher = new Mock<IChivalry2Launcher>();
            launcher.Setup(l => l.Launch(It.IsAny<LaunchOptions>())).ReturnsAsync((Either<LaunchFailed, Process>)process);
            Action<int, bool>? exit = null;
            var watcher = new Mock<IChivalryProcessWatcher>();
            watcher.Setup(w => w.OnExit(process, It.IsAny<Action<int, bool>>()))
                .Callback<Process, Action<int, bool>>((_, callback) => exit = callback)
                .ReturnsAsync(attached);
            var shutdowns = 0;
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var timerField = typeof(ServerVM).GetField("_updateTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ServerVM? live = null;
            tab = new ServersTabVM(settings, manager, scan, Mock.Of<IUserDialogueSpawner>(), launcher.Object,
                new ObservableCollection<ServerConfigurationVM>([config]), watcher.Object, catalog, () => {
                    Assert.True(Application.Current.Dispatcher.CheckAccess());
                    Assert.Empty(tab!.RunningServers);
                    Assert.Null(timerField.GetValue(live));
                    shutdowns++;
                    shutdown.SetResult();
                });
            tab.RunningServers.CollectionChanged += (_, args) => {
                Assert.True(Application.Current.Dispatcher.CheckAccess());
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                    removed.SetResult();
            };

            await tab.LaunchSelected(headless);
            live = Assert.Single(tab.RunningServers).live;
            Assert.NotNull(timerField.GetValue(live));
            Assert.NotNull(exit);
            Assert.Equal(0, shutdowns);

            if (!attached) {
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.Single(tab.RunningServers);
                Assert.NotNull(timerField.GetValue(live));
                Assert.Equal(0, shutdowns);
            }
            else {
                await Task.Run(() => exit!(code, acceptable));
                await removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (installationType == InstallationType.EpicGamesStore)
                    await shutdown.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.Equal(installationType == InstallationType.EpicGamesStore ? 1 : 0, shutdowns);
                Assert.Empty(tab.RunningServers);
                Assert.Null(timerField.GetValue(live));
            }
            launcher.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Once);
        }
        finally {
            tab?.Dispose();
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
        }
    });
}
