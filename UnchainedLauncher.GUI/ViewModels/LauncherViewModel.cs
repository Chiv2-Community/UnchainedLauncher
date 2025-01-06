﻿using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Views;
using List = System.Windows.Documents.List;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class LauncherViewModel : INotifyPropertyChanged {
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
        public IOfficialChivalry2Launcher VanillaLauncher { get; }
        public IOfficialChivalry2Launcher ClientSideModdedLauncher { get; }
        public IUnchainedChivalry2Launcher UnchainedLauncher { get; }

        public IReleaseLocator PluginReleaseLocator { get; }
        
        private IVersionExtractor<string> FileVersionExtractor { get; }
        private IUserDialogueSpawner UserDialogueSpawner { get; }

        private IVersionExtractor<string> FileVersionExtractor { get; }
        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public LauncherViewModel(SettingsViewModel settings, IModManager modManager, IOfficialChivalry2Launcher vanillaLauncher, IOfficialChivalry2Launcher clientSideModdedLauncher, IUnchainedChivalry2Launcher moddedLauncher, IReleaseLocator pluginReleaseLocator, IVersionExtractor<string> fileVersionExtractor, IUserDialogueSpawner dialogueSpawner) {
            Settings = settings;
            ModManager = modManager;

            VanillaLauncher = vanillaLauncher;
            ClientSideModdedLauncher = clientSideModdedLauncher;
            UnchainedLauncher = moddedLauncher;
            UserDialogueSpawner = dialogueSpawner;
            
            FileVersionExtractor = fileVersionExtractor;

            LaunchVanillaCommand = new AsyncRelayCommand(async () => await LaunchVanilla(false));
            LaunchModdedVanillaCommand = new AsyncRelayCommand(async () => await LaunchVanilla(true));
            LaunchUnchainedCommand = new AsyncRelayCommand(async () => await LaunchUnchained());

            PluginReleaseLocator = pluginReleaseLocator;
        }

        public async Task<Option<Process>> LaunchVanilla(bool enableMods) {
            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var args = Settings.CLIArgs;
            var launchResult = enableMods
                ? await VanillaLauncher.Launch(args)
                : await ClientSideModdedLauncher.Launch(args);

            if (!IsReusable())
                Settings.CanClick = false;

            return launchResult.Match(
                Left: error => {
                    UserDialogueSpawner.DisplayMessage("Failed to launch Chivalry 2. Check the logs for details.");
                    Settings.CanClick = true;
                    return None;
                },
                Right: process => {
                    CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        public async Task<Option<Process>> LaunchUnchained() {
            if (!IsReusable()) Settings.CanClick = false;

            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                None,
                None
            );

            var launchResult = await UnchainedLauncher.Launch(options, Settings.EnablePluginAutomaticUpdates, Settings.CLIArgs);

            return launchResult.Match(
                Left: _ => {
                    UserDialogueSpawner.DisplayMessage($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                    Settings.CanClick = true;

                    return None;
                },
                Right: process => {
                    CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        private Thread CreateChivalryProcessWatcher(Process process) {
            var thread = new Thread(async void () => {
                try {
                    await process.WaitForExitAsync();
                    if (IsReusable()) Settings.CanClick = true;

                    if (process.ExitCode == 0) return;

                    logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                    UserDialogueSpawner.DisplayMessage($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                }
                catch (Exception e) {
                    logger.Error("Failure occured while waiting for Chivalry process to exit", e);
                }
            });

            thread.Start();

            return thread;
        }
    }
}