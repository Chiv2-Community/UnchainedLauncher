using C2GUILauncher.JsonModels;
using C2GUILauncher.JsonModels.Metadata.V2;
using C2GUILauncher.Mods;
using CommunityToolkit.Mvvm.Input;
using Octokit;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

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

        private readonly Window Window;


        public LauncherViewModel(Window window, SettingsViewModel settings, ServerSettingsViewModel serverSettings, ModManager modManager) {
            CanClick = true;

            this.Settings = settings;
            this.ServerSettings = serverSettings;
            this.ModManager = modManager;

            this.LaunchVanillaCommand = new RelayCommand(LaunchVanilla);
            this.LaunchModdedCommand = new RelayCommand(
                () => LaunchModded(BuildModsString())
                //() => LaunchModded(BuildModsString())
            ); //ugly wrapper lambda
            this.LaunchServerCommand = new RelayCommand(LaunchServer);
            this.LaunchServerHeadlessCommand = new RelayCommand(LaunchServerHeadless);
            this.Window = window;
        }

        private void LaunchVanilla() {
            try {
                // For a vanilla launch we need to pass the args through to the vanilla launcher.
                // Skip the first arg which is the path to the exe.
                var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                Chivalry2Launchers.VanillaLauncher.Launch(args);
                CanClick = false;
                Window.Close();
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        private void LaunchModded(string mapTarget, string[]? exArgs = null, Process? serverRegister = null) {
            // For a modded installation we need to download the mod files and then launch via the modded launcher.
            // For steam installations, args do not get passed through.

            // Get the installation type. If auto detect fails, exit this function.
            var installationType = GetInstallationType();
            if (installationType == InstallationType.NotSet) return;

            // Pass args through if the args box has been modified, or if we're an EGS install
            var shouldSendArgs = installationType == InstallationType.EpicGamesStore || this.Settings.CLIArgsModified;

            // pass empty string for args, if we shouldn't send any.
            var args = shouldSendArgs ? this.Settings.CLIArgs : "";

            //setup necessary cli args for a modded launch
            List<string> cliArgs = args.Split(" ").ToList();
            int TBLloc = cliArgs.IndexOf("TBL") + 1;

            //add map target for agmods built by caller. This looks like "agmods?map=frontend?mods=...?rcon"
            cliArgs.Insert(TBLloc, mapTarget);
            //add extra args like -nullrhi or -rcon
            if (exArgs != null) {
                cliArgs.AddRange(exArgs);
            }

            args = string.Join(" ", cliArgs);

            // Download the mod files, potentially using debug dlls
            var launchThread = new Thread(async () => {
                try {
                    if (this.Settings.EnablePluginAutomaticUpdates) {
                        List<ModReleaseDownloadTask> downloadTasks = this.ModManager.DownloadModFiles(this.Settings.EnablePluginLogging).ToList();
                        await Task.WhenAll(downloadTasks.Select(x => x.DownloadTask.Task));
                    }
                    var dlls = Directory.EnumerateFiles(Chivalry2Launchers.PluginDir, "*.dll").ToArray();
                    Chivalry2Launchers.ModdedLauncher.Dlls = dlls;
                    var process = Chivalry2Launchers.ModdedLauncher.Launch(args);

                    serverRegister?.Start();

                    await Window.Dispatcher.BeginInvoke((Action)delegate () {
                        Window.Hide();
                    });

                    await process.WaitForExitAsync();
                    serverRegister?.CloseMainWindow();
                    await Window.Dispatcher.BeginInvoke((Action)delegate () {
                        Window.Close();
                    });
                } catch (Exception ex) {
                    MessageBox.Show(ex.ToString());
                }
            });

            launchThread.Start();
            CanClick = false;
        }

        private async Task<Process?> MakeRegistrationProcess() {
            if (!File.Exists("RegisterUnchainedServer.exe")) {
                DownloadTask serverRegisterDownload = HttpHelpers.DownloadFileAsync(
                "https://github.com/Chiv2-Community/C2ServerAPI/releases/latest/download/RegisterUnchainedServer.exe",
                "./RegisterUnchainedServer.exe"); //TODO: this breaks if `./` is not used as the directory. This is HttpHelpers' fault

                try {
                    await serverRegisterDownload.Task;
                } catch (Exception e) {
                    MessageBox.Show("Failed to download the Unchained server registration program:\n" + e.Message);
                    return null;
                }
            }

            Process serverRegister = new Process();
            //We *must* use cmd.exe as a wrapper to start RegisterUnchainedServer.exe, otherwise we have no way to
            //close the window later

            //TODO: Get this to actually be able to be closed
            serverRegister.StartInfo.FileName = "cmd.exe";

            string registerCommand = $"RegisterUnchainedServer.exe " +
                $"-n ^\"{ServerSettings.ServerName.Replace("\"", "^\"")}^\" " +
                $"-d ^\"{ServerSettings.ServerDescription.Replace("\"", "^\"").Replace("\n", "^\n")}^\" " +
                $"-r ^\"{ServerSettings.ServerList}^\" " +
                $"-c ^\"{ServerSettings.RconPort}^\"";
            serverRegister.StartInfo.Arguments = $"/c \"{registerCommand}\"";
            serverRegister.StartInfo.CreateNoWindow = false;
            //MessageBox.Show($"{serverRegister.StartInfo.Arguments}");

            return serverRegister;
        }

        private string BuildModsString(bool server = false) {
            if (this.ModManager.EnabledModReleases.Any()) {
                string modsString = this.ModManager.EnabledModReleases
                    .Select(mod => mod.Manifest)
                    .Where(manifest => manifest.ModType == ModType.Server || manifest.ModType == ModType.Shared)
                    .Where(manifest => manifest.AgMod)
                    .Select(manifest => manifest.Name.Replace(" ", ""))
                    .Aggregate("", (agg, name) => agg + name + ",");

                bool hasAdditional = this.Settings.AdditionalModActors != "";
                if (modsString != "" && hasAdditional)
                {
                    modsString = "--all-mod-actors " + modsString;
                    if (hasAdditional)
                        modsString += this.Settings.AdditionalModActors;
                    else
                        modsString = modsString[..^1]; //cut off dangling comma
                }

                //return modsString+ " --default-mod-actors ModMenu,FrontendMod --next-map-name to_coxwell --next-map-mod-actors GiantSlayers,FilthyPeasants";
                return modsString;
            } else {
                return "";
            }
        }

        private async void LaunchServer() {
            try {
                Process? serverRegister = await MakeRegistrationProcess();
                if (serverRegister == null) {
                    return;
                }

                string loaderMap = "agmods?map=frontend" + BuildModsString() + "?listen";
                string[] exArgs = { $"-port {ServerSettings.GamePort}" };

                LaunchModded(loaderMap, exArgs, serverRegister);

            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }

        }

        private async void LaunchServerHeadless() {
            Process? serverRegister = await MakeRegistrationProcess();
            if (serverRegister == null) {
                return;
            }

            try {
                //modify command line args and enable required mods for RCON connectivity
                //string RCONMap = "?rcon" + BuildModsString(); //ensure the RCON zombie blueprint gets started
                string RCONMap = BuildModsString();

                //MessageBox.Show(RCONMap);

                string[] exArgs = {
                    $"-port {ServerSettings.GamePort}", //specify server port
                    "-nullrhi", //disable rendering
                    $"-rcon {ServerSettings.RconPort}", //let the serverplugin know that we want RCON running on the given port
                    "-RenderOffScreen", //super-disable rendering
                    "-unattended", //let it know no one's around to help
                    "-nosound", //disable sound
                    "--next-map-name to_coxwell"
                };
                LaunchModded(RCONMap, exArgs, serverRegister);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
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
