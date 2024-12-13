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
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Views;
using UnchainedLauncher.Core.Installer;
using System.ComponentModel;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;
    
    public partial class LauncherViewModel: INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(LauncherViewModel));
        public ICommand LaunchVanillaCommand { get; }
        public ICommand LaunchModdedVanillaCommand { get; }
        public ICommand LaunchUnchainedCommand { get; }

        public SettingsViewModel Settings { get; }
        
        private IModManager ModManager { get; }

        public string ButtonToolTip =>
            (!Settings.CanClick && !IsReusable())
                ? "Unchained cannot launch an EGS installation more than once.  Restart the launcher if you wish to launch the game again."
                : "";
        public IChivalry2Launcher Launcher { get; }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public LauncherViewModel(SettingsViewModel settings, IModManager modManager, IChivalry2Launcher launcher) {
            Settings = settings;
            ModManager = modManager;

            this.LaunchVanillaCommand = new RelayCommand(() => LaunchVanilla(false));
            this.LaunchModdedVanillaCommand = new RelayCommand(() => LaunchVanilla(true));
            this.LaunchUnchainedCommand = new RelayCommand(() => LaunchUnchained());

            Launcher = launcher;
        }

        public Option<Process> LaunchVanilla(bool enableMods) {
            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var args = Environment.GetCommandLineArgs().Skip(1);
            var launchResult = enableMods
                ? Launcher.LaunchModdedVanilla(args)
                : Launcher.LaunchVanilla(args);
            
            if(!IsReusable())
                Settings.CanClick = false;

            return launchResult.Match(
                Left: error => {
                    MessageBox.Show("Failed to launch Chivalry 2. Check the logs for details.");
                    Settings.CanClick = true;
                    
                    return None;
                },
                Right: process =>
                {
                    CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        public Option<Process> LaunchUnchained()
        {
            if (!IsReusable()) Settings.CanClick = false;

            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                ModManager.EnabledModReleases.AsEnumerable().ToSome(),
                None
            );

            var exArgs = Settings.CLIArgs.Split(" ");

            var launchResult = Launcher.LaunchUnchained(options, exArgs);

            return launchResult.Match(
                Left: error =>
                {
                    MessageBox.Show($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                    Settings.CanClick = true;

                    return None;
                },
                Right: process =>
                {
                    CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        private Thread CreateChivalryProcessWatcher(Process process)
        {
            var thread = new Thread(async void () => {
                await process.WaitForExitAsync();
            
                if(IsReusable()) Settings.CanClick = true;
            
                if (process.ExitCode == 0) return;
            
                logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                MessageBox.Show($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
            });

            thread.Start();
            
            return thread;
        }
    }
}
