using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using System.Collections.Immutable;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;
using Release = UnchainedLauncher.Core.JsonModels.Metadata.V3.Release;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class LocalModRegistry : JsonRegistry {
        public override string Name => $"Local filesystem registry at {RegistryPath}";
        public string RegistryPath { get; set; }
        public LocalModRegistry(string registryPath) {
            RegistryPath = registryPath;
            if (!Directory.Exists(RegistryPath)) Directory.CreateDirectory(RegistryPath);
        }

        public event Action<ReleaseCoordinates>? OnRegistryChanged;

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

        public Task<bool> DeleteRelease(ReleaseCoordinates coordinates) {
            return GetMod(coordinates)
                .Bind(mod => GetModRelease(coordinates)
                .Map(release => (mod, release)))
                .Match(
                    result => {
                        var mod = result.mod!;
                        var toDelete = result.release!;
                        
                        var modPath = Path.Combine(RegistryPath, coordinates.Org, coordinates.ModuleName);
                        var releasePath = Path.Combine(modPath, toDelete.Tag);
                        mod.Releases.Remove(toDelete);

                        if (mod.Releases.Count == 0) {
                            Directory.Delete(modPath, true);
                        }
                        else {  
                            Directory.Delete(releasePath, true);
                            WriteMod(mod with {
                                // we know this unwrap is safe because if there were no releases, we would have deleted
                                LatestManifest = mod.LatestRelease.ValueUnsafe().Manifest
                            });
                        }
                        
                        OnRegistryChanged?.Invoke(coordinates);
                        return true;
                    },
                    _ => {
                        Logger.Warn($"Failed to delete release {coordinates.Org}/{coordinates.ModuleName}/{coordinates.Version}. Mod or specific release not found.");
                        return false;
                    }
                );
        }

        public async Task AddRelease(Release newVersion, string pakPath) {
            ReleaseCoordinates coordinates = ReleaseCoordinates.FromRelease(newVersion);
            string modDirectory = Path.Combine(RegistryPath, coordinates.Org, coordinates.ModuleName);
            string manifestPath = Path.Combine(modDirectory, $"{coordinates.ModuleName}.json");
            var mod = await InternalGetModMetadata(manifestPath)
                .Match(
                    existing => new Mod(
                                newVersion.Manifest,
                                existing.Releases
                                    .Filter(x => !ReleaseCoordinates.FromRelease(x).Matches(newVersion))
                                    .Append(newVersion).ToList()
                            ),
                    _ => new Mod(
                        newVersion.Manifest,
                        new List<Release> { newVersion }
                        )
                    );

            CopyFileForRelease(newVersion, pakPath);
            WriteMod(mod);
            OnRegistryChanged?.Invoke(coordinates);
        }

        private async Task CopyFileForRelease(Release release, string sourceFile) {
            Task.Run(() => {
                string releaseDirectory = Path.Combine(
                    RegistryPath,
                    release.Manifest.Organization,
                    release.Manifest.Name,
                    release.Tag
                );

                string releasePakPath = Path.Combine(releaseDirectory, release.PakFileName);
                Directory.CreateDirectory(releaseDirectory);
                if (sourceFile == releasePakPath) return;
                File.Copy(sourceFile, releasePakPath, true);
            });
        }

        private async Task WriteMod(Mod modToWrite) {
            ModIdentifier ident = ModIdentifier.FromMod(modToWrite);
            string modDirectory = Path.Combine(RegistryPath, ident.Org, ident.ModuleName);
            string manifestPath = Path.Combine(modDirectory, $"{ident.ModuleName}.json");
            Directory.CreateDirectory(modDirectory);
            await File.WriteAllTextAsync(manifestPath, JsonHelpers.Serialize(modToWrite));
        }

        private EitherAsync<RegistryMetadataException, Mod> InternalGetModMetadata(string jsonManifestPath) {
            var dir = Path.GetDirectoryName(jsonManifestPath);
            if (dir == null) {
                return
                    LeftAsync<RegistryMetadataException, Mod>(RegistryMetadataException.Parse($"Failed to parse json manifest path {jsonManifestPath}. Got null directory name.", None));
            }

            var parts = dir.Split(Path.DirectorySeparatorChar);
            if (parts.Length() < 2) return LeftAsync<RegistryMetadataException, Mod>(RegistryMetadataException.PackageListRetrieval($"Failed to determine module id for file at path {jsonManifestPath}", None));
            var modIdParts = parts.Reverse().Take(2).Reverse();

            return GetMod(new ModIdentifier(modIdParts.First(), modIdParts.Last()));
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
                            Logger.Warn($"Found multiple candidates for manifests at {path}. Using the first one.");
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