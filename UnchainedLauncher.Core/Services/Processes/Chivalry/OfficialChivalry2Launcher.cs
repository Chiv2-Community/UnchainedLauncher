using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public class OfficialChivalry2Launcher : IOfficialChivalry2Launcher {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(OfficialChivalry2Launcher));

        private IChivalry2LaunchPreparer<Unit> Chivalry2LaunchPreparer { get; }
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public OfficialChivalry2Launcher(IChivalry2LaunchPreparer<Unit> preparer, IProcessLauncher processLauncher, string workingDirectory) {
            WorkingDirectory = workingDirectory;
            Chivalry2LaunchPreparer = preparer;
            Launcher = processLauncher;
        }

        public async Task<Either<LaunchFailed, Process>> Launch(string args) {
            await Chivalry2LaunchPreparer.PrepareLaunch(Unit.Default);

            Logger.LogListInfo("Launch args: ", args);
            var launchResult = Launcher.Launch(WorkingDirectory, args);

            launchResult.Match(
                Left: e => Logger.Error(e),
                Right: _ => Logger.Info("Successfully launched Chivalry 2.")
            );

            return launchResult;
        }
    }
}