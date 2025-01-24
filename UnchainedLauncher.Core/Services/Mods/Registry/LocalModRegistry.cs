using LanguageExt;
using LanguageExt.Common;
using System.Collections.Immutable;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class LocalModRegistry : JsonRegistry {
        public override string Name => $"Local filesystem registry at {RegistryPath}";
        public IModRegistryDownloader ModRegistryDownloader { get; }
        public string RegistryPath { get; }
        public LocalModRegistry(string registryPath, IModRegistryDownloader downloader) {
            RegistryPath = registryPath;
            ModRegistryDownloader = downloader;
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
                .Bind(release => ModRegistryDownloader.ModPakStream(release))
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
    }
}