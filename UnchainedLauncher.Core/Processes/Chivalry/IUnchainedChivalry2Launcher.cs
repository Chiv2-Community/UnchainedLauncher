using LanguageExt;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    public interface IUnchainedChivalry2Launcher {
        /// <summary>
        /// Launches a modded instance of the game with additional parameters, supporting server hosting and potentially
        /// DLL Injection.
        /// </summary>
        /// <param name="launchOptions"></param>
        /// <param name="args"></param>
        /// <returns>
        /// Left if the game failed to launch.
        /// Right if the game was launched successfully.
        /// </returns>
        public Either<ProcessLaunchFailure, Process> Launch(ModdedLaunchOptions launchOptions, string args);
    }
}