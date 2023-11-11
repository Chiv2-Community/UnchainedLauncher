using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using log4net.Repository.Hierarchy;
using log4net;
using C2GUILauncher.Views;
using System.Runtime.CompilerServices;
using C2GUILauncher.JsonModels.Metadata.V3;
using Semver;
using C2GUILauncher.ViewModels;

namespace C2GUILauncher {
    public class Chivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Launcher));
        public static string GameBinPath = FilePaths.BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public static string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";
        public static GithubReleaseSynchronizer PluginSynchronizer = 
            new GithubReleaseSynchronizer(
                "Chiv2-Community",
                "UnchainedPlugin",
                "UnchainedPlugin.dll",
                CoreMods.UnchainedPluginPath
            );

        private static readonly HashSet<int> GracefulExitCodes = new HashSet<int> { 0, -1073741510 };

        /// <summary>
        /// The original launcher is used to launch the game with no mods.
        /// </summary>
        private static ProcessLauncher VanillaLauncher { get; } = new ProcessLauncher(OriginalLauncherPath, Directory.GetCurrentDirectory());

        /// <summary>
        /// The modded launcher is used to launch the game with mods. The DLLs here are the relative paths to the DLLs that are to be injected.
        /// </summary>
        private static ProcessLauncher ModdedLauncher { get; } = new ProcessLauncher(GameBinPath, FilePaths.BinDir);

        private ModManager ModManager { get; }
        public Chivalry2Launcher(ModManager modManager) {
            ModManager = modManager;
        }

        public Process LaunchVanilla(IEnumerable<string> args) {
            logger.Info("Attempting to launch vanilla game.");
            LogList("Launch args: ", args);
            return VanillaLauncher.Launch(string.Join(" ", args));
        }

        public async Task<Thread?> LaunchModded(Window window, InstallationType installationType, List<string> args, bool checkForPluginUpdates, Process? serverRegister = null) {
            if (installationType == InstallationType.NotSet) return null;

            try {
                await UpdateAndInstallBaseFiles(window, checkForPluginUpdates);

                var downloadTasks = this.ModManager.DownloadAndValidateMods();
                Task.WaitAll(downloadTasks.Select(x => x.DownloadTask.Task).ToArray());
            } catch (DownloadCancelledException ex) {
                logger.Info(ex);
                return new Thread(() => logger.Info("Cancelling launch."));
            } catch (AggregateException ex) {
                if (ex.InnerExceptions.Count == 1) {
                    if (ex.InnerException is DownloadCancelledException dce) {
                        logger.Info(dce);
                        return new Thread(() => logger.Info("Cancelling launch."));
                    }
                } else {
                    logger.Error("Failed to download mods and plugins.", ex);
                    var result = MessageBox.Show("Failed to download mods and plugins. Check the logs for details. Continue Anyway?", "Continue Launching Chivalry 2 Unchained?", MessageBoxButton.YesNo);

                    if (result == MessageBoxResult.No) {
                        return new Thread(() => logger.Info("Cancelling launch."));
                    }

                    logger.Info("Continuing launch.");
                }
            }

            logger.Info("Attempting to launch modded game.");


            // Download the mod files, potentially using debug dlls
            var launchThread = new Thread(() => {
                try {
                    var dlls = Directory.EnumerateFiles(FilePaths.PluginDir, "*.dll").ToArray();
                    ModdedLauncher.Dlls = dlls;

                    LogList($"Mods Enabled:", ModManager.EnabledModReleases.Select(mod => mod.Manifest.Name + " " + mod.Tag));
                    LogList($"Launch args:", args);

                    serverRegister?.Start();

                    var restartOnCrash = serverRegister != null;

                    do {
                        logger.Info("Starting Chivalry 2 Unchained.");
                        var process = ModdedLauncher.Launch(string.Join(" ", args));

                        window.Dispatcher.BeginInvoke(delegate () { window.Hide(); }).Wait();
                        process.WaitForExitAsync().Wait();

                        var exitedGracefully = GracefulExitCodes.Contains(process.ExitCode);
                        if(!restartOnCrash || exitedGracefully) break;

                        window.Dispatcher.BeginInvoke(delegate () { window.Show(); }).Wait();

                        logger.Info($"Detected Chivalry2 crash (Exit code {process.ExitCode}). Restarting in 10 seconds. You may close the launcher while it is visible to prevent further restarts.");
                        Task.Delay(10000).Wait();
                    } while (true);

                    logger.Info("Process exited. Closing RCON and UnchainedLauncher.");

                    serverRegister?.CloseMainWindow();

                    window.Dispatcher.BeginInvoke(delegate () { window.Close(); }).Wait();

                } catch (Exception ex) {
                    logger.Error(ex);
                    MessageBox.Show("Failed to launch Chivalry 2 Uncahined. Check the logs for details.");
                }
            });

            return launchThread;
        }

        public async Task UpdateAndInstallBaseFiles(Window window, bool checkForPluginUpdates) {
            Release? unchainedModsLatestRelease = null;
            if (!this.ModManager.EnabledModReleases.Any(x => x.Manifest.RepoUrl.EndsWith("Chiv2-Community/Unchained-Mods"))) {
                var hasModList = this.ModManager.Mods.Any();

                if (!hasModList)
                    await this.ModManager.UpdateModsList();

                unchainedModsLatestRelease = this.ModManager.Mods.First(x => x.LatestManifest.RepoUrl.EndsWith("Chiv2-Community/Unchained-Mods")).Releases.First();
            }

            var isPluginInstalled = false;
            try {
                isPluginInstalled = File.Exists(CoreMods.UnchainedPluginPath);
            } catch (Exception ex) {
                logger.Error("Failed to check if UnchainedPlugin.dll is installed. Assuming it is not.", ex);
            }

            SemVersion? currentPluginVersion = null;
            SemVersion? latestPluginVersion = null;

            string? latestUnchainedPluginUrl = null;
            string? latestUnchainedPluginTag = null;


            if (checkForPluginUpdates || !isPluginInstalled) {
                try {
                    currentPluginVersion = PluginSynchronizer.GetCurrentVersion();

                    if (currentPluginVersion == null)
                        currentPluginVersion = SemVersion.Parse(File.ReadAllText(FilePaths.UnchainedPluginVersionPath), SemVersionStyles.AllowV);
                } catch (FileNotFoundException) {
                    logger.Warn("Failed to find UnchainedPlugin version file. Assuming no version.");
                } catch (FormatException) {
                    logger.Warn("Failed to parse UnchainedPlugin version file. Assuming no version.");
                }

                if (currentPluginVersion != null) 
                    latestUnchainedPluginTag = await PluginSynchronizer.CheckForUpdates(currentPluginVersion);
                else
                    latestUnchainedPluginTag = await PluginSynchronizer.GetLatestTag();

                if (latestUnchainedPluginTag != null)
                    latestPluginVersion = SemVersion.Parse(latestUnchainedPluginTag, SemVersionStyles.AllowV);
            }

            var updates = new List<DependencyUpdate>();

            if (unchainedModsLatestRelease != null) {
                updates.Add(new DependencyUpdate(
                    unchainedModsLatestRelease.Manifest.Name,
                    null,
                    unchainedModsLatestRelease.Tag,
                    unchainedModsLatestRelease.Manifest.RepoUrl + "/releases/tag/" + unchainedModsLatestRelease.Tag,
                    "Required to join and host modded servers"
                ));
            }

            if (latestPluginVersion != null && (currentPluginVersion == null || latestPluginVersion > currentPluginVersion)) {
                updates.Add(new DependencyUpdate(
                    "UnchainedPlugin.dll",
                    isPluginInstalled ? currentPluginVersion?.ToString() ?? "Unknown" : null,
                    latestUnchainedPluginTag!,
                    latestUnchainedPluginUrl!,
                    "Required to play Chivalry 2 Unchained"
                ));
            }

            if(!updates.Any()) 
                return;

            var updatingPlugin = currentPluginVersion != null;
            var installingUnchainedMods = unchainedModsLatestRelease != null;

            // Set up the update request window
            var titleText = "Chivalry 2 Unchained Core Update";
            var messageText = updatingPlugin ? "Update core plugin?" : "First Unchained Launch Setup.  Install Unchained Mods and Plugin?";
            var yesButtonText = "Yes";
            var noButtonText = "No";
            var cancelButtonText = "Cancel";

            MessageBoxResult? result = null;
            await window.Dispatcher.BeginInvoke(delegate () {
                result = UpdatesWindow.Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates);
            });

            if (result == MessageBoxResult.Cancel) {
                logger.Info("User cancelled core mod update.");
                throw new DownloadCancelledException("User cancelled core mod update.");
            }

            if (result == MessageBoxResult.No) {
                logger.Info("User declined core mod update.");
                return;
            }

            if(result == MessageBoxResult.Yes) {
                if (latestPluginVersion != null) {
                    logger.Info("User accepted core mod update.");
                    await PluginSynchronizer.DownloadRelease(latestUnchainedPluginTag!);
                }

                if(unchainedModsLatestRelease != null) {
                    logger.Info("User accepted core mod update.");
                    await ModManager.EnableModRelease(unchainedModsLatestRelease).DownloadTask.Task;
                }
            }
        }


        private void LogList<T>(string initialMessage, IEnumerable<T> list) {
            logger.Info("");
            logger.Info(initialMessage);
            foreach (var item in list) {
                logger.Info("    " + (item?.ToString() ?? "null"));
            }
            logger.Info("");
        }
    }

    public class DownloadCancelledException : Exception {
        public DownloadCancelledException(string message) : base(message) { }
    }
}
