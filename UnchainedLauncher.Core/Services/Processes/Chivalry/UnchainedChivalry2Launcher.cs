using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    using static Prelude;

    public class UnchainedChivalry2Launcher : IChivalry2Launcher {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(UnchainedLauncher));

        private IProcessLauncher Launcher { get; }
        private IChivalry2LaunchPreparer<LaunchOptions> LaunchPreparer { get; }
        private string InstallationRootDir { get; }
        private IProcessInjector ProcessInjector { get; }

        public UnchainedChivalry2Launcher(
            IChivalry2LaunchPreparer<LaunchOptions> preparer,
            IProcessLauncher processLauncher,
            string installationRootDir,
            IProcessInjector processInjector) {

            InstallationRootDir = installationRootDir;
            ProcessInjector = processInjector;

            LaunchPreparer = preparer;
            Launcher = processLauncher;
        }

        public async Task<Either<LaunchFailed, Process>> Launch(LaunchOptions options) {
            var launchResult = await TryLaunch(options);
            return launchResult.MapLeft(failure => failure.AsLaunchFailed(options.LaunchArgs));
        }

        public async Task<Either<UnchainedLaunchFailure, Process>> TryLaunch(LaunchOptions launchOptions) {
            Logger.Info("Attempting to launch modded game.");

            var updatedLaunchOpts = await LaunchPreparer.PrepareLaunch(launchOptions);
            if (updatedLaunchOpts.IsNone) {
                return Left(UnchainedLaunchFailure.LaunchCancelled());
            }

            var moddedLaunchArgs = launchOptions.LaunchArgs;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL", StringComparison.Ordinal);
            var offsetIndex = tblLoc == -1 ? 0 : tblLoc + 3;

            var launchOpts = updatedLaunchOpts.Map(x => x.ToCLIArgs()).ValueUnsafe();
            
            moddedLaunchArgs = moddedLaunchArgs.Insert(offsetIndex, $" {string.Join(" ", launchOpts)} ");

            Logger.Info($"Launch args: {moddedLaunchArgs}");

            var launchResult = Launcher.Launch(Path.Combine(InstallationRootDir, FilePaths.BinDir), "-unchained " + moddedLaunchArgs);

            return launchResult.Match(
                Left: error => {
                    Logger.Error(error);
                    return Left(UnchainedLaunchFailure.LaunchFailed(error));
                },
                Right: proc => {
                    Logger.Info("Successfully launched Chivalry 2 process.");
                    return InjectDlLs(proc);
                }
            );
        }

        private Either<UnchainedLaunchFailure, Process> InjectDlLs(Process process) {
            Logger.Info($"Injecting DLLs into {process.Id}");
            if (ProcessInjector.Inject(process)) return Right(process);

            process.Kill();
            return Left(UnchainedLaunchFailure.InjectionFailed());
        }
    }
}