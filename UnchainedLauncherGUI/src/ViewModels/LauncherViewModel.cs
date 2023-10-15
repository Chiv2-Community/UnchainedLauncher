using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core;

namespace UnchainedLauncher.GUI.ViewModels {

    [AddINotifyPropertyChangedInterface]
    public class LauncherViewModel {
        private static readonly ILog logger = LogManager.GetLogger(nameof(LauncherViewModel));
        public ICommand LaunchVanillaCommand { get; }
        public ICommand LaunchModdedCommand { get; }

        private SettingsViewModel Settings { get; }
        private ModManager ModManager { get; }

        public bool CanClick { get; set; }

        private readonly Window Window;

        public Chivalry2Launcher Launcher { get; }


        public LauncherViewModel(Window window, SettingsViewModel settings, ModManager modManager, Chivalry2Launcher launcher) {
            CanClick = true;

            Settings = settings;
            ModManager = modManager;

            this.LaunchVanillaCommand = new RelayCommand(LaunchVanilla);
            this.LaunchModdedCommand = new RelayCommand(async () => await LaunchModded());

            Window = window;

            Launcher = launcher;
        }

        public void LaunchVanilla() {
            try {
                // For a vanilla launch we need to pass the args through to the vanilla launcher.
                // Skip the first arg which is the path to the exe.
                Launcher.LaunchVanilla(Environment.GetCommandLineArgs().Skip(1));
                CanClick = false;
                Window.Close();
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        public async Task LaunchModded(IEnumerable<string>? exArgs = null, Process? serverRegister = null) {
            CanClick = false;

            await EnableUnchainedMods();

            // Pass args through if the args box has been modified, or if we're an EGS install
            var shouldSendArgs = Settings.InstallationType == InstallationType.EpicGamesStore || Settings.CLIArgsModified;

            // pass empty string for args, if we shouldn't send any.
            var args = shouldSendArgs ? this.Settings.CLIArgs : "";

            var serverBrowserBackendArg = "--server-browser-backend " + Settings.ServerBrowserBackend.Trim();

            //setup necessary cli args for a modded launch
            List<string> cliArgs = args.Split(" ").ToList();
            int TBLloc = cliArgs.IndexOf("TBL") + 1;

            BuildModArgs().ToList().ForEach(arg => cliArgs.Insert(TBLloc, arg));
            if (exArgs != null) {
                cliArgs.AddRange(exArgs);
            }

            cliArgs.Add(serverBrowserBackendArg);
            cliArgs = cliArgs.Where(x => x != null).Select(x => x.Trim()).Where(x => x.Any()).ToList();

            try {
                await Window.Dispatcher.BeginInvoke(delegate () { Window.Hide(); });
                var maybeThread = Launcher.LaunchModded(Settings.InstallationType, cliArgs, serverRegister);
                if (maybeThread != null) {
                    maybeThread.Join();
                    await Window.Dispatcher.BeginInvoke(delegate () { Window.Close(); });
                }
            } catch (Exception) {
                MessageBox.Show("Failed to launch Chivalry 2 Uncahined. Check the logs for details.");
                return;
            } finally {
                CanClick = true;
            }

        }

        private async Task EnableUnchainedMods() {
            try {
                if (!this.ModManager.EnabledModReleases.Any(x => x.Manifest.RepoUrl.EndsWith("Chiv2-Community/Unchained-Mods"))) {
                    logger.Info("Unchained-Mods mod not enabled. Enabling.");
                    var hasModList = this.ModManager.Mods.Any();

                    if (!hasModList)
                        await this.ModManager.UpdateModsList();

                    var latestUnchainedMod = this.ModManager.Mods.First(x => x.LatestManifest.RepoUrl.EndsWith("Chiv2-Community/Unchained-Mods")).Releases.First();
                    var modReleaseDownloadTask = this.ModManager.EnableModRelease(latestUnchainedMod);

                    await modReleaseDownloadTask.DownloadTask.Task;
                }

                List<ModReleaseDownloadTask> downloadTasks = this.ModManager.DownloadModFiles(Settings.EnablePluginAutomaticUpdates).ToList();
                await Task.WhenAll(downloadTasks.Select(x => x.DownloadTask.Task));
            } catch (Exception ex) {
                logger.Error("Failed to download mods and plugins.", ex);
                var result = MessageBox.Show("Failed to download mods and plugins. Check the logs for details. Continue Anyway?", "Continue Launching Chivalry 2 Unchained?", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.No) {
                    logger.Info("Cancelling launch.");
                    return;
                }

                logger.Info("Continuing launch.");
            }
        }

        public string[] BuildModArgs() {
            if (ModManager.EnabledModReleases.Any()) {
                var serverMods = ModManager.EnabledModReleases
                    .Select(mod => mod.Manifest)
                    .Where(manifest => manifest.ModType == ModType.Server || manifest.ModType == ModType.Shared);

                string modActorsListString = 
                    BuildCommaSeparatedArgsList(
                        "all-mod-actors", 
                        serverMods
                            .Where(manifest => manifest.OptionFlags.ActorMod)
                            .Select(manifest => manifest.Name.Replace(" ", "")),
                        Settings.AdditionalModActors
                    );

                // same as modActorsListString for now. Just turn on all the enabled mods for first map.
                string nextMapModActors =
                    BuildCommaSeparatedArgsList(
                        "next-map-mod-actors",
                        serverMods
                            .Where(manifest => manifest.OptionFlags.ActorMod)
                            .Select(manifest => manifest.Name.Replace(" ", "")),
                        Settings.AdditionalModActors
                    );

                return new string[] { modActorsListString, nextMapModActors }.Where(x => x.Trim() != "").ToArray();
            } else {
                return Array.Empty<string>();
            }
        }

        private static string BuildCommaSeparatedArgsList(string argName, IEnumerable<string> args, string extraArgs = "") {
            if(!args.Any() && extraArgs == "") {
                return "";
            }

            var argsString = args.Aggregate("", (agg, name) => agg + name + ",");
            if(extraArgs != "") {
                argsString += extraArgs;
            } else if(argsString != ""){
                argsString = argsString[..^1];
            } else {
                return "";
            }

            return $"--{argName} {argsString}";
        }
    }
}
