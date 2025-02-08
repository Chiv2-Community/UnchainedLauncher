using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    using static LanguageExt.Prelude;
    public class Chivalry2Launcher : IChivalry2Launcher {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Chivalry2Launcher));

        private IChivalry2LaunchPreparer<ModdedLaunchOptions> LaunchPreparer { get; }
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public Chivalry2Launcher(
            IChivalry2LaunchPreparer<ModdedLaunchOptions> preparer, 
            IProcessLauncher processLauncher, 
            string workingDirectory) {
            WorkingDirectory = workingDirectory;
            LaunchPreparer = preparer;
            Launcher = processLauncher;
        }

        public Task<Either<LaunchFailed, Process>> Launch(string args, ModdedLaunchOptions options) {
            Logger.Info($"Launch args: {args}");
            return LaunchPreparer.PrepareLaunch(options).Match(
                None: () => Left(new LaunchFailed(
                    FilePaths.OriginalLauncherPath,
                    args,
                    "launch preparer(s) failed.")),
                Some: _ => Launcher.Launch(WorkingDirectory, args)
                );
        }
    }
}