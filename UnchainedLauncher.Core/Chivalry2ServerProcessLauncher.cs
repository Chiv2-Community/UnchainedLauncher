using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Processes;

namespace UnchainedLauncher.Core {
    public class Chivalry2ServerProcessLauncher {
        public Chivalry2Launcher Launcher { get; set; }
        public ModdedLaunchOptions ModdedLaunchOptions { get; set; }
        public LanguageExt.Option<ServerLaunchOptions> ServerLaunchOptions { get; set; }
        string ExtraArgs { get; set; }
        public Chivalry2ServerProcessLauncher(Chivalry2Launcher launcher,
                                           ModdedLaunchOptions options,
                                           string extraArgs) {
            Launcher = launcher;
            ModdedLaunchOptions = options;
            ExtraArgs = extraArgs;
        }

        public LanguageExt.Option<LanguageExt.Either<ProcessLaunchFailure, Process>> Launch() {
            var launch = Launcher.LaunchUnchained(ModdedLaunchOptions, ExtraArgs);
            return launch;
        }
    }
}