using System.Diagnostics;
using log4net;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Extensions;
using LanguageExt;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core
{
    public class Chivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Launcher));
        public static readonly string GameBinPath = FilePaths.BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public static readonly string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";

        private static readonly LanguageExt.HashSet<int> GracefulExitCodes = new LanguageExt.HashSet<int> { 0, -1073741510 };

        private static RestartPolicy DefaultRestartPolicy = new RestartPolicy(Prelude.None, 30000, x => !GracefulExitCodes.Contains(x));

        /// <summary>
        /// The original launcher is used to launch the game with no mods.
        /// </summary>
        private static ProcessLauncher VanillaLauncher { get; } = new ProcessLauncher(OriginalLauncherPath, Directory.GetCurrentDirectory(), Prelude.Some(DefaultRestartPolicy));

        /// <summary>
        /// The modded launcher is used to launch the game with mods. The DLLs here are the relative paths to the DLLs that are to be injected.
        /// </summary>
        private static ProcessLauncher ModdedLauncher { get; } = new ProcessLauncher(GameBinPath, FilePaths.BinDir, Prelude.Some(DefaultRestartPolicy));

        public Chivalry2Launcher() {
        }

        public Process LaunchVanilla(IEnumerable<string> args) {
            logger.Info("Attempting to launch vanilla game.");
            logger.LogListInfo("Launch args: ", args);
            return VanillaLauncher.Launch(string.Join(" ", args));
        }

        public Thread? LaunchModded(InstallationType installationType, ModdedLaunchOptions launchOptions, Option<ServerLaunchOptions> serverLaunchOptions, IEnumerable<string> extraArgs) {
            if (installationType == InstallationType.NotSet) return null;

            logger.Info("Attempting to launch modded game.");

            var launchThread = new Thread(async () => {
                try {
                    var moddedLaunchArgs = extraArgs.ToList();
                    var tblLoc = moddedLaunchArgs.IndexOf("TBL") + 1;

                    launchOptions.ServerBrowserBackend.IfSome(backend => moddedLaunchArgs.Add($"--server-browser-backend {backend}"));
                    launchOptions.NextMapModActors.IfSome(modActors => moddedLaunchArgs.Insert(tblLoc, $"--next-map-mod-actors {string.Join(",", modActors)}"));
                    launchOptions.AllModActors.IfSome(modActors => moddedLaunchArgs.Insert(tblLoc, $"--all-mod-actors {string.Join(",", modActors)}"));
                    launchOptions.SavedDirSuffix.IfSome(suffix => moddedLaunchArgs.Add($"--saved-dir-suffix {suffix}"));

                    serverLaunchOptions.IfSome(options => {
                        if(options.headless) {
                            moddedLaunchArgs.Add("-nullrhi");
                            moddedLaunchArgs.Add("-unattended");
                            moddedLaunchArgs.Add("-nosound");
                        }

                        options.Password.IfSome(password => moddedLaunchArgs.Add($"ServerPassword={password.Trim()}"));
                        options.Map.IfSome(map => moddedLaunchArgs.Add($"--next-map-name {map}"));
                        options.GamePort.IfSome(port => moddedLaunchArgs.Add($"Port={port}"));
                        options.BeaconPort.IfSome(port => moddedLaunchArgs.Add($"GameServerPingPort={port}"));
                        options.QueryPort.IfSome(port => moddedLaunchArgs.Add($"GameServerQueryPort={port}"));
                        options.RconPort.IfSome(port => moddedLaunchArgs.Add($"--rcon {port}"));
                    }); 

                    var dlls = Directory.EnumerateFiles(FilePaths.PluginDir, "*.dll").ToArray();
                    ModdedLauncher.Dlls = dlls;

                    logger.LogListInfo($"Launch args:", moddedLaunchArgs);

                    do {
                        // TODO: Overhaul restart behavior. Should be managed by the caller of LaunchModded.
                        logger.Info("Starting Chivalry 2 Unchained.");
                        ModdedLauncher.Launch(string.Join(" ", moddedLaunchArgs));

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
        Option<string> Password,
        Option<string> Map,
        Option<int> GamePort,
        Option<int> BeaconPort,
        Option<int> QueryPort,
        Option<int> RconPort
    );

    public record ModdedLaunchOptions(
        Option<string> ServerBrowserBackend,
        Option<IEnumerable<Release>> EnabledMods,
        Option<string> SavedDirSuffix
    );
}
