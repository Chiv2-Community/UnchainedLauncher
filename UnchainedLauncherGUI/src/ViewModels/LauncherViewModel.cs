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
using System.Threading;
using LanguageExt;
using System.Collections.Immutable;
using LanguageExt.SomeHelp;

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

        public async Task LaunchModded(IEnumerable<string>? exArgs = null) {
            CanClick = false;

            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                ModManager.EnabledModReleases.AsEnumerable().ToSome(),
                Prelude.None
            );

            try {
                new Chivalry2Launcher()
                    .LaunchModded(InstallationType.Steam, options, Prelude.None, exArgs ?? new List<string>());
            } catch (Exception) {
                MessageBox.Show("Failed to launch Chivalry 2 Uncahined. Check the logs for details.");
                return;
            } finally {
                CanClick = true;
            }

        }
    }
}
