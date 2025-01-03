﻿using LanguageExt;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    public class OfficialChivalry2Launcher : IOfficialChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(typeof(OfficialChivalry2Launcher));
        private IProcessLauncher Launcher { get; }
        private string WorkingDirectory { get; }

        public OfficialChivalry2Launcher(IProcessLauncher processLauncher, string workingDirectory) {
            WorkingDirectory = workingDirectory;
            Launcher = processLauncher;
        }

        public Either<ProcessLaunchFailure, Process> Launch(string args) {

            logger.Info("Attempting to launch vanilla game.");
            logger.LogListInfo("Launch args: ", args);

            PrepareUnmoddedLaunchSigs();

            var launchResult = Launcher.Launch(WorkingDirectory, string.Join(" ", args));

            launchResult.Match(
                Left: error => error.Match(
                    LaunchFailedError: e => logger.Error($"Failed to launch Chivalry 2. {e.ExecutablePath} {e.Args}", e.Underlying),
                    InjectionFailedError: e => logger.Error($"This should be impossible. Report a bug please", e.Underlying)
                ),
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