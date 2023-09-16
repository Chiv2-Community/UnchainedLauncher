using C2GUILauncher.JsonModels;
using C2GUILauncher.JsonModels.Metadata.V3;
using C2GUILauncher.Mods;
using CommunityToolkit.Mvvm.Input;
using log4net;
using log4net.Repository.Hierarchy;
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
            this.LaunchModdedCommand = new RelayCommand(() => LaunchModded());

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

        public void LaunchModded(string[]? exArgs = null, Process? serverRegister = null) {
            // Pass args through if the args box has been modified, or if we're an EGS install
            var shouldSendArgs = Settings.InstallationType == InstallationType.EpicGamesStore || Settings.CLIArgsModified;

            // pass empty string for args, if we shouldn't send any.
            var args = shouldSendArgs ? this.Settings.CLIArgs : "";

            //setup necessary cli args for a modded launch
            List<string> cliArgs = args.Split(" ").ToList();
            int TBLloc = cliArgs.IndexOf("TBL") + 1;

            BuildModArgs().ToList().ForEach(arg => cliArgs.Insert(TBLloc, arg));
            if (exArgs != null) {
                cliArgs.AddRange(exArgs);
            }

            cliArgs = cliArgs.Select(x => x.Trim()).Where(x => x != null && x != "").ToList();

            var maybeThread = Launcher.LaunchModded(Window, Settings.InstallationType, cliArgs, Settings.EnablePluginAutomaticUpdates, Settings.EnablePluginLogging, serverRegister);

            if (maybeThread == null) {
                MessageBox.Show("Failed to launch game. Please select an InstallationType if one isn't set.");
                return;
            }

            CanClick = false;
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

                string globalModsListString =
                    BuildCommaSeparatedArgsList(
                        "default-mod-actors",
                        serverMods
                            .Where(manifest => manifest.OptionFlags.GlobalMod)
                            .Select(manifest => manifest.Name.Replace(" ", ""))
                            .ToList()
                    );

                return new string[] { modActorsListString, globalModsListString }.Where(x => x.Trim() != "").ToArray();
            } else {
                return Array.Empty<string>();
            }
        }

        private static string BuildCommaSeparatedArgsList(string argName, IEnumerable<string> args, string extraArgs = "") {
            if(args.Any() && extraArgs == "") {
                return "";
            }

            var argsString = args.Aggregate("", (agg, name) => agg + name + ",");
            if(extraArgs != "") {
                argsString += extraArgs;
            } else if(argsString != ""){
                argsString = argsString[..^1];
            }

            return $"--{argName} {argsString}";
        }
    }
}
