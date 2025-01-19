using LanguageExt;
using LanguageExt.SomeHelp;
using static LanguageExt.Prelude;
using log4net;
using log4net.Core;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class GithubModRegistry : JsonRegistry, IModRegistry {
        private static readonly ILog logger = LogManager.GetLogger(typeof(GithubModRegistry));
        public IModRegistryDownloader ModRegistryDownloader { get; }
        public string Organization { get; }
        public string RepoName { get; }
        public string PackageDBBaseUrl => $"https://raw.githubusercontent.com/{Organization}/{RepoName}/db/package_db";
        public string PackageDBPackageListUrl => $"{PackageDBBaseUrl}/mod_list_index.txt";

        public GithubModRegistry(string organization, string repoName, IModRegistryDownloader downloader) {
            Organization = organization;
            RepoName = repoName;
            ModRegistryDownloader = downloader;
        }

        public override Task<GetAllModsResult> GetAllMods() {
            return Prelude
                .TryAsync(HttpHelpers.GetStringContentsAsync(PackageDBPackageListUrl).Task)
                .Select(RegistryUtils.ParseLineSeparatedPackageList)
                .Select(packages => packages.Select(GetModMetadata))
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
    }
}