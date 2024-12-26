using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.SomeHelp;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Utilities.Releases;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Views;
using Environment = UnchainedLauncher.Core.API.A2S.Environment;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class LauncherViewModel : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(LauncherViewModel));
        public ICommand LaunchVanillaCommand { get; }
        public ICommand LaunchModdedVanillaCommand { get; }
        public ICommand LaunchUnchainedCommand { get; }

        private SettingsViewModel Settings { get; }
        private ModManager ModManager { get; }

        public bool CanClick { get; set; }

        public string ButtonToolTip =>
            (!CanClick && !IsReusable())
                ? "Unchained cannot launch an EGS installation more than once.  Restart the launcher if you wish to launch the game again."
                : "";
        public Chivalry2Launcher Launcher { get; }

        public IReleaseLocator PluginReleaseLocator { get; }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public LauncherViewModel(SettingsViewModel settings, ModManager modManager, Chivalry2Launcher launcher, IReleaseLocator pluginReleaseLocator) {
            CanClick = true;

            Settings = settings;
            ModManager = modManager;

            LaunchVanillaCommand = new RelayCommand(() => LaunchVanilla(false));
            LaunchModdedVanillaCommand = new RelayCommand(() => LaunchVanilla(true));
            LaunchUnchainedCommand = new AsyncRelayCommand(() => LaunchUnchained(Prelude.None));

            Launcher = launcher;
            PluginReleaseLocator = pluginReleaseLocator;
        }

        public Option<Process> LaunchVanilla(bool enableMods) {
            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var args = System.Environment.GetCommandLineArgs().Skip(1);
            var launchResult = enableMods
                ? Launcher.LaunchModdedVanilla(args)
                : Launcher.LaunchVanilla(args);

            if (!IsReusable())
                CanClick = false;

            return launchResult.Match(
                Left: error => {
                    MessageBox.Show("Failed to launch Chivalry 2. Check the logs for details.");
                    CanClick = true;

                    return Prelude.None;
                },
                Right: process => {
                    CreateChivalryProcessWatcher(process);
                    return Prelude.Some(process);
                }
            );
        }

        public async Task<Option<Process>> LaunchUnchained(Option<ServerLaunchOptions> serverOpts) {
            var shouldContinue = await UpdatePlugin();

            if (!shouldContinue)
                return None;

            if (!IsReusable())
                CanClick = false;

            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                ModManager.EnabledModReleases.AsEnumerable().ToSome(),
                Prelude.None
            );

            var exArgs = Settings.CLIArgs.Split(" ");

            var launchResult = Launcher.LaunchUnchained(Settings.InstallationType, options, serverOpts, exArgs);

            return launchResult.Match(
                None: () => {
                    MessageBox.Show("Installation type not set. Please set it in the settings tab.");
                    return Prelude.None;
                },
                Some: errorOrSuccess => errorOrSuccess.Match(
                        Left: error => {
                            MessageBox.Show($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                            CanClick = true;

                            return Prelude.None;
                        },
                        Right: process => {
                            CreateChivalryProcessWatcher(process);
                            return Prelude.Some(process);
                        }
                    )
                );
        }

        private async Task<bool> UpdatePlugin() {
            if (!Settings.EnablePluginAutomaticUpdates && pluginExists) return true;

          
        }

        private Thread CreateChivalryProcessWatcher(Process process) {
            var thread = new Thread(async void () => {
                await process.WaitForExitAsync();

                if (IsReusable()) CanClick = true;

                if (process.ExitCode == 0) return;

                logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                MessageBox.Show($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
            });

            thread.Start();

            return thread;
        }
    }
}