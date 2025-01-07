using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Processes;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public class OfficialChivalry2Launcher : IOfficialChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(typeof(OfficialChivalry2Launcher));
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public OfficialChivalry2Launcher(IProcessLauncher processLauncher, string workingDirectory) {
            WorkingDirectory = workingDirectory;
            Launcher = processLauncher;
        }

        public async Task<Either<LaunchFailed, Process>> Launch(string args) {

            logger.Info("Attempting to launch vanilla game.");
            logger.LogListInfo("Launch args: ", args);

            PrepareUnmoddedLaunchSigs();

            var launchResult = Launcher.Launch(WorkingDirectory, string.Join(" ", args));

            launchResult.Match(
                Left: e => logger.Error(e),
                Right: _ => logger.Info("Successfully launched Chivalry 2.")
            );

            return launchResult;
        }

        private static void PrepareUnmoddedLaunchSigs() {
            logger.Info("Removing .sig files");
            SigFileHelper.RemoveAllNonDefaultSigFiles();
        }
    }
}