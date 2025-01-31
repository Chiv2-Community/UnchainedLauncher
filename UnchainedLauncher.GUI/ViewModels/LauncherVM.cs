using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.GUI.ViewModels {

    public partial class LauncherVM : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(LauncherVM));

        public SettingsVM Settings { get; }

        public string ButtonToolTip =>
            (!Settings.CanClick && !IsReusable())
                ? "Unchained cannot launch an EGS installation more than once.  Restart the launcher if you wish to launch the game again."
                : "";
        public IOfficialChivalry2Launcher VanillaLauncher { get; }
        public IOfficialChivalry2Launcher ClientSideModdedLauncher { get; }
        public IUnchainedChivalry2Launcher UnchainedLauncher { get; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public LauncherVM(SettingsVM settings, IOfficialChivalry2Launcher vanillaLauncher, IOfficialChivalry2Launcher clientSideModdedLauncher, IUnchainedChivalry2Launcher moddedLauncher, IUserDialogueSpawner dialogueSpawner) {
            Settings = settings;

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

        [RelayCommand]
        public async Task<Option<Process>> LaunchUnchained() {
            if (!IsReusable()) Settings.CanClick = false;

            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                Settings.EnablePluginAutomaticUpdates,
                None,
                None
            );

            var launchResult = await UnchainedLauncher.Launch(options, Settings.EnablePluginAutomaticUpdates, Settings.CLIArgs);

            return launchResult.Match(
                Left: e => {
                    logger.Error(e);
                    if (e is not UnchainedLaunchFailure.LaunchCancelledError)
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