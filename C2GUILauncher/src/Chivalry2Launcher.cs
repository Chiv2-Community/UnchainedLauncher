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

namespace C2GUILauncher {
    public class Chivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Launcher));
        public static string GameBinPath = FilePaths.BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public static string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";

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
            return VanillaLauncher.Launch(string.Join(" ", args));
        }

        public Thread? LaunchModded(Window window, InstallationType installationType, List<string> args, bool downloadPlugin, bool enablePluginLogging, Process? serverRegister = null) {
            if (installationType == InstallationType.NotSet) return null;

            logger.Info("Attempting to launch modded game.");


            LogList($"Mods Enabled:", ModManager.EnabledModReleases.Select(mod => mod.Manifest.Name + " " + mod.Tag));
            LogList($"Launch args:", args);

            // Download the mod files, potentially using debug dlls
            var launchThread = new Thread(async () => {
                try {
                    if (downloadPlugin) {
                        List<ModReleaseDownloadTask> downloadTasks = this.ModManager.DownloadModFiles(downloadPlugin, enablePluginLogging).ToList();
                        await Task.WhenAll(downloadTasks.Select(x => x.DownloadTask.Task));
                    }
                    var dlls = Directory.EnumerateFiles(FilePaths.PluginDir, "*.dll").ToArray();
                    ModdedLauncher.Dlls = dlls;

                    var process = ModdedLauncher.Launch(string.Join(" ", args));

                    serverRegister?.Start();

                    await window.Dispatcher.BeginInvoke(delegate () {
                        window.Hide();
                    });

                    await process.WaitForExitAsync();
                    serverRegister?.CloseMainWindow();

                    await window.Dispatcher.BeginInvoke(delegate () {
                        window.Close();
                    });
                } catch (Exception ex) {
                    MessageBox.Show(ex.ToString());
                }
            });

            launchThread.Start();
            return launchThread;
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
}
