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

            var maybeUpdatedLaunchOpts = await LaunchPreparer.PrepareLaunch(launchOptions);
            if (maybeUpdatedLaunchOpts.IsNone) {
                return Left(UnchainedLaunchFailure.LaunchCancelled());
            }

            var updatedLaunchOpts = maybeUpdatedLaunchOpts.ValueUnsafe();

            var moddedLaunchArgs = launchOptions.LaunchArgs;
            var allArgs = updatedLaunchOpts.ToCLIArgs();
            var mapUriArgs = allArgs.Filter(x => x is UEMapUrlParameter);
            var launchOpts = allArgs.Filter<CLIArg>(x => x is not UEMapUrlParameter);
            
            var mapString = "TBL";
            foreach (var mapUriArg in mapUriArgs) {
                mapString += mapUriArg.Rendered;
            }
            
            Logger.Info($"Launch args:");
            Logger.Info($"    {mapString}");
            foreach (var launchOpt in launchOpts) {
                Logger.Info($"    {launchOpt.Rendered}");
            }

            var workingDir = Path.Combine(InstallationRootDir, FilePaths.BinDir);
            var launchOptString = mapString + string.Join(" ", launchOpts.Select(x => x.Rendered));
            var launchResult = Launcher.Launch(workingDir, launchOptString);

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