using LanguageExt;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry {
    public class GithubModRegistry : ModRegistry {
        public string Organization { get; }
        public string RepoName { get; }

        public string PackageDBBaseUrl => $"https://raw.githubusercontent.com/{Organization}/{RepoName}/db/package_db";
        public string PackageDBPackageListUrl => $"{PackageDBBaseUrl}/mod_list_index.txt";

        public GithubModRegistry(string organization, string repoName) {
            Organization = organization;
            RepoName = repoName;
        }

        public override Task<(IEnumerable<RegistryMetadataException>, IEnumerable<Mod>)> GetAllMods() {
            return Prelude
                .TryAsync(HttpHelpers.GetStringContentsAsync(PackageDBPackageListUrl).Task)
                .Select(listString => listString.Split("\n"))
                .Select(packages => packages.Select(GetModMetadata))
                .Select(results => results.Partition())
                .ToEither()
                .MapLeft(e => new RegistryMetadataException(PackageDBPackageListUrl, e))
                .Match(
                    t => t, // return the result if successful
                    e => (new[] { e }.AsEnumerable(),Enumerable.Empty<Mod>()) // return an error if an error ocurred that prevented anything from being returned
                );
        }

        public override EitherAsync<RegistryMetadataException, string> GetModMetadataString(string modPath) {
            return Prelude
                .TryAsync(HttpHelpers.GetStringContentsAsync($"{PackageDBBaseUrl}/packages/{modPath}.json").Task)
                .ToEither()
                .MapLeft(e => new RegistryMetadataException(modPath, e));
        }
    }
}
