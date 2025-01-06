using LanguageExt;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    public interface IOfficialChivalry2Launcher {
        /// <summary>
        /// Launches a vanilla game. Implementations may do extra work to enable client side pak file loading
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// Left if the game failed to launch.
        /// Right if the game was launched successfully.
        /// </returns>
        public Task<Either<LaunchFailed, Process>> Launch(string args);
    }
}