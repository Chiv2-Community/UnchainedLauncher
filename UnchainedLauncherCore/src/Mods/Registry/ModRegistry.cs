using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods.Registry.Resolver;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry
{

    public record GetAllModsResult(IEnumerable<GetAllModsResult> Errors, IEnumerable<Mod> Mods) {
        public bool HasErrors => Errors.Any();

        public GetAllModsResult AddError(GetAllModsResult error) {
            return this with { Errors = Errors.Append(error) };
        }

        public GetAllModsResult AddMod(Mod mod) {
            return this with { Mods = Mods.Append(mod) };
        }

        public static GetAllModsResult operator +(GetAllModsResult a, GetAllModsResult b) {
            return new GetAllModsResult(a.Errors.Concat(b.Errors), a.Mods.Concat(b.Mods));
        }
    };

        /// <summary>
        /// A ModRegistry contains all relevant metadata for a repository of mods
        /// A ModRegistry does not necessarily know how to download the mods, however
        /// it will provide the coordinates which can be used to download the mods via
        /// ModRegistryPakFetcher
        /// </summary>
    public interface IModRegistry
    {
        protected static readonly ILog logger = LogManager.GetLogger(nameof(IModRegistry));
        public ModRegistryDownloader ModRegistryDownloader { get; }
        public string Name { get; }


        /// <summary>
        /// Get all mod metadata from this registry
        /// </summary>
        /// <returns></returns>
        public abstract Task<GetAllModsResult> GetAllMods();

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

        public EitherAsync<Error, FileWriter> DownloadPak(PakTarget coordinates, string outputLocation) {
            return 
                ModRegistryDownloader
                    .ModPakStream(coordinates)
                    .Map(stream => new FileWriter(outputLocation, stream));
        }
        public EitherAsync<Error, FileWriter> DownloadPak(string org, string repoName, string fileName, string releaseTag, string outputLocation) {
            return DownloadPak(new PakTarget(org, repoName, fileName, releaseTag), outputLocation);
        }

        public EitherAsync<Error, FileWriter> DownloadPak(Release release, string outputLocation) {
            return DownloadPak(release.Manifest.Organization, release.Manifest.RepoName, release.PakFileName, release.Tag, outputLocation);
        }
    }

    public class RegistryMetadataException : Exception {
        public string ModPath { get; }
        public RegistryMetadataException(string modPath, Exception underlying) : base($"Failed to get mod metadata at {modPath}", underlying) { 
            ModPath = modPath;
        }
    }
}
