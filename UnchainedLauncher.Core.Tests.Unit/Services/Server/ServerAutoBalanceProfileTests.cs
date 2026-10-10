using Moq;
using StructuredINI.Codecs;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server {
    public class ServerAutoBalanceProfileTests {
        [Theory]
        [InlineData(48, false, false)]
        [InlineData(64, false, false)]
        [InlineData(49, true, false)]
        [InlineData(48, false, true)]
        [InlineData(49, true, true)]
        public void ProfileLoadSavePreservesPlayerCapAndBalanceRules(int iniMaxPlayers, bool desyncPatch, bool explicitRules) {
            RunSta(() => {
                var name = "AutoBalanceProfileTest_" + Guid.NewGuid().ToString("N");
                var profilePath = FilePaths.Chiv2ConfigPath(ServerConfigurationVM.SavedDirSuffix(name));
                Directory.CreateDirectory(profilePath);
                try {
                    void WriteProfile(int maxPlayers) => File.WriteAllText(Path.Combine(profilePath, "Game.ini"),
                        $"[/Script/Engine.GameSession]\nMaxPlayers={maxPlayers}\n[/Script/TBL.TBLGameMode]\n!AutoBalanceOptions=ClearArray\n" +
                        (explicitRules ? "AutoBalanceOptions=(MinNumPlayers=12,MaxNumPlayers=32,AllowedNumPlayersDifference=3)\n" : ""));

                    WriteProfile(iniMaxPlayers);
                    var manager = new Mock<IModManager>();
                    manager.SetupGet(m => m.EnabledModReleaseCoordinates).Returns([]);
                    manager.SetupGet(m => m.Mods).Returns([]);
                    var scan = new ModScanTabVM();
                    var catalog = new AvailableModsAndMapsService(manager.Object, scan);
                    var configuration = new ServerConfigurationVM(manager.Object, scan, catalog,
                        name: name, localIp: "127.0.0.1", desyncPatch: desyncPatch);

                    void AssertSavedProfile(int maxPlayers) {
                        var playerCap = maxPlayers - (desyncPatch ? 1 : 0);
                        var expected = explicitRules ? new AutoBalance(12, 32, 3) : new AutoBalance(0, playerCap, 8);
                        Assert.Equal(playerCap, configuration.GameSession.MaxPlayers);
                        Assert.True(configuration.SaveINI());
                        var saved = Chivalry2INI.LoadINIProfile(ServerConfigurationVM.SavedDirSuffix(name));
                        Assert.Equal(maxPlayers, saved.Game.GameSession.MaxPlayers);
                        Assert.Equal(expected, Assert.Single(saved.Game.TBLGameMode.AutoBalanceOptions));
                        Assert.Equal(expected, Assert.Single(configuration.GameMode.ToModel().AutoBalanceOptions));
                    }

                    AssertSavedProfile(iniMaxPlayers);
                    // Reloading an edited INI must refresh the fallback cap, not retain the previous one.
                    WriteProfile(iniMaxPlayers + 8);
                    configuration.LoadINI(name, desyncPatch);
                    AssertSavedProfile(iniMaxPlayers + 8);
                    configuration.LoadINI(name, desyncPatch);
                    AssertSavedProfile(iniMaxPlayers + 8);
                }
                finally {
                    Directory.Delete(profilePath, true);
                }
            });
        }

        private static void RunSta(Action action) {
            if (!OperatingSystem.IsWindows()) {
                action();
                return;
            }
            Exception? error = null;
            var thread = new Thread(() => {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Profile test STA thread did not finish.");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}