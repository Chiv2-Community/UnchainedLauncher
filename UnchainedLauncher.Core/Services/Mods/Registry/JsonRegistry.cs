using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public abstract class JsonRegistry : IModRegistry {
        protected static readonly ILog logger = LogManager.GetLogger(nameof(JsonRegistry));

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
        public EitherAsync<RegistryMetadataException, JsonModels.Metadata.V3.Mod> GetMod(ModIdentifier modPath) {
            return GetModMetadataString(modPath).Bind(json =>
                // Try to deserialize as V3 first
                JsonHelpers.Deserialize<JsonModels.Metadata.V3.Mod>(json).RecoverWith(e => {
                    // If that fails, try to deserialize as V2
                    logger.Warn("Falling back to V2 deserialization: " + e?.Message ?? "unknown failure");
                    return JsonHelpers.Deserialize<JsonModels.Metadata.V2.Mod>(json)
                        .Select(JsonModels.Metadata.V3.Mod.FromV2);
                }).RecoverWith(e => {
                    // If that fails, try to deserialize as V1
                    logger.Warn("Falling back to V1 deserialization" + e?.Message ?? "unknown failure");
                    return JsonHelpers.Deserialize<JsonModels.Metadata.V1.Mod>(json)
                        .Select(JsonModels.Metadata.V2.Mod.FromV1)
                        .Select(JsonModels.Metadata.V3.Mod.FromV2);
                })
                .ToEither()
                .ToAsync()
                .MapLeft(err => RegistryMetadataException.Parse($"Failed to parse mod manifest at {modPath}", Expected.New(err)))
            );
        }
    }
}