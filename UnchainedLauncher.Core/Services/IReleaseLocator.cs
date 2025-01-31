using Semver;

namespace UnchainedLauncher.Core.Services {
    /// <summary>
    /// An IReleaseLocator is used for finding releases for non-mod files.
    ///
    /// This means things like the plugin DLL, the launcher itself, or other non-mod files. 
    /// </summary>
    public interface IReleaseLocator {
        /// <summary>
        /// Returns the latest stable release of the application associated with this IReleaseLocator instance.
        /// </summary>
        /// <returns></returns>
        public Task<ReleaseTarget?> GetLatestRelease();

        /// <summary>
        /// Returns all releases including pre-releases of the application associated with this IReleaseLocator instance.
        /// </summary>
        /// <returns></returns>
        public Task<IEnumerable<ReleaseTarget>> GetAllReleases();
    }

    public record ReleaseTarget(
        string PageUrl,
        string DescriptionMarkdown,
        SemVersion Version,
        IEnumerable<ReleaseAsset> Assets,
        DateTimeOffset CreatedDate,
        bool IsLatestStable,
        bool IsPrerelease) {
        public ReleaseTarget AsLatestStable() => this with { IsLatestStable = true };

        public string DisplayText => $"v{Version} ({CreatedDate:d})" + (IsLatestStable ? " Recommended" : "");

    }
    public record ReleaseAsset(string Name, string DownloadUrl);
}