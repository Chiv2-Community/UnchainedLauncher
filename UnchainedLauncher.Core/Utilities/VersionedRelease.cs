using Octokit;
using Semver;

namespace UnchainedLauncher.Core.Utilities {

    public record VersionedRelease(Release Release, SemVersion Version, bool IsLatestStable) {
        public VersionedRelease AsLatestStable() => this with { IsLatestStable = true };
        public string DisplayText => $"{Release.TagName} ({Release.CreatedAt:d})" + (IsLatestStable ? " Recommended" : "");

        public static VersionedRelease CreateMockRelease(SemVersion version) {
            var ghRepo = "http://github.com/chiv2-community/UnchainedLauncher";
            return new VersionedRelease(new Release(
                ghRepo,
                ghRepo,
                $"{ghRepo}/releases/download/v{version}/",
                $"{ghRepo}/releases/download/v{version}/UnchainedLauncher.exe",
                1234,
                "test",
                $"v{version}",
                "50e90c3",
                $"Unchained Launcher v{version}",
                "This is a test release from mock data",
                false,
                false,
                DateTimeOffset.Now,
                DateTimeOffset.Now,
                new Author(),
                $"{ghRepo}/archive/refs/tags/v{version}.tar.gz",
                $"{ghRepo}/archive/refs/tags/v{version}.zip",
                new List<Octokit.ReleaseAsset> {
                new Octokit.ReleaseAsset()
                }) {
            }, version, false);
        }

        public static readonly IEnumerable<VersionedRelease> DefaultMockReleases = new List<VersionedRelease> {
            CreateMockRelease(SemVersion.Parse("1.0.0-RC1", SemVersionStyles.Any)),
            CreateMockRelease(SemVersion.Parse("0.7.4", SemVersionStyles.Any)),
            CreateMockRelease(SemVersion.Parse("0.7.3", SemVersionStyles.Any))
        }.OrderByDescending(x => x.Version);
    }
}