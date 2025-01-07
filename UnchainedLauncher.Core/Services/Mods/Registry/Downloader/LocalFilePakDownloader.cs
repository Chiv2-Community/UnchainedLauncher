using LanguageExt;
using LanguageExt.Common;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public class LocalFilePakDownloader : IModRegistryDownloader {
        public string PakReleasesDir;

        public LocalFilePakDownloader(string pakReleasesDir) {
            PakReleasesDir = pakReleasesDir;
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(PakTarget target) {
            // Paks will be found in PakReleasesDir/org/repoName/releaseTag/fileName
            var path = Path.Combine(PakReleasesDir, target.Org, target.RepoName, target.ReleaseTag, target.FileName);

            if (!File.Exists(path))
                return
                    Prelude
                        .LeftAsync<ModPakStreamAcquisitionFailure, SizedStream>(
                            new ModPakStreamAcquisitionFailure(
                                target,
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
                            target,
                            Error.New($"Failed to fetch pak from {path}.", e)
                        )
                    );
        }
    }
}