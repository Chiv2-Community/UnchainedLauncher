using Semver;
using UnchainedLauncher.Core.Extensions;
using Xunit;

namespace UnchainedLauncher.Core.Tests.Unit.Extensions {
    public class SemVersionExtensionsTests {
        [Theory]
        [InlineData("1.2.3", "1.2.3", true)]
        [InlineData("1.2.3+build", "1.2.3", true)]
        [InlineData("1.2.3-beta", "1.2.3-beta", true)]
        [InlineData("1.2.3-beta+build", "1.2.3-beta", true)]
        [InlineData("1.2.3", "1.2.4", false)]
        [InlineData("1.2.3-alpha", "1.2.3-beta", false)]
        [InlineData(null, "1.2.3", false)]
        [InlineData("1.2.3", null, false)]
        public void MatchesRelease_ShouldMatchCorrectVersions(string? currentStr, string? releaseStr, bool expected) {
            SemVersion? current = currentStr != null ? SemVersion.Parse(currentStr, SemVersionStyles.Any) : null;
            SemVersion? release = releaseStr != null ? SemVersion.Parse(releaseStr, SemVersionStyles.Any) : null;

            var result = current.MatchesRelease(release);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1.2.3", "1.2.2", true)]
        [InlineData("1.2.3", "1.2.3", true)]
        [InlineData("1.2.3", "1.2.4", false)]
        [InlineData("1.2.3-beta", "1.2.3-alpha", true)]
        [InlineData("1.2.3-alpha", "1.2.3-beta", false)]
        [InlineData("1.2.3+build", "1.2.3", true)]
        public void IsAtLeast_ShouldReturnCorrectComparison(string currentStr, string releaseStr, bool expected) {
            SemVersion current = SemVersion.Parse(currentStr, SemVersionStyles.Any);
            SemVersion release = SemVersion.Parse(releaseStr, SemVersionStyles.Any);

            var result = current.IsAtLeast(release);

            Assert.Equal(expected, result);
        }
    }
}
