using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using log4net;
using Semver;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry {

    public record ModIdentifier(string Org, string ModuleName) {
        public static ModIdentifier FromMod(Mod mod) => 
            new ModIdentifier(mod.LatestManifest.Organization, mod.LatestManifest.RepoName);
        
        public static ModIdentifier FromReleaseCoordinates(ReleaseCoordinates coords) => new ModIdentifier(coords.Org, coords.ModuleName);
        public static ModIdentifier FromRelease(Release release) => new ModIdentifier(release.Manifest.Organization, release.Manifest.RepoName);
    }

    public record ReleaseCoordinates(string Org, string ModuleName, string Version) {
        public static ReleaseCoordinates FromRelease(Release release) => 
            new ReleaseCoordinates(release.Manifest.Organization, release.Manifest.RepoName, release.Tag); 
    }

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

        /// <summary>
        /// Get all mod metadata from this registry
        /// </summary>
        /// <returns></returns>
        public Task<GetAllModsResult> GetAllMods();

        /// <summary>
        /// Deserializes the string format from GetModMetadataString into a Mod object
        /// </summary>
        /// <param name="modId"></param>
        /// <returns></returns>
        public EitherAsync<RegistryMetadataException, Mod> GetModMetadata(ModIdentifier modId);

        public EitherAsync<RegistryMetadataException, Release> GetRelease(ReleaseCoordinates coords) {
            return GetModMetadata(ModIdentifier.FromReleaseCoordinates(coords)).Bind<RegistryMetadataException, Release>(modMetadata => {
                var release = modMetadata.Releases.Find(x => x.Version == coords.Version);

                return release == null
                    ? LeftAsync<RegistryMetadataException, Release>(RegistryMetadataException.NotFound(coords, None))
                    : RightAsync<RegistryMetadataException, Release>(release);
            });
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation);
    }

    public abstract record RegistryMetadataException(string Message, int Code, Option<Error> Underlying) : Expected(Message, Code, Underlying) {
        public record ParseException(string Message, Option<Error> Underlying)
            : RegistryMetadataException(Message, 0, Underlying);

        public record NotFoundException(ModIdentifier ModId, Option<SemVersion> Version, Option<Error> Underlying)
            : RegistryMetadataException(FormatMessage(ModId, Version), 0, Underlying) {
            private static string FormatMessage(ModIdentifier modId, Option<SemVersion> version) =>
                $"Failed to get mod metadata for '{modId.Org}/{modId.ModuleName}" +
                version.Map(v => $" / {v}'").IfNone("'");
        };
        public record PackageListRetrievalException(string Message, Option<Error> Underlying): RegistryMetadataException(Message, 0, Underlying);
        
        public static RegistryMetadataException Parse(string message, Option<Error> underlying) => new ParseException(message, underlying);
        public static RegistryMetadataException NotFound(ModIdentifier modId, Option<Error> underlying) => new NotFoundException(modId, None, underlying);
        public static RegistryMetadataException NotFound(ReleaseCoordinates coords , Option<Error> underlying) => 
            new NotFoundException(ModIdentifier.FromReleaseCoordinates(coords), Some(coords.Version), underlying);
        public static RegistryMetadataException PackageListRetrieval(string message, Option<Error> underlying) => new PackageListRetrievalException(message, underlying);

        public T Match<T>(
            Func<ParseException, T> parseExceptionFunc,
            Func<NotFoundException, T> notFoundFunc,
            Func<PackageListRetrievalException, T> packageListRetrievalFunc
        ) => this switch {
            NotFoundException notFound => notFoundFunc(notFound),
            PackageListRetrievalException packageListRetrievalException => packageListRetrievalFunc(packageListRetrievalException),
            ParseException parseError => parseExceptionFunc(parseError)
        };
    } 
}