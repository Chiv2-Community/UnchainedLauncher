using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    using static Prelude;

    public class UnchainedChivalry2Launcher : IUnchainedChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UnchainedLauncher));

        private IProcessLauncher Launcher { get; }
        private Func<IEnumerable<string>> FetchDLLs { get; }
        private string InstallationRootDir { get; }

        public UnchainedChivalry2Launcher(IProcessLauncher processLauncher, string installationRootDir,
            Func<IEnumerable<string>> dlls) {
            FetchDLLs = dlls;
            InstallationRootDir = installationRootDir;
            Launcher = processLauncher;
        }

        public Either<ProcessLaunchFailure, Process> Launch(ModdedLaunchOptions launchOptions, string args) {
            logger.Info("Attempting to launch modded game.");

            var moddedLaunchArgs = args;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL", StringComparison.Ordinal);
            var offsetIndex = tblLoc == -1 ? 0 : tblLoc + 3;

            var launchOpts = launchOptions.ToCLIArgs();

            moddedLaunchArgs.Insert(offsetIndex, " " + launchOpts);

            PrepareModdedLaunchSigs();

            logger.Info($"Launch args: {moddedLaunchArgs}");

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
                    return Prelude.Left(error);
                },
                Right: proc => {
                    logger.Info("Successfully launched Chivalry 2 Unchained");
                    return InjectDLLs(proc);
                }
            );
        }

        private Either<ProcessLaunchFailure, Process> InjectDLLs(Process process) {
            IEnumerable<string>? dlls = null;
            try {
                dlls = FetchDLLs();
                if (!dlls.Any()) return Prelude.Right(process);
                logger.LogListInfo("Injecting DLLs:", dlls);
                Inject.InjectAll(process, dlls);
            }
            catch (Exception e) {
                return Prelude.Left(ProcessLaunchFailure.InjectionFailed(Optional(dlls), e));
            }
            return Prelude.Right(process);
        }

        private static void PrepareModdedLaunchSigs() {
            logger.Info("Verifying .sig file presence");
            SigFileHelper.CheckAndCopySigFiles();
            SigFileHelper.DeleteOrphanedSigFiles();
        }
    }
}