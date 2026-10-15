using LanguageExt;
using LanguageExt.Common;
using LanguageExt.SomeHelp;
using log4net;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class GithubModRegistry : JsonRegistry, IModRegistry {
        private static new readonly ILog Logger = LogManager.GetLogger(typeof(GithubModRegistry));

        public override string Name => $"Github mod registry at {Organization}/{RepoName}";
        public string Organization { get; set; }
        public string RepoName { get; set; }
        private readonly string _revisionPath = "refs/heads/main";
        public string RegistryBaseUrl => $"https://raw.githubusercontent.com/{Organization}/{RepoName}/{_revisionPath}";
        public string PackageDBBaseUrl => $"{RegistryBaseUrl}/package_db";
        public string DllDatabaseUrl => $"{RegistryBaseUrl}/dll_db";
        public string PackageDBPackageListUrl => $"{PackageDBBaseUrl}/mod_list_index.txt";

        public GithubModRegistry(string organization, string repoName) {
            Organization = organization;
            RepoName = repoName;
        }

        public GithubModRegistry(string baseUrl) {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "raw.githubusercontent.com"
                || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) {
                throw new InvalidDataException("Invalid GitHub registry base URL.");
            }

            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 3 || parts.Any(string.IsNullOrWhiteSpace)
                || parts[^1] is "package_db" or "dll_db") {
                throw new InvalidDataException("GitHub registry base URL must identify a repository revision, not a database.");
            }

            Organization = parts[0];
            RepoName = parts[1];
            _revisionPath = string.Join('/', parts.Skip(2));
        }

        public override Task<GetAllModsResult> GetAllMods() {
            return Prelude
                .TryAsync(HttpHelpers.GetStringContentsAsync(PackageDBPackageListUrl).Task)
                .Select(RegistryUtils.ParseLineSeparatedPackageList)
                .Select(packages => packages.Select(GetMod))
                .Select(results => results.Partition())
                .ToEither()
                .MapLeft(e => RegistryMetadataException.PackageListRetrieval($"Failed to retrieve package list from {PackageDBPackageListUrl}", e))
                .Match(
                    t => new GetAllModsResult(t.Lefts, t.Rights), // return the result if successful
                    e => new GetAllModsResult(e.ToSome().ToSeq(), Prelude.Seq<Mod>()) // return an error if an error ocurred that prevented anything from being returned
                );
        }

        protected override EitherAsync<RegistryMetadataException, string> GetModMetadataString(ModIdentifier modIdentifier) {
            return Prelude
                .TryAsync(HttpHelpers.GetStringContentsAsync($"{PackageDBBaseUrl}/packages/{modIdentifier.Org}/{modIdentifier.ModuleName}.json").Task)
                .ToEither()
                .MapLeft(e => RegistryMetadataException.NotFound(modIdentifier, e));
        }

        public override EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation) =>
            GetMod(coordinates)
                .Map(releaseMetadata => releaseMetadata.Releases.Find(x => x.Tag == coordinates.Version))
                .Bind(maybeRelease => Optional(maybeRelease).ToEitherAsync(() => RegistryMetadataException.NotFound(coordinates, None)))
                .MapLeft(e => new ModPakStreamAcquisitionFailure(coordinates, e))
                .Bind(GetGithubPakStream)
                .Map(sizedStream => new FileWriter(outputLocation, sizedStream.Stream, sizedStream.Size));

        private EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> GetGithubPakStream(Release target) {
            var url = GetGithubPakDownloadUrl(target);
            var length = HttpHelpers.GetContentLengthAsync(url);
            var streamDownloadTask = HttpHelpers.GetByteContentsAsync(url).Task;
            return
                Prelude.TryAsync(length)
                    .Bind(length => Prelude.TryAsync(streamDownloadTask)
                        .Map(stream => new SizedStream(stream, length))
                    )
                    .ToEither()
                    .MapLeft(e => new ModPakStreamAcquisitionFailure(ReleaseCoordinates.FromRelease(target), Error.New($"Failed to fetch pak from {url}.", e)));
        }

        private string GetGithubPakDownloadUrl(Release release) =>
            $"https://github.com/{release.Info.Organization}/{release.Info.RepoName}/releases/download/{release.Tag}/{release.PakFileName}";
    }
}