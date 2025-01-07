using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Processes;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public class ClientSideModdedOfficialChivalry2Launcher : IOfficialChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(typeof(ClientSideModdedOfficialChivalry2Launcher));
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public ClientSideModdedOfficialChivalry2Launcher(IProcessLauncher processLauncher, string workingDirectory) {
            WorkingDirectory = workingDirectory;
            Launcher = processLauncher;
        }

        public async Task<Either<LaunchFailed, Process>> Launch(string args) {
            logger.Info("Attempting to launch vanilla game with pak loading.");
            logger.LogListInfo("Launch args: ", args);

            PrepareModdedLaunchSigs();

            var launchResult = Launcher.Launch(WorkingDirectory, args);

            launchResult.Match(
                Left: error => logger.Error(error),
                Right: _ => logger.Info("Successfully launched Chivalry 2.")
            );

            return launchResult;
        }

        private static void PrepareModdedLaunchSigs() {
            logger.Info("Verifying .sig file presence");
            SigFileHelper.CheckAndCopySigFiles();
            SigFileHelper.DeleteOrphanedSigFiles();
        }
    }
}