using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Processes;

namespace UnchainedLauncher.Core
{
    public class Chivalry2ServerProcessLauncher
    {
        public Chivalry2Launcher Launcher { get; set; }
        public InstallationType InstallationType { get; set; }
        public ModdedLaunchOptions ModdedLaunchOptions { get; set; }
        public LanguageExt.Option<ServerLaunchOptions> ServerLaunchOptions { get; set; }
        IEnumerable<string> ExtraArgs { get; set; }
        public Chivalry2ServerProcessLauncher(Chivalry2Launcher launcher,
                                           InstallationType installType,
                                           ModdedLaunchOptions options,
                                           LanguageExt.Option<ServerLaunchOptions> serverOptions,
                                           IEnumerable<string> extraArgs)
        {
            Launcher = launcher;
            InstallationType = installType;
            ModdedLaunchOptions = options;
            ServerLaunchOptions = serverOptions;
            ExtraArgs = extraArgs;
        }

        public LanguageExt.Option<LanguageExt.Either<ProcessLaunchFailure, Process>> Launch()
        {
            var launch = Launcher.LaunchUnchained(InstallationType, ModdedLaunchOptions, ServerLaunchOptions, ExtraArgs);
            return launch;
        }
    }
}
