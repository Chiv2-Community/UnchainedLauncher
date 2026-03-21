using Semver;

namespace UnchainedLauncher.Core.Extensions {
    public static class SemVersionExtensions {
        /// <param name="current">The current version, which may contain metadata.</param>
        extension(SemVersion? current)
        {
            /// <summary>
            /// Compares the current version to a release version as seen in "SelectLatestVersion" in the installer.
            /// This method is intended to identify if the current version "matches" a release version,
            /// particularly when handling metadata (+build info) which should be ignored during match.
            /// </summary>
            /// <param name="release">The release version.</param>
            /// <returns>True if they match (ignoring metadata).</returns>
            public bool MatchesRelease(SemVersion? release) {
                if (current == null || release == null) return false;

                // If it's a prerelease, we ignore the metadata (e.g., v1.2.3-beta+build.1 matches v1.2.3-beta)
                return PrecedenceIgnoreCaseComparer.Compare(current.WithoutMetadata(), release.WithoutMetadata()) == 0;
            }

            /// <summary>
            /// Checks if the current version is at least the provided version (ignoring metadata).
            /// </summary>
            public bool IsAtLeast(SemVersion? release) {
                if (current == null || release == null) return false;
                return PrecedenceIgnoreCaseComparer.Compare(current, release) >= 0;
            }
        }

        /// <summary>
        /// A comparer that ignores case for precedence, ensuring that e.g. 1.0.0-RC1 > 1.0.0-rc0
        /// </summary>
        public static IComparer<SemVersion> PrecedenceIgnoreCaseComparer { get; } = Comparer<SemVersion>.Create((x, y) => {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var xLower = SemVersion.Parse(x.ToString().ToLowerInvariant(), SemVersionStyles.Any);
            var yLower = SemVersion.Parse(y.ToString().ToLowerInvariant(), SemVersionStyles.Any);
            return xLower.ComparePrecedenceTo(yLower);
        });
    }
}