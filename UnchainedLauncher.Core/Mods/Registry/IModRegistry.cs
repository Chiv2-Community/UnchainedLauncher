using LanguageExt;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry {

    public record GetAllModsResult(IEnumerable<RegistryMetadataException> Errors, IEnumerable<Mod> Mods) {
        public bool HasErrors => Errors.Any();

        public GetAllModsResult AddError(RegistryMetadataException error) {
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
    public interface IModRegistry {
        protected static readonly ILog logger = LogManager.GetLogger(nameof(IModRegistry));
        public IModRegistryDownloader ModRegistryDownloader { get; }
        public string Name { get; }


        /// <summary>
        /// Get all mod metadata from this registry
        /// </summary>
        /// <returns></returns>
        public abstract Task<GetAllModsResult> GetAllMods();

        /// <summary>
        /// Gets the mod metadata in a string format, located at the given path within the registry
        /// </summary>
        /// <param name="modPath"></param>
        /// <returns></returns>
        public abstract EitherAsync<RegistryMetadataException, string> GetModMetadataString(string modPath);

        /// <summary>
        /// Deserializes the string format from GetModMetadataString into a Mod object
        /// </summary>
        /// <param name="modPath"></param>
        /// <returns></returns>
        public abstract EitherAsync<RegistryMetadataException, Mod> GetModMetadata(string modPath);


        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(PakTarget coordinates, string outputLocation) {
            return
                ModRegistryDownloader
                    .ModPakStream(coordinates)
                    .Map(stream => new FileWriter(outputLocation, stream.Stream, stream.Size));
        }
        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(string org, string repoName, string fileName, string releaseTag, string outputLocation) {
            return DownloadPak(new PakTarget(org, repoName, fileName, releaseTag), outputLocation);
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(Release release, string outputLocation) {
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