using System.Diagnostics;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    public class Chivalry2ServerProcessLauncher {
        public IUnchainedChivalry2Launcher Launcher { get; set; }
        public ModdedLaunchOptions ModdedLaunchOptions { get; set; }
        public LanguageExt.Option<ServerLaunchOptions> ServerLaunchOptions { get; set; }
        string ExtraArgs { get; set; }
        public Chivalry2ServerProcessLauncher(IUnchainedChivalry2Launcher launcher,
                                           ModdedLaunchOptions options,
                                           string extraArgs) {
            Launcher = launcher;
            ModdedLaunchOptions = options;
            ExtraArgs = extraArgs;
        }

        public LanguageExt.Option<LanguageExt.Either<ProcessLaunchFailure, Process>> Launch() {
            var launch = Launcher.Launch(ModdedLaunchOptions, ExtraArgs);
            return launch;
        }
    }
}