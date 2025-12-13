using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    using static LanguageExt.Prelude;
    public class Chivalry2Launcher : IChivalry2Launcher {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Chivalry2Launcher));

        private IChivalry2LaunchPreparer<LaunchOptions> LaunchPreparer { get; }
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public Chivalry2Launcher(
            IChivalry2LaunchPreparer<LaunchOptions> preparer,
            IProcessLauncher processLauncher,
            string workingDirectory) {
            WorkingDirectory = workingDirectory;
            LaunchPreparer = preparer;
            Launcher = processLauncher;
        }

        public Task<Either<LaunchFailed, Process>> Launch(LaunchOptions options) {
            Logger.Info($"Launch args: {options.LaunchArgs}");
            return LaunchPreparer.PrepareLaunch(options).Match(
                None: () => Left(new LaunchFailed(
                    Launcher.Executable,
                    options.LaunchArgs,
                    Error.New("Launch preparers failed"))),
                Some: _ => Launcher.Launch(WorkingDirectory, options.LaunchArgs)
                );
        }
    }
}