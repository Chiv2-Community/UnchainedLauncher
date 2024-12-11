

using System.Collections.Immutable;
using LanguageExt;
using log4net;
using Octokit;
using Semver;
using UnchainedLauncher.Core.Installer;

namespace UnchainedLauncher.Core.Utilities
{
    using static LanguageExt.Prelude;
    
    public record ReleaseTarget(
        string PageUrl,
        string DescriptionMarkdown,
        SemVersion Version,
        IEnumerable<ReleaseAsset> Assets,
        DateTimeOffset CreatedDate,
        bool IsLatestStable,
        bool IsPrerelease)
    {
        public ReleaseTarget AsLatestStable() => this with { IsLatestStable = true };
        
        public string DisplayText => $"v{Version} ({CreatedDate:d})" + (IsLatestStable ? " Recommended" : "");

    }
    public record ReleaseAsset(string Name, string DownloadUrl);

    public interface IReleaseLocator
    {
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
    
    public class GithubReleaseLocator: IReleaseLocator
    {
        public static readonly ILog logger = LogManager.GetLogger(nameof(GithubReleaseLocator));
        
        public GitHubClient GitHubClient { get; }
        private string RepoOwner;
        private string RepoName;
        
        private IEnumerable<ReleaseTarget>? ReleaseCache { get; set; } = null;
        private ReleaseTarget? LatestRelease { get; set; } = null;

        public GithubReleaseLocator(GitHubClient githubClient, string repoOwner, string repoName)
        {
            GitHubClient = githubClient;
            RepoName = repoName;    
            RepoOwner = repoOwner;
        }
        
        
        public async Task<ReleaseTarget?> GetLatestRelease() {
            if(LatestRelease != null) {
                return LatestRelease;
            }

            await ProcessGithubReleases();
            
            return LatestRelease;
        }

        public async Task<IEnumerable<ReleaseTarget>> GetAllReleases()
        {
            if (ReleaseCache != null) {
                return ReleaseCache;
            }

            return await ProcessGithubReleases();
        }

        private async Task<IEnumerable<ReleaseTarget>> ProcessGithubReleases()
        {
            try {
                var maybeReleases = Optional(await GitHubClient.Repository.Release.GetAll(RepoOwner, RepoName));
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

                ReleaseTarget? latestRelease = results.Filter(r => !r.IsPrerelease).MaxBy(x => x.Version)?.AsLatestStable();

                logger.Info($"Found {results.Count()} releases, latest stable release is {latestRelease?.Version}");

                if (latestRelease != null)
                    results = results?.ToList().Select(x => x.Version == latestRelease.Version ? x.AsLatestStable() : x);

                ReleaseCache = results;
                LatestRelease = latestRelease;

                return Optional(ReleaseCache).ToList().Flatten();
            } catch (Exception e)
            {
                logger.Error("Failed to connect to github to retrieve version information", e);
                return ImmutableList.CreateBuilder<ReleaseTarget>().ToImmutableList();
            }
        }

        private static Option<SemVersion> ParseTag(string tag) {
            try {
                var versionString = tag;
                if (versionString.StartsWith("v")) {
                    versionString = versionString[1..];
                }
                return Some(SemVersion.Parse(versionString, SemVersionStyles.Any));
            } catch {
                logger.Info($"Failed to parse version tag {tag}");
                return None;
            }
        }
    }
}