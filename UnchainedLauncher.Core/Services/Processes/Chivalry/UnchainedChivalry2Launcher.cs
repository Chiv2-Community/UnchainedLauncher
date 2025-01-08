using LanguageExt;
using log4net;
using Semver;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    using static Prelude;

    public class UnchainedChivalry2Launcher : IUnchainedChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UnchainedLauncher));

        private IProcessLauncher Launcher { get; }
        private Func<IEnumerable<string>> FetchDLLs { get; }
        private IChivalry2LaunchPreparer LaunchPreparer { get; }
        private string InstallationRootDir { get; }

        public UnchainedChivalry2Launcher(
            IChivalry2LaunchPreparer preparer,
            IProcessLauncher processLauncher,
            string installationRootDir,
            Func<IEnumerable<string>> dlls) {

            FetchDLLs = dlls;
            InstallationRootDir = installationRootDir;

            LaunchPreparer = preparer;
            Launcher = processLauncher;
        }

        public async Task<Either<UnchainedLaunchFailure, Process>> Launch(ModdedLaunchOptions launchOptions, bool updateUnchainedDependencies, string args) {
            logger.Info("Attempting to launch modded game.");

            var moddedLaunchArgs = args;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL", StringComparison.Ordinal);
            var offsetIndex = tblLoc == -1 ? 0 : tblLoc + 3;

            var launchOpts = launchOptions.ToCLIArgs();

            moddedLaunchArgs.Insert(offsetIndex, " " + launchOpts);

            var launchPrepResult = await LaunchPreparer.PrepareLaunch();
            if (launchPrepResult == false) {
                return Left(UnchainedLaunchFailure.LaunchCancelled());
            }

            PrepareModdedLaunchSigs();

            logger.Info($"Launch args: {moddedLaunchArgs}");

            var launchResult = Launcher.Launch(Path.Combine(InstallationRootDir, FilePaths.BinDir), moddedLaunchArgs);

            return launchResult.Match(
                Left: error => {
                    logger.Error(error);
                    return Left(UnchainedLaunchFailure.LaunchFailed(error));
                },
                Right: proc => {
                    logger.Info("Successfully launched Chivalry 2 Unchained");
                    return InjectDLLs(proc);
                }
            );
        }
        
        private Either<UnchainedLaunchFailure, Process> InjectDLLs(Process process) {
            IEnumerable<string>? dlls = null;
            try {
                dlls = FetchDLLs();
                if (!dlls.Any()) return Right(process);
                logger.LogListInfo("Injecting DLLs:", dlls);
                Inject.InjectAll(process, dlls);
                return Right(process);
            }
            catch (Exception e) {
                process.Kill();
                logger.Error(e);
                return Left(UnchainedLaunchFailure.InjectionFailed(Optional(dlls), e));
            }
        }

        private static void PrepareModdedLaunchSigs() {
            logger.Info("Verifying .sig file presence");
            SigFileHelper.CheckAndCopySigFiles();
            SigFileHelper.DeleteOrphanedSigFiles();
        }
    }
}