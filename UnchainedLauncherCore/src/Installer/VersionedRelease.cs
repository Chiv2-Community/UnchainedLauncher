using Octokit;
using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.Installer
{
    public record VersionedRelease(Release Release, SemVersion Version, bool IsLatestStable) {
        public VersionedRelease AsLatestStable() => this with { IsLatestStable = true };
        public string DisplayText => $"{Release.TagName} ({Release.CreatedAt.ToString("d")})" + (IsLatestStable ? " Recommended" : "");


        public static VersionedRelease CreateMockRelease(SemVersion version) =>
            new VersionedRelease(new Release(
                "http://github.com/chiv2-community/UnchainedLauncher",
                "http://github.com/chiv2-community/UnchainedLauncher",
                $"https://github.com/Chiv2-Community/UnchainedLauncher/releases/download/v{version}/",
                $"https://github.com/Chiv2-Community/UnchainedLauncher/releases/download/v{version}/UnchainedLauncher.exe",
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
                $"https://github.com/Chiv2-Community/UnchainedLauncher/archive/refs/tags/v{version}.tar.gz",
                $"https://github.com/Chiv2-Community/UnchainedLauncher/archive/refs/tags/v{version}.zip",
                new List<ReleaseAsset> {
                new ReleaseAsset()
                }) {
            }, version, false);

        public static readonly IEnumerable<VersionedRelease> DefaultMockReleases = new List<VersionedRelease> {
            CreateMockRelease(SemVersion.Parse("1.0.0-RC1", SemVersionStyles.Any)),
            CreateMockRelease(SemVersion.Parse("0.7.4", SemVersionStyles.Any)),
            CreateMockRelease(SemVersion.Parse("0.7.3", SemVersionStyles.Any))
        }.OrderByDescending(x => x.Version);

    }
}
