using LanguageExt;
using LanguageExt.Common;
using LanguageExt.SomeHelp;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class GithubModRegistry : JsonRegistry, IModRegistry {
        private static readonly ILog logger = LogManager.GetLogger(typeof(GithubModRegistry));

        public override string Name => $"Github mod registry at {Organization}/{RepoName}";
        public string Organization { get; set; }
        public string RepoName { get; set; }
        public string PackageDBBaseUrl => $"https://raw.githubusercontent.com/{Organization}/{RepoName}/db/package_db";
        public string PackageDBPackageListUrl => $"{PackageDBBaseUrl}/mod_list_index.txt";

        public GithubModRegistry(string organization, string repoName) {
            Organization = organization;
            RepoName = repoName;
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
            $"https://github.com/{release.Manifest.Organization}/{release.Manifest.RepoName}/releases/download/{release.Tag}/{release.PakFileName}";
    }
}