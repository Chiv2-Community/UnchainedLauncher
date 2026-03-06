using Semver;

namespace UnchainedLauncher.Core.Extensions {
    public static class SemVersionExtensions {
        /// <summary>
        /// Compares the current version to a release version as seen in "SelectLatestVersion" in the installer.
        /// This method is intended to identify if the current version "matches" a release version,
        /// particularly when handling metadata (+build info) which should be ignored during match.
        /// </summary>
        /// <param name="current">The current version, which may contain metadata.</param>
        /// <param name="release">The release version.</param>
        /// <returns>True if they match (ignoring metadata).</returns>
        public static bool MatchesRelease(this SemVersion? current, SemVersion? release) {
            if (current == null || release == null) return false;

            // If it's a prerelease, we ignore the metadata (e.g., v1.2.3-beta+build.1 matches v1.2.3-beta)
            if (current.IsPrerelease) {
                return current.WithoutMetadata().Equals(release.WithoutMetadata());
            }

            // Otherwise, simple equality check (metadata is usually not present in stable releases, but we'll be safe)
            return current.WithoutMetadata().Equals(release.WithoutMetadata());
        }

        /// <summary>
        /// Checks if the current version is at least the provided version (ignoring metadata).
        /// </summary>
        public static bool IsAtLeast(this SemVersion? current, SemVersion? release) {
            if (current == null || release == null) return false;
            return current.ComparePrecedenceTo(release) >= 0;
        }
    }
}
