using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public abstract class JsonRegistry : IModRegistry {
        protected static readonly ILog Logger = LogManager.GetLogger(nameof(JsonRegistry));

        // Re-export IModRegistryMethods that will not be implemented here.
        public abstract string Name { get; }
        public abstract EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation);

        public abstract Task<GetAllModsResult> GetAllMods();

        /// <summary>
        /// Gets the mod metadata in a string format.
        /// 
        /// Implementations of JsonRegistry need only implement this, and then the JsonRegistry GetModMetadata
        /// method will handle the parsing of the mod metadata.
        /// </summary>
        /// <param name="modId"></param>
        /// <returns></returns>
        protected abstract EitherAsync<RegistryMetadataException, string> GetModMetadataString(ModIdentifier modId);

        /// <summary>
        /// Gets the mod metadata located at the given path within the registry
        /// </summary>
        /// <param name="modPath"></param>
        /// <returns></returns>
        public EitherAsync<RegistryMetadataException, JsonModels.ModMetadata.Mod> GetMod(ModIdentifier modPath) {
            return GetModMetadataString(modPath).Bind(json =>
                JsonHelpers
                    .Deserialize<Mod>(json)
                    .ToEither()
                    .ToAsync()
                    .MapLeft(err => RegistryMetadataException.Parse($"Failed to parse mod manifest at {modPath}", Expected.New(err)))
            );
        }

        public EitherAsync<RegistryMetadataException, Release> GetModRelease(ReleaseCoordinates coords) =>
            GetMod(coords)
                .Map(mod => Optional(mod.Releases.Find(coords.Matches)))
                .Bind<Release>(maybeRelease =>
                    maybeRelease.ToEitherAsync(() => RegistryMetadataException.NotFound(coords, None)));
    }
}