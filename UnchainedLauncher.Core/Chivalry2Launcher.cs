using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using LanguageExt;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core
{
    using static Prelude;
    public interface IChivalry2Launcher
    {
        /// <summary>
        /// Launches an unmodified vanilla game
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// Left if the game failed to launch.
        /// Right if the game was launched successfully.
        /// </returns>
        public Either<ProcessLaunchFailure, Process> LaunchVanilla(string args);
        
        /// <summary>
        /// Launches a vanilla game with pak loading enabled
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// Left if the game failed to launch.
        /// Right if the game was launched successfully.
        /// </returns>
        public Either<ProcessLaunchFailure, Process> LaunchModdedVanilla(string args);

        /// <summary>
        /// Launches the game with the provided launch options
        /// </summary>
        /// <param name="launchOptions"></param>
        /// <param name="args"></param>
        /// <returns>
        /// Left if the game failed to launch.
        /// Right if the game was launched successfully.
        /// </returns>
        public Either<ProcessLaunchFailure, Process> LaunchUnchained(ModdedLaunchOptions launchOptions, string args);
    }
    
    
    public class Chivalry2Launcher: IChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Launcher));

        private IProcessLauncher Launcher { get; }
        private IEnumerable<string> DLLs { get; }
        private string InstallationRootDir { get; }

        public Chivalry2Launcher(IProcessLauncher processLauncher, string installationRootDir, IEnumerable<string> dlls) {
            DLLs = dlls;
            InstallationRootDir = installationRootDir;
            Launcher = processLauncher;
        }

        public Either<ProcessLaunchFailure, Process> LaunchVanilla(string args) {

            logger.Info("Attempting to launch vanilla game.");
            logger.LogListInfo("Launch args: ", args);

            PrepareUnmoddedLaunchSigs();

            var launchResult = Launcher.Launch(InstallationRootDir, string.Join(" ", args));

            launchResult.Match(
                Left: error => error.Match(
                    LaunchFailedError: e => logger.Error($"Failed to launch Chivalry 2. {e.ExecutablePath} {e.Args}", e.Underlying),
                    InjectionFailedError: e => logger.Error($"This should be impossible. Report a bug please", e.Underlying)
                ),
                Right: _ => logger.Info("Successfully launched Chivalry 2.")
            );

            return launchResult;
        }

        public Either<ProcessLaunchFailure, Process> LaunchModdedVanilla(string args) {
            logger.Info("Attempting to launch vanilla game with pak loading.");
            logger.LogListInfo("Launch args: ", args);

            PrepareModdedLaunchSigs();

            var launchResult = Launcher.Launch(InstallationRootDir, args);

            launchResult.Match(
                Left: error => error.Match(
                    LaunchFailedError: e => logger.Error($"Failed to launch Chivalry 2. {e.ExecutablePath} {e.Args}", e.Underlying),
                    InjectionFailedError: e => logger.Error($"This should be impossible. Report a bug please", e.Underlying)
                ),
                Right: _ => logger.Info("Successfully launched Chivalry 2.")
            );

            return launchResult;
        }
        
        public Either<ProcessLaunchFailure, Process> LaunchUnchained(ModdedLaunchOptions launchOptions, string args) {
            logger.Info("Attempting to launch modded game.");

            var moddedLaunchArgs = args;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL");
            var offsetIndex = tblLoc == - 1 ? 0 : tblLoc + 3;

            var launchOpts = launchOptions.ToCLIArgs();

            moddedLaunchArgs.Insert(offsetIndex, " " + launchOpts);

            logger.Info($"Launch args: {moddedLaunchArgs}");

            PrepareModdedLaunchSigs();

            var launchResult = Launcher.Launch(Path.Combine(InstallationRootDir, FilePaths.BinDir), moddedLaunchArgs);

            return launchResult.Match(
                Left: error => {
                    error.Match(
                        LaunchFailedError: e =>
                            logger.Error($"Failed to launch Chivalry 2 Unchained. {e.ExecutablePath} {e.Args}",
                                e.Underlying),
                        InjectionFailedError: e =>
                            logger.Error($"Failed to inject DLLs into Chivalry 2 Unchained. {e.DllPaths}", e.Underlying)
                    );
                    return Left(error);
                },
                Right: proc => {
                    logger.Info("Successfully launched Chivalry 2 Unchained");
                    return InjectDLLs(proc);
                }
            );
        }

        private Either<ProcessLaunchFailure,Process> InjectDLLs(Process process)
        {
            if (!DLLs.Any()) return Right(process);
            
            try {
                logger.LogListInfo("Injecting DLLs:", DLLs);
                Inject.InjectAll(process, DLLs);
            } catch (Exception e) {
                return Left(ProcessLaunchFailure.InjectionFailed(Some(DLLs), e));
            }
            return Right(process);
        }

        private static void PrepareModdedLaunchSigs() {
            logger.Info("Verifying .sig file presence");
            SigFileHelper.CheckAndCopySigFiles();
            SigFileHelper.DeleteOrphanedSigFiles();
        }

        private static void PrepareUnmoddedLaunchSigs() {
            logger.Info("Removing .sig files");
            SigFileHelper.RemoveAllNonDefaultSigFiles();
        }
    }

    public record ServerLaunchOptions(
        bool Headless,
        string Name,
        string Description,
        Option<string> Password,
        string Map,
        int GamePort,
        int BeaconPort,
        int QueryPort,
        int RconPort
    ) {
        public IEnumerable<String> ToCLIArgs() {
            var args = new List<string>();
            if (Headless) {
                args.Add("-nullrhi");
                args.Add("-unattended");
                args.Add("-nosound");
            }

            Password.IfSome(password => args.Add($"ServerPassword={password.Trim()}"));
            args.Add($"--next-map-name {Map}");
            args.Add($"Port={GamePort}");
            args.Add($"GameServerPingPort={BeaconPort}");
            args.Add($"GameServerQueryPort={QueryPort}");
            args.Add($"--rcon {RconPort}");

            return args;
        }
    };




    public record ModdedLaunchOptions(
        string ServerBrowserBackend,
        Option<IEnumerable<Release>> EnabledMods,
        Option<string> SavedDirSuffix,
        Option<ServerLaunchOptions> ServerLaunchOptions
    ) {
        public IEnumerable<string> ToCLIArgs() {
            var args = new List<string> {
                $"--server-browser-backend {ServerBrowserBackend}"
            };
            ServerLaunchOptions.IfSome(opts => args.AddRange(opts.ToCLIArgs()));
            EnabledMods.IfSome(mods => args.AddRange(mods.Select(mod => $"--mod {mod.Manifest.RepoUrl}")));
            SavedDirSuffix.IfSome(suffix => args.Add($"--saved-dir-suffix {suffix}"));
            return args;
        }
    };
}