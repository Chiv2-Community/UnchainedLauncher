namespace UnchainedLauncher.Core.Utilities.Releases
{
    public interface IReleaseLocator {
        /// <summary>
        /// Returns the latest stable release of the target application.
        /// </summary>
        /// <returns></returns>
        public Task<ReleaseTarget?> GetLatestRelease();

        /// <summary>
        /// Returns all releases of the target application, including pre-releases.
        /// </summary>
        /// <returns></returns>
        public Task<IEnumerable<ReleaseTarget>> GetAllReleases();
    }
}