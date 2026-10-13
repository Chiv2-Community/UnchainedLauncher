using Semver;
using UnchainedLauncher.GUI.JsonModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit {
    public sealed class PluginReleaseDefaultsTests {
        [Theory]
        [InlineData("1.0.0-RC4", "1.0.0-RC4", false, true)]
        [InlineData("1.0.0-RC4", "1.0.0-RC4", null, true)]
        [InlineData("1.0.0-RC4", "0.5.0", false, true)]
        [InlineData("1.0.0-RC4", null, false, true)]
        [InlineData("1.0.0", "1.0.0", true, true)]
        [InlineData("1.0.0", "1.0.0", false, false)]
        [InlineData("1.0.0", "1.0.0", null, false)]
        [InlineData("1.0.0", "1.0.0-RC4", true, false)]
        [InlineData("1.0.0", "0.5.0", true, false)]
        [InlineData("1.0.0", null, true, false)]
        [InlineData("1.0.0+build.2", "1.0.0+build.1", true, true)]
        public void PrereleaseBuildsEnableUnstablePluginsRegardlessOfSavedPreference(string current, string? previous, bool? saved, bool expected) {
            var settings = new LauncherSettings(null, null, null, null, null, null, saved,
                previous == null ? null : SemVersion.Parse(previous, SemVersionStyles.Strict));
            Assert.Equal(expected, settings.ShouldAllowUnstablePluginReleases(SemVersion.Parse(current, SemVersionStyles.Strict)));
        }
    }
}
