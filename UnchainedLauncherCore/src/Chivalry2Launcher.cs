using System.Diagnostics;
using log4net;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Extensions;
using LanguageExt;

namespace UnchainedLauncher.Core
{
    public class Chivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Launcher));
        public static readonly string GameBinPath = FilePaths.BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public static readonly string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";

        private static readonly LanguageExt.HashSet<int> GracefulExitCodes = new LanguageExt.HashSet<int> { 0, -1073741510 };

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
            logger.LogListInfo("Launch args: ", args);
            return VanillaLauncher.Launch(string.Join(" ", args));
        }

        public Thread? LaunchModded(InstallationType installationType, ModdedLaunchOptions launchOptions, Option<ServerLaunchOptions> serverLaunchOptions, List<string> extraArgs) {
            if (installationType == InstallationType.NotSet) return null;

            logger.Info("Attempting to launch modded game.");

            var launchThread = new Thread(async () => {
                try {
                    var moddedLaunchArgs = new List<string>();
                    launchOptions.ServerBrowserBackend.IfSome(backend => moddedLaunchArgs.Add($"--server-browser-backend {backend}"));
                    launchOptions.NextMapModActors.IfSome(modActors => moddedLaunchArgs.Add($"--next-map-mod-actors {string.Join(",", modActors)}"));
                    launchOptions.AllModActors.IfSome(modActors => moddedLaunchArgs.Add($"--all-mod-actors {string.Join(",", modActors)}"));
                    launchOptions.SavedDirSuffix.IfSome(suffix => moddedLaunchArgs.Add($"--saved-dir-suffix {suffix}"));

                    var dlls = Directory.EnumerateFiles(FilePaths.PluginDir, "*.dll").ToArray();
                    ModdedLauncher.Dlls = dlls;

                    logger.LogListInfo($"Mods Enabled:", ModManager.EnabledModReleases.Select(mod => mod.Manifest.Name + " " + mod.Tag));
                    logger.LogListInfo($"Launch args:", args);

                    serverRegister?.Start();

                    var restartOnCrash = serverRegister != null;

                    do {
                        // TODO: Overhaul restart behavior. Should be managed by the caller of LaunchModded.
                        logger.Info("Starting Chivalry 2 Unchained.");
                        var process = ModdedLauncher.Launch(string.Join(" ", args));

                        await process.WaitForExitAsync();

                        var exitedGracefully = GracefulExitCodes.Contains(process.ExitCode);
                        if (!restartOnCrash || exitedGracefully) break;

                        logger.Info($"Detected Chivalry2 crash (Exit code {process.ExitCode}). Restarting in 10 seconds. You may close the launcher while it is visible to prevent further restarts.");
                        await Task.Delay(10000);
                    } while (true);

                    logger.Info("Process exited. Closing RCON and UnchainedLauncherG");

                    serverRegister?.CloseMainWindow();

                } catch (Exception ex) {
                    logger.Error(ex);
                    throw;
                }
            });

            launchThread.Start();
            return launchThread;
        }
    }

    public record ServerLaunchOptions(
        bool headless,
        Option<string> Name,
        Option<string> Description,
        Option<string> Password,
        Option<string> Map,
        Option<int> GamePort,
        Option<int> BeaconPort,
        Option<int> QueryPort,
        Option<int> RconPort
    );

    public record ModdedLaunchOptions(
        Option<string> ServerBrowserBackend,
        Option<IEnumerable<string>> NextMapModActors,
        Option<IEnumerable<string>> AllModActors,
        Option<string> SavedDirSuffix
    );
}
