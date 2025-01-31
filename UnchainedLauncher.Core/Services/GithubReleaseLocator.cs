using LanguageExt;
using log4net;
using Octokit;
using Semver;
using System.Collections.Immutable;

namespace UnchainedLauncher.Core.Services {
    using static LanguageExt.Prelude;

    /// <summary>
    /// The GithubReleaseLocator finds all github releases associated with a specified repository and provides targets
    /// for downloading those releases.
    /// </summary>
    public class GithubReleaseLocator : IReleaseLocator {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(GithubReleaseLocator));

        private readonly GitHubClient _gitHubClient;
        private readonly string _repoOwner;
        private readonly string _repoName;

        private IEnumerable<ReleaseTarget>? ReleaseCache { get; set; }
        private ReleaseTarget? LatestRelease { get; set; }

        public GithubReleaseLocator(GitHubClient githubClient, string repoOwner, string repoName) {
            _gitHubClient = githubClient;
            _repoName = repoName;
            _repoOwner = repoOwner;
        }

        public async Task<ReleaseTarget?> GetLatestRelease() {
            if (LatestRelease != null) {
                return LatestRelease;
            }

            await ProcessGithubReleases();

            return LatestRelease;
        }

        public async Task<IEnumerable<ReleaseTarget>> GetAllReleases() {
            if (ReleaseCache != null) {
                return ReleaseCache;
            }

            return await ProcessGithubReleases();
        }

        private async Task<IEnumerable<ReleaseTarget>> ProcessGithubReleases() {
            try {
                var maybeReleases = Optional(await _gitHubClient.Repository.Release.GetAll(_repoOwner, _repoName));
                var results =
                    from releases in maybeReleases
                    from release in releases
                    from version in ParseTag(release.TagName)
                    select new ReleaseTarget(
                        release.HtmlUrl,
                        release.Body,
                        version,
                        from asset in release.Assets
                        select new ReleaseAsset(asset.Name, asset.BrowserDownloadUrl),
                        release.CreatedAt,
                        false,
                        version.IsPrerelease || release.Prerelease);

                var latestRelease = results.Filter(r => !r.IsPrerelease).MaxBy(x => x.Version)?.AsLatestStable();

                Logger.Info($"Found {results.Count()} releases, latest stable release is {latestRelease?.Version}");

                if (latestRelease != null)
                    results = results?.ToList().Select(x => x.Version == latestRelease.Version ? x.AsLatestStable() : x);

                ReleaseCache = results;
                LatestRelease = latestRelease;

                return Optional(ReleaseCache).ToList().Flatten();
            }
            catch (Exception e) {
                Logger.Error("Failed to connect to github to retrieve version information", e);
                return ImmutableList.CreateBuilder<ReleaseTarget>().ToImmutableList();
            }
        }

        private static Option<SemVersion> ParseTag(string tag) {
            var versionString = tag;
            if (versionString.StartsWith("v")) {
                versionString = versionString[1..];
            }

            SemVersion.TryParse(versionString, SemVersionStyles.Any, out var parsedVersion);
            if (parsedVersion == null) Logger.Error($"Failed to parse git tag {tag}");

            return Optional(parsedVersion);
        }
    }
}