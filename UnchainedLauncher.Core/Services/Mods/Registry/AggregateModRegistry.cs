﻿using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    record NamedModRegistry(string Name, IModRegistry Registry);

    public class AggregateModRegistry : IModRegistry {
        private static readonly ILog _logger = LogManager.GetLogger(typeof(AggregateModRegistry));
        
        public string Name => 
            "AggregateModRegistry of: \n\t" + 
                string.Join("\n\t", _modRegistries.Select(m => m.Name));

        private IOrderedEnumerable<IModRegistry> _modRegistries { get; }
        public async Task<GetAllModsResult> GetAllMods() {
            // TODO: Switch to SequenceParallel to speed this up
            //       It may produce out-of-order results, so something will need to be done to fix the ordering.
            var allModsFromAllRegistries =
                await _modRegistries
                    .Select(reg => reg.GetAllMods())
                    .SequenceSerial();

            var allErrors = allModsFromAllRegistries.SelectMany(namedRegistry => namedRegistry.Errors);
            var mods = allModsFromAllRegistries.SelectMany(namedRegistry => namedRegistry.Mods);
            var deduplicatedMods = DeduplicateMods(mods);

            return new GetAllModsResult(allErrors, deduplicatedMods);

            // Helper methods used to deduplicate all mods and their releases
            Mod MergeMods(Mod first, Mod second) {
                var firstLatestReleaseDate = first.LatestRelease.Select(x => x.ReleaseDate);
                var secondLatestReleaseDate = second.LatestRelease.Select(x => x.ReleaseDate);
                var latestManifest =
                    firstLatestReleaseDate > secondLatestReleaseDate ? first.LatestManifest : second.LatestManifest;

                var releases = MergeReleases(first, second);

                return new Mod(latestManifest, releases);
            }

            List<Release> MergeReleases(Mod first, Mod second) {
                var initialReleaseMap =
                    first.Releases.Select(x => (ReleaseCoordinates.FromRelease(x), x)).ToHashMap();

                // TODO: How to handle two releases with the same identifier but differing hashes?
                return second.Releases.Fold(initialReleaseMap, (deduplicatedReleases, release) =>
                    deduplicatedReleases.AddOrUpdate(
                        key: ReleaseCoordinates.FromRelease(release),
                        Some: current => current,
                        None: () => release)
                ).Values.ToList();
            }

            IEnumerable<Mod> DeduplicateMods(IEnumerable<Mod> allMods) {
                var initialMods = new HashMap<ModIdentifier, Mod>();

                return allMods.Fold(initialMods, (deduplicatedMods, mod) =>
                    deduplicatedMods.AddOrUpdate(
                        key: ModIdentifier.FromMod(mod),
                        Some: existingMod => MergeMods(existingMod, mod),
                        None: () => mod
                    )
                ).Values.ToList();
            }
        }

        public EitherAsync<RegistryMetadataException, Mod> GetMod(ModIdentifier modId) {
            return InternalGetModMetadata(modId, _modRegistries, None).ToAsync();

            // TODO: Clean this up. All these conversions between EitherAsync<...> and Task<Either<...>> are so ugly.
            async Task<Either<RegistryMetadataException, Mod>> InternalGetModMetadata(
                ModIdentifier modId,
                IEnumerable<IModRegistry> remainingRegistries, Option<Error> previousError) {
                var registry = remainingRegistries.FirstOrDefault();
                if (registry == null)
                    return Left(
                        RegistryMetadataException.NotFound(modId, previousError)
                    );

                var result =
                    await registry.GetMod(modId);

                return await result
                    .ToAsync()
                    .BindLeft<RegistryMetadataException>(err =>
                        InternalGetModMetadata(modId, remainingRegistries.Tail(),
                            Some(err.ToErrorException().ToError())).ToAsync()
                    )
                    .ToEither();
            }
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation) {
            return InternalDownloadPak(coordinates, _modRegistries, None).ToAsync();

            // TODO: Clean this up. All these conversions between EitherAsync<...> and Task<Either<...>> are so ugly.
            async Task<Either<ModPakStreamAcquisitionFailure, FileWriter>> InternalDownloadPak(
                ReleaseCoordinates coordinates,
                IEnumerable<IModRegistry> remainingRegistries, Option<Error> previousError) {
                var registry = remainingRegistries.FirstOrDefault();
                if (registry == null)
                    return Left(new ModPakStreamAcquisitionFailure(coordinates, previousError.IfNone(Errors.None)));

                var result =
                    await registry.DownloadPak(coordinates, outputLocation);

                return await result
                    .ToAsync()
                    .BindLeft<ModPakStreamAcquisitionFailure>(err =>
                        InternalDownloadPak(coordinates, remainingRegistries.Tail(),
                            Some(err.ToErrorException().ToError())).ToAsync()
                    )
                    .ToEither();
            }
        }


    }
}