using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class AggregateModRegistry : IModRegistry {
        private static readonly ILog _logger = LogManager.GetLogger(typeof(AggregateModRegistry));

        public string Name =>
            "AggregateModRegistry of: \n\t" +
                string.Join("\n\t", ModRegistries.Select(m => m.Name));

        public IList<IModRegistry> ModRegistries { get; }

        public AggregateModRegistry(IEnumerable<IModRegistry> modRegistries) {
            ModRegistries = modRegistries.OrderBy(m => m.Name).ToList();
        }

        public AggregateModRegistry(params IModRegistry[] registries) : this(registries.ToList()) { }

        public async Task<GetAllModsResult> GetAllMods() {
            // TODO: Switch to SequenceParallel to speed this up
            //       It may produce out-of-order results, so something will need to be done to fix the ordering.
            var allModsFromAllRegistries =
                await ModRegistries
                    .Select(reg => reg.GetAllMods())
                    .SequenceSerial().Select(x => x.ToList());

            var allErrors = allModsFromAllRegistries.SelectMany(namedRegistry => namedRegistry.Errors);
            var mods = allModsFromAllRegistries.SelectMany(namedRegistry => namedRegistry.Mods);
            var deduplicatedMods = DeduplicateMods(mods);

            return new GetAllModsResult(allErrors, deduplicatedMods);
        }

        public EitherAsync<RegistryMetadataException, Mod> GetMod(ModIdentifier modId) {
            return InternalGetModMetadata(modId, ModRegistries.ToList(), None, None).ToAsync();

            async Task<Either<RegistryMetadataException, Mod>> InternalGetModMetadata(
                ModIdentifier modId,
                List<IModRegistry> remainingRegistries,
                Option<Mod> previousMod,
                Option<Error> previousError) {
                if (!remainingRegistries.Any()) {
                    return previousMod.ToEither(() => RegistryMetadataException.NotFound(modId, previousError));
                }

                var currentRegistry = remainingRegistries[0];
                var remainingRegistriesTail = remainingRegistries.Tail().ToList();

                var result = await currentRegistry.GetMod(modId);
                return await result.Match<Task<Either<RegistryMetadataException, Mod>>>(
                    Left: async error =>
                        await InternalGetModMetadata(
                            modId,
                            remainingRegistriesTail,
                            previousMod,
                            Some(error.ToErrorException().ToError())
                        ),
                    Right: async newMod =>
                        await InternalGetModMetadata(
                            modId,
                            remainingRegistriesTail,
                            previousMod.Match(
                                None: () => Some(newMod),
                                Some: prevMod => Some(MergeMods(prevMod, newMod))
                            ),
                            previousError
                        )
                );
            }
        }

        // This could be made more efficient by iterating through each registry and calling GetModRelease
        // But this is simpler and also works.
        public EitherAsync<RegistryMetadataException, Release> GetModRelease(ReleaseCoordinates coords) =>
            GetMod(coords)
                .Map(mod => Optional(mod.Releases.Find(coords.Matches)))
                .Bind(maybeRelease =>
                    maybeRelease.ToEitherAsync(() => RegistryMetadataException.NotFound(coords, None)));

        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation) {
            return InternalDownloadPak(coordinates, ModRegistries, None).ToAsync();

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

        // Helper methods used to deduplicate all mods and their releases
        private Mod MergeMods(Mod first, Mod second) {
            var firstLatestReleaseDate = first.LatestRelease.Select(x => x.ReleaseDate);
            var secondLatestReleaseDate = second.LatestRelease.Select(x => x.ReleaseDate);
            var latestManifest =
                firstLatestReleaseDate > secondLatestReleaseDate ? first.LatestManifest : second.LatestManifest;

            var releases = MergeReleases(first, second);

            return new Mod(latestManifest, releases);
        }

        private List<Release> MergeReleases(Mod first, Mod second) {
            var initialReleaseMap =
                first.Releases.Select(x => (ReleaseCoordinates.FromRelease(x), x)).ToHashMap();

            // TODO: How to handle two releases with the same identifier but differing hashes?
            return second.Releases.Fold(initialReleaseMap, (deduplicatedReleases, release) =>
                deduplicatedReleases.AddOrUpdate(
                    key: ReleaseCoordinates.FromRelease(release),
                    Some: current => current,
                    None: () => release)
            ).Values.OrderByDescending(x => x.ReleaseDate).ToList();
        }

        private IEnumerable<Mod> DeduplicateMods(IEnumerable<Mod> allMods) {
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
}