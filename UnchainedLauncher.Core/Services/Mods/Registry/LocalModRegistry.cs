using LanguageExt;
using LanguageExt.Common;
using System.Collections.Immutable;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class LocalModRegistry : JsonRegistry {
        public override string Name => $"Local filesystem registry at {RegistryPath}";
        public string RegistryPath { get; set; }
        public LocalModRegistry(string registryPath) {
            RegistryPath = registryPath;
        }

        public override EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation) {
            return GetMod(coordinates)
                .Map(releaseMetadata => Optional(releaseMetadata.Releases.Find(x => x.Tag == coordinates.Version)))
                .MapLeft(e => new ModPakStreamAcquisitionFailure(coordinates, e))
                .Bind(maybeRelease => maybeRelease.ToEitherAsync(() =>
                    new ModPakStreamAcquisitionFailure(
                        coordinates,
                        Error.New($"Failed to fetch pak. No releases found for {coordinates.Org} / {coordinates.ModuleName} / {coordinates.Version}.")
                )))
                .Bind(ModPakStream)
                .Map(sizedStream => new FileWriter(outputLocation, sizedStream.Stream, sizedStream.Size));
        }

        public override Task<GetAllModsResult> GetAllMods() {
            return Task
                .Run(() => Directory.EnumerateFiles(RegistryPath, "*.json", SearchOption.AllDirectories))
                .Bind(paths =>
                    paths.ToImmutableList()
                        .Select(InternalGetModMetadata)
                        .Partition()
                        .Select(t => new GetAllModsResult(t.Lefts, t.Rights))
                );

            EitherAsync<RegistryMetadataException, Mod> InternalGetModMetadata(string jsonManifestPath) {
                var dir = Path.GetDirectoryName(jsonManifestPath);
                var parts = dir.Split(Path.DirectorySeparatorChar);
                if (parts.Length() < 2) return LeftAsync<RegistryMetadataException, Mod>(RegistryMetadataException.PackageListRetrieval($"Failed to determine module id for file at path {jsonManifestPath}", None));
                var modIdParts = parts.Reverse().Take(2).Reverse();

                return GetMod(new ModIdentifier(modIdParts.First(), modIdParts.Last()));
            }
        }

        protected override EitherAsync<RegistryMetadataException, string> GetModMetadataString(ModIdentifier modId) {
            return EitherAsync<RegistryMetadataException, string>
                .Right(Path.Combine(RegistryPath, modId.Org, modId.ModuleName))
                .BindAsync(path => Task.Run(() => {
                    if (!Directory.Exists(path))
                        return EitherAsync<RegistryMetadataException, string>
                            .Left(RegistryMetadataException.NotFound(modId, Some(Error.New(new IOException("File not found")))));

                    var manifests = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly);

                    switch (manifests.Count()) {
                        case > 1:
                            logger.Warn($"Found multiple candidates for manifests at {path}. Using the first one.");
                            break;
                        case 0:
                            return LeftAsync<RegistryMetadataException, string>(RegistryMetadataException.NotFound(modId, Some(Error.New("No manifests"))));
                    }

                    var manifest = manifests.Single();

                    return Prelude
                        .TryAsync(Task.Run(() => File.ReadAllText(manifest)))
                        .ToEither()
                        .MapLeft(e => RegistryMetadataException.NotFound(modId, e));
                }));
        }

        private EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(Release release) {
            // Paks will be found in PakReleasesDir/org/repoName/releaseTag/fileName
            var path = Path.Combine(RegistryPath, release.Manifest.Organization, release.Manifest.RepoName, release.Tag, release.PakFileName);

            if (!File.Exists(path))
                return
                    LeftAsync<ModPakStreamAcquisitionFailure, SizedStream>(
                            new ModPakStreamAcquisitionFailure(
                                ReleaseCoordinates.FromRelease(release),
                                Error.New($"Failed to fetch pak. File not found: {path}")
                            )
                        );

            return
                TryAsync(Task.Run(() => File.OpenRead(path)))
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