using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public class LocalFilePakDownloader : IModRegistryDownloader {
        public string PakReleasesDir { get; set; }

        public LocalFilePakDownloader(string pakReleasesDir) {
            PakReleasesDir = pakReleasesDir;
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(Release release) {
            // Paks will be found in PakReleasesDir/org/repoName/releaseTag/fileName
            var path = Path.Combine(PakReleasesDir, release.Manifest.Organization, release.Manifest.RepoName, release.Tag, release.PakFileName);

            if (!File.Exists(path))
                return
                    Prelude
                        .LeftAsync<ModPakStreamAcquisitionFailure, SizedStream>(
                            new ModPakStreamAcquisitionFailure(
                                ReleaseCoordinates.FromRelease(release),
                                Error.New($"Failed to fetch pak. File not found: {path}")
                            )
                        );

            return
                Prelude
                    .TryAsync(Task.Run(() => File.OpenRead(path)))
                    .Map(stream => new SizedStream(stream, stream.Length))
                    .ToEither()
                    .MapLeft(e =>
                        new ModPakStreamAcquisitionFailure(
                            ReleaseCoordinates.FromRelease(release),
                            Error.New($"Failed to fetch pak from {path}.", e)
                        )
                    );
        }
    }
}