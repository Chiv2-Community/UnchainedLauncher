using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using System.Diagnostics;
using System.Text.RegularExpressions;
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

            var launchOptString = BuildArguments(updatedLaunchOpts);
            var workingDir = Path.Combine(InstallationRootDir, FilePaths.BinDir);
            var injectionFailed = false;
            var launchResult = Launcher.Launch(workingDir, launchOptString, process => {
                Logger.Info($"Injecting DLLs into {process.Id} before resuming the game.");
                try {
                    injectionFailed = !ProcessInjector.Inject(process, server: updatedLaunchOpts.ServerLaunchOptions.IsSome);
                    return !injectionFailed;
                }
                catch {
                    injectionFailed = true;
                    throw;
                }
            });

            return launchResult.Match<Either<UnchainedLaunchFailure, Process>>(
                Left: error => {
                    Logger.Error(error);
                    if (injectionFailed) return Left(UnchainedLaunchFailure.InjectionFailed());
                    return Left(UnchainedLaunchFailure.LaunchFailed(error));
                },
                Right: proc => {
                    Logger.Info("Successfully launched Chivalry 2 process.");
                    return Right(proc);
                }
            );
        }

        public static string BuildArguments(LaunchOptions updatedLaunchOpts) {

            var allArgs = updatedLaunchOpts.ToCLIArgs();
            var rawArgs = allArgs.Filter(x => x is RawArgs);
            var mapUriArgs = allArgs.Filter(x => x is UEMapUrlParameter);
            var launchOpts = allArgs.Filter(x => x is not (UEMapUrlParameter or RawArgs));

            var mapUriString = string.Join("", mapUriArgs.Select(x => x.Rendered));

            var rawArgsString = string.Join(" ", rawArgs.Select(x => x.Rendered));
            var mapString = rawArgsString;
            if (!string.IsNullOrWhiteSpace(mapUriString)) {
                if (Regex.IsMatch(rawArgsString, @"\bTBL\b")) {
                    // Insert map url params immediately after the `TBL` token (not at the end of all raw args)
                    mapString = new Regex(@"\bTBL\b").Replace(rawArgsString, $"TBL{mapUriString}", 1);
                }
                else {
                    mapString = string.IsNullOrWhiteSpace(rawArgsString)
                        ? $"TBL{mapUriString}"
                        : $"TBL{mapUriString} {rawArgsString}";
                }
            }

            var cliArgs = launchOpts as CLIArg[] ?? launchOpts.ToArray();
            return string.Join(" ", new[] { mapString }
                .Concat(cliArgs.Select(x => x.Rendered))
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

    }
}
