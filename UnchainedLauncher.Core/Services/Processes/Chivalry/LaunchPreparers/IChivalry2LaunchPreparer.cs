namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    
    /// <summary>
    /// Performs tasks that sets up a proper launch
    /// </summary>
    public interface IChivalry2LaunchPreparer {
        /// <summary>
        /// Runs the preparations
        /// </summary>
        /// <returns>a Task containing False when preparations fail. True when successful.</returns>
        public Task<bool> PrepareLaunch();

        public IChivalry2LaunchPreparer AndThen(IChivalry2LaunchPreparer otherLaunchPreparer) {
            return new ComposedChivalry2LaunchPreparer(this, otherLaunchPreparer);
        }
    }
    
    public class ComposedChivalry2LaunchPreparer: IChivalry2LaunchPreparer {
        private readonly IChivalry2LaunchPreparer _launchPreparer1;
        private readonly IChivalry2LaunchPreparer _launchPreparer2;

        public ComposedChivalry2LaunchPreparer(IChivalry2LaunchPreparer launchPreparer1, IChivalry2LaunchPreparer launchPreparer2) {
            _launchPreparer1 = launchPreparer1;
            _launchPreparer2 = launchPreparer2;
        }
        
        public async Task<bool> PrepareLaunch() {
            return (await _launchPreparer1.PrepareLaunch()) && (await _launchPreparer2.PrepareLaunch());
        }
    }
}