using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.SomeHelp;
using log4net;
using Semver;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Views;

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

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public LauncherViewModel(SettingsViewModel settings, IModManager modManager, IOfficialChivalry2Launcher vanillaLauncher, IOfficialChivalry2Launcher clientSideModdedLauncher, IUnchainedChivalry2Launcher moddedLauncher, IReleaseLocator pluginReleaseLocator) {
            Settings = settings;
            ModManager = modManager;

            VanillaLauncher = vanillaLauncher;
            ClientSideModdedLauncher = clientSideModdedLauncher;
            UnchainedLauncher = moddedLauncher;


            this.LaunchVanillaCommand = new RelayCommand(() => LaunchVanilla(false));
            this.LaunchModdedVanillaCommand = new RelayCommand(() => LaunchVanilla(true));
            this.LaunchUnchainedCommand = new AsyncRelayCommand(async () => await LaunchUnchained());

            PluginReleaseLocator = pluginReleaseLocator;
        }

        public Option<Process> LaunchVanilla(bool enableMods) {
            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var args = Settings.CLIArgs;
            var launchResult = enableMods
                ? VanillaLauncher.Launch(args)
                : ClientSideModdedLauncher.Launch(args);

            if (!IsReusable())
                Settings.CanClick = false;

            return launchResult.Match(
                Left: error => {
                    MessageBox.Show("Failed to launch Chivalry 2. Check the logs for details.");
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
            var shouldContinue = await UpdatePlugin();

            if (!shouldContinue)
                return None;

            if (!IsReusable()) Settings.CanClick = false;


            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                ModManager.EnabledModReleases.AsEnumerable().ToSome(),
                None,
                None
            );

            var launchResult = UnchainedLauncher.Launch(options, Settings.CLIArgs);

            return launchResult.Match(
                Left: error => {
                    MessageBox.Show($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                    Settings.CanClick = true;

                    return Prelude.None;
                },
                Right: process => {
                    CreateChivalryProcessWatcher(process);
                    return Prelude.Some(process);
                }
            );
        }

        private async Task<bool> UpdatePlugin() {
            var pluginPath = Path.Combine(Directory.GetCurrentDirectory(), FilePaths.UnchainedPluginPath);
            var pluginExists = File.Exists(pluginPath);

            if (!Settings.EnablePluginAutomaticUpdates && pluginExists) return true;

            var latestPlugin = await PluginReleaseLocator.GetLatestRelease();
            if (latestPlugin == null) return false;

            SemVersion? currentPluginVersion = null;
            if (pluginExists) {
                var fileInfo = FileVersionInfo.GetVersionInfo(pluginPath);

                var versionString = fileInfo.ProductVersion ?? fileInfo.FileVersion;
                logger.Debug("Raw plugin version: " + versionString);
                var splitVersionString = versionString.Split('.');
                versionString = String.Join('.', splitVersionString.Take(3));
                logger.Debug("Cleaned plugin version: " + versionString);

                var successful = SemVersion.TryParse(
                    versionString,
                    SemVersionStyles.Any,
                    out currentPluginVersion
                );

                // If new version is the same as or less than current, don't download anything
                if (successful && currentPluginVersion.ComparePrecedenceTo(latestPlugin.Version) >= 0) return true;
            }

            var titleString = pluginExists
                ? "Update Unchained Plugin"
                : "Install Unchained Plugin";

            var messageText = pluginExists
                ? "Updates for the Unchained Plugin are available."
                : "The unchained plugin is not installed.";


            var userResponse = UpdatesWindow.Show(
                titleString,
                messageText,
                "Yes",
                "No",
                "Cancel",
                new DependencyUpdate(
                    "UnchainedPlugin.dll",
                    Optional(currentPluginVersion?.ToString()),
                    latestPlugin.Version.ToString(),
                    latestPlugin.PageUrl,
                    "Used for hosting and connecting to player owned servers. Required to run Chivalry 2 Unchained."
                )
            );

            // The cases in here are all for exiting early.
            switch (userResponse) {
                // Continue launch, don't download or install anything
                case MessageBoxResult.No:
                    return true;
                // Do not continue launch, don't download or install anything
                case MessageBoxResult.Cancel:
                case MessageBoxResult.None:
                    return false;

            }

            var downloadResult = await HttpHelpers.DownloadReleaseTarget(
                latestPlugin,
                asset => (asset.Name == "UnchainedPlugin.dll") ? pluginPath : null
            );

            if (downloadResult == false)
                MessageBox.Show("Failed to download Unchained Plugin. Aborting launch. Check the logs for more details.");

            return downloadResult;
        }

        private Thread CreateChivalryProcessWatcher(Process process) {
            var thread = new Thread(async void () => {
                try {
                    await process.WaitForExitAsync();
                    if (IsReusable()) Settings.CanClick = true;

                    if (process.ExitCode == 0) return;

                    logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                    MessageBox.Show($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
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