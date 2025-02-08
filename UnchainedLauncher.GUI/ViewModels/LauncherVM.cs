using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;
    public partial class LauncherVM : INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(LauncherVM));

        public SettingsVM Settings { get; }

        public string ButtonToolTip =>
            (!Settings.CanClick && !IsReusable())
                ? "Unchained cannot launch an EGS installation more than once.  Restart the launcher if you wish to launch the game again."
                : "";
        public IChivalry2Launcher VanillaLauncher { get; }
        public IChivalry2Launcher ClientSideModdedLauncher { get; }
        public IChivalry2Launcher UnchainedLauncher { get; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }
        private IModManager ModManager { get; }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public LauncherVM(SettingsVM settings, IModManager modManager, IChivalry2Launcher vanillaLauncher, IChivalry2Launcher clientSideModdedLauncher, IChivalry2Launcher moddedLauncher, IUserDialogueSpawner dialogueSpawner) {
            Settings = settings;
            ModManager = modManager;
            VanillaLauncher = vanillaLauncher;
            ClientSideModdedLauncher = clientSideModdedLauncher;
            UnchainedLauncher = moddedLauncher;
            UserDialogueSpawner = dialogueSpawner;
        }

        [RelayCommand]
        public Task<Option<Process>> LaunchVanilla() => InternalLaunchVanilla(false);

        [RelayCommand]
        public Task<Option<Process>> LaunchModdedVanilla() => InternalLaunchVanilla(true);

        private async Task<Option<Process>> InternalLaunchVanilla(bool enableMods) {
            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var args = Settings.CLIArgs;
            var launchResult = enableMods
                ? await ClientSideModdedLauncher.Launch(args,
                        new ModdedLaunchOptions(
                            ModManager.GetEnabledAndDependencies(),
                            "",
                            false,
                            None,
                            None
                        )
                    )
                : await VanillaLauncher.Launch(args,
                    new ModdedLaunchOptions(
                        new List<ReleaseCoordinates>(),
                        "",
                        false,
                        None,
                        None)
                    );

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

        [RelayCommand]
        public async Task<Option<Process>> LaunchUnchained() {
            if (!IsReusable()) Settings.CanClick = false;

            var options = new ModdedLaunchOptions(
                ModManager.GetEnabledAndDependencies(),
                Settings.ServerBrowserBackend,
                Settings.EnablePluginAutomaticUpdates,
                None,
                None
            );

            var launchResult = await UnchainedLauncher.Launch(Settings.CLIArgs, options);

            return launchResult.Match(
                Left: e => {
                    Logger.Error(e);
                    if (e.Underlying is not UnchainedLaunchFailure.LaunchCancelledError)
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
            //TODO: add a Process.Exited event instead. 
            var thread = new Thread(async void () => {
                try {
                    await process.WaitForExitAsync();
                    if (IsReusable()) Settings.CanClick = true;

                    if (process.ExitCode == 0) return;

                    Logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                    UserDialogueSpawner.DisplayMessage($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                }
                catch (Exception e) {
                    Logger.Error("Failure occured while waiting for Chivalry process to exit", e);
                }
            });

            thread.Start();

            return thread;
        }
    }
}