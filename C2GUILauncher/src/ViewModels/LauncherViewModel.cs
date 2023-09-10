using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using C2GUILauncher.src.ViewModels;

namespace C2GUILauncher.ViewModels {

    [AddINotifyPropertyChangedInterface]
    public class LauncherViewModel {
        public ICommand LaunchVanillaCommand { get; }
        public ICommand LaunchModdedCommand { get; }
        public ICommand LaunchServerCommand { get; }
        public ICommand LaunchServerHeadlessCommand { get; }

        private SettingsViewModel Settings { get; }
        private ServerSettingsViewModel ServerSettings { get; }

        private ModManager ModManager { get; }

        public bool CanClick { get; set; }

        private bool CLIArgsModified { get; set; }



        public LauncherViewModel(SettingsViewModel settings, ServerSettingsViewModel serverSettings, ModManager modManager) {
            CanClick = true;

            this.Settings = settings;
            this.ServerSettings = serverSettings;
            this.ModManager = modManager;

            this.LaunchVanillaCommand = new RelayCommand(LaunchVanilla);
            this.LaunchModdedCommand = new RelayCommand(() => LaunchModded(null)); //ugly wrapper lambda
            this.LaunchServerCommand = new RelayCommand(LaunchServer);
            this.LaunchServerHeadlessCommand = new RelayCommand(LaunchServerHeadless);
        }

        private void LaunchVanilla() {
            try {
                // For a vanilla launch we need to pass the args through to the vanilla launcher.
                // Skip the first arg which is the path to the exe.
                var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                Chivalry2Launchers.VanillaLauncher.Launch(args);
                CanClick = false;
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        private void LaunchModded(Process? serverRegister = null) {
            // For a modded installation we need to download the mod files and then launch via the modded launcher.
            // For steam installations, args do not get passed through.

            // Get the installation type. If auto detect fails, exit this function.
            var installationType = GetInstallationType();
            if (installationType == InstallationType.NotSet) return;

            // Pass args through if the args box has been modified, or if we're an EGS install
            var shouldSendArgs = installationType == InstallationType.EpicGamesStore || this.CLIArgsModified;

            // pass empty string for args, if we shouldn't send any.
            var args = shouldSendArgs ? this.Settings.CLIArgs : "";

            // Download the mod files, potentially using debug dlls
            var launchThread = new Thread(async () => {
                try {
                    if (this.Settings.EnablePluginAutomaticUpdates) {
                        List<DownloadTask> downloadTasks = this.ModManager.DownloadModFiles(this.Settings.EnablePluginLogging).ToList();
                        await Task.WhenAll(downloadTasks.Select(x => x.Task));
                    }
                    var dlls = Directory.EnumerateFiles(Chivalry2Launchers.PluginDir, "*.dll").ToArray();
                    Chivalry2Launchers.ModdedLauncher.Dlls = dlls;
                    var process = Chivalry2Launchers.ModdedLauncher.Launch(args);

                    serverRegister?.Start();
                    await process.WaitForExitAsync();
                    serverRegister?.CloseMainWindow();
                    Environment.Exit(0);
                } catch (Exception ex) {
                    MessageBox.Show(ex.ToString());
                }
            });

            launchThread.Start();
            CanClick = false;
        }

        private Process? makeRegistrationProcess() {
            if (!File.Exists("RegisterUnchainedServer.exe")) {
                DownloadTask serverRegisterDownload = HttpHelpers.DownloadFileAsync(
                "https://github.com/Chiv2-Community/C2ServerAPI/releases/latest/download/RegisterUnchainedServer.exe",
                "./RegisterUnchainedServer.exe"); //TODO: this breaks if `./` is not used as the directory. This is HttpHelpers' fault

                serverRegisterDownload.Task.Wait();
                if (!serverRegisterDownload.Task.IsCompletedSuccessfully) {
                    MessageBox.Show("Failed to download the Unchained server registration program:\n" +
                        serverRegisterDownload?.Task.Exception?.Message);
                    return null;
                }
            }

            Process serverRegister = new Process();
            //We *must* use cmd.exe as a wrapper to start RegisterUnchainedServer.exe, otherwise we have no way to
            //close the window later

            //TODO: Get this to actually be able to be closed
            serverRegister.StartInfo.FileName = "cmd.exe";
            
            string registerCommand = $"RegisterUnchainedServer.exe " +
                $"-n ^\"{ServerSettings.serverName.Replace("\"", "^\"")}^\" " +
                $"-d ^\"{ServerSettings.serverDescription.Replace("\"", "^\"").Replace("\n", "^\n")}^\" " +
                $"-r ^\"{ServerSettings.serverList}^\" " +
                $"-c ^\"{ServerSettings.rconPort}^\"";
            serverRegister.StartInfo.Arguments = $"/c \"{registerCommand}\"";
            serverRegister.StartInfo.CreateNoWindow = false;
            //MessageBox.Show($"{serverRegister.StartInfo.Arguments}");

            return serverRegister;
        }

        private void LaunchServer() {
            try {
                Process? serverRegister = makeRegistrationProcess();
                if (serverRegister == null) {
                    return;
                }
                CLIArgsModified = true;
                List<string> cliArgs = this.Settings.CLIArgs.Split(" ").ToList();
                cliArgs.Add($"-port {ServerSettings.gamePort}");

                this.Settings.CLIArgs = string.Join(" ", cliArgs);
                LaunchModded(serverRegister);
                
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
            
        }

        private void LaunchServerHeadless() {
            Process? serverRegister = makeRegistrationProcess();
            if (serverRegister == null) {
                return;
            }

            try {
                //modify command line args and enable required mods for RCON connectivity
                string RCONMap = "agmods?map=frontend?rcon"; //ensure the RCON zombie blueprint gets started
                CLIArgsModified = true;
                List<string> cliArgs = this.Settings.CLIArgs.Split(" ").ToList();
                int TBLloc = cliArgs.IndexOf("TBL");
                cliArgs.Insert(TBLloc, RCONMap);
                cliArgs.Add($"-port {ServerSettings.gamePort}");
                cliArgs.Add("-nullrhi"); //disable rendering
                //this isn't used, but will be needed later
                cliArgs.Add($"-rcon {ServerSettings.rconPort}"); //let the serverplugin know that we want RCON running on the given port
                cliArgs.Add("-RenderOffScreen"); //super-disable rendering
                cliArgs.Add("-unattended"); //let it know no one's around to help
                cliArgs.Add("-nosound"); //disable sound

                this.Settings.CLIArgs = string.Join(" ", cliArgs);
            } catch(Exception ex) {
                MessageBox.Show(ex.ToString());
            }

            LaunchModded(serverRegister);
        }

        private InstallationType GetInstallationType() {
            var installationType = this.Settings.InstallationType;
            if (installationType == InstallationType.NotSet) {
                installationType = InstallationTypeUtils.AutoDetectInstallationType();
                if (installationType == InstallationType.NotSet) {
                    MessageBox.Show("Could not detect installation type. Please select one manually.");
                }
            }

            return installationType;
        }



        private static class InstallationTypeUtils {
            const string SteamPathSearchString = "Steam";
            const string EpicGamesPathSearchString = "Epic Games";

            public static InstallationType AutoDetectInstallationType() {
                var currentDir = Directory.GetCurrentDirectory();
                return currentDir switch {
                    var _ when currentDir.Contains(SteamPathSearchString) => InstallationType.Steam,
                    var _ when currentDir.Contains(EpicGamesPathSearchString) => InstallationType.EpicGamesStore,
                    _ => InstallationType.NotSet,
                };
            }
        }
    }
}
