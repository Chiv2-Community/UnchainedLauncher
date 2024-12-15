using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;

namespace UnchainedLauncher.Core.Processes.Chivalry
{
    public class ClientSideModdedOfficialChivalry2Launcher : IOfficialChivalry2Launcher  {
        private static readonly ILog logger = LogManager.GetLogger(typeof(ClientSideModdedOfficialChivalry2Launcher));
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public ClientSideModdedOfficialChivalry2Launcher(IProcessLauncher processLauncher, string workingDirectory) {
            WorkingDirectory = workingDirectory;
            Launcher = processLauncher;
        }
        
        public Either<ProcessLaunchFailure, Process> Launch(string args) {
            logger.Info("Attempting to launch vanilla game with pak loading.");
            logger.LogListInfo("Launch args: ", args);

            PrepareModdedLaunchSigs();

            var launchResult = Launcher.Launch(WorkingDirectory, args);

            launchResult.Match(
                Left: error => error.Match(
                    LaunchFailedError: e => logger.Error($"Failed to launch Chivalry 2. {e.ExecutablePath} {e.Args}", e.Underlying),
                    InjectionFailedError: e => logger.Error($"This should be impossible. Report a bug please", e.Underlying)
                ),
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