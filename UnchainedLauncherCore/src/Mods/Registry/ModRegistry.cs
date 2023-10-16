using LanguageExt;
using log4net;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods.Registry.Resolver;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry
{
    /// <summary>
    /// A ModRegistry contains all relevant metadata for a repository of mods
    /// A ModRegistry does not necessarily know how to download the mods, however
    /// it will provide the coordinates which can be used to download the mods via
    /// ModRegistryPakFetcher
    /// </summary>
    public abstract class ModRegistry
    {
        protected static readonly ILog logger = LogManager.GetLogger(nameof(ModRegistry));
        public ModRegistryDownloader ModRegistryDownloader { get; }
        public string Name { get; }

        public ModRegistry(string name, ModRegistryDownloader downloader) {
            Name = name;
            ModRegistryDownloader = downloader;
        }


        /// <summary>
        /// Get all mod metadata from this registry
        /// </summary>
        /// <returns></returns>
        public abstract Task<(IEnumerable<RegistryMetadataException>, IEnumerable<Mod>)> GetAllMods();

        /// <summary>
        /// Gets the mod metadata string located at the given path within the registry
        /// </summary>
        /// <param name="modPath"></param>
        /// <returns></returns>
        public abstract EitherAsync<RegistryMetadataException, string> GetModMetadataString(string modPath);

        /// <summary>
        /// Gets the mod metadata located at the given path within the registry
        /// </summary>
        /// <param name="modPath"></param>
        /// <returns></returns>
        public EitherAsync<RegistryMetadataException, Mod> GetModMetadata(string modPath) {
            return GetModMetadataString(modPath).Bind(json =>
                // Try to deserialize as V3 first
                JsonHelpers.Deserialize<Mod>(json).RecoverWith(e => {
                    // If that fails, try to deserialize as V2
                    logger.Warn("Falling back to V2 deserialization: " + e?.Message ?? "unknown failure");
                    return JsonHelpers.Deserialize<JsonModels.Metadata.V2.Mod>(json)
                        .Select(Mod.FromV2);
                }).RecoverWith(e => {
                    // If that fails, try to deserialize as V1
                    logger.Warn("Falling back to V1 deserialization" + e?.Message ?? "unknown failure");
                    return JsonHelpers.Deserialize<JsonModels.Metadata.V1.Mod>(json)
                        .Select(JsonModels.Metadata.V2.Mod.FromV1)
                        .Select(Mod.FromV2);
                })
                .ToEither()
                .ToAsync()
                .MapLeft(err => new RegistryMetadataException(modPath, err))
            );
        }

        public EitherAsync<string, Stream> DownloadPak(PakTarget coordinates) {
              return ModRegistryDownloader.DownloadModPak(coordinates);
        }
        public EitherAsync<string, Stream> DownloadPak(string Org, string RepoName, string FileName, string ReleaseTag) {
            return DownloadPak(new PakTarget(Org, RepoName, FileName, ReleaseTag));
        }

        public EitherAsync<string, Stream> DownloadPak(Release release) {
              return DownloadPak(release.Manifest.Organization, release.Manifest.RepoName, release.PakFileName, release.Tag);
        }
    }

    public class RegistryMetadataException : Exception {
        public string ModPath { get; }
        public RegistryMetadataException(string modPath, Exception underlying) : base($"Failed to get mod metadata at {modPath}", underlying) { 
            ModPath = modPath;
        }
    }
}
